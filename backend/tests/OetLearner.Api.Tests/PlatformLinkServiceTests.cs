using System.Collections.Generic;
using System.Web;
using Microsoft.Extensions.Options;
using OetLearner.Api.Configuration;
using OetLearner.Api.Services;
using Xunit;

namespace OetLearner.Api.Tests;

public class PlatformLinkServiceTests
{
    private static PlatformLinkService Build(
        string? api = "https://api.test/",
        string? web = "https://web.test/",
        string? checkoutBase = "https://checkout.test/start",
        string? fallbackDomain = "fallback.test")
    {
        var platform = new PlatformOptions
        {
            PublicApiBaseUrl = api,
            PublicWebBaseUrl = web,
        };
        if (fallbackDomain is not null)
        {
            platform.FallbackEmailDomain = fallbackDomain;
        }
        else
        {
            // Force the "treat as missing" branch.
            platform.FallbackEmailDomain = "";
        }
        return new PlatformLinkService(
            Options.Create(platform),
            Options.Create(new BillingOptions { CheckoutBaseUrl = checkoutBase }));
    }

    // ── BuildApiUrl ─────────────────────────────────────────────────────

    [Theory]
    [InlineData("/v1/health", "https://api.test/v1/health")]
    [InlineData("v1/health", "https://api.test/v1/health")]
    public void BuildApiUrl_NormalisesLeadingSlashAndJoinsBase(string input, string expected)
    {
        Assert.Equal(expected, Build().BuildApiUrl(input));
    }

    [Fact]
    public void BuildApiUrl_AppendsTrailingSlashOnBaseWhenMissing()
    {
        var svc = Build(api: "https://api.test");
        Assert.Equal("https://api.test/v1/x", svc.BuildApiUrl("/v1/x"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void BuildApiUrl_FallsBackToRelativeWhenBaseMissing(string? api)
    {
        var svc = Build(api: api);
        Assert.Equal("/v1/x", svc.BuildApiUrl("v1/x"));
    }

    // ── BuildWebUrl ─────────────────────────────────────────────────────

    [Fact]
    public void BuildWebUrl_JoinsRelativePathToWebBase()
    {
        Assert.Equal("https://web.test/dashboard", Build().BuildWebUrl("/dashboard"));
    }

    [Fact]
    public void BuildWebUrl_FallsBackToRelativeWhenBaseMissing()
    {
        Assert.Equal("/dashboard", Build(web: null).BuildWebUrl("/dashboard"));
    }

    // ── BuildCheckoutUrl ────────────────────────────────────────────────

    [Fact]
    public void BuildCheckoutUrl_ThrowsWhenBaseMissing()
    {
        var svc = Build(checkoutBase: null);
        Assert.Throws<System.InvalidOperationException>(() =>
            svc.BuildCheckoutUrl("sess", "subscription", 1));
    }

    [Fact]
    public void BuildCheckoutUrl_IncludesRequiredParams()
    {
        var url = Build().BuildCheckoutUrl("sess-1", "subscription", 2);
        var qs = HttpUtility.ParseQueryString(new System.Uri(url).Query);
        Assert.Equal("sess-1", qs["sessionId"]);
        Assert.Equal("subscription", qs["productType"]);
        Assert.Equal("2", qs["quantity"]);
        Assert.Null(qs["planId"]);
        Assert.Null(qs["couponCode"]);
        Assert.Null(qs["addOnCodes"]);
    }

    [Fact]
    public void BuildCheckoutUrl_IncludesOptionalParamsWhenSupplied()
    {
        var url = Build().BuildCheckoutUrl(
            sessionId: "sess",
            productType: "plan",
            quantity: 1,
            planId: "premium",
            couponCode: "SAVE10",
            addOnCodes: new[] { "alpha", "beta" },
            quoteId: "q-1");
        var qs = HttpUtility.ParseQueryString(new System.Uri(url).Query);
        Assert.Equal("premium", qs["planId"]);
        Assert.Equal("SAVE10", qs["couponCode"]);
        Assert.Equal("alpha,beta", qs["addOnCodes"]);
        Assert.Equal("q-1", qs["quoteId"]);
    }

    [Fact]
    public void BuildCheckoutUrl_FiltersWhitespaceAddOnCodes()
    {
        var url = Build().BuildCheckoutUrl(
            sessionId: "s", productType: "p", quantity: 1,
            addOnCodes: new[] { "alpha", "   ", "", "beta" });
        var qs = HttpUtility.ParseQueryString(new System.Uri(url).Query);
        Assert.Equal("alpha,beta", qs["addOnCodes"]);
    }

    [Fact]
    public void BuildCheckoutUrl_OmitsAddOnCodesParamWhenNull()
    {
        var url = Build().BuildCheckoutUrl("s", "p", 1, addOnCodes: null);
        var qs = HttpUtility.ParseQueryString(new System.Uri(url).Query);
        Assert.Null(qs["addOnCodes"]);
    }

    [Fact]
    public void BuildCheckoutUrl_PreservesExistingQueryStringOnBase()
    {
        var svc = Build(checkoutBase: "https://checkout.test/start?ref=site");
        var url = svc.BuildCheckoutUrl("sess", "sub", 1);
        var qs = HttpUtility.ParseQueryString(new System.Uri(url).Query);
        Assert.Equal("site", qs["ref"]);
        Assert.Equal("sess", qs["sessionId"]);
    }

    // ── BuildFallbackEmail ──────────────────────────────────────────────

    [Theory]
    [InlineData("Subject-123", "subject-123@fallback.test")]
    [InlineData("ABC_DEF", "abc-def@fallback.test")]
    [InlineData("user@example.com", "user-example-com@fallback.test")]
    public void BuildFallbackEmail_LowercasesAndSanitises(string input, string expected)
    {
        Assert.Equal(expected, Build().BuildFallbackEmail(input));
    }

    [Theory]
    [InlineData("---")]
    [InlineData("@@@")]
    [InlineData("   ")]
    public void BuildFallbackEmail_FallsBackToUserWhenAllPunctuation(string input)
    {
        Assert.Equal("user@fallback.test", Build().BuildFallbackEmail(input));
    }

    [Fact]
    public void BuildFallbackEmail_TrimsLeadingAndTrailingHyphens()
    {
        Assert.Equal("abc@fallback.test", Build().BuildFallbackEmail("---abc---"));
    }

    [Theory]
    [InlineData(null, "subject@example.invalid")]
    [InlineData("", "subject@example.invalid")]
    [InlineData("   ", "subject@example.invalid")]
    [InlineData("@custom.test", "subject@custom.test")]
    [InlineData("custom.test", "subject@custom.test")]
    public void BuildFallbackEmail_HandlesDomainEdgeCases(string? domain, string expected)
    {
        Assert.Equal(expected, Build(fallbackDomain: domain).BuildFallbackEmail("subject"));
    }
}
