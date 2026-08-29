using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text.Json;
using OetLearner.Api.Services.Ai;

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
