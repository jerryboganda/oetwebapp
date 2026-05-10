using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services.Rulebook;

/// <summary>
/// Phase 7 — per-feature provider routing. The gateway's existing logic
/// uses an explicit <c>request.Provider</c> first, then falls back to the
/// active highest-priority <see cref="AiProvider"/>. This resolver inserts
/// a third source between those two: a DB-backed override keyed by
/// <c>featureCode</c>, written via <c>/v1/admin/ai/feature-routes</c>.
///
/// <para>
/// The resolver returns <c>null</c> when no active route exists, signalling
/// to the gateway that it should keep the existing fallback behaviour.
/// This keeps Phase 7 strictly additive — features without an override row
/// behave exactly as they did before the phase landed.
/// </para>
/// </summary>
public interface IAiFeatureRouteResolver
{
    Task<AiFeatureRouteResolution?> ResolveAsync(string featureCode, CancellationToken ct);

    /// <summary>Convenience — returns whether the feature code is allowed
    /// to be platform-routed. Used by the admin upsert endpoint to refuse
    /// routes that would put a platform-only feature on a BYOK-only
    /// provider.</summary>
    bool IsKnownFeatureCode(string featureCode);
}

public sealed record AiFeatureRouteResolution(string ProviderCode, string? Model);

public sealed class AiFeatureRouteResolver(LearnerDbContext db) : IAiFeatureRouteResolver
{
    /// <summary>Feature codes recognised by the routing UI. Anything outside
    /// this set is rejected at upsert time so we don't accumulate dead
    /// routes for codes nobody emits.</summary>
    public static readonly IReadOnlyList<string> KnownFeatureCodes = new[]
    {
        AiFeatureCodes.WritingGrade,
        AiFeatureCodes.WritingSampleScore,
        AiFeatureCodes.WritingCoachSuggest,
        AiFeatureCodes.WritingCoachExplain,
        AiFeatureCodes.SpeakingGrade,
        AiFeatureCodes.MockFullGrade,
        AiFeatureCodes.MockRemediationDraft,
        AiFeatureCodes.ConversationOpening,
        AiFeatureCodes.ConversationReply,
        AiFeatureCodes.ConversationEvaluation,
        AiFeatureCodes.PronunciationTip,
        AiFeatureCodes.PronunciationScore,
        AiFeatureCodes.PronunciationFeedback,
        AiFeatureCodes.SummarisePassage,
        AiFeatureCodes.VocabularyGloss,
        AiFeatureCodes.RecallsMistakeExplain,
        AiFeatureCodes.RecallsRevisionPlan,
        AiFeatureCodes.AdminContentGeneration,
        AiFeatureCodes.AdminGrammarDraft,
        AiFeatureCodes.AdminPronunciationDraft,
        AiFeatureCodes.AdminVocabularyDraft,
        AiFeatureCodes.AdminConversationDraft,
        AiFeatureCodes.AdminListeningDraft,
        AiFeatureCodes.AdminWritingDraft,
    };

    /// <summary>Subset of <see cref="KnownFeatureCodes"/> the bulk-route
    /// action targets when admin presses "Route all to Copilot". Per PRD
    /// Phase 7 — explicit list, no wildcards. Keep in sync with
    /// <c>docs/AI-COPILOT-PROGRESS.md</c>.</summary>
    public static readonly IReadOnlyList<string> CopilotBulkRouteTargets = new[]
    {
        AiFeatureCodes.VocabularyGloss,
        AiFeatureCodes.RecallsMistakeExplain,
        AiFeatureCodes.RecallsRevisionPlan,
        AiFeatureCodes.ConversationOpening,
        AiFeatureCodes.ConversationReply,
        AiFeatureCodes.WritingCoachSuggest,
        AiFeatureCodes.WritingCoachExplain,
        AiFeatureCodes.SummarisePassage,
    };

    public async Task<AiFeatureRouteResolution?> ResolveAsync(string featureCode, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(featureCode)) return null;
        var row = await db.AiFeatureRoutes.AsNoTracking()
            .FirstOrDefaultAsync(r => r.FeatureCode == featureCode && r.IsActive, ct);
        if (row is null) return null;
        return new AiFeatureRouteResolution(row.ProviderCode, row.Model);
    }

    public bool IsKnownFeatureCode(string featureCode) =>
        !string.IsNullOrWhiteSpace(featureCode)
        && KnownFeatureCodes.Contains(featureCode, StringComparer.OrdinalIgnoreCase);
}
