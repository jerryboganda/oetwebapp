using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using OetLearner.Api.Data;

#nullable disable

namespace OetLearner.Api.Data.Migrations
{
    /// <summary>
    /// W3 of the AI cost/reliability remediation (incident INC-2026-CLAUDE-01) —
    /// creates <c>AiExplanationCacheEntries</c>, the reusable, cross-learner
    /// cache backing requirement 8 of the owner's 2026-08-28 AI/Cloud API plan
    /// ("if the same explanation... already exists, reuse it instead of
    /// creating another AI call"). See
    /// <c>Domain/AiExplanationCacheEntities.cs</c> and
    /// <c>Services/Ai/AiExplanationCacheService.cs</c>.
    ///
    /// <para>
    /// Strictly additive: one brand-new table, zero existing tables touched.
    /// Hand-authored per repo convention: inline <c>[Migration]</c>/
    /// <c>[DbContext]</c>, no Designer file, ModelSnapshot deliberately
    /// untouched. <c>IF NOT EXISTS</c> guards on the table/indexes so this
    /// migration is replay-safe if both blue and green attempt
    /// <c>Database.MigrateAsync()</c> concurrently — matching the convention
    /// <c>20261105090000_AddAiOperationResourceSlot</c> already established
    /// (and that <c>20261104090000_AddAiModelPricingAndFeaturePolicy</c>
    /// notably omitted).
    /// </para>
    /// </summary>
    [DbContext(typeof(LearnerDbContext))]
    [Migration("20261106090000_AddAiExplanationCache")]
    public partial class AddAiExplanationCache : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                CREATE TABLE IF NOT EXISTS "AiExplanationCacheEntries" (
                    "Id" character varying(64) NOT NULL,
                    "Module" character varying(16) NOT NULL,
                    "QuestionId" character varying(64) NOT NULL,
                    "Language" character varying(8) NOT NULL,
                    "CacheKey" character varying(64) NOT NULL,
                    "ExplanationJson" text NOT NULL,
                    "CreatedAt" timestamp with time zone NOT NULL,
                    "LastServedAt" timestamp with time zone NOT NULL,
                    "ServedCount" integer NOT NULL,
                    CONSTRAINT "PK_AiExplanationCacheEntries" PRIMARY KEY ("Id")
                );

                CREATE UNIQUE INDEX IF NOT EXISTS "UX_AiExplanationCacheEntries_CacheKey"
                    ON "AiExplanationCacheEntries" ("CacheKey");

                CREATE INDEX IF NOT EXISTS "IX_AiExplanationCacheEntries_Module_QuestionId"
                    ON "AiExplanationCacheEntries" ("Module", "QuestionId");
                """);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "AiExplanationCacheEntries");
        }
    }
}
