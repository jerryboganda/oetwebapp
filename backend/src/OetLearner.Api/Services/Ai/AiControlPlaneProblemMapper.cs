using OetLearner.Api.Services.Rulebook;

namespace OetLearner.Api.Services.Ai;

/// <summary>
/// One mapped problem response for an AI control-plane exception. Mirrors the
/// existing top-level exception-handler payload shape in <c>Program.cs</c>
/// (<c>code</c> / <c>message</c> / <c>retryable</c> / <c>correlationId</c>) so
/// clients need no new parsing.
/// </summary>
public sealed record AiControlPlaneProblem(
    int StatusCode,
    string Code,
    string Message,
    bool Retryable,
    int? RetryAfterSeconds = null,
    string? OperationId = null,
    string? State = null);

/// <summary>
/// W2 of the AI cost/reliability remediation (incident INC-2026-CLAUDE-01) —
/// maps the control-plane exception vocabulary onto HTTP.
///
/// <para>
/// Without this, every one of these outcomes fell through to the generic
/// handler and surfaced as <c>500 internal_server_error</c>. That is untrue
/// (nothing crashed), unactionable (the client cannot tell "already done" from
/// "try again in a moment"), and it hides a deliberate refusal behind a
/// server-fault alert.
/// </para>
///
/// <para>
/// Extracted from the <c>Program.cs</c> handler lambda purely so it is unit
/// testable — the handler itself is a top-level statement and cannot be
/// invoked directly. <c>AiControlPlaneProblemMappingTests</c> pins both the
/// mapping table and the fact that <c>Program.cs</c> still calls it.
/// </para>
/// </summary>
public static class AiControlPlaneProblemMapper
{
    /// <summary>Advertised backoff for a call that lost the race to a still
    /// in-flight twin. Matches the coordinator's bounded ~4s wait: by the time
    /// the client comes back, the winner has almost always finished.</summary>
    public const int InFlightRetryAfterSeconds = 5;

    /// <summary>Returns null when <paramref name="exception"/> is not an AI
    /// control-plane exception, so the caller falls through to its existing
    /// branches unchanged.</summary>
    public static AiControlPlaneProblem? TryMap(Exception? exception) => exception switch
    {
        // The idempotency key is bound to a DIFFERENT request. Retrying the
        // same payload cannot resolve it — the caller must change the action
        // (e.g. bump its resource version), so this is NOT retryable.
        AiOperationConflictException conflict => new AiControlPlaneProblem(
            StatusCodes.Status409Conflict,
            conflict.ErrorCode,
            "This AI request conflicts with a different request already registered for the same resource. Reload and try again with the latest version.",
            Retryable: false),

        // The work really happened; W2 stores a durable pointer, not the
        // completion body. Telling the caller "409, here is the operation" is
        // the only truthful answer that does not buy a second provider call.
        AiOperationDuplicateResultUnavailableException duplicate => new AiControlPlaneProblem(
            StatusCodes.Status409Conflict,
            duplicate.ErrorCode,
            "This AI request was already completed. Reload the result instead of running it again.",
            Retryable: false,
            OperationId: duplicate.OperationId,
            State: duplicate.State.ToString()),

        // Another caller owns the identical action right now. Genuinely
        // retryable, and 409 keeps it in the same family as the existing
        // concurrency_conflict branch rather than implying the server is down.
        AiOperationInFlightException inFlight => new AiControlPlaneProblem(
            StatusCodes.Status409Conflict,
            inFlight.ErrorCode,
            "The same AI request is already running. Please try again in a moment.",
            Retryable: true,
            RetryAfterSeconds: InFlightRetryAfterSeconds),

        // Fail-closed policy refusal. 503 is truthful: the capability is
        // switched off, not the request malformed. Only the sanitized machine
        // reason is echoed — never the exception message, feature internals or
        // any provider body.
        AiFeaturePolicyRefusedException refused => new AiControlPlaneProblem(
            StatusCodes.Status503ServiceUnavailable,
            "ai_feature_policy_refused",
            $"This AI feature is currently unavailable ({Sanitize(refused.Reason)}). Please try again later.",
            Retryable: false),

        _ => null,
    };

    /// <summary>Defence in depth: the registry only ever produces short
    /// snake_case machine reasons, but nothing that reaches a client body
    /// should be able to carry free-form text.</summary>
    private static string Sanitize(string? reason)
    {
        if (string.IsNullOrWhiteSpace(reason)) return "policy_unknown";

        var span = reason.Trim();
        if (span.Length > 48) span = span[..48];

        return string.Concat(span.Select(c =>
            char.IsAsciiLetterOrDigit(c) || c is '_' or '-' ? c : '_'));
    }
}
