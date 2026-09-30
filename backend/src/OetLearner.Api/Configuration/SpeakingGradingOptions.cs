namespace OetLearner.Api.Configuration;

/// <summary>
/// Owner directive 2026-09-30: Speaking grading (<c>speaking.grade</c>) runs first on the
/// Claude Max subscription sidecar and falls back to the default route (today Anthropic
/// API) when that pinned call fails. Bound from <c>Speaking:Grading</c>
/// (env <c>Speaking__Grading__PinnedProviderCode</c> / <c>Speaking__Grading__PinnedModel</c>).
///
/// An empty <see cref="PinnedProviderCode"/> switches the feature off: every grade is
/// one unpinned gateway call, exactly the behaviour before this option existed.
/// </summary>
public sealed class SpeakingGradingOptions
{
    public const string SectionName = "Speaking:Grading";

    /// <summary>Registry code of the provider row to try first (for example <c>writing-claude-sub</c>). Empty = off.</summary>
    public string PinnedProviderCode { get; set; } = string.Empty;

    /// <summary>Model to request from the pinned provider. Empty = the provider row's default model.</summary>
    public string PinnedModel { get; set; } = string.Empty;
}
