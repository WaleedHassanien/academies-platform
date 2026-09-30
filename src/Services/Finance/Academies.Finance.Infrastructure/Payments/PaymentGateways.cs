using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Academies.BuildingBlocks.Application.Exceptions;
using Academies.Finance.Application;
using Microsoft.Extensions.Options;

namespace Academies.Finance.Infrastructure.Payments;

public sealed class PaymentOptions
{
    public const string Section = "Payments";

    /// <summary>"Fake" (development: pays instantly), "PayPal" or "Stripe".</summary>
    public string Provider { get; set; } = "Fake";

    public StripeOptions Stripe { get; set; } = new();

    public AutoPayOptions AutoPay { get; set; } = new();

    /// <summary>Public URL the browser uses for /api (the SPA origin, which proxies /api to the gateway).</summary>
    public string PublicBaseUrl { get; set; } = "http://localhost:4200";

    /// <summary>Where the payer lands after checkout (the parent portal).</summary>
    public string ReturnUrl { get; set; } = "http://localhost:4200/parent";

    public PayPalOptions PayPal { get; set; } = new();
}

/// <summary>Saving cards for automatic monthly charges.</summary>
public sealed class AutoPayOptions
{
    /// <summary>"Stripe", "Fake" (development) or "None". Empty: Stripe when Payments:Provider is Stripe, otherwise Fake.</summary>
    public string? Provider { get; set; }

    /// <summary>Where the payer lands after saving a card.</summary>
    public string? ReturnUrl { get; set; }
}

public sealed class PayPalOptions
{
    public string? ClientId { get; set; }
    public string? ClientSecret { get; set; }

    /// <summary>"sandbox" or "live".</summary>
    public string Mode { get; set; } = "sandbox";

    /// <summary>Webhook id from the PayPal app settings, needed to verify webhook signatures.</summary>
    public string? WebhookId { get; set; }

    /// <summary>Currency PayPal charges in when it can't charge the invoice's own currency.</summary>
    public string Currency { get; set; } = "USD";

    /// <summary>Invoice currencies PayPal charges as they are.</summary>
    public static readonly IReadOnlyList<string> Native = ["USD", "EUR", "GBP"];

    /// <summary>
    /// Rates into <see cref="Currency"/> for currencies PayPal can't charge, e.g. <c>"EGP": 0.0206</c>
    /// (1 EGP = 0.0206 USD) or <c>"SAR": 0.2667</c>. PayPal supports neither EGP nor SAR.
    /// </summary>
    public Dictionary<string, decimal> ExchangeRates { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public Uri BaseAddress => new(Mode.Equals("live", StringComparison.OrdinalIgnoreCase)
        ? "https://api-m.paypal.com/"
        : "https://api-m.sandbox.paypal.com/");

    /// <summary>Converts an academy-currency amount into what PayPal will charge.</summary>
    public (decimal Amount, string Currency) ToChargeAmount(decimal amount, string currency)
    {
        if (currency.Equals(Currency, StringComparison.OrdinalIgnoreCase) || Native.Contains(currency.ToUpperInvariant()))
        {
            return (Math.Round(amount, 2, MidpointRounding.AwayFromZero), currency.ToUpperInvariant());
        }

        if (!ExchangeRates.TryGetValue(currency, out var rate) || rate <= 0)
        {
            throw new BusinessRuleException(
                $"PayPal can't charge {currency}. Set Payments:PayPal:ExchangeRates:{currency} (1 {currency} in {Currency}).",
                "payments.no_exchange_rate");
        }

        var converted = Math.Round(amount * rate, 2, MidpointRounding.AwayFromZero);
        return (Math.Max(converted, 0.01m), Currency);
    }
}

/// <summary>
/// Development gateway: the checkout link points back at Finance, which marks the payment paid
/// and redirects to the portal. Never enable it in production.
/// </summary>
public sealed class FakePaymentGateway(IOptions<PaymentOptions> options) : IPaymentGateway
{
    public string Name => "Fake";

    public Task<CheckoutSession> CreateCheckoutAsync(CheckoutRequest request, CancellationToken ct = default) =>
        Task.FromResult(new CheckoutSession(
            request.Reference,
            $"{options.Value.PublicBaseUrl.TrimEnd('/')}/api/finance/payments/fake-checkout/{Uri.EscapeDataString(request.Reference)}",
            request.Amount,
            request.Currency));

    public Task<GatewayCapture> CaptureAsync(string providerSessionId, CancellationToken ct = default) =>
        Task.FromResult(new GatewayCapture(providerSessionId, CaptureOutcome.Completed));
}

/// <summary>
/// PayPal Checkout over the Orders v2 REST API (no SDK), in four steps:
/// <list type="number">
/// <item>Create an order (intent CAPTURE, our reference as custom_id).</item>
/// <item>Send the payer to PayPal's approval page.</item>
/// <item>When they return, capture the order on the server.</item>
/// <item>Treat webhooks as the backstop for payers who closed the tab before returning.</item>
/// </list>
/// Academy currencies PayPal can't charge (EGP) are converted with <see cref="PayPalOptions.ExchangeRates"/>.
/// </summary>
public sealed class PayPalPaymentGateway(HttpClient http, IOptions<PaymentOptions> options) : IPaymentGateway
{
    private static readonly JsonSerializerOptions WithoutNulls = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };
    private static readonly SemaphoreSlim TokenLock = new(1, 1);
    private static (string Token, DateTime ExpiresUtc)? _token;

    public string Name => "PayPal";

    public async Task<CheckoutSession> CreateCheckoutAsync(CheckoutRequest request, CancellationToken ct = default)
    {
        var o = options.Value;
        var (amount, currency) = o.PayPal.ToChargeAmount(request.Amount, request.Currency);
        var api = $"{o.PublicBaseUrl.TrimEnd('/')}/api/finance/payments/paypal";

        var body = new
        {
            intent = "CAPTURE",
            purchase_units = new[]
            {
                new
                {
                    reference_id = request.Reference,
                    custom_id = request.Reference,
                    invoice_id = request.Reference,
                    description = $"{request.Description} ({request.Amount.ToString("0.00", CultureInfo.InvariantCulture)} {request.Currency})",
                    amount = new { currency_code = currency, value = amount.ToString("0.00", CultureInfo.InvariantCulture) },
                },
            },
            payment_source = new
            {
                paypal = new
                {
                    email_address = request.CustomerEmail,
                    experience_context = new
                    {
                        brand_name = "Academies Platform",
                        user_action = "PAY_NOW",
                        shipping_preference = "NO_SHIPPING",
                        return_url = $"{api}/return",
                        cancel_url = $"{api}/cancel",
                    },
                },
            },
        };

        using var message = await AuthorizedAsync(HttpMethod.Post, "v2/checkout/orders", ct);
        message.Headers.Add("PayPal-Request-Id", request.Reference); // idempotent retries
        message.Content = JsonContent.Create(body, options: WithoutNulls);

        using var response = await http.SendAsync(message, ct);
        var order = await ReadAsync(response, "create order", ct);
        var approve = order["links"]!.AsArray()
            .FirstOrDefault(l => l!["rel"]!.GetValue<string>() is "payer-action" or "approve")?["href"]!.GetValue<string>()
            ?? throw new InvalidOperationException("PayPal did not return an approval link.");

        return new CheckoutSession(order["id"]!.GetValue<string>(), approve, amount, currency);
    }

    public async Task<GatewayCapture> CaptureAsync(string providerSessionId, CancellationToken ct = default)
    {
        var id = Uri.EscapeDataString(providerSessionId);
        using var message = await AuthorizedAsync(HttpMethod.Post, $"v2/checkout/orders/{id}/capture", ct);
        message.Headers.Add("PayPal-Request-Id", $"{providerSessionId}-capture");
        message.Content = new StringContent("{}", Encoding.UTF8, "application/json");
        using var response = await http.SendAsync(message, ct);

        if (response.StatusCode == HttpStatusCode.UnprocessableEntity)
        {
            var error = await response.Content.ReadAsStringAsync(ct);
            if (error.Contains("ORDER_ALREADY_CAPTURED", StringComparison.Ordinal))
            {
                return await GetOrderOutcomeAsync(providerSessionId, ct);
            }

            // e.g. INSTRUMENT_DECLINED: the payer's funding source was refused.
            var order = await GetOrderAsync(providerSessionId, ct);
            return new GatewayCapture(CustomId(order), CaptureOutcome.Failed);
        }

        var captured = await ReadAsync(response, "capture order", ct);
        var capture = captured["purchase_units"]![0]!["payments"]!["captures"]![0]!;
        return new GatewayCapture(capture["custom_id"]!.GetValue<string>(), ToOutcome(capture["status"]!.GetValue<string>()));
    }

    /// <summary>
    /// Asks PayPal to confirm that a webhook came from it
    /// (<c>/v1/notifications/verify-webhook-signature</c>). Needs <see cref="PayPalOptions.WebhookId"/>.
    /// </summary>
    public async Task<bool> VerifyWebhookAsync(Func<string, string?> header, string body, CancellationToken ct = default)
    {
        var webhookId = options.Value.PayPal.WebhookId;
        if (string.IsNullOrWhiteSpace(webhookId))
        {
            return false;
        }

        var payload = new JsonObject
        {
            ["auth_algo"] = header("PAYPAL-AUTH-ALGO"),
            ["cert_url"] = header("PAYPAL-CERT-URL"),
            ["transmission_id"] = header("PAYPAL-TRANSMISSION-ID"),
            ["transmission_sig"] = header("PAYPAL-TRANSMISSION-SIG"),
            ["transmission_time"] = header("PAYPAL-TRANSMISSION-TIME"),
            ["webhook_id"] = webhookId,
            ["webhook_event"] = JsonNode.Parse(body),
        };

        using var message = await AuthorizedAsync(HttpMethod.Post, "v1/notifications/verify-webhook-signature", ct);
        message.Content = new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json");
        using var response = await http.SendAsync(message, ct);
        if (!response.IsSuccessStatusCode)
        {
            return false;
        }

        var result = await ReadAsync(response, "verify webhook", ct);
        return result["verification_status"]?.GetValue<string>() == "SUCCESS";
    }

    private async Task<GatewayCapture> GetOrderOutcomeAsync(string orderId, CancellationToken ct)
    {
        var order = await GetOrderAsync(orderId, ct);
        var capture = order["purchase_units"]?[0]?["payments"]?["captures"]?[0];
        var status = capture?["status"]?.GetValue<string>() ?? order["status"]!.GetValue<string>();
        return new GatewayCapture(CustomId(order), ToOutcome(status));
    }

    private async Task<JsonNode> GetOrderAsync(string orderId, CancellationToken ct)
    {
        using var message = await AuthorizedAsync(HttpMethod.Get, $"v2/checkout/orders/{Uri.EscapeDataString(orderId)}", ct);
        using var response = await http.SendAsync(message, ct);
        return await ReadAsync(response, "get order", ct);
    }

    private static string CustomId(JsonNode order) =>
        order["purchase_units"]![0]!["custom_id"]!.GetValue<string>();

    private static CaptureOutcome ToOutcome(string status) => status switch
    {
        "COMPLETED" => CaptureOutcome.Completed,
        "PENDING" => CaptureOutcome.Pending,
        _ => CaptureOutcome.Failed,
    };

    private async Task<HttpRequestMessage> AuthorizedAsync(HttpMethod method, string path, CancellationToken ct)
    {
        var message = new HttpRequestMessage(method, path);
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await TokenAsync(ct));
        return message;
    }

    /// <summary>OAuth2 client-credentials token, cached until shortly before it expires.</summary>
    private async Task<string> TokenAsync(CancellationToken ct)
    {
        if (_token is { } cached && cached.ExpiresUtc > DateTime.UtcNow)
        {
            return cached.Token;
        }

        await TokenLock.WaitAsync(ct);
        try
        {
            if (_token is { } again && again.ExpiresUtc > DateTime.UtcNow)
            {
                return again.Token;
            }

            var p = options.Value.PayPal;
            if (string.IsNullOrWhiteSpace(p.ClientId) || string.IsNullOrWhiteSpace(p.ClientSecret))
            {
                throw new InvalidOperationException("Payments:PayPal:ClientId and ClientSecret are not configured.");
            }

            using var message = new HttpRequestMessage(HttpMethod.Post, "v1/oauth2/token")
            {
                Content = new FormUrlEncodedContent([new("grant_type", "client_credentials")]),
            };
            message.Headers.Authorization = new AuthenticationHeaderValue(
                "Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{p.ClientId}:{p.ClientSecret}")));

            using var response = await http.SendAsync(message, ct);
            var token = await ReadAsync(response, "get token", ct);
            var expiresIn = token["expires_in"]!.GetValue<int>();
            _token = (token["access_token"]!.GetValue<string>(), DateTime.UtcNow.AddSeconds(Math.Max(60, expiresIn - 120)));
            return _token.Value.Token;
        }
        finally
        {
            TokenLock.Release();
        }
    }

    private static async Task<JsonNode> ReadAsync(HttpResponseMessage response, string what, CancellationToken ct)
    {
        var text = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"PayPal {what} failed ({(int)response.StatusCode}): {text}");
        }

        return JsonNode.Parse(text) ?? throw new InvalidOperationException($"PayPal {what} returned an empty body.");
    }
}
