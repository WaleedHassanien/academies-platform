using System.Globalization;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Academies.Finance.Application;
using Microsoft.Extensions.Options;

namespace Academies.Finance.Infrastructure.Payments;

public sealed class StripeOptions
{
    /// <summary>The secret key (sk_test_… or sk_live_…) from dashboard.stripe.com › Developers › API keys.</summary>
    public string? SecretKey { get; set; }

    /// <summary>The signing secret (whsec_…) of the webhook pointing at /api/finance/payments/webhooks/stripe.</summary>
    public string? WebhookSecret { get; set; }
}

/// <summary>
/// Stripe over its REST API (no SDK): Checkout for one-off payments, Checkout in setup mode to save
/// a card, and off-session PaymentIntents to charge that card every month. Stripe charges USD, EUR,
/// GBP, SAR and EGP directly, so no conversion is needed.
/// </summary>
public sealed class StripeGateway(HttpClient http, IOptions<PaymentOptions> options) : IPaymentGateway, IAutoPayGateway
{
    public static readonly Uri BaseAddress = new("https://api.stripe.com/");

    public string Name => "Stripe";

    private string Api => $"{options.Value.PublicBaseUrl.TrimEnd('/')}/api/finance/payments/stripe";

    public async Task<CheckoutSession> CreateCheckoutAsync(CheckoutRequest request, CancellationToken ct = default)
    {
        var form = new List<KeyValuePair<string, string>>
        {
            new("mode", "payment"),
            new("success_url", $"{Api}/return?session_id={{CHECKOUT_SESSION_ID}}"),
            new("cancel_url", $"{Api}/cancel"),
            new("client_reference_id", request.Reference),
            new("metadata[reference]", request.Reference),
            new("payment_intent_data[metadata][reference]", request.Reference),
            new("line_items[0][quantity]", "1"),
            new("line_items[0][price_data][currency]", request.Currency.ToLowerInvariant()),
            new("line_items[0][price_data][unit_amount]", Minor(request.Amount)),
            new("line_items[0][price_data][product_data][name]", request.Description),
        };
        if (!string.IsNullOrWhiteSpace(request.CustomerEmail))
        {
            form.Add(new("customer_email", request.CustomerEmail));
        }

        var session = await PostAsync("v1/checkout/sessions", form, request.Reference, ct);
        return new CheckoutSession(session["id"]!.GetValue<string>(), session["url"]!.GetValue<string>(), request.Amount, request.Currency.ToUpperInvariant());
    }

    /// <summary>Stripe takes the money during Checkout; this reads how it ended.</summary>
    public async Task<GatewayCapture> CaptureAsync(string providerSessionId, CancellationToken ct = default)
    {
        var session = await GetAsync($"v1/checkout/sessions/{Uri.EscapeDataString(providerSessionId)}", ct);
        var reference = session["client_reference_id"]?.GetValue<string>() ?? string.Empty;
        return new GatewayCapture(reference, OutcomeOf(session));
    }

    public async Task<CardSetupSession> CreateCardSetupAsync(CardSetupRequest request, CancellationToken ct = default)
    {
        var customerForm = new List<KeyValuePair<string, string>> { new("metadata[reference]", request.Reference) };
        if (!string.IsNullOrWhiteSpace(request.CustomerEmail))
        {
            customerForm.Add(new("email", request.CustomerEmail));
        }

        if (!string.IsNullOrWhiteSpace(request.CustomerName))
        {
            customerForm.Add(new("name", request.CustomerName));
        }

        var customer = await PostAsync("v1/customers", customerForm, $"{request.Reference}-customer", ct);
        var session = await PostAsync("v1/checkout/sessions",
        [
            new("mode", "setup"),
            new("customer", customer["id"]!.GetValue<string>()),
            new("payment_method_types[0]", "card"),
            new("success_url", $"{Api}/setup-return?session_id={{CHECKOUT_SESSION_ID}}"),
            new("cancel_url", $"{Api}/setup-cancel"),
            new("client_reference_id", request.Reference),
            new("metadata[reference]", request.Reference),
            new("setup_intent_data[metadata][reference]", request.Reference),
            new("setup_intent_data[description]", request.Description),
        ], request.Reference, ct);
        return new CardSetupSession(session["id"]!.GetValue<string>(), session["url"]!.GetValue<string>());
    }

    public async Task<SavedCard?> CompleteCardSetupAsync(string providerSetupId, CancellationToken ct = default)
    {
        var session = await GetAsync($"v1/checkout/sessions/{Uri.EscapeDataString(providerSetupId)}?expand[]=setup_intent.payment_method", ct);
        if (session["status"]?.GetValue<string>() != "complete" || session["setup_intent"] is not JsonObject intent
            || intent["payment_method"] is not JsonObject method)
        {
            return null;
        }

        return new SavedCard(
            session["client_reference_id"]?.GetValue<string>() ?? string.Empty,
            session["customer"]!.GetValue<string>(),
            method["id"]!.GetValue<string>(),
            method["card"]?["brand"]?.GetValue<string>(),
            method["card"]?["last4"]?.GetValue<string>());
    }

    public async Task<CardChargeResult> ChargeAsync(CardChargeRequest request, CancellationToken ct = default)
    {
        using var message = Authorized(HttpMethod.Post, "v1/payment_intents", request.Reference);
        message.Content = new FormUrlEncodedContent(
        [
            new("amount", Minor(request.Amount)),
            new("currency", request.Currency.ToLowerInvariant()),
            new("customer", request.CustomerId),
            new("payment_method", request.PaymentMethodId),
            new("off_session", "true"),
            new("confirm", "true"),
            new("description", request.Description),
            new("metadata[reference]", request.Reference),
        ]);
        using var response = await http.SendAsync(message, ct);
        var body = JsonNode.Parse(await response.Content.ReadAsStringAsync(ct));

        if (!response.IsSuccessStatusCode)
        {
            // 402 card_declined / authentication_required: the payer must pay by hand or save another card.
            var error = body?["error"];
            return new CardChargeResult(false, error?["payment_intent"]?["id"]?.GetValue<string>(),
                error?["message"]?.GetValue<string>() ?? $"Stripe refused the charge ({(int)response.StatusCode}).");
        }

        var status = body?["status"]?.GetValue<string>();
        return status == "succeeded"
            ? new CardChargeResult(true, body!["id"]!.GetValue<string>(), null)
            : new CardChargeResult(false, body?["id"]?.GetValue<string>(), $"Payment {status}.");
    }

    /// <summary>
    /// Checks the <c>Stripe-Signature</c> header: HMAC-SHA256 of "{t}.{body}" with the webhook secret,
    /// within five minutes of <c>t</c>.
    /// </summary>
    public static bool VerifySignature(string? header, string body, string? secret, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(header) || string.IsNullOrWhiteSpace(secret))
        {
            return false;
        }

        var parts = header.Split(',', StringSplitOptions.TrimEntries).Select(p => p.Split('=', 2)).Where(p => p.Length == 2).ToList();
        var timestamp = parts.FirstOrDefault(p => p[0] == "t")?[1];
        if (!long.TryParse(timestamp, NumberStyles.None, CultureInfo.InvariantCulture, out var t)
            || Math.Abs(now.ToUnixTimeSeconds() - t) > 300)
        {
            return false;
        }

        var expected = Convert.ToHexStringLower(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes($"{timestamp}.{body}")));
        return parts.Where(p => p[0] == "v1").Any(p =>
            CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(expected), Encoding.ASCII.GetBytes(p[1])));
    }

    public static CaptureOutcome OutcomeOf(JsonNode session) =>
        session["payment_status"]?.GetValue<string>() is "paid" or "no_payment_required" ? CaptureOutcome.Completed
        : session["status"]?.GetValue<string>() == "expired" ? CaptureOutcome.Failed
        : CaptureOutcome.Pending;

    private static string Minor(decimal amount) =>
        ((long)Math.Round(amount * 100, MidpointRounding.AwayFromZero)).ToString(CultureInfo.InvariantCulture);

    private HttpRequestMessage Authorized(HttpMethod method, string path, string? idempotencyKey = null)
    {
        var key = options.Value.Stripe.SecretKey;
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new InvalidOperationException("Payments:Stripe:SecretKey is not configured.");
        }

        var message = new HttpRequestMessage(method, path);
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
        if (idempotencyKey is not null)
        {
            message.Headers.Add("Idempotency-Key", idempotencyKey);
        }

        return message;
    }

    private async Task<JsonNode> PostAsync(string path, IEnumerable<KeyValuePair<string, string>> form, string idempotencyKey, CancellationToken ct)
    {
        using var message = Authorized(HttpMethod.Post, path, idempotencyKey);
        message.Content = new FormUrlEncodedContent(form);
        using var response = await http.SendAsync(message, ct);
        return await ReadAsync(response, path, ct);
    }

    private async Task<JsonNode> GetAsync(string path, CancellationToken ct)
    {
        using var message = Authorized(HttpMethod.Get, path);
        using var response = await http.SendAsync(message, ct);
        return await ReadAsync(response, path, ct);
    }

    private static async Task<JsonNode> ReadAsync(HttpResponseMessage response, string what, CancellationToken ct)
    {
        var text = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Stripe {what} failed ({(int)response.StatusCode}): {text}");
        }

        return JsonNode.Parse(text) ?? throw new InvalidOperationException($"Stripe {what} returned an empty body.");
    }
}

/// <summary>
/// Development auto-pay: "saving a card" is a link back to Finance that stores a test card, and
/// every charge succeeds. Never enable it in production.
/// </summary>
public sealed class FakeAutoPayGateway(IOptions<PaymentOptions> options) : IAutoPayGateway
{
    public string Name => "Fake";

    public Task<CardSetupSession> CreateCardSetupAsync(CardSetupRequest request, CancellationToken ct = default) =>
        Task.FromResult(new CardSetupSession(
            request.Reference, $"{options.Value.PublicBaseUrl.TrimEnd('/')}/api/finance/payments/fake-card/{Uri.EscapeDataString(request.Reference)}"));

    public Task<SavedCard?> CompleteCardSetupAsync(string providerSetupId, CancellationToken ct = default) =>
        Task.FromResult<SavedCard?>(new SavedCard(providerSetupId, "cus_fake", "pm_fake", "visa", "4242"));

    public Task<CardChargeResult> ChargeAsync(CardChargeRequest request, CancellationToken ct = default) =>
        Task.FromResult(new CardChargeResult(true, $"pi_fake_{request.Reference}", null));
}
