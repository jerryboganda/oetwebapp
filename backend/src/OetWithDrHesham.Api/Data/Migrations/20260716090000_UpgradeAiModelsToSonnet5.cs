using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Infrastructure;
using OetWithDrHesham.Api.Data;

#nullable disable

namespace OetWithDrHesham.Api.Data.Migrations
{
    [DbContext(typeof(LearnerDbContext))]
    [Migration("20260716090000_UpgradeAiModelsToSonnet5")]
    public partial class UpgradeAiModelsToSonnet5 : Migration
    {
        // Project-wide model upgrade: Claude Sonnet 4.6 / Haiku 4.5 → Sonnet 5.0
        // (`claude-sonnet-5`) for every AI feature (owner directive 2026-07-01).
        //
        // The code-level defaults (seeders, orchestrators, feature-route defaults,
        // prompt templates) were updated in the same change, so fresh databases
        // seed the new id directly. This migration reconciles the rows the
        // idempotent seeders already inserted on existing deployments — those
        // seeders skip-if-exists, so they would never have rewritten the model.
        //
        // Scoped by the OLD model ids only, so rows pointing at other providers
        // (e.g. an OpenAI `gpt-4o` fallback) are left untouched. Irreversible by
        // design: Down() is a no-op — we cannot know which rows were Haiku vs
        // Sonnet before the merge, and the whole point is to be on Sonnet 5.

        private const string OldModels = "'claude-sonnet-4-6', 'claude-haiku-4-5'";

        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Canonical provider default (Code = 'anthropic' and any other row
            // still pinned to a retired Claude id).
            migrationBuilder.Sql(
                $@"UPDATE ""AiProviders"" SET ""DefaultModel"" = 'claude-sonnet-5'
                   WHERE ""DefaultModel"" IN ({OldModels});");

            // Per-feature route overrides (speaking.score.v2, patient turn, card
            // draft, writing/reading/listening routes, etc.).
            migrationBuilder.Sql(
                $@"UPDATE ""AiFeatureRoutes"" SET ""Model"" = 'claude-sonnet-5'
                   WHERE ""Model"" IN ({OldModels});");

            // Conversation reply / evaluation model overrides (admin-editable).
            migrationBuilder.Sql(
                $@"UPDATE ""ConversationSettings"" SET ""ReplyModel"" = 'claude-sonnet-5'
                   WHERE ""ReplyModel"" IN ({OldModels});");
            migrationBuilder.Sql(
                $@"UPDATE ""ConversationSettings"" SET ""EvaluationModel"" = 'claude-sonnet-5'
                   WHERE ""EvaluationModel"" IN ({OldModels});");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Intentionally irreversible — see the class comment.
        }
    }
}
