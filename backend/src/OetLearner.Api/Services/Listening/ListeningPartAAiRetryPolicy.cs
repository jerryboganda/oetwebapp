namespace OetLearner.Api.Services.Listening;

// ═════════════════════════════════════════════════════════════════════════════
// Listening Part A AI advisory review — terminal-state vocabulary and bounded
// attempt scheduling (W0, incident INC-2026-CLAUDE-01).
//
// Split out of ListeningPartAAiScoringService so the scorer stays under the
// repo's 500-line file rule and so the classification rules are unit-testable
// on their own. Nothing here talks to the network or the database.
// ═════════════════════════════════════════════════════════════════════════════

/// <summary>
/// Values persisted to <c>ListeningAnswer.AiSkipReason</c>. A non-null skip
/// reason is <b>terminal</b>: the worker never re-selects the row, no further
/// provider call is made for it, and <c>AiScoredAt</c> stays null because a skip
/// is not an AI score. Deterministic marking is untouched in every case.
/// </summary>
public static class ListeningPartAAiSkipReasons
{
    /// <summary>No effective, author-approved rationale exists for the gap, so
    /// there is nothing to ground an advisory verdict on. Zero provider calls.</summary>
    public const string NoEvidence = "skipped_no_evidence";

    /// <summary>The provider answered with a locally valid payload that carried
    /// no usable verdict for this gap. Terminal on purpose: replaying identical
    /// evidence would buy the same unusable answer a second time.</summary>
    public const string NoMatchingVerdicts = "no_matching_verdicts";

    /// <summary>Provider rejected the credential (HTTP 401/402/403). Terminal by
    /// design: auto-retrying against a dead key is the loop that caused
    /// INC-2026-CLAUDE-01 and would burn the attempt cap the moment a key
    /// expires. The work is preserved, not lost — after an admin activates and
    /// canaries a replacement credential, the explicitly confirmed
    /// <c>scripts/ops/requeue-listening-credential-quarantined.sql</c> hands
    /// these answers back to the worker.</summary>
    public const string CredentialQuarantined = "credential_quarantined";

    /// <summary>Provider rejected the request itself (invalid model, invalid
    /// configuration, malformed request). Retrying the same payload cannot
    /// succeed, so no retry is scheduled.</summary>
    public const string ProviderRejected = "provider_rejected";

    /// <summary>The request was sent but the outcome is unknown (client timeout,
    /// lost 2xx body, or a connect/TLS failure the runtime cannot prove happened
    /// before the request left the process). Terminal so an ambiguous,
    /// possibly-billed call is never auto-repeated.</summary>
    public const string IndeterminateTimeout = "indeterminate_timeout";

    /// <summary>All durable attempts were spent on transient failures.</summary>
    public const string RetriesExhausted = "retries_exhausted";
}

/// <summary>
/// Bounded attempt scheduling for the Part A advisory scorer: at most
/// <see cref="MaxAttempts"/> scheduled attempts per answer, honouring
/// <c>Retry-After</c> when the provider supplies it and using jittered
/// exponential backoff otherwise.
/// </summary>
public static class ListeningPartAAiRetryPolicy
{
    /// <summary>Hard cap on the number of provider attempts SCHEDULED for one
    /// answer. It bounds the retry loop that caused INC-2026-CLAUDE-01; it is
    /// not, on its own, a global at-most-three-physical-calls guarantee, because
    /// the worker still runs in both API slots and two slots can read the same
    /// pre-increment <c>AiAttemptCount</c>. Cross-slot exactly-once is delivered
    /// by the W4 coordinator's database leasing (<c>SKIP LOCKED</c>).</summary>
    public const int MaxAttempts = 3;

    /// <summary>Cool-off applied when the platform Anthropic credential is not
    /// configured. Deliberately <b>not</b> terminal and it does not consume an
    /// attempt: nothing left the process, and an admin can still add or rotate
    /// the key. It only stops the 20 s re-selection spin.</summary>
    public static readonly TimeSpan UnconfiguredProviderCooldown = TimeSpan.FromMinutes(15);

    private static readonly TimeSpan BaseDelay = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan MaxBackoff = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan MaxHonouredRetryAfter = TimeSpan.FromHours(6);

    /// <summary>
    /// Map a provider HTTP status onto a terminal skip reason, or null when the
    /// failure is transient and may be retried within <see cref="MaxAttempts"/>.
    /// </summary>
    public static string? TerminalSkipReasonForStatus(int statusCode) => statusCode switch
    {
        // Auth / payment / permission: quarantine, never auto-retry.
        401 or 402 or 403 => ListeningPartAAiSkipReasons.CredentialQuarantined,
        // Explicitly transient.
        408 or 409 or 425 or 429 => null,
        >= 500 => null,
        // Any other 4xx is a request/model/configuration defect: replaying the
        // same payload cannot succeed, so it must not be retried.
        >= 400 => ListeningPartAAiSkipReasons.ProviderRejected,
        _ => null,
    };

    /// <summary>
    /// Delay before the next attempt. <paramref name="attemptNumber"/> is the
    /// 1-based number of the attempt that just failed.
    /// </summary>
    public static TimeSpan NextDelay(int attemptNumber, TimeSpan? retryAfter)
    {
        var exponent = Math.Clamp(attemptNumber - 1, 0, 6);
        var scaled = TimeSpan.FromTicks(BaseDelay.Ticks * (1L << exponent));
        if (scaled > MaxBackoff) scaled = MaxBackoff;

        // ±20% jitter so both API slots never wake on the same instant.
        var jitterFactor = 0.8 + (Random.Shared.NextDouble() * 0.4);
        var jittered = TimeSpan.FromTicks((long)(scaled.Ticks * jitterFactor));

        if (retryAfter is not { } advertised || advertised <= jittered) return jittered;
        return advertised > MaxHonouredRetryAfter ? MaxHonouredRetryAfter : advertised;
    }
}
