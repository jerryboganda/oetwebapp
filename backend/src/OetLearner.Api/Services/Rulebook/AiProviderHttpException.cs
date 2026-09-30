namespace OetLearner.Api.Services.Rulebook;

/// <summary>
/// Thrown when a model provider returned a non-success HTTP status. Callers
/// classify retry vs terminal from <see cref="StatusCode"/>; the message never
/// includes the raw provider body (it can echo learner content or credentials)
/// and stays byte-identical because AiRetryPolicy, the gateway classifier and the
/// Copilot provider parse it. What could be learned from the body lives in
/// <see cref="ProviderError"/>: allow-listed tokens plus a redacted single-line
/// text of at most 300 characters. That text may reach one structured server log
/// line and nothing else: never <see cref="Exception.Message"/>, a database row
/// or a client response.
/// </summary>
public sealed class AiProviderHttpException : InvalidOperationException
{
    public AiProviderHttpException(
        string provider,
        int statusCode,
        string? reasonPhrase,
        TimeSpan? retryAfter = null,
        AiProviderError? providerError = null)
        : base(AiProviderErrorMessages.HttpFailure(provider, statusCode, reasonPhrase))
    {
        StatusCode = statusCode;
        RetryAfter = retryAfter;
        ProviderError = providerError;
    }

    public int StatusCode { get; }
    public TimeSpan? RetryAfter { get; }

    /// <summary>Parsed provider error, when the adapter captured one.</summary>
    public AiProviderError? ProviderError { get; }

    /// <summary>The parsed class, or the class implied by <see cref="StatusCode"/> alone.</summary>
    public AiProviderErrorClass ErrorClass
        => ProviderError?.Class ?? AiProviderErrorParser.ClassifyStatus(StatusCode);
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
