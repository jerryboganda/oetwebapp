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
///
/// <para>
/// Speaking module additions (OET Speaking plan P1.3): three Speaking-only
/// codes are registered here so the AiGatewayService routes them through
/// the per-feature DB override path. Defaults are seeded by
/// <see cref="OetLearner.Api.Services.Seeding.SpeakingAiRouteSeed"/>.
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

/// <summary>
/// Speaking-module feature codes that are NOT in the canonical
/// <see cref="AiFeatureCodes"/> set on <c>Domain/AiEntities.cs</c>.
/// These are kept local to the Rulebook namespace because the Speaking
/// module ships its own AI features (scoring v2, patient-turn LLM, card
/// drafting) that are gated behind their own DB-routed override rows.
/// <para>
/// Pinned by the unit tests in <c>RolePlayCardProfessionFilterTests</c>
/// and the seeder <see cref="OetLearner.Api.Services.Seeding.SpeakingAiRouteSeed"/>.
/// </para>
/// </summary>
public static class SpeakingAiFeatureCodes
{
    /// <summary>OET Speaking dual-grader v2 — scoring-critical.
    /// Default provider/model: Anthropic <c>claude-sonnet-4-6</c> with prompt
    /// caching on; fallback OpenAI <c>gpt-4o</c>.</summary>
    public const string SpeakingScoreV2 = "speaking.score.v2";

    /// <summary>Per-turn AI patient LLM used during the AI role-play loop.
    /// Cheap, low-latency. Default: Anthropic <c>claude-haiku-4-5</c> with
    /// prompt caching on; fallback OpenAI <c>gpt-4o-mini</c>.</summary>
    public const string SpeakingPatientTurnV1 = "speaking.patient.turn.v1";

    /// <summary>Admin AI-draft tool for new role-play cards. Default:
    /// Anthropic <c>claude-sonnet-4-6</c> with prompt caching on.</summary>
    public const string CardDraftV1 = "card.draft.v1";

    /// <summary>All Speaking-module feature codes registered by this file.
    /// Iterate this set when seeding or validating allowlists.</summary>
    public static readonly IReadOnlyList<string> All = new[]
    {
        SpeakingScoreV2,
        SpeakingPatientTurnV1,
        CardDraftV1,
    };
}

/// <summary>
/// Default provider routes for route-backed AI features. Acts as
/// the static fallback list for <see cref="AiFeatureRouteResolver"/> when
/// no DB row exists for a given feature code.
/// </summary>
public static class AiFeatureRouteDefaults
{
    public static readonly IReadOnlyList<SpeakingAiRouteDefault> Defaults = new[]
    {
        new SpeakingAiRouteDefault(
            FeatureCode: SpeakingAiFeatureCodes.SpeakingScoreV2,
            PrimaryProviderCode: "anthropic",
            PrimaryModel: "claude-sonnet-4-6",
            FallbackProviderCode: "openai",
            FallbackModel: "gpt-4o",
            PromptCachingEnabled: true,
            Description: "Speaking dual-grader v2 (scoring-critical)."),
        new SpeakingAiRouteDefault(
            FeatureCode: SpeakingAiFeatureCodes.SpeakingPatientTurnV1,
            PrimaryProviderCode: "anthropic",
            PrimaryModel: "claude-haiku-4-5",
            FallbackProviderCode: "openai",
            FallbackModel: "gpt-4o-mini",
            PromptCachingEnabled: true,
            Description: "AI patient per-turn LLM (cheap, low-latency)."),
        new SpeakingAiRouteDefault(
            FeatureCode: SpeakingAiFeatureCodes.CardDraftV1,
            PrimaryProviderCode: "anthropic",
            PrimaryModel: "claude-sonnet-4-6",
            FallbackProviderCode: null,
            FallbackModel: null,
            PromptCachingEnabled: true,
            Description: "Admin role-play card AI draft tool."),
        new SpeakingAiRouteDefault(
            FeatureCode: AiFeatureCodes.ConversationOpening,
            PrimaryProviderCode: "anthropic",
            PrimaryModel: "claude-sonnet-4-6",
            FallbackProviderCode: "openai",
            FallbackModel: "gpt-4o",
            PromptCachingEnabled: true,
            Description: "Conversation opening turn (scenario-setting quality)."),
        new SpeakingAiRouteDefault(
            FeatureCode: AiFeatureCodes.ConversationReply,
            PrimaryProviderCode: "anthropic",
            PrimaryModel: "claude-haiku-4-5",
            FallbackProviderCode: "openai",
            FallbackModel: "gpt-4o-mini",
            PromptCachingEnabled: true,
            Description: "Conversation live reply turn (cheap, low-latency)."),
        new SpeakingAiRouteDefault(
            FeatureCode: AiFeatureCodes.ConversationEvaluation,
            PrimaryProviderCode: "anthropic",
            PrimaryModel: "claude-sonnet-4-6",
            FallbackProviderCode: "openai",
            FallbackModel: "gpt-4o",
            PromptCachingEnabled: true,
            Description: "Conversation rubric evaluation (scoring-critical)."),
    };
}

public static class SpeakingAiRouteDefaults
{
    public static readonly IReadOnlyList<SpeakingAiRouteDefault> Defaults = AiFeatureRouteDefaults.Defaults
        .Where(d => SpeakingAiFeatureCodes.All.Contains(d.FeatureCode, StringComparer.OrdinalIgnoreCase))
        .ToList();
}

/// <summary>
/// Immutable description of the default routing for a single Speaking
/// AI feature. Read by the seeder + admin UI to pre-populate the
/// route editor.
/// </summary>
public sealed record SpeakingAiRouteDefault(
    string FeatureCode,
    string PrimaryProviderCode,
    string PrimaryModel,
    string? FallbackProviderCode,
    string? FallbackModel,
    bool PromptCachingEnabled,
    string Description);

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
        AiFeatureCodes.AdminReadingDraft,
        AiFeatureCodes.AdminWritingDraft,
        AiFeatureCodes.AiAssistantAdmin,
        AiFeatureCodes.AiAssistantExpert,
        AiFeatureCodes.AiAssistantLearner,
        // Speaking module (OET Speaking plan P1.3 — see SpeakingAiFeatureCodes).
        SpeakingAiFeatureCodes.SpeakingScoreV2,
        SpeakingAiFeatureCodes.SpeakingPatientTurnV1,
        SpeakingAiFeatureCodes.CardDraftV1,
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
        if (row is not null)
        {
            return new AiFeatureRouteResolution(row.ProviderCode, row.Model);
        }

        // Static fallback: if no DB row exists, return the known route default
        // default so the gateway has a route even before the seeder has run
        // (CI tests, fresh DBs, etc.). Other feature codes keep the existing
        // null behaviour — the gateway falls through to the global default
        // provider.
        var staticDefault = AiFeatureRouteDefaults.Defaults
            .FirstOrDefault(d => string.Equals(d.FeatureCode, featureCode, StringComparison.OrdinalIgnoreCase));
        if (staticDefault is not null)
        {
            return new AiFeatureRouteResolution(staticDefault.PrimaryProviderCode, staticDefault.PrimaryModel);
        }

        return null;
    }

    public bool IsKnownFeatureCode(string featureCode) =>
        !string.IsNullOrWhiteSpace(featureCode)
        && KnownFeatureCodes.Contains(featureCode, StringComparer.OrdinalIgnoreCase);
}
