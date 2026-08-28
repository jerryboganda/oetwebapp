using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using OetLearner.Api.Data;

#nullable disable

namespace OetLearner.Api.Data.Migrations
{
    /// <summary>
    /// W2 of the AI cost/reliability remediation (incident INC-2026-CLAUDE-01),
    /// following W1 (20261102090000_AddAiControlPlaneCore /
    /// 20261103090000_ExtendAiUsageRecordProvenance).
    ///
    /// Creates two brand-new tables:
    /// <list type="bullet">
    ///   <item><c>AiModelPrices</c> — effective-dated provider/model rate card
    ///   (<c>Domain/AiPricingEntities.cs</c>), unique on
    ///   (ProviderId, Model, EffectiveFrom). Seeded with the current Anthropic
    ///   Claude Sonnet 5 rate (matching the existing flat rate already
    ///   configured on the <c>AiProviders</c> row via
    ///   <c>CoreAiProviderSeeder</c>: 0.003/0.015 USD per 1k input/output)
    ///   plus Anthropic's documented prompt-caching write/read multipliers,
    ///   and a public-list OpenAI gpt-4o rate for parity — both tagged
    ///   <c>PricingVersion = '2026-11-01'</c>. Cache rates are sourced from
    ///   Anthropic's Messages API prompt-caching documentation (write ≈1.25×
    ///   base input token price; read ≈0.1× base input token price) since no
    ///   repo-configured cache rate exists yet; OpenAI's cached-input
    ///   discount (50% of input) is public list pricing, not a repo value —
    ///   both are explicit, sourced, non-zero seeds, not placeholders.</item>
    ///   <item><c>AiFeaturePolicies</c> — versioned feature policy registry
    ///   (<c>Domain/AiFeaturePolicyEntities.cs</c>), unique on
    ///   (FeatureCode, PolicyVersion). Seeded with PolicyVersion 1 for every
    ///   constant in <c>AiFeatureCodes</c> (except <c>Unclassified</c>, which
    ///   is deliberately never a registered feature) plus the three
    ///   Speaking-module codes in <c>AiFeatureRouteResolver.SpeakingAiFeatureCodes</c>
    ///   — 62 rows total. Module/OperationClass/RequiresGrounding values here
    ///   are literal snapshots of the same derivation rules implemented in
    ///   <c>Services/Rulebook/AiFeaturePolicyRegistry.cs</c>'s
    ///   <c>AiFeaturePolicyDefaults</c> (module = feature code's first
    ///   dot-segment; OperationClass by admin./class./tutor./grade/score
    ///   heuristics with two explicit overrides; RequiresGrounding = false
    ///   only for the direct, non-gateway OCR/STT/Listening-Part-A/B/C call
    ///   codes) — this migration is the historical row-for-row match of that
    ///   logic at authoring time, not a live reference to it, per the repo's
    ///   "migrations never reference mutable Domain/Service code" convention.</item>
    /// </list>
    ///
    /// <para>
    /// Both tables are populated via <c>INSERT ... SELECT ... FROM (VALUES …)
    /// WHERE NOT EXISTS (...)</c> so re-running this migration (or seeding an
    /// already-migrated database by hand) is a no-op — no duplicate rows, no
    /// overwritten manual edits. <c>EffectiveFrom</c> is stamped
    /// <c>2020-01-01T00:00:00Z</c> (epoch-style, matching the
    /// <c>AssessmentGovernanceSeeder</c> convention) so every historical and
    /// future call resolves deterministically to these rows until an admin
    /// surface supersedes them with a higher <c>PolicyVersion</c> /
    /// newer <c>EffectiveFrom</c>.
    /// </para>
    ///
    /// <para>
    /// Strictly additive: two brand-new tables, zero existing tables touched.
    /// Hand-authored per repo convention: inline <c>[Migration]</c>/
    /// <c>[DbContext]</c>, no Designer file, ModelSnapshot deliberately
    /// untouched. Ordered after 20261103090000 and before the
    /// already-committed 20261128090000_HardenAiCreditSourceValidity — no
    /// schema overlap with that migration (it only touches
    /// AiPackageCreditLots/AiPackageCreditTransactions).
    /// </para>
    /// </summary>
    [DbContext(typeof(LearnerDbContext))]
    [Migration("20261104090000_AddAiModelPricingAndFeaturePolicy")]
    public partial class AddAiModelPricingAndFeaturePolicy : Migration
    {
        private const string SeedFeaturePoliciesSql = @"
INSERT INTO ""AiFeaturePolicies"" (
    ""Id"", ""FeatureCode"", ""Module"", ""OperationClass"", ""IsActive"", ""PolicyVersion"",
    ""RequiresGrounding"", ""CacheDimensions"", ""EffectiveFrom"", ""EffectiveTo"",
    ""CreatedBy"", ""CreatedAt"", ""UpdatedAt""
)
SELECT
    v.id, v.feature_code, v.module, v.operation_class, true, 1,
    v.requires_grounding, NULL,
    TIMESTAMPTZ '2020-01-01T00:00:00Z', NULL,
    'system:w2-seed', TIMESTAMPTZ '2026-11-04T09:00:00Z', TIMESTAMPTZ '2026-11-04T09:00:00Z'
FROM (VALUES
    ('aifp_writing_grade', 'writing.grade', 'writing', 0, true),
    ('aifp_writing_sample_score', 'writing.sample_score', 'writing', 0, true),
    ('aifp_speaking_grade', 'speaking.grade', 'speaking', 0, true),
    ('aifp_mock_full_grade', 'mock.full_grade', 'mock', 0, true),
    ('aifp_writing_coach_suggest', 'writing.coach.suggest', 'writing', 1, true),
    ('aifp_writing_coach_explain', 'writing.coach.explain', 'writing', 1, true),
    ('aifp_writing_coach_v1', 'writing.coach.v1', 'writing', 1, true),
    ('aifp_writing_rewrite_v1', 'writing.rewrite.v1', 'writing', 1, true),
    ('aifp_writing_scenario_generate_v1', 'writing.scenario.generate.v1', 'writing', 1, true),
    ('aifp_writing_exemplar_embed_v1', 'writing.exemplar.embed.v1', 'writing', 1, true),
    -- OperationClass 0 (ScoringCritical), not the generic grade/score heuristic's
    -- 1 (InteractiveLearning): an appeal is a quality-sensitive second-opinion
    -- review of an official grade (owner directive 2026-08-28 AI/Cloud API
    -- plan, point 7) and must not be treated as an ordinary interactive-
    -- learning call — see AiFeaturePolicyDefaults.ScoringOverrides.
    ('aifp_writing_appeal_v1', 'writing.appeal.v1', 'writing', 0, true),
    ('aifp_writing_canon_detect_v1', 'writing.canon.detect.v1', 'writing', 1, true),
    ('aifp_writing_drill_grade_v1', 'writing.drill.grade.v1', 'writing', 0, true),
    ('aifp_writing_outline_v1', 'writing.outline.v1', 'writing', 1, true),
    ('aifp_writing_paraphrase_v1', 'writing.paraphrase.v1', 'writing', 1, true),
    ('aifp_writing_ask_v1', 'writing.ask.v1', 'writing', 1, true),
    ('aifp_conversation_reply', 'conversation.reply', 'conversation', 1, true),
    ('aifp_conversation_opening', 'conversation.opening', 'conversation', 1, true),
    ('aifp_pronunciation_tip', 'pronunciation.tip', 'pronunciation', 1, true),
    ('aifp_summarise_passage', 'summarise.passage', 'summarise', 1, true),
    ('aifp_vocabulary_gloss', 'vocabulary.gloss', 'vocabulary', 1, true),
    ('aifp_recalls_mistake_explain', 'recalls.mistake_explain', 'recalls', 1, true),
    ('aifp_recalls_revision_plan', 'recalls.revision_plan', 'recalls', 1, true),
    ('aifp_mock_remediation_draft', 'mock.remediation_draft', 'mock', 1, true),
    ('aifp_pronunciation_score', 'pronunciation.score', 'pronunciation', 0, true),
    ('aifp_pronunciation_linguistic_score_v1', 'pronunciation.linguistic.score.v1', 'pronunciation', 0, true),
    ('aifp_pronunciation_feedback', 'pronunciation.feedback', 'pronunciation', 1, true),
    ('aifp_conversation_evaluation', 'conversation.evaluation', 'conversation', 0, true),
    ('aifp_admin_content_generation', 'admin.content_generation', 'admin', 2, true),
    ('aifp_admin_grammar_draft', 'admin.grammar_draft', 'admin', 2, true),
    ('aifp_admin_pronunciation_draft', 'admin.pronunciation_draft', 'admin', 2, true),
    ('aifp_admin_vocabulary_draft', 'admin.vocabulary_draft', 'admin', 2, true),
    ('aifp_admin_conversation_draft', 'admin.conversation_draft', 'admin', 2, true),
    ('aifp_admin_listening_draft', 'admin.listening_draft', 'admin', 2, true),
    ('aifp_admin_reading_draft', 'admin.reading_draft', 'admin', 2, true),
    ('aifp_admin_writing_draft', 'admin.writing_draft', 'admin', 2, true),
    ('aifp_admin_listening_skill_tag', 'admin.listening.skill_tag', 'admin', 2, true),
    ('aifp_admin_listening_transcript_segment', 'admin.listening.transcript_segment', 'admin', 2, true),
    ('aifp_ai_assistant_admin', 'ai_assistant.admin', 'ai_assistant', 1, true),
    ('aifp_ai_assistant_expert', 'ai_assistant.expert', 'ai_assistant', 1, true),
    ('aifp_ai_assistant_learner', 'ai_assistant.learner', 'ai_assistant', 1, true),
    ('aifp_reading_explanation_v1', 'reading.explanation.v1', 'reading', 1, true),
    ('aifp_reading_vocabulary_card', 'reading.vocabulary.card', 'reading', 1, true),
    ('aifp_listening_explanation_v1', 'listening.explanation.v1', 'listening', 1, true),
    ('aifp_class_recording_transcribe_v1', 'class.recording.transcribe.v1', 'class', 2, true),
    ('aifp_class_recording_summarize_v1', 'class.recording.summarize.v1', 'class', 2, true),
    ('aifp_class_recording_translate_v1', 'class.recording.translate.v1', 'class', 2, true),
    ('aifp_class_assistant_qna_v1', 'class.assistant.qna.v1', 'class', 2, true),
    ('aifp_tutor_recommendation_v1', 'tutor.recommendation.v1', 'tutor', 2, true),
    ('aifp_ocr_listening_parta', 'ocr.listening.parta', 'ocr', 1, false),
    ('aifp_ocr_content_pdf_fallback', 'ocr.content.pdf_fallback', 'ocr', 1, false),
    ('aifp_ocr_writing_handwriting', 'ocr.writing.handwriting', 'ocr', 1, false),
    ('aifp_listening_parta_extract', 'listening.parta.extract', 'listening', 1, false),
    ('aifp_listening_parta_score', 'listening.parta.score', 'listening', 0, false),
    ('aifp_ocr_listening_partbc', 'ocr.listening.partbc', 'ocr', 1, false),
    ('aifp_listening_partbc_extract', 'listening.partbc.extract', 'listening', 1, false),
    ('aifp_stt_speaking_transcribe', 'stt.speaking.transcribe', 'stt', 1, false),
    ('aifp_stt_pronunciation_transcribe', 'stt.pronunciation.transcribe', 'stt', 1, false),
    ('aifp_stt_conversation_transcribe', 'stt.conversation.transcribe', 'stt', 1, false),
    ('aifp_speaking_score_v2', 'speaking.score.v2', 'speaking', 0, true),
    ('aifp_speaking_patient_turn_v1', 'speaking.patient.turn.v1', 'speaking', 1, true),
    ('aifp_card_draft_v1', 'card.draft.v1', 'speaking', 2, true)
) AS v(id, feature_code, module, operation_class, requires_grounding)
WHERE NOT EXISTS (
    SELECT 1 FROM ""AiFeaturePolicies"" existing
    WHERE existing.""FeatureCode"" = v.feature_code AND existing.""PolicyVersion"" = 1
);";

        private const string SeedModelPricesSql = @"
INSERT INTO ""AiModelPrices"" (
    ""Id"", ""ProviderId"", ""Model"", ""EffectiveFrom"", ""EffectiveTo"", ""PricingVersion"",
    ""InputPer1k"", ""OutputPer1k"", ""CacheWritePer1k"", ""CacheReadPer1k"", ""CreatedAt"", ""UpdatedAt""
)
SELECT
    v.id, v.provider_id, v.model, TIMESTAMPTZ '2020-01-01T00:00:00Z', NULL, '2026-11-01',
    v.input_per_1k, v.output_per_1k, v.cache_write_per_1k, v.cache_read_per_1k,
    TIMESTAMPTZ '2026-11-04T09:00:00Z', TIMESTAMPTZ '2026-11-04T09:00:00Z'
FROM (VALUES
    -- Base input/output rates match the AiProviders row already seeded by
    -- CoreAiProviderSeeder for anthropic/claude-sonnet-5 (0.003/0.015 USD
    -- per 1k). Cache write/read multipliers follow Anthropic's documented
    -- Messages API prompt-caching convention: writing a cache entry costs
    -- ~1.25x the base input rate; reading from cache costs ~0.1x the base
    -- input rate (see https://docs.anthropic.com/en/docs/build-with-claude/prompt-caching).
    ('aimp_anthropic_claude_sonnet_5', 'anthropic', 'claude-sonnet-5', 0.003, 0.015, 0.00375, 0.0003),
    -- Public OpenAI gpt-4o list pricing (no repo-configured AiProviders row
    -- exists for this provider/model): $2.50 / $10.00 per 1M tokens input/
    -- output, cached-input tokens billed at OpenAI's documented 50%
    -- discount ($1.25 per 1M). OpenAI does not bill a separate cache-write
    -- step, so CacheWritePer1k is NULL (see AiPricingResolver.ComputeCostUsd).
    ('aimp_openai_gpt_4o', 'openai', 'gpt-4o', 0.0025, 0.01, NULL, 0.00125)
) AS v(id, provider_id, model, input_per_1k, output_per_1k, cache_write_per_1k, cache_read_per_1k)
WHERE NOT EXISTS (
    SELECT 1 FROM ""AiModelPrices"" existing
    WHERE existing.""ProviderId"" = v.provider_id
      AND existing.""Model"" = v.model
      AND existing.""EffectiveFrom"" = TIMESTAMPTZ '2020-01-01T00:00:00Z'
);";

        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AiModelPrices",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ProviderId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Model = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    EffectiveFrom = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    EffectiveTo = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    PricingVersion = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    InputPer1k = table.Column<decimal>(type: "numeric", nullable: false),
                    OutputPer1k = table.Column<decimal>(type: "numeric", nullable: false),
                    CacheWritePer1k = table.Column<decimal>(type: "numeric", nullable: true),
                    CacheReadPer1k = table.Column<decimal>(type: "numeric", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AiModelPrices", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "UX_AiModelPrices_Provider_Model_EffectiveFrom",
                table: "AiModelPrices",
                columns: new[] { "ProviderId", "Model", "EffectiveFrom" },
                unique: true);

            migrationBuilder.CreateTable(
                name: "AiFeaturePolicies",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    FeatureCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Module = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    OperationClass = table.Column<int>(type: "integer", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    PolicyVersion = table.Column<int>(type: "integer", nullable: false),
                    RequiresGrounding = table.Column<bool>(type: "boolean", nullable: false),
                    CacheDimensions = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    EffectiveFrom = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    EffectiveTo = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedBy = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AiFeaturePolicies", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "UX_AiFeaturePolicies_FeatureCode_PolicyVersion",
                table: "AiFeaturePolicies",
                columns: new[] { "FeatureCode", "PolicyVersion" },
                unique: true);

            migrationBuilder.Sql(SeedModelPricesSql);
            migrationBuilder.Sql(SeedFeaturePoliciesSql);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "AiFeaturePolicies");
            migrationBuilder.DropTable(name: "AiModelPrices");
        }
    }
}
