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
    /// Default provider/model: Anthropic <c>claude-sonnet-5</c> with prompt
    /// caching on; fallback OpenAI <c>gpt-4o</c>.</summary>
    public const string SpeakingScoreV2 = "speaking.score.v2";

    /// <summary>Per-turn AI patient LLM used during the AI role-play loop.
    /// Cheap, low-latency. Default: Anthropic <c>claude-sonnet-5</c> with
    /// prompt caching on; fallback OpenAI <c>gpt-4o-mini</c>.</summary>
    public const string SpeakingPatientTurnV1 = "speaking.patient.turn.v1";

    /// <summary>Admin AI-draft tool for new role-play cards. Default:
    /// Anthropic <c>claude-sonnet-5</c> with prompt caching on.</summary>
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
            PrimaryModel: "claude-sonnet-5",
            FallbackProviderCode: "openai",
            FallbackModel: "gpt-4o",
            PromptCachingEnabled: true,
            Description: "Speaking dual-grader v2 (scoring-critical)."),
        new SpeakingAiRouteDefault(
            FeatureCode: SpeakingAiFeatureCodes.SpeakingPatientTurnV1,
            PrimaryProviderCode: "anthropic",
            PrimaryModel: HaikuModel,
            FallbackProviderCode: "openai",
            FallbackModel: "gpt-4o-mini",
            PromptCachingEnabled: true,
            // Cost-tiering (owner directive 2026-08-28 AI/Cloud API plan, point
            // 7): high-frequency, non-scoring, real-time conversational turns —
            // exactly the "simple Q&A" cheap-tier candidate the plan names.
            // README.md already documented this as Haiku; this restores the
            // code to match after 20260716090000_UpgradeAiModelsToSonnet5
            // collapsed it onto the premium model project-wide.
            Description: "AI patient per-turn LLM (cheap tier — Claude Haiku)."),
        new SpeakingAiRouteDefault(
            FeatureCode: SpeakingAiFeatureCodes.CardDraftV1,
            PrimaryProviderCode: "anthropic",
            PrimaryModel: HaikuModel,
            FallbackProviderCode: "openai",
            FallbackModel: "gpt-4o-mini",
            PromptCachingEnabled: true,
            Description: "Admin role-play card AI draft tool (cheap tier — admin/background generation)."),
        new SpeakingAiRouteDefault(
            FeatureCode: AiFeatureCodes.ConversationOpening,
            PrimaryProviderCode: "anthropic",
            PrimaryModel: "claude-sonnet-5",
            FallbackProviderCode: "openai",
            FallbackModel: "gpt-4o",
            PromptCachingEnabled: true,
            Description: "Conversation opening turn (scenario-setting quality)."),
        new SpeakingAiRouteDefault(
            FeatureCode: AiFeatureCodes.ConversationReply,
            PrimaryProviderCode: "anthropic",
            PrimaryModel: "claude-sonnet-5",
            FallbackProviderCode: "openai",
            FallbackModel: "gpt-4o",
            PromptCachingEnabled: true,
            Description: "Conversation live reply turn (Claude Sonnet 4.6 — contextual understanding)."),
        new SpeakingAiRouteDefault(
            FeatureCode: AiFeatureCodes.ConversationEvaluation,
            PrimaryProviderCode: "anthropic",
            PrimaryModel: "claude-sonnet-5",
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
            PrimaryModel: HaikuModel,
            FallbackProviderCode: "openai",
            FallbackModel: "gpt-4o-mini",
            PromptCachingEnabled: true,
            Description: "Class recording AI summary, chapters, action items, keyTopics (cheap tier — summarisation)."),
        new SpeakingAiRouteDefault(
            FeatureCode: AiFeatureCodes.ClassRecordingTranslate,
            PrimaryProviderCode: "anthropic",
            PrimaryModel: HaikuModel,
            FallbackProviderCode: "openai",
            FallbackModel: "gpt-4o-mini",
            PromptCachingEnabled: true,
            Description: "Class recording summary EN→AR translation (cheap tier)."),
        new SpeakingAiRouteDefault(
            FeatureCode: AiFeatureCodes.ClassAssistantQna,
            PrimaryProviderCode: "anthropic",
            PrimaryModel: HaikuModel,
            FallbackProviderCode: "openai",
            FallbackModel: "gpt-4o-mini",
            PromptCachingEnabled: true,
            Description: "'Ask AI about this class' transcript RAG Q&A (cheap tier — simple Q&A)."),
        new SpeakingAiRouteDefault(
            FeatureCode: AiFeatureCodes.TutorRecommendation,
            PrimaryProviderCode: "anthropic",
            PrimaryModel: HaikuModel,
            FallbackProviderCode: "openai",
            FallbackModel: "gpt-4o-mini",
            PromptCachingEnabled: true,
            Description: "Post-attendance next-class recommendation (cheap tier)."),

        // ── Premium tier: Claude Sonnet 5 ────────────────────────────────────
        // Owner directive 2026-08-28 AI/Cloud API plan, point 7: "Keep the
        // higher-quality Claude model for: writing official grading, speaking
        // official assessment, and appeals/quality-sensitive assessment."
        // Admins still override per feature via the DB-backed AiFeatureRoutes
        // (which win over these static defaults), and the resolver's key-guard
        // falls through to the keyed top provider when no Anthropic key is
        // configured — so non-Anthropic deployments are untouched.
        SonnetDefault(AiFeatureCodes.WritingGrade, "Writing submission grading — official."),
        SonnetDefault(AiFeatureCodes.WritingSampleScore, "Writing sample/exemplar scoring."),
        SonnetDefault(AiFeatureCodes.WritingRewriteV1, "Writing rewrite assistant — grading-adjacent quality."),
        SonnetDefault(AiFeatureCodes.WritingScenarioGenerateV1, "Writing scenario generation — grading-adjacent quality."),
        SonnetDefault(AiFeatureCodes.WritingAppealV1, "Writing appeal second opinion — quality-sensitive per owner directive."),
        SonnetDefault(AiFeatureCodes.SpeakingGrade, "Speaking role-play grading — official."),
        SonnetDefault(AiFeatureCodes.MockFullGrade, "Full mock grading — official."),
        SonnetDefault(AiFeatureCodes.PronunciationScore, "Pronunciation scoring (text) — feeds a score."),

        // ── Cheap tier: Claude Haiku ──────────────────────────────────────────
        // "For cheaper/simpler tasks such as Reading explanations, Listening
        // explanations, vocabulary, simple Q&A, summaries, and admin/background
        // generation, please use a cheaper model where quality testing confirms
        // it is good enough." Also restores the pre-20260716090000_
        // UpgradeAiModelsToSonnet5 tiering WritingPromptTemplateRegistrar.cs
        // already documents (coach / canon-detect / drill-grade / outline /
        // paraphrase / ask), which that migration collapsed onto one model.
        HaikuDefault(AiFeatureCodes.WritingCoachSuggest, "Writing coach live suggestions."),
        HaikuDefault(AiFeatureCodes.WritingCoachExplain, "Writing coach explanations."),
        HaikuDefault(AiFeatureCodes.WritingCoachV1, "Writing module V2 coach."),
        HaikuDefault(AiFeatureCodes.WritingCanonDetectV1, "Writing canon detection."),
        HaikuDefault(AiFeatureCodes.WritingDrillGradeV1, "Writing practice-drill grading (non-official)."),
        HaikuDefault(AiFeatureCodes.WritingOutlineV1, "Writing outline generation."),
        HaikuDefault(AiFeatureCodes.WritingParaphraseV1, "Writing paraphrase tool."),
        HaikuDefault(AiFeatureCodes.WritingAskV1, "Writing ask/clarify tool."),
        HaikuDefault(AiFeatureCodes.MockRemediationDraft, "Mock remediation plan draft — non-scoring personalisation."),
        HaikuDefault(AiFeatureCodes.PronunciationTip, "Pronunciation tip generation — non-scoring."),
        HaikuDefault(AiFeatureCodes.PronunciationFeedback, "Pronunciation corrective feedback — non-scoring."),
        HaikuDefault(AiFeatureCodes.ReadingExplanation, "Reading question explanations."),
        HaikuDefault(AiFeatureCodes.ReadingVocabularyCard, "Reading vocabulary cards."),
        HaikuDefault(AiFeatureCodes.ListeningExplanation, "Listening question explanations."),
        HaikuDefault(AiFeatureCodes.SummarisePassage, "Passage summarisation."),
        HaikuDefault(AiFeatureCodes.VocabularyGloss, "On-demand vocabulary glossing."),
        HaikuDefault(AiFeatureCodes.RecallsMistakeExplain, "Recalls mistake explanation."),
        HaikuDefault(AiFeatureCodes.RecallsRevisionPlan, "Recalls revision plan."),
        HaikuDefault(AiFeatureCodes.AdminContentGeneration, "Admin content generation draft."),
        HaikuDefault(AiFeatureCodes.AdminGrammarDraft, "Admin grammar rule draft."),
        HaikuDefault(AiFeatureCodes.AdminPronunciationDraft, "Admin pronunciation rule draft."),
        HaikuDefault(AiFeatureCodes.AdminVocabularyDraft, "Admin vocabulary term draft."),
        HaikuDefault(AiFeatureCodes.AdminConversationDraft, "Admin conversation scenario draft."),
        HaikuDefault(AiFeatureCodes.AdminListeningDraft, "Admin listening question draft."),
        HaikuDefault(AiFeatureCodes.AdminReadingDraft, "Admin reading passage draft."),
        HaikuDefault(AiFeatureCodes.AdminWritingDraft, "Admin writing task draft."),
    };

    /// <summary>Current cheap-tier Anthropic model — see
    /// <c>AGENTS.md</c>/environment model-id reference. Single source of truth
    /// so every cheap-tier route below agrees.</summary>
    private const string HaikuModel = "claude-haiku-4-5-20251001";

    /// <summary>Builds a default route entry pinned to the premium
    /// contextual-understanding model (Anthropic Claude Sonnet 5) with an
    /// OpenAI gpt-4o fallback and prompt caching on. Reserved for official
    /// grading, official assessment, and appeals/quality-sensitive review.</summary>
    private static SpeakingAiRouteDefault SonnetDefault(string featureCode, string description) =>
        new(featureCode, "anthropic", "claude-sonnet-5", "openai", "gpt-4o", true, description);

    /// <summary>Builds a default route entry pinned to the cheap tier
    /// (Anthropic Claude Haiku) with a cheap OpenAI gpt-4o-mini fallback and
    /// prompt caching on. For explanations, vocabulary, simple Q&A, summaries,
    /// and admin/background generation — never for scoring-critical calls.</summary>
    private static SpeakingAiRouteDefault HaikuDefault(string featureCode, string description) =>
        new(featureCode, "anthropic", HaikuModel, "openai", "gpt-4o-mini", true, description);
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
        AiFeatureCodes.ListeningExplanation,
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
