using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Infrastructure;
using OetLearner.Api.Data;

#nullable disable

namespace OetLearner.Api.Data.Migrations
{
    /// <summary>
    /// Audit P2-2 closure (May 2026). Adds the relational
    /// <c>WritingRuleViolations</c> table that the new admin Writing
    /// rule-violation analytics endpoint queries.
    ///
    /// Additive-only:
    ///   • Whole-table guarded with <c>CREATE TABLE IF NOT EXISTS</c>.
    ///   • Indexes guarded with <c>CREATE INDEX IF NOT EXISTS</c>.
    ///   • No backfill — existing attempts pre-dating this migration keep
    ///     their JSON-only finding history; new evaluations populate both
    ///     surfaces (attempt analysis JSON for learners, this table for
    ///     analytics).
    ///
    /// Migration timestamp 20260513120000 sequences after Track C's
    /// 20260512100000_AddWalletRowVersion so the deterministic apply order
    /// is preserved.
    /// </summary>
    [DbContext(typeof(LearnerDbContext))]
    [Migration("20260513120000_AddWritingRuleViolations")]
    public partial class AddWritingRuleViolations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                CREATE TABLE IF NOT EXISTS "WritingRuleViolations" (
                    "Id" varchar(64) NOT NULL,
                    "AttemptId" varchar(64) NOT NULL,
                    "EvaluationId" varchar(64) NULL,
                    "UserId" varchar(64) NOT NULL,
                    "Profession" varchar(64) NOT NULL,
                    "LetterType" varchar(64) NOT NULL,
                    "RuleId" varchar(128) NOT NULL,
                    "Severity" varchar(16) NOT NULL,
                    "Source" varchar(16) NOT NULL,
                    "Message" varchar(1024) NOT NULL,
                    "Quote" varchar(1024) NULL,
                    "GeneratedAt" timestamptz NOT NULL,
                    CONSTRAINT "PK_WritingRuleViolations" PRIMARY KEY ("Id")
                );
                """);

            migrationBuilder.Sql("""
                CREATE INDEX IF NOT EXISTS "IX_WritingRuleViolations_RuleId_GeneratedAt"
                ON "WritingRuleViolations" ("RuleId", "GeneratedAt");
                """);

            migrationBuilder.Sql("""
                CREATE INDEX IF NOT EXISTS "IX_WritingRuleViolations_Profession_GeneratedAt"
                ON "WritingRuleViolations" ("Profession", "GeneratedAt");
                """);

            migrationBuilder.Sql("""
                CREATE INDEX IF NOT EXISTS "IX_WritingRuleViolations_AttemptId"
                ON "WritingRuleViolations" ("AttemptId");
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP INDEX IF EXISTS "IX_WritingRuleViolations_AttemptId";
                """);
            migrationBuilder.Sql("""
                DROP INDEX IF EXISTS "IX_WritingRuleViolations_Profession_GeneratedAt";
                """);
            migrationBuilder.Sql("""
                DROP INDEX IF EXISTS "IX_WritingRuleViolations_RuleId_GeneratedAt";
                """);
            migrationBuilder.Sql("""
                DROP TABLE IF EXISTS "WritingRuleViolations";
                """);
        }
    }
}
