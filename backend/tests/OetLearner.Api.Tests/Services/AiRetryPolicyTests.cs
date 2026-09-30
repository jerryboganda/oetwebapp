using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text.Json;
using OetLearner.Api.Services.Ai;
using OetLearner.Api.Services.Rulebook;

namespace OetLearner.Api.Tests.Services;

public sealed class AiRetryPolicyTests
{
    [Fact]
    public void Classify_401_IsQuarantine()
    {
        var result = AiRetryPolicy.Classify(
            new HttpRequestException("denied", null, HttpStatusCode.Unauthorized),
            requestLikelySent: false);

        Assert.Equal(AiRetryDisposition.Quarantine, result.Disposition);
        Assert.Equal(0, result.MaxRetries);
        Assert.False(result.AllowLocalRepair);
    }

    [Fact]
    public void Classify_403_IsQuarantine()
    {
        var result = AiRetryPolicy.Classify(
            new HttpRequestException("forbidden", null, HttpStatusCode.Forbidden),
            requestLikelySent: true);

        Assert.Equal(AiRetryDisposition.Quarantine, result.Disposition);
    }

    [Fact]
    public void Classify_402_IsQuarantine()
    {
        var result = AiRetryPolicy.Classify(
            new InvalidOperationException("AI provider call failed: HTTP 402 payment required."),
            requestLikelySent: false);

        Assert.Equal(AiRetryDisposition.Quarantine, result.Disposition);
    }

    [Fact]
    public void Classify_InvalidModel_IsQuarantine()
    {
        var result = AiRetryPolicy.Classify(
            new InvalidOperationException("invalid_model: claude-unknown"),
            requestLikelySent: false);

        Assert.Equal(AiRetryDisposition.Quarantine, result.Disposition);
    }

    [Fact]
    public void Classify_429_IsRetry()
    {
        var result = AiRetryPolicy.Classify(
            new HttpRequestException("rate limited", null, HttpStatusCode.TooManyRequests),
            requestLikelySent: true);

        Assert.Equal(AiRetryDisposition.Retry, result.Disposition);
        Assert.Equal(AiRetryPolicy.MaxProviderRetries, result.MaxRetries);
        Assert.False(result.AllowLocalRepair);
    }

    [Fact]
    public void Classify_5xx_IsRetry()
    {
        var result = AiRetryPolicy.Classify(
            new InvalidOperationException("AI provider call failed: HTTP 503 unavailable."),
            requestLikelySent: true);

        Assert.Equal(AiRetryDisposition.Retry, result.Disposition);
        Assert.Equal(3, result.MaxRetries);
    }

    [Fact]
    public void Classify_PreSendConnection_IsRetry()
    {
        var result = AiRetryPolicy.Classify(
            new HttpRequestException("connection refused", new SocketException()),
            requestLikelySent: false);

        Assert.Equal(AiRetryDisposition.Retry, result.Disposition);
        Assert.Equal(AiRetryPolicy.MaxProviderRetries, result.MaxRetries);
    }

    [Fact]
    public void Classify_PostSendTimeout_IsIndeterminate()
    {
        var result = AiRetryPolicy.Classify(new TimeoutException("provider hung"), requestLikelySent: true);

        Assert.Equal(AiRetryDisposition.Indeterminate, result.Disposition);
        Assert.Equal(0, result.MaxRetries);
    }

    [Fact]
    public void Classify_SchemaInvalid_AllowsOneLocalRepairRetry()
    {
        var result = AiRetryPolicy.Classify(
            new JsonException("schema_invalid: missing criterionScores"),
            requestLikelySent: true);

        Assert.Equal(AiRetryDisposition.Retry, result.Disposition);
        Assert.True(result.AllowLocalRepair);
        Assert.Equal(AiRetryPolicy.MaxSchemaRepairRetries, result.MaxRetries);
    }

    [Fact]
    public void Classify_OtherClientError_IsTerminal()
    {
        var result = AiRetryPolicy.Classify(
            new HttpRequestException("bad request", null, HttpStatusCode.BadRequest),
            requestLikelySent: true);

        Assert.Equal(AiRetryDisposition.Terminal, result.Disposition);
    }

    private static AiProviderHttpException Typed(
        int status,
        AiProviderErrorClass errorClass,
        TimeSpan? retryAfter = null,
        TimeSpan? headerRetryAfter = null)
        => new(
            "Anthropic",
            status,
            "reason",
            headerRetryAfter,
            new AiProviderError(errorClass, status, null, null, null, null, retryAfter));

    [Theory]
    [InlineData(400)]
    [InlineData(429)]
    public void Classify_TypedQuotaExhausted_IsQuarantineWithNoRetries(int status)
    {
        // Anthropic reports "credit balance is too low" as 400, the sidecar reports exhausted quota as 429.
        var result = AiRetryPolicy.Classify(Typed(status, AiProviderErrorClass.QuotaExhausted), requestLikelySent: true);

        Assert.Equal(AiRetryDisposition.Quarantine, result.Disposition);
        Assert.Equal(0, result.MaxRetries);
        Assert.False(result.AllowLocalRepair);
    }

    [Fact]
    public void Classify_TypedAuth_IsQuarantine()
    {
        var result = AiRetryPolicy.Classify(Typed(400, AiProviderErrorClass.Auth), requestLikelySent: true);

        Assert.Equal(AiRetryDisposition.Quarantine, result.Disposition);
        Assert.Equal(0, result.MaxRetries);
    }

    [Fact]
    public void Classify_TypedQuotaExhausted_InsideAWrapperException_IsStillQuarantine()
    {
        var wrapped = new InvalidOperationException("wrapper", Typed(400, AiProviderErrorClass.QuotaExhausted));

        Assert.Equal(AiRetryDisposition.Quarantine, AiRetryPolicy.Classify(wrapped, requestLikelySent: true).Disposition);
    }

    [Fact]
    public void Classify_TypedOverloaded529_IsRetry()
    {
        var result = AiRetryPolicy.Classify(Typed(529, AiProviderErrorClass.Overloaded), requestLikelySent: true);

        Assert.Equal(AiRetryDisposition.Retry, result.Disposition);
        Assert.Equal(AiRetryPolicy.MaxProviderRetries, result.MaxRetries);
    }

    [Fact]
    public void Classify_TypedInvalidRequest400_IsTerminal()
    {
        var result = AiRetryPolicy.Classify(Typed(400, AiProviderErrorClass.InvalidRequest), requestLikelySent: true);

        Assert.Equal(AiRetryDisposition.Terminal, result.Disposition);
    }

    [Fact]
    public void Classify_TypedRateLimited_HonoursTheProviderRetryAfter()
    {
        var result = AiRetryPolicy.Classify(
            Typed(429, AiProviderErrorClass.RateLimited, headerRetryAfter: TimeSpan.FromSeconds(6)),
            requestLikelySent: true);

        Assert.Equal(AiRetryDisposition.Retry, result.Disposition);
        Assert.NotNull(result.SuggestedDelay);
        Assert.True(result.SuggestedDelay >= TimeSpan.FromSeconds(6));
    }

    [Fact]
    public void Classify_TypedRateLimited_FallsBackToTheParsedProviderErrorRetryAfter()
    {
        // Gemini reports the wait in RetryInfo, not in a header.
        var result = AiRetryPolicy.Classify(
            Typed(429, AiProviderErrorClass.RateLimited, retryAfter: TimeSpan.FromSeconds(9)),
            requestLikelySent: true);

        Assert.True(result.SuggestedDelay >= TimeSpan.FromSeconds(9));
    }

    [Fact]
    public void Classify_RetryAfterBeyondTheCap_IsCapped()
    {
        var result = AiRetryPolicy.Classify(
            Typed(429, AiProviderErrorClass.RateLimited, headerRetryAfter: TimeSpan.FromHours(2)),
            requestLikelySent: true);

        // The cap plus at most the deterministic jitter (5% at the first attempt).
        Assert.True(result.SuggestedDelay >= AiRetryPolicy.MaxHonouredRetryAfter);
        Assert.True(result.SuggestedDelay < AiRetryPolicy.MaxHonouredRetryAfter * 1.1);
    }

    [Fact]
    public void ComputeRetryDelay_HonoursRetryAfterAndAddsJitter()
    {
        var delay = AiRetryPolicy.ComputeRetryDelay(1, TimeSpan.FromSeconds(4), jitterFactor: 0.25);
        Assert.True(delay >= TimeSpan.FromSeconds(4));
        Assert.True(delay < TimeSpan.FromSeconds(6));
    }

    [Fact]
    public void ComputeRetryDelay_ExponentialBackoffIsDeterministic()
    {
        var first = AiRetryPolicy.ComputeRetryDelay(1, retryAfter: null, jitterFactor: 0.25);
        var second = AiRetryPolicy.ComputeRetryDelay(2, retryAfter: null, jitterFactor: 0.25);
        Assert.Equal(first, AiRetryPolicy.ComputeRetryDelay(1, retryAfter: null, jitterFactor: 0.25));
        Assert.True(second > first);
    }
}
