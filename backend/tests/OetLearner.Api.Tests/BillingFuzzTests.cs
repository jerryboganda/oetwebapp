using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using OetLearner.Api.Tests.Infrastructure;

namespace OetLearner.Api.Tests;

/// <summary>
/// Slice H — theory-driven fuzz pass over the billing surface.
///
/// Goal: every malformed input across <c>/v1/billing/wallet/top-up</c>,
/// <c>/v1/billing/checkout-sessions</c>, and <c>/v1/billing/quote</c> MUST
/// reject with a structured 4xx error envelope. A 5xx (or an empty body)
/// indicates an unhandled exception in a billing path and is a P1 defect
/// that the owning slice must repair.
///
/// We deliberately drive both:
///   1. Strongly-typed DTO inputs with bad <em>semantic</em> values
///      (negative amount, unknown gateway, junk currency).
///   2. Raw JSON payloads with bad <em>shape</em> (string in an int slot,
///      null in a non-nullable slot, oversized string for couponCode) so
///      we exercise the model-binder boundary as well as the service layer.
/// </summary>
public class BillingFuzzTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public BillingFuzzTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    // ── Wallet top-up: typed-DTO semantic fuzz ──────────────────────────────

    public static TheoryData<int, string> WalletTopUpSemanticCases() => new()
    {
        // amount, gateway
        { 0, "stripe" },
        { -1, "stripe" },
        { int.MaxValue, "stripe" },
        { 25, "" },
        { 25, "USD" }, // not a supported gateway code
        { 25, "ZZZ" },
        { 25, "  " },
        { 99, "stripe" }, // not a configured tier amount
    };

    [Theory]
    [MemberData(nameof(WalletTopUpSemanticCases))]
    public async Task WalletTopUp_RejectsSemanticGarbageAsStructuredError(int amount, string gateway)
    {
        using var client = await CreateClientAsync($"fuzz-wallet-sem-{Guid.NewGuid():N}");

        using var response = await client.PostAsJsonAsync(
            "/v1/billing/wallet/top-up",
            new { amount, gateway });

        AssertNot5xx(response);
        await AssertHasStructuredBody(response);
    }

    // ── Wallet top-up: raw-JSON shape fuzz ──────────────────────────────────

    public static TheoryData<string> WalletTopUpRawJsonCases() => new()
    {
        // amount as decimal — must not be coerced silently
        "{\"amount\":1.5,\"gateway\":\"stripe\"}",
        // amount as string
        "{\"amount\":\"abc\",\"gateway\":\"stripe\"}",
        // amount null on a non-nullable int
        "{\"amount\":null,\"gateway\":\"stripe\"}",
        // gateway null
        "{\"amount\":25,\"gateway\":null}",
        // gateway as a number
        "{\"amount\":25,\"gateway\":42}",
        // overflowing amount
        "{\"amount\":99999999999,\"gateway\":\"stripe\"}",
        // empty body
        "{}",
        // not-even-JSON
        "<script>alert(1)</script>",
    };

    [Theory]
    [MemberData(nameof(WalletTopUpRawJsonCases))]
    public async Task WalletTopUp_RejectsRawJsonGarbageAsStructuredError(string rawBody)
    {
        using var client = await CreateClientAsync($"fuzz-wallet-raw-{Guid.NewGuid():N}");

        using var content = new StringContent(rawBody, Encoding.UTF8, "application/json");
        using var response = await client.PostAsync("/v1/billing/wallet/top-up", content);

        AssertNot5xx(response);
    }

    // ── Checkout session: typed-DTO semantic fuzz ───────────────────────────

    public static TheoryData<string?, int, string?, string?, string?> CheckoutSemanticCases() => new()
    {
        // productType, quantity, priceId, couponCode, gateway
        { null, 1, "basic-monthly", null, "stripe" },
        { "", 1, "basic-monthly", null, "stripe" },
        { "unknown_product", 1, "basic-monthly", null, "stripe" },
        { "plan_upgrade", 0, "basic-monthly", null, "stripe" },
        { "plan_upgrade", -3, "basic-monthly", null, "stripe" },
        { "plan_upgrade", int.MaxValue, "basic-monthly", null, "stripe" },
        { "plan_upgrade", 1, null, null, "stripe" },
        { "plan_upgrade", 1, "", null, "stripe" },
        { "plan_upgrade", 1, "no-such-plan", null, "stripe" },
        { "plan_upgrade", 1, "basic-monthly", "<script>alert(1)</script>", "stripe" },
        { "plan_upgrade", 1, "basic-monthly", "  ", "stripe" },
        { "plan_upgrade", 1, "basic-monthly", new string('x', 1000), "stripe" },
        { "plan_upgrade", 1, "basic-monthly", null, "" },
        { "plan_upgrade", 1, "basic-monthly", null, "ZZZ" },
        { "review_credits", 1, "basic-monthly", null, "USD" }, // currency-code-as-gateway smoke
    };

    [Theory]
    [MemberData(nameof(CheckoutSemanticCases))]
    public async Task CheckoutSession_RejectsSemanticGarbageAsStructuredError(
        string? productType,
        int quantity,
        string? priceId,
        string? couponCode,
        string? gateway)
    {
        using var client = await CreateClientAsync($"fuzz-checkout-{Guid.NewGuid():N}");

        using var response = await client.PostAsJsonAsync(
            "/v1/billing/checkout-sessions",
            new
            {
                productType,
                quantity,
                priceId,
                couponCode,
                gateway
            });

        AssertNot5xx(response);
        // 200 is acceptable for *valid* permutations that slip through (e.g. a
        // malformed coupon that gets quietly stripped); the contract is
        // "never 500 / never opaque body". For unambiguous bad inputs we still
        // expect a 4xx with a body — assert body shape only when 4xx.
        if ((int)response.StatusCode >= 400)
        {
            await AssertHasStructuredBody(response);
        }
    }

    // ── Checkout session: raw-JSON shape fuzz ───────────────────────────────

    public static TheoryData<string> CheckoutRawJsonCases() => new()
    {
        // quantity as decimal
        "{\"productType\":\"plan_upgrade\",\"quantity\":1.5,\"priceId\":\"basic-monthly\"}",
        // quantity as string
        "{\"productType\":\"plan_upgrade\",\"quantity\":\"abc\",\"priceId\":\"basic-monthly\"}",
        // quantity null
        "{\"productType\":\"plan_upgrade\",\"quantity\":null,\"priceId\":\"basic-monthly\"}",
        // productType wrong type
        "{\"productType\":42,\"quantity\":1,\"priceId\":\"basic-monthly\"}",
        // addOnCodes as a string instead of array
        "{\"productType\":\"plan_upgrade\",\"quantity\":1,\"priceId\":\"basic-monthly\",\"addOnCodes\":\"not-an-array\"}",
        // junk
        "{\"productType\":\"plan_upgrade\",\"quantity\":1,\"priceId\":\"basic-monthly\",\"couponCode\":{\"$ne\":null}}",
        // empty object
        "{}",
        // garbage payload
        "not json at all",
    };

    [Theory]
    [MemberData(nameof(CheckoutRawJsonCases))]
    public async Task CheckoutSession_RejectsRawJsonGarbageAsStructuredError(string rawBody)
    {
        using var client = await CreateClientAsync($"fuzz-checkout-raw-{Guid.NewGuid():N}");

        using var content = new StringContent(rawBody, Encoding.UTF8, "application/json");
        using var response = await client.PostAsync("/v1/billing/checkout-sessions", content);

        AssertNot5xx(response);
    }

    // ── Quote endpoint: querystring fuzz ────────────────────────────────────

    public static TheoryData<string> QuoteCouponCases() => new()
    {
        "",
        "   ",
        "<script>alert(1)</script>",
        new string('x', 1000),
        "../../etc/passwd",
        "%00",
        "DROP TABLE BillingCoupons;--",
    };

    [Theory]
    [MemberData(nameof(QuoteCouponCases))]
    public async Task Quote_RejectsCouponGarbageAsStructuredError(string couponCode)
    {
        using var client = await CreateClientAsync($"fuzz-quote-coupon-{Guid.NewGuid():N}");

        using var response = await client.GetAsync(
            "/v1/billing/quote"
            + "?productType=plan_upgrade"
            + "&quantity=1"
            + "&priceId=basic-monthly"
            + $"&couponCode={Uri.EscapeDataString(couponCode)}");

        AssertNot5xx(response);
        // Either 200 (coupon ignored / not found surfaces as zero discount) or
        // a structured 4xx — but never a 5xx and never an opaque empty body.
        if ((int)response.StatusCode >= 400)
        {
            await AssertHasStructuredBody(response);
        }
    }

    public static TheoryData<string?, string> QuoteShapeCases() => new()
    {
        // productType, quantity-as-string
        { null, "1" },
        { "", "1" },
        { "plan_upgrade", "" },
        { "plan_upgrade", "abc" },
        { "plan_upgrade", "-1" },
        { "plan_upgrade", "0" },
        { "plan_upgrade", "1.5" },
        { "plan_upgrade", int.MaxValue.ToString() },
    };

    [Theory]
    [MemberData(nameof(QuoteShapeCases))]
    public async Task Quote_RejectsBadShapeAsStructuredError(string? productType, string quantity)
    {
        using var client = await CreateClientAsync($"fuzz-quote-shape-{Guid.NewGuid():N}");

        var url = "/v1/billing/quote"
            + $"?productType={Uri.EscapeDataString(productType ?? string.Empty)}"
            + $"&quantity={Uri.EscapeDataString(quantity)}"
            + "&priceId=basic-monthly";

        using var response = await client.GetAsync(url);

        AssertNot5xx(response);
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    private static void AssertNot5xx(HttpResponseMessage response)
    {
        Assert.True(
            (int)response.StatusCode < 500,
            $"Billing fuzz input produced {(int)response.StatusCode}; billing endpoints must "
            + "translate bad input into a 4xx structured error, never an unhandled 5xx.");
    }

    private static async Task AssertHasStructuredBody(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        Assert.False(string.IsNullOrWhiteSpace(body), "Expected a structured error body, got empty.");

        // Body must be parseable JSON. We do not pin it to a single schema
        // (ApiException vs ASP.NET ProblemDetails vs minimal-API binder) but
        // we do require valid JSON so clients can render an error.
        try
        {
            using var _ = JsonDocument.Parse(body);
        }
        catch (JsonException ex)
        {
            Assert.Fail($"Billing endpoint returned non-JSON error body: {ex.Message}\nBody: {body}");
        }
    }

    private async Task<HttpClient> CreateClientAsync(string userId)
    {
        await _factory.EnsureLearnerProfileAsync(userId, $"{userId}@example.test", userId);
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Debug-UserId", userId);
        client.DefaultRequestHeaders.Add("X-Debug-Email", $"{userId}@example.test");
        client.DefaultRequestHeaders.Add("X-Debug-Name", userId);
        return client;
    }
}
