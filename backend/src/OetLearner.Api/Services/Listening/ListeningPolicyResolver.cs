using OetLearner.Api.Domain;

namespace OetLearner.Api.Services.Listening;

/// <summary>
/// Effective per-Listening-attempt policy view. Resolves the source of truth
/// in this order: per-user override → singleton ListeningPolicy row →
/// <see cref="ListeningPolicyDefaults"/>. Pure value record — never queries
/// the DB itself; consumers pass already-loaded entities in.
/// </summary>
public sealed record EffectiveListeningPolicy(
    int PreviewMsA1, int PreviewMsA2, int PreviewMsC1, int PreviewMsC2,
    int ReviewMsA1, int ReviewMsA2, int ReviewMsC1,
    int ReviewMsC2FinalCbt, int ReviewMsC2FinalPaper,
    int BetweenSectionTransitionMs, int PartBQuestionWindowMs,
    int ConfirmTokenTtlMs, int TechReadinessTtlMs,
    int FinalReviewAllPartsMsPaper,
    bool OneWayLocksEnabled, bool ConfirmDialogRequired,
    bool UnansweredWarningRequired,
    bool HighlightingEnabledPartA, bool HighlightingEnabledPartBC,
    bool OptionStrikethroughEnabled, bool InAppZoomEnabled,
    bool BrowserZoomAllowed, bool AnnotationsPersistOnAdvance,
    bool TechReadinessRequired,
    int ExtraTimePct, bool AccessibilityModeEnabled);

public static class ListeningPolicyResolver
{
    public static EffectiveListeningPolicy Resolve(
        ListeningPolicy? policy,
        ListeningUserPolicyOverride? userOverride)
    {
        // Per-row helpers fall back to per-Part defaults when the column is null.
        int I(int? v, int fallback) => v ?? fallback;
        bool B(bool? v, bool fallback) => v ?? fallback;

        return new EffectiveListeningPolicy(
            PreviewMsA1: I(policy?.PreviewWindowMsA1, ListeningPolicyDefaults.PreviewMsA1),
            PreviewMsA2: I(policy?.PreviewWindowMsA2, ListeningPolicyDefaults.PreviewMsA2),
            PreviewMsC1: I(policy?.PreviewWindowMsC1, ListeningPolicyDefaults.PreviewMsC1),
            PreviewMsC2: I(policy?.PreviewWindowMsC2, ListeningPolicyDefaults.PreviewMsC2),
            ReviewMsA1: I(policy?.ReviewWindowMsA1, ListeningPolicyDefaults.ReviewMsA1),
            ReviewMsA2: I(policy?.ReviewWindowMsA2, ListeningPolicyDefaults.ReviewMsA2),
            ReviewMsC1: I(policy?.ReviewWindowMsC1, ListeningPolicyDefaults.ReviewMsC1),
            ReviewMsC2FinalCbt: I(policy?.ReviewWindowMsC2FinalCbt, ListeningPolicyDefaults.ReviewMsC2FinalCbt),
            ReviewMsC2FinalPaper: I(policy?.ReviewWindowMsC2FinalPaper, ListeningPolicyDefaults.ReviewMsC2FinalPaper),
            BetweenSectionTransitionMs: I(policy?.BetweenSectionTransitionMs, ListeningPolicyDefaults.BetweenSectionTransitionMs),
            PartBQuestionWindowMs: I(policy?.PartBQuestionWindowMs, ListeningPolicyDefaults.PartBQuestionWindowMs),
            ConfirmTokenTtlMs: I(policy?.ConfirmTokenTtlMs, ListeningPolicyDefaults.ConfirmTokenTtlMs),
            TechReadinessTtlMs: I(policy?.TechReadinessTtlMs, ListeningPolicyDefaults.TechReadinessTtlMs),
            FinalReviewAllPartsMsPaper: I(policy?.FinalReviewAllPartsMsPaper, ListeningPolicyDefaults.FinalReviewAllPartsMsPaper),
            OneWayLocksEnabled: B(policy?.OneWayLocksEnabled, true),
            ConfirmDialogRequired: B(policy?.ConfirmDialogRequired, true),
            UnansweredWarningRequired: B(policy?.UnansweredWarningRequired, true),
            HighlightingEnabledPartA: B(policy?.HighlightingEnabledPartA, false),
            HighlightingEnabledPartBC: B(policy?.HighlightingEnabledPartBC, true),
            OptionStrikethroughEnabled: B(policy?.OptionStrikethroughEnabled, true),
            InAppZoomEnabled: B(policy?.InAppZoomEnabled, true),
            BrowserZoomAllowed: true,
            AnnotationsPersistOnAdvance: B(policy?.AnnotationsPersistOnAdvance, true),
            TechReadinessRequired: B(policy?.TechReadinessRequired, true),
            ExtraTimePct: userOverride?.ExtraTimeEntitlementPct ?? policy?.DefaultExtraTimePct ?? 0,
            AccessibilityModeEnabled: userOverride?.AccessibilityModeEnabled ?? false);
    }
}
