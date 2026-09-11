using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using OetLearner.Api.Data;

#nullable disable

namespace OetLearner.Api.Data.Migrations
{
    /// <summary>
    /// Writing Rule Enforcement Addendum Rev8 (11 Sep 2026) §7/§14: every
    /// saved Model Answer records the exact validator version + rule-pack
    /// fingerprint it passed under, when, the full validation report, the
    /// repair count and the body word count. Existing rows get NULL
    /// ValidatorVersion — i.e. "never verified under the current rules" —
    /// which is exactly the required semantics: an old VERIFIED flag is not
    /// sufficient after a rule update until the saved answer is revalidated.
    /// Hand-authored (additive only, idempotent).
    /// </summary>
    [DbContext(typeof(LearnerDbContext))]
    [Migration("20261230090000_WritingModelAnswerValidatorProvenance")]
    public partial class WritingModelAnswerValidatorProvenance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                ALTER TABLE "WritingTaskModelAnswers" ADD COLUMN IF NOT EXISTS "ValidatorVersion" character varying(64) NULL;
                ALTER TABLE "WritingTaskModelAnswers" ADD COLUMN IF NOT EXISTS "RulePackHash" character varying(64) NULL;
                ALTER TABLE "WritingTaskModelAnswers" ADD COLUMN IF NOT EXISTS "ValidatedAt" timestamp with time zone NULL;
                ALTER TABLE "WritingTaskModelAnswers" ADD COLUMN IF NOT EXISTS "ValidationReportJson" jsonb NOT NULL DEFAULT '{}';
                ALTER TABLE "WritingTaskModelAnswers" ADD COLUMN IF NOT EXISTS "RepairCount" integer NOT NULL DEFAULT 0;
                ALTER TABLE "WritingTaskModelAnswers" ADD COLUMN IF NOT EXISTS "BodyWordCount" integer NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                ALTER TABLE "WritingTaskModelAnswers" DROP COLUMN IF EXISTS "ValidatorVersion";
                ALTER TABLE "WritingTaskModelAnswers" DROP COLUMN IF EXISTS "RulePackHash";
                ALTER TABLE "WritingTaskModelAnswers" DROP COLUMN IF EXISTS "ValidatedAt";
                ALTER TABLE "WritingTaskModelAnswers" DROP COLUMN IF EXISTS "ValidationReportJson";
                ALTER TABLE "WritingTaskModelAnswers" DROP COLUMN IF EXISTS "RepairCount";
                ALTER TABLE "WritingTaskModelAnswers" DROP COLUMN IF EXISTS "BodyWordCount";
                """);
        }
    }
}
