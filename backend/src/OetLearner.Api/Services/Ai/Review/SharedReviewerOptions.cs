namespace OetLearner.Api.Services.Ai.Review;

/// <summary>
/// Central policy for the shared secondary-reviewer capacity used by BOTH Writing and Speaking.
/// Bound from <c>Reviewer:Shared</c> (appsettings / environment, e.g. <c>Reviewer__Shared__MaxAttempts</c>)
/// at startup; <see cref="CODEX_REVIEWER_MAX_CONCURRENCY"/> is honoured as a flat environment override of
/// <see cref="MaxConcurrency"/> (owner directive: the limit must be visible in one place).
/// </summary>
public sealed class SharedReviewerOptions
{
    public const string SectionName = "Reviewer:Shared";

    /// <summary>Flat env var that overrides <see cref="MaxConcurrency"/> (applied at startup).</summary>
    public const string MaxConcurrencyEnvVar = "CODEX_REVIEWER_MAX_CONCURRENCY";

    /// <summary>Maximum simultaneous Codex reviewer jobs across Writing AND Speaking (per process).
    /// Owner directive 2026-10-10: three Codex accounts = three serial sidecar lanes, so the gate matches
    /// them. The hard bound is still each sidecar's own lane (one CLI run per account).</summary>
    public int MaxConcurrency { get; set; } = 3;

    /// <summary>How long a job may wait for a Codex slot before the API fallback takes it. Never blocks until a quota reset.</summary>
    public int MaxQueueWaitSeconds { get; set; } = 45;

    /// <summary>Attempts on the Codex route before the API fallback (per review pass).</summary>
    public int CodexAttempts { get; set; } = 2;

    /// <summary>Wall-clock budget of one Codex attempt.</summary>
    public int CodexAttemptSeconds { get; set; } = 300;

    /// <summary>Total budget of the Codex phase for one review (queue wait excluded). Kept so the
    /// worst case (queue wait + Codex phase + API fallback) stays inside the Speaking inline
    /// assessment ceiling and the Writing letter claim lease.</summary>
    public int CodexBudgetSeconds { get; set; } = 480;

    /// <summary>Seconds to wait between transient Codex retries.</summary>
    public int CodexRetryDelaySeconds { get; set; } = 10;

    /// <summary>Attempts on the fallback provider once Codex has exhausted its policy.</summary>
    public int ApiAttempts { get; set; } = 1;

    /// <summary>Wall-clock budget of one fallback attempt.</summary>
    public int ApiAttemptSeconds { get; set; } = 240;

    /// <summary>Fallback provider route pinned for the same review prompt. Reuse: the repo's existing Anthropic API row.</summary>
    public string ApiFallbackProvider { get; set; } = "anthropic";

    /// <summary>Model for the fallback route.</summary>
    public string ApiFallbackModel { get; set; } = "claude-opus-5-5";

    /// <summary>
    /// The startup-configured instance both reviewers use. Tests construct their own instances and pass them
    /// to the gate/runner explicitly; this one is mutated only during service configuration.
    /// </summary>
    public static SharedReviewerOptions Current { get; } = new();
}
