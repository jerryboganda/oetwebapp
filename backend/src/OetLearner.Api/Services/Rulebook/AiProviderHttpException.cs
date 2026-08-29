namespace OetLearner.Api.Services.Rulebook;

/// <summary>
/// Thrown when a model provider returned a non-success HTTP status. Callers
/// classify retry vs terminal from <see cref="StatusCode"/>; the message never
/// includes the raw provider body (it can echo learner content or credentials).
/// </summary>
public sealed class AiProviderHttpException : InvalidOperationException
{
    public AiProviderHttpException(string provider, int statusCode, string? reasonPhrase, TimeSpan? retryAfter = null)
        : base(AiProviderErrorMessages.HttpFailure(provider, statusCode, reasonPhrase))
    {
        StatusCode = statusCode;
        RetryAfter = retryAfter;
    }

    public int StatusCode { get; }
    public TimeSpan? RetryAfter { get; }
}

/// <summary>
/// Thrown when the provider answered 2xx but the response body could not be
/// read. The call may already have been billed, so callers must treat this as
/// terminal and never auto-retry.
/// </summary>
public sealed class AiProviderBodyReadException : InvalidOperationException
{
    public AiProviderBodyReadException(string provider, Exception inner)
        : base($"{provider} 2xx response body could not be read ({inner.GetType().Name}).", inner)
    {
    }
}
