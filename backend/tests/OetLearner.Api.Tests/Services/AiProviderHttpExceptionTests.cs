using OetLearner.Api.Services.Rulebook;

namespace OetLearner.Api.Tests.Services;

/// <summary>
/// The typed provider failure. Its Message is parsed by AiRetryPolicy, the gateway
/// classifier and the Copilot provider, so it must stay byte-identical, and the parsed
/// provider text must never leak into it (post-mortem INC-2026-CLAUDE-01).
/// </summary>
public sealed class AiProviderHttpExceptionTests
{
    private static AiProviderError CreditBalanceError(TimeSpan? retryAfter = null) => new(
        AiProviderErrorClass.QuotaExhausted,
        400,
        "invalid_request_error",
        null,
        "Your credit balance is too low to access the Anthropic API.",
        "req_011CTexample",
        retryAfter);

    [Fact]
    public void Message_IsUnchangedByAnAttachedProviderError()
    {
        var withError = new AiProviderHttpException("Anthropic", 400, "Bad Request", null, CreditBalanceError());
        var without = new AiProviderHttpException("Anthropic", 400, "Bad Request");

        Assert.Equal("Anthropic call failed: HTTP 400 Bad Request.", withError.Message);
        Assert.Equal(without.Message, withError.Message);
    }

    [Fact]
    public void Message_NeverContainsTheProviderText()
    {
        var ex = new AiProviderHttpException("Anthropic", 400, "Bad Request", null, CreditBalanceError());

        Assert.DoesNotContain("credit balance", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("req_011CTexample", ex.Message);
        Assert.DoesNotContain("invalid_request_error", ex.Message);
    }

    [Fact]
    public void ProviderErrorClassAndRetryAfter_RoundTrip()
    {
        var providerError = CreditBalanceError(TimeSpan.FromSeconds(12));

        var ex = new AiProviderHttpException("Anthropic", 400, "Bad Request", TimeSpan.FromSeconds(5), providerError);

        Assert.Same(providerError, ex.ProviderError);
        Assert.Equal(AiProviderErrorClass.QuotaExhausted, ex.ErrorClass);
        Assert.Equal(400, ex.StatusCode);
        Assert.Equal(TimeSpan.FromSeconds(5), ex.RetryAfter);
    }

    [Theory]
    [InlineData(401, AiProviderErrorClass.Auth)]
    [InlineData(402, AiProviderErrorClass.QuotaExhausted)]
    [InlineData(400, AiProviderErrorClass.InvalidRequest)]
    [InlineData(429, AiProviderErrorClass.RateLimited)]
    [InlineData(529, AiProviderErrorClass.Overloaded)]
    [InlineData(502, AiProviderErrorClass.ServerError)]
    public void ErrorClass_WithoutAProviderError_IsDerivedFromTheStatus(int status, AiProviderErrorClass expected)
    {
        var ex = new AiProviderHttpException("Anthropic", status, "reason");

        Assert.Null(ex.ProviderError);
        Assert.Equal(expected, ex.ErrorClass);
    }

    [Fact]
    public void ProviderErrorClass_WinsOverTheStatus()
    {
        // Anthropic reports "credit balance is too low" as HTTP 400.
        var ex = new AiProviderHttpException("Anthropic", 400, "Bad Request", null, CreditBalanceError());

        Assert.Equal(AiProviderErrorClass.QuotaExhausted, ex.ErrorClass);
    }
}
