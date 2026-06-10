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
            PrimaryModel: "claude-sonnet-4-6",
            FallbackProviderCode: "openai",
            FallbackModel: "gpt-4o",
            PromptCachingEnabled: true,
            Description: "AI patient per-turn LLM (Claude Sonnet 4.6 — contextual understanding)."),
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
            PrimaryModel: "claude-sonnet-4-6",
            FallbackProviderCode: "openai",
            FallbackModel: "gpt-4o",
            PromptCachingEnabled: true,
            Description: "Conversation live reply turn (Claude Sonnet 4.6 — contextual understanding)."),
        new SpeakingAiRouteDefault(
            FeatureCode: AiFeatureCodes.ConversationEvaluation,
            PrimaryProviderCode: "anthropic",
            PrimaryModel: "claude-sonnet-4-6",
            FallbackProviderCode: "openai",
            FallbackModel: "gpt-4o",
            PromptCachingEnabled: true,
            Description: "Conversation rubric evaluation (scoring-critical)."),
        new SpeakingAiRouteDefault(
            FeatureCode: AiFeatureCodes.PronunciationLinguisticScore,
            PrimaryProviderCode: "gemini-pronunciation-audio",
            PrimaryModel: "gemini-3.5-flash",
            FallbackProviderCode: "azure-phoneme",
            FallbackModel: null,
            PromptCachingEnabled: false,
            Description: "Gemini native-audio pronunciation linguistic scoring."),

        // ── Live Class Recording AI Pipeline (Wave A2) ─────────────
        new SpeakingAiRouteDefault(
            FeatureCode: AiFeatureCodes.ClassRecordingTranscribe,
            PrimaryProviderCode: "openai",
            PrimaryModel: "whisper-1",
            FallbackProviderCode: null,
            FallbackModel: null,
            PromptCachingEnabled: false,
            Description: "Class recording audio-to-text (Whisper Large-v3)."),
        new SpeakingAiRouteDefault(
            FeatureCode: AiFeatureCodes.ClassRecordingSummarize,
            PrimaryProviderCode: "anthropic",
            PrimaryModel: "claude-sonnet-4-6",
            FallbackProviderCode: "openai",
            FallbackModel: "gpt-4o",
            PromptCachingEnabled: true,
            Description: "Class recording AI summary, chapters, action items, keyTopics."),
        new SpeakingAiRouteDefault(
            FeatureCode: AiFeatureCodes.ClassRecordingTranslate,
            PrimaryProviderCode: "anthropic",
            PrimaryModel: "claude-sonnet-4-6",
            FallbackProviderCode: "openai",
            FallbackModel: "gpt-4o",
            PromptCachingEnabled: true,
            Description: "Class recording summary EN→AR translation."),
        new SpeakingAiRouteDefault(
            FeatureCode: AiFeatureCodes.ClassAssistantQna,
            PrimaryProviderCode: "anthropic",
            PrimaryModel: "claude-sonnet-4-6",
            FallbackProviderCode: "openai",
            FallbackModel: "gpt-4o",
            PromptCachingEnabled: true,
            Description: "'Ask AI about this class' transcript RAG Q&A (Claude Sonnet 4.6)."),
        new SpeakingAiRouteDefault(
            FeatureCode: AiFeatureCodes.TutorRecommendation,
            PrimaryProviderCode: "anthropic",
            PrimaryModel: "claude-sonnet-4-6",
            FallbackProviderCode: "openai",
            FallbackModel: "gpt-4o",
            PromptCachingEnabled: true,
            Description: "Post-attendance next-class recommendation (Claude Sonnet 4.6)."),

        // ── Universal Claude Sonnet 4.6 contextual-understanding defaults ────
        // Every remaining text-LLM feature defaults to anthropic/claude-sonnet-4-6
        // (locked product decision). Admins still override per feature via the
        // DB-backed AiFeatureRoutes (which win over these static defaults), and
        // the resolver's key-guard falls through to the keyed top provider when
        // no Anthropic key is configured — so non-Anthropic deployments are
        // untouched. Exclusions kept on their own providers: pronunciation
        // linguistic scoring (Gemini native audio), class-recording transcribe
        // (Whisper STT), and writing-exemplar embeddings.
        SonnetDefault(AiFeatureCodes.WritingGrade, "Writing submission grading."),
        SonnetDefault(AiFeatureCodes.WritingSampleScore, "Writing sample/exemplar scoring."),
        SonnetDefault(AiFeatureCodes.WritingCoachSuggest, "Writing coach live suggestions."),
        SonnetDefault(AiFeatureCodes.WritingCoachExplain, "Writing coach explanations."),
        SonnetDefault(AiFeatureCodes.WritingCoachV1, "Writing module V2 coach."),
        SonnetDefault(AiFeatureCodes.WritingRewriteV1, "Writing rewrite assistant."),
        SonnetDefault(AiFeatureCodes.WritingScenarioGenerateV1, "Writing scenario generation."),
        SonnetDefault(AiFeatureCodes.WritingAppealV1, "Writing appeal second opinion."),
        SonnetDefault(AiFeatureCodes.WritingCanonDetectV1, "Writing canon detection."),
        SonnetDefault(AiFeatureCodes.WritingDrillGradeV1, "Writing drill grading."),
        SonnetDefault(AiFeatureCodes.WritingOutlineV1, "Writing outline generation."),
        SonnetDefault(AiFeatureCodes.WritingParaphraseV1, "Writing paraphrase tool."),
        SonnetDefault(AiFeatureCodes.WritingAskV1, "Writing ask/clarify tool."),
        SonnetDefault(AiFeatureCodes.SpeakingGrade, "Speaking role-play grading."),
        SonnetDefault(AiFeatureCodes.MockFullGrade, "Full mock grading."),
        SonnetDefault(AiFeatureCodes.MockRemediationDraft, "Mock remediation plan draft."),
        SonnetDefault(AiFeatureCodes.PronunciationTip, "Pronunciation tip generation."),
        SonnetDefault(AiFeatureCodes.PronunciationScore, "Pronunciation scoring (text)."),
        SonnetDefault(AiFeatureCodes.PronunciationFeedback, "Pronunciation corrective feedback."),
        SonnetDefault(AiFeatureCodes.ReadingExplanation, "Reading question explanations."),
        SonnetDefault(AiFeatureCodes.ReadingVocabularyCard, "Reading vocabulary cards."),
        SonnetDefault(AiFeatureCodes.SummarisePassage, "Passage summarisation."),
        SonnetDefault(AiFeatureCodes.VocabularyGloss, "On-demand vocabulary glossing."),
        SonnetDefault(AiFeatureCodes.RecallsMistakeExplain, "Recalls mistake explanation."),
        SonnetDefault(AiFeatureCodes.RecallsRevisionPlan, "Recalls revision plan."),
        SonnetDefault(AiFeatureCodes.AdminContentGeneration, "Admin content generation draft."),
        SonnetDefault(AiFeatureCodes.AdminGrammarDraft, "Admin grammar rule draft."),
        SonnetDefault(AiFeatureCodes.AdminPronunciationDraft, "Admin pronunciation rule draft."),
        SonnetDefault(AiFeatureCodes.AdminVocabularyDraft, "Admin vocabulary term draft."),
        SonnetDefault(AiFeatureCodes.AdminConversationDraft, "Admin conversation scenario draft."),
        SonnetDefault(AiFeatureCodes.AdminListeningDraft, "Admin listening question draft."),
        SonnetDefault(AiFeatureCodes.AdminReadingDraft, "Admin reading passage draft."),
        SonnetDefault(AiFeatureCodes.AdminWritingDraft, "Admin writing task draft."),
    };

    /// <summary>Builds a default route entry pinned to the universal
    /// contextual-understanding model (Anthropic Claude Sonnet 4.6) with an
    /// OpenAI gpt-4o fallback and prompt caching on.</summary>
    private static SpeakingAiRouteDefault SonnetDefault(string featureCode, string description) =>
        new(featureCode, "anthropic", "claude-sonnet-4-6", "openai", "gpt-4o", true, description);
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
        AiFeatureCodes.PronunciationLinguisticScore,
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
        // Live Class Recording AI Pipeline (Wave A2).
        AiFeatureCodes.ClassRecordingTranscribe,
        AiFeatureCodes.ClassRecordingSummarize,
        AiFeatureCodes.ClassRecordingTranslate,
        AiFeatureCodes.ClassAssistantQna,
        AiFeatureCodes.TutorRecommendation,
        // Reading explanations / vocabulary cards — route to Claude Sonnet 4.6.
        AiFeatureCodes.ReadingExplanation,
        AiFeatureCodes.ReadingVocabularyCard,
        // Writing module V2 coaching tools (text LLM; embeddings excluded).
        AiFeatureCodes.WritingCoachV1,
        AiFeatureCodes.WritingRewriteV1,
        AiFeatureCodes.WritingScenarioGenerateV1,
        AiFeatureCodes.WritingAppealV1,
        AiFeatureCodes.WritingCanonDetectV1,
        AiFeatureCodes.WritingDrillGradeV1,
        AiFeatureCodes.WritingOutlineV1,
        AiFeatureCodes.WritingParaphraseV1,
        AiFeatureCodes.WritingAskV1,
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
        var canonicalFeatureCode = CanonicalFeatureCode(featureCode);
        if (canonicalFeatureCode is null) return null;
        var row = await db.AiFeatureRoutes.AsNoTracking()
            .FirstOrDefaultAsync(r => r.FeatureCode == canonicalFeatureCode && r.IsActive, ct);
        if (row is not null)
        {
            return new AiFeatureRouteResolution(row.ProviderCode, row.Model);
        }

        // Static fallback: if no DB row exists, return the known route default
        // so the gateway has a route even before the seeder has run (CI tests,
        // fresh DBs, etc.). Other feature codes keep the existing null
        // behaviour — the gateway falls through to the global default provider.
        //
        // KEY-GUARD: a static default is only honoured when its provider is
        // actually usable (row exists, active, and carries a key — directly or
        // via the failover account pool). Otherwise we try the entry's declared
        // fallback provider, and finally return null so the gateway falls
        // through to its highest-priority keyed provider. This is what keeps a
        // keyless seeded `anthropic` row (added by CoreAiProviderSeeder) from
        // breaking live grading on deployments that have not configured an
        // Anthropic key — the exact pre-seeder behaviour is preserved there.
        var staticDefault = AiFeatureRouteDefaults.Defaults
            .FirstOrDefault(d => string.Equals(d.FeatureCode, canonicalFeatureCode, StringComparison.OrdinalIgnoreCase));
        if (staticDefault is not null)
        {
            if (await IsProviderUsableAsync(staticDefault.PrimaryProviderCode, ct))
                return new AiFeatureRouteResolution(staticDefault.PrimaryProviderCode, staticDefault.PrimaryModel);

            if (!string.IsNullOrWhiteSpace(staticDefault.FallbackProviderCode)
                && await IsProviderUsableAsync(staticDefault.FallbackProviderCode!, ct))
                return new AiFeatureRouteResolution(staticDefault.FallbackProviderCode!, staticDefault.FallbackModel);

            return null;
        }

        return null;
    }

    /// <summary>True when the provider code resolves to an active row that
    /// carries a usable key — either directly on the row or on an active row in
    /// its multi-account failover pool. Used by the static-default key-guard so
    /// a keyless seeded row never short-circuits the gateway's keyed-provider
    /// fallthrough.</summary>
    private async Task<bool> IsProviderUsableAsync(string providerCode, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(providerCode)) return false;
        var provider = await db.AiProviders.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Code == providerCode && p.IsActive, ct);
        if (provider is null) return false;
        if (!string.IsNullOrEmpty(provider.EncryptedApiKey)) return true;
        return await db.AiProviderAccounts.AsNoTracking()
            .AnyAsync(a => a.ProviderId == provider.Id
                           && a.IsActive
                           && a.EncryptedApiKey != null
                           && a.EncryptedApiKey != "", ct);
    }

    public bool IsKnownFeatureCode(string featureCode) =>
        CanonicalFeatureCode(featureCode) is not null;

    public static string? CanonicalFeatureCode(string? featureCode) =>
        string.IsNullOrWhiteSpace(featureCode)
            ? null
            : KnownFeatureCodes.FirstOrDefault(code =>
                string.Equals(code, featureCode.Trim(), StringComparison.OrdinalIgnoreCase));
}
