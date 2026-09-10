using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using OetLearner.Api.Data;

#nullable disable

namespace OetLearner.Api.Data.Migrations
{
    /// <summary>
    /// Fixes a feature-policy misclassification found while diagnosing the
    /// admin AI Assistant returning "The AI assistant is temporarily
    /// unavailable": the 20261104090000 seed migration put
    /// <c>ai_assistant.admin</c> in OperationClass 1 (InteractiveLearning) —
    /// the same $1.00/day budget pool as every learner AI Companion chat —
    /// instead of 2 (AdminBatch), even though
    /// <c>Services/Ai/AiBudgetClasses.IsAdminBatchFeature</c> already treats
    /// <see cref="OetLearner.Api.Domain.AiFeatureCodes.AiAssistantAdmin"/> as
    /// admin-batch traffic (mirrored in
    /// <c>Services/Rulebook/AiFeaturePolicyRegistry.AiFeaturePolicyDefaults
    /// .ClassOverrides</c>, updated alongside this migration). Under real
    /// learner load the shared InteractiveLearning pool is exhausted within
    /// minutes, so every admin assistant turn was refused regardless of
    /// whether the admin had sent a single message that day.
    ///
    /// Per the "old policy versions are never mutated in place" convention
    /// (see <c>Domain/AiFeaturePolicyEntities.cs</c>), this does not edit the
    /// historical PolicyVersion 1 row seeded by 20261104090000 — it inserts a
    /// PolicyVersion 2 row with the corrected OperationClass.
    /// <c>AiFeaturePolicyRegistry.LookupAsync</c> always resolves the highest
    /// active, currently-effective version for a feature code, so this
    /// supersedes v1 without touching it. Idempotent (NOT EXISTS guard), same
    /// pattern as the seed migration it follows.
    /// </summary>
    [DbContext(typeof(LearnerDbContext))]
    [Migration("20261228090000_FixAiAssistantAdminBudgetClass")]
    public partial class FixAiAssistantAdminBudgetClass : Migration
    {
        private const string Sql = @"
INSERT INTO ""AiFeaturePolicies"" (
    ""Id"", ""FeatureCode"", ""Module"", ""OperationClass"", ""IsActive"", ""PolicyVersion"",
    ""RequiresGrounding"", ""CacheDimensions"", ""EffectiveFrom"", ""EffectiveTo"",
    ""CreatedBy"", ""CreatedAt"", ""UpdatedAt""
)
SELECT
    'aifp_ai_assistant_admin_v2', 'ai_assistant.admin', 'ai_assistant', 2, true, 2,
    true, NULL,
    TIMESTAMPTZ '2026-09-10T00:00:00Z', NULL,
    'system:fix-ai-assistant-admin-budget-class',
    TIMESTAMPTZ '2026-09-10T00:00:00Z', TIMESTAMPTZ '2026-09-10T00:00:00Z'
WHERE NOT EXISTS (
    SELECT 1 FROM ""AiFeaturePolicies"" existing
    WHERE existing.""FeatureCode"" = 'ai_assistant.admin' AND existing.""PolicyVersion"" = 2
);";

        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(Sql);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"DELETE FROM ""AiFeaturePolicies"" WHERE ""Id"" = 'aifp_ai_assistant_admin_v2';");
        }
    }
}
