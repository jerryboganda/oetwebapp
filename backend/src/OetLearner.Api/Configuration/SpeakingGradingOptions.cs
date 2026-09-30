namespace OetLearner.Api.Configuration;

/// <summary>
/// Owner directive 2026-09-30: Speaking grading (<c>speaking.grade</c>) runs first on the
/// Claude Max subscription sidecar and falls back to the default route (today Anthropic
/// API) when that pinned call fails. Bound from <c>Speaking:Grading</c>
/// (env <c>Speaking__Grading__PinnedProviderCode</c> / <c>Speaking__Grading__PinnedModel</c> /
/// <c>Speaking__Grading__PinnedTimeoutSeconds</c>).
///
/// An empty <see cref="PinnedProviderCode"/> switches the feature off: every grade is
/// one unpinned gateway call, exactly the behaviour before this option existed.
/// </summary>
public sealed class SpeakingGradingOptions
{
    public const string SectionName = "Speaking:Grading";

    /// <summary>Largest <see cref="PinnedTimeoutSeconds"/> the chain honours; a bigger value is clamped to it.</summary>
    public const int MaxPinnedTimeoutSeconds = 1500;

    /// <summary>Registry code of the provider row to try first (for example <c>writing-claude-sub</c>). Empty = off.</summary>
    public string PinnedProviderCode { get; set; } = string.Empty;

    /// <summary>Model to request from the pinned provider. Empty = the provider row's default model.</summary>
    public string PinnedModel { get; set; } = string.Empty;

    /// <summary>
    /// Wall-clock budget, in seconds, for the WHOLE pinned (level 1) call: the sidecar's queue wait,
    /// the CLI run and the gateway's own provider retries. On expiry the pinned call is cancelled and
    /// grading falls back to the default route. It exists because the Anthropic adapter's HttpClient
    /// allows 30 minutes per attempt, the sidecar has no queue timeout, and the operation lease is
    /// 30 minutes while level 2 (maximum reasoning) needs about 12 of them. Keep it above the
    /// sidecar's own 300 s CLI timeout so a slow but valid grade is not discarded. 0 or less = no cap;
    /// values above <see cref="MaxPinnedTimeoutSeconds"/> are clamped to it.
    /// </summary>
    public int PinnedTimeoutSeconds { get; set; } = 900;
}
