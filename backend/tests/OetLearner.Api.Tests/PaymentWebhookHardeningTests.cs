using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using OetLearner.Api.Configuration;
using OetLearner.Api.Services;

namespace OetLearner.Api.Tests;

/// <summary>
/// Slice B (billing-hardening) — webhook hardening guardrails.
/// Covers signature rejection, replay rejection, and PII redaction at ingestion.
/// </summary>
public class PaymentWebhookHardeningTests
{
    private const string Secret = "whsec_test_billing_hardening_b";

    private static StripeGateway BuildStripe(int maxAge = 300, bool sandbox = false) => new(
        new HttpClient(),
        Options.Create(new BillingOptions
        {
            AllowSandboxFallbacks = sandbox,
            WebhookMaxAgeSeconds = maxAge,
            Stripe = new StripeBillingOptions { WebhookSecret = Secret }
        }));

    private static (string payload, Dictionary<string, string> headers) SignStripe(
        object body,
        DateTimeOffset? at = null,
        string? overrideSignature = null)
    {
        var ts = (at ?? DateTimeOffset.UtcNow).ToUnixTimeSeconds();
        var payload = JsonSerializer.Serialize(body);
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(Secret));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes($"{ts}.{payload}"));
        var v1 = overrideSignature ?? Convert.ToHexString(hash).ToLowerInvariant();
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Stripe-Signature"] = $"t={ts.ToString(CultureInfo.InvariantCulture)},v1={v1}"
        };
        return (payload, headers);
    }

    [Fact]
    public async Task Stripe_RejectsForgedSignature_BeforePersistence()
    {
        var gateway = BuildStripe();
        var (payload, headers) = SignStripe(
            new Dictionary<string, object?>
            {
                ["id"] = "evt_1",
                ["type"] = "checkout.session.completed",
                ["data"] = new Dictionary<string, object?> { ["object"] = new { id = "cs_1" } }
            },
            overrideSignature: "deadbeef");

        var result = await gateway.HandleWebhookAsync(payload, headers, default);

        Assert.False(result.Processed);
        Assert.Equal("signature_verification_failed", result.EventType);
        Assert.Equal("{}", result.SafePayloadJson); // no body leak
    }

    [Fact]
    public async Task Stripe_RejectsMissingSignatureHeader()
    {
        var gateway = BuildStripe();
        var result = await gateway.HandleWebhookAsync("{\"id\":\"evt_x\"}", new Dictionary<string, string>(), default);
        Assert.False(result.Processed);
        Assert.Contains("missing", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Stripe_RejectsReplayedTimestampOlderThanMaxAge()
    {
        var gateway = BuildStripe(maxAge: 60);
        var stale = DateTimeOffset.UtcNow.AddMinutes(-15);
        var (payload, headers) = SignStripe(new { id = "evt_replay", type = "checkout.session.completed" }, at: stale);

        var result = await gateway.HandleWebhookAsync(payload, headers, default);

        Assert.False(result.Processed);
        Assert.Contains("window", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Stripe_AcceptsFreshSignedPayload_AndRedactsPii()
    {
        var gateway = BuildStripe(maxAge: 300);
        var (payload, headers) = SignStripe(new Dictionary<string, object?>
        {
            ["id"] = "evt_ok",
            ["type"] = "checkout.session.completed",
            ["data"] = new Dictionary<string, object?>
            {
                ["object"] = new
                {
                    id = "cs_ok",
                    payment_status = "paid",
                    amount_total = 4200,
                    currency = "aud",
                    customer_email = "secret@victim.com",
                    customer_details = new { email = "secret@victim.com", name = "Real Name", address = new { city = "Secretville" } },
                    receipt_email = "leak@example.com",
                    shipping = new { name = "Leak", address = new { line1 = "1 Leak St" } }
                }
            }
        });

        var result = await gateway.HandleWebhookAsync(payload, headers, default);

        Assert.True(result.Processed);
        Assert.Equal("completed", result.NormalizedStatus);
        Assert.NotNull(result.SafePayloadJson);
        Assert.DoesNotContain("secret@victim.com", result.SafePayloadJson);
        Assert.DoesNotContain("Real Name", result.SafePayloadJson);
        Assert.DoesNotContain("Secretville", result.SafePayloadJson);
        Assert.DoesNotContain("leak@example.com", result.SafePayloadJson);
        Assert.DoesNotContain("1 Leak St", result.SafePayloadJson);
        // But safe fields ARE preserved.
        Assert.Contains("\"cs_ok\"", result.SafePayloadJson);
        Assert.Contains("\"aud\"", result.SafePayloadJson);
    }

    [Fact]
    public async Task Stripe_DisputeEvents_AreCategorisedAndStatusNormalised()
    {
        var gateway = BuildStripe();
        var (payload, headers) = SignStripe(new Dictionary<string, object?>
        {
            ["id"] = "evt_dispute",
            ["type"] = "charge.dispute.created",
            ["data"] = new Dictionary<string, object?>
            {
                ["object"] = new { id = "dp_1", payment_intent = "pi_abc", reason = "fraudulent", amount = 4200, currency = "aud" }
            }
        });

        var result = await gateway.HandleWebhookAsync(payload, headers, default);

        Assert.True(result.Processed);
        Assert.Equal(PaymentWebhookCategories.Dispute, result.EventCategory);
        Assert.Equal("dispute_opened", result.NormalizedStatus);
        Assert.Equal("pi_abc", result.GatewayTransactionId);
    }

    [Fact]
    public async Task Stripe_RefundEvents_AreCategorised()
    {
        var gateway = BuildStripe();
        var (payload, headers) = SignStripe(new Dictionary<string, object?>
        {
            ["id"] = "evt_refund",
            ["type"] = "charge.refunded",
            ["data"] = new Dictionary<string, object?>
            {
                ["object"] = new { id = "ch_1", payment_intent = "pi_refund", amount_refunded = 1000, currency = "aud" }
            }
        });

        var result = await gateway.HandleWebhookAsync(payload, headers, default);

        Assert.True(result.Processed);
        Assert.Equal(PaymentWebhookCategories.Refund, result.EventCategory);
        Assert.Equal("refunded", result.NormalizedStatus);
        Assert.Equal("pi_refund", result.GatewayTransactionId);
    }

    [Fact]
    public void DeadLetter_ThresholdIsConfigurable_AndDefaultsToFive()
    {
        var defaults = new BillingOptions();
        Assert.Equal(5, defaults.WebhookMaxAttempts);
        Assert.Equal(300, defaults.WebhookMaxAgeSeconds);
    }
}
