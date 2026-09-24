using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Academies.BuildingBlocks.Application.Exceptions;
using Academies.Finance.Application;
using Academies.Finance.Infrastructure.Payments;
using Microsoft.Extensions.Options;
using Shouldly;

namespace Academies.Finance.Tests;

/// <summary>PayPal Orders v2 calls (US-039) against a scripted HTTP handler: no network.</summary>
public sealed class PayPalGatewayTests
{
    private static PayPalPaymentGateway Gateway(FakePayPal paypal) =>
        new(new HttpClient(paypal) { BaseAddress = new Uri("https://api-m.sandbox.paypal.com/") },
            Options.Create(new PaymentOptions
            {
                Provider = "PayPal",
                PublicBaseUrl = "https://app.test",
                PayPal = new PayPalOptions
                {
                    ClientId = "id", ClientSecret = "secret", WebhookId = "WH-1",
                    ExchangeRates = new(StringComparer.OrdinalIgnoreCase) { ["EGP"] = 0.02m },
                },
            }));

    [Fact]
    public async Task EGP_is_converted_to_USD_and_the_payer_is_sent_to_the_approval_link()
    {
        var paypal = new FakePayPal();
        paypal.On("v2/checkout/orders", """
            {"id":"ORDER-1","status":"PAYER_ACTION_REQUIRED","links":[{"rel":"self","href":"x"},{"rel":"payer-action","href":"https://paypal.test/approve/ORDER-1"}]}
            """);

        var session = await Gateway(paypal).CreateCheckoutAsync(
            new CheckoutRequest("PAY-1-9-abc", 1500m, "EGP", "Tuition month 3", "parent@test"), TestContext.Current.CancellationToken);

        session.ProviderSessionId.ShouldBe("ORDER-1");
        session.CheckoutUrl.ShouldBe("https://paypal.test/approve/ORDER-1");
        session.ChargedAmount.ShouldBe(30.00m);
        session.ChargedCurrency.ShouldBe("USD");

        var sent = JsonNode.Parse(paypal.Bodies["v2/checkout/orders"])!;
        var unit = sent["purchase_units"]![0]!;
        unit["custom_id"]!.GetValue<string>().ShouldBe("PAY-1-9-abc");
        unit["amount"]!["currency_code"]!.GetValue<string>().ShouldBe("USD");
        unit["amount"]!["value"]!.GetValue<string>().ShouldBe("30.00");
        sent["payment_source"]!["paypal"]!["experience_context"]!["return_url"]!.GetValue<string>()
            .ShouldBe("https://app.test/api/finance/payments/paypal/return");
        paypal.Headers["v2/checkout/orders"].ShouldContain("PayPal-Request-Id: PAY-1-9-abc");
    }

    [Fact]
    public async Task USD_is_charged_as_is()
    {
        var paypal = new FakePayPal();
        paypal.On("v2/checkout/orders", """{"id":"O","links":[{"rel":"approve","href":"https://paypal.test/a"}]}""");

        var session = await Gateway(paypal).CreateCheckoutAsync(
            new CheckoutRequest("PAY-1-2-x", 49.999m, "USD", "Tuition", null), TestContext.Current.CancellationToken);

        session.ChargedAmount.ShouldBe(50.00m);
        JsonNode.Parse(paypal.Bodies["v2/checkout/orders"])!["payment_source"]!["paypal"]!["email_address"].ShouldBeNull();
    }

    [Fact]
    public void A_currency_without_a_rate_is_refused() =>
        Should.Throw<BusinessRuleException>(() => new PayPalOptions().ToChargeAmount(100, "EGP")).Code.ShouldBe("payments.no_exchange_rate");

    [Fact]
    public async Task Capture_reads_our_reference_and_status()
    {
        var paypal = new FakePayPal();
        paypal.On("v2/checkout/orders/ORDER-1/capture", """
            {"id":"ORDER-1","status":"COMPLETED","purchase_units":[{"payments":{"captures":[{"id":"CAP-1","status":"COMPLETED","custom_id":"PAY-1-9-abc"}]}}]}
            """);

        var capture = await Gateway(paypal).CaptureAsync("ORDER-1", TestContext.Current.CancellationToken);

        capture.ShouldBe(new GatewayCapture("PAY-1-9-abc", CaptureOutcome.Completed));
    }

    [Fact]
    public async Task Capturing_twice_reports_the_final_state_instead_of_failing()
    {
        var paypal = new FakePayPal();
        paypal.On("v2/checkout/orders/ORDER-1/capture", """{"name":"UNPROCESSABLE_ENTITY","details":[{"issue":"ORDER_ALREADY_CAPTURED"}]}""", HttpStatusCode.UnprocessableEntity);
        paypal.On("v2/checkout/orders/ORDER-1", """
            {"id":"ORDER-1","status":"COMPLETED","purchase_units":[{"custom_id":"PAY-1-9-abc","payments":{"captures":[{"status":"COMPLETED"}]}}]}
            """);

        (await Gateway(paypal).CaptureAsync("ORDER-1", TestContext.Current.CancellationToken))
            .ShouldBe(new GatewayCapture("PAY-1-9-abc", CaptureOutcome.Completed));
    }

    [Fact]
    public async Task Declined_funding_is_a_failed_capture()
    {
        var paypal = new FakePayPal();
        paypal.On("v2/checkout/orders/ORDER-2/capture", """{"name":"UNPROCESSABLE_ENTITY","details":[{"issue":"INSTRUMENT_DECLINED"}]}""", HttpStatusCode.UnprocessableEntity);
        paypal.On("v2/checkout/orders/ORDER-2", """{"id":"ORDER-2","status":"APPROVED","purchase_units":[{"custom_id":"PAY-1-7-zz"}]}""");

        (await Gateway(paypal).CaptureAsync("ORDER-2", TestContext.Current.CancellationToken))
            .ShouldBe(new GatewayCapture("PAY-1-7-zz", CaptureOutcome.Failed));
    }

    [Fact]
    public async Task Webhook_signature_is_checked_with_PayPal()
    {
        var paypal = new FakePayPal();
        paypal.On("v1/notifications/verify-webhook-signature", """{"verification_status":"SUCCESS"}""");
        var headers = new Dictionary<string, string> { ["PAYPAL-TRANSMISSION-ID"] = "T1", ["PAYPAL-AUTH-ALGO"] = "SHA256withRSA" };

        var ok = await Gateway(paypal).VerifyWebhookAsync(
            h => headers.GetValueOrDefault(h), """{"event_type":"PAYMENT.CAPTURE.COMPLETED","resource":{}}""", TestContext.Current.CancellationToken);

        ok.ShouldBeTrue();
        var sent = JsonNode.Parse(paypal.Bodies["v1/notifications/verify-webhook-signature"])!;
        sent["webhook_id"]!.GetValue<string>().ShouldBe("WH-1");
        sent["transmission_id"]!.GetValue<string>().ShouldBe("T1");
        sent["webhook_event"]!["event_type"]!.GetValue<string>().ShouldBe("PAYMENT.CAPTURE.COMPLETED");
    }

    /// <summary>Answers the OAuth call automatically and replays scripted responses per path.</summary>
    private sealed class FakePayPal : HttpMessageHandler
    {
        private readonly Dictionary<string, (string Body, HttpStatusCode Status)> _responses = [];

        public Dictionary<string, string> Bodies { get; } = [];
        public Dictionary<string, string> Headers { get; } = [];

        public void On(string path, string body, HttpStatusCode status = HttpStatusCode.OK) => _responses[path] = (body, status);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath.TrimStart('/');
            if (path == "v1/oauth2/token")
            {
                return Json("""{"access_token":"TOKEN","expires_in":32400}""", HttpStatusCode.OK);
            }

            request.Headers.Authorization!.Parameter.ShouldBe("TOKEN");
            Bodies[path] = request.Content is null ? "" : await request.Content.ReadAsStringAsync(ct);
            Headers[path] = request.Headers.ToString();
            return _responses.TryGetValue(path, out var r) ? Json(r.Body, r.Status) : Json("{}", HttpStatusCode.NotFound);
        }

        private static HttpResponseMessage Json(string body, HttpStatusCode status) =>
            new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    }
}
