using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using OetLearner.Api.Data;

#nullable disable

namespace OetLearner.Api.Data.Migrations
{
    /// <summary>
    /// WAI-01/WAI-03 — the paid credit reference of a Writing letter and its grade
    /// recovery state (run epoch, automatic re-queue, candidate-safe failure code).
    /// Additive and idempotent; the NOT NULL columns carry defaults because the
    /// blue/green slots overlap and the old slot keeps inserting rows without them.
    /// No new index: IX_WritingSubmissions_Status_Pending already covers the sweep.
    /// </summary>
    [DbContext(typeof(LearnerDbContext))]
    [Migration("20270104090000_AddWritingGradeRecoveryState")]
    public partial class AddWritingGradeRecoveryState : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                ALTER TABLE "WritingSubmissions" ADD COLUMN IF NOT EXISTS "CreditReference" character varying(128);
                ALTER TABLE "WritingSubmissions" ADD COLUMN IF NOT EXISTS "GradeEpoch" integer NOT NULL DEFAULT 0;
                ALTER TABLE "WritingSubmissions" ADD COLUMN IF NOT EXISTS "AutoRetryCount" integer NOT NULL DEFAULT 0;
                ALTER TABLE "WritingSubmissions" ADD COLUMN IF NOT EXISTS "NextAutoRetryAt" timestamp with time zone;
                ALTER TABLE "WritingSubmissions" ADD COLUMN IF NOT EXISTS "LastFailureAt" timestamp with time zone;
                ALTER TABLE "WritingSubmissions" ADD COLUMN IF NOT EXISTS "FailureCode" character varying(32);
                ALTER TABLE "WritingSubmissions" ADD COLUMN IF NOT EXISTS "FailureRetryable" boolean;

                -- Owner rule MAX-ALWAYS-ON (2 Oct 2026): every Writing grade starts on the
                -- Claude Max subscription. Clear the week-long "skip Max" marker the old
                -- pipeline wrote after any two L1 blips, and undo a forced-Codex mode. Both
                -- columns are inert from this release on; re-running is a no-op.
                UPDATE "RuntimeSettings" SET "WritingAiClaudeQuotaExceededUntil" = NULL
                WHERE "WritingAiClaudeQuotaExceededUntil" IS NOT NULL;
                UPDATE "RuntimeSettings" SET "WritingAiProviderMode" = 'auto'
                WHERE "WritingAiProviderMode" = 'codex';
                """);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                ALTER TABLE "WritingSubmissions" DROP COLUMN IF EXISTS "CreditReference";
                ALTER TABLE "WritingSubmissions" DROP COLUMN IF EXISTS "GradeEpoch";
                ALTER TABLE "WritingSubmissions" DROP COLUMN IF EXISTS "AutoRetryCount";
                ALTER TABLE "WritingSubmissions" DROP COLUMN IF EXISTS "NextAutoRetryAt";
                ALTER TABLE "WritingSubmissions" DROP COLUMN IF EXISTS "LastFailureAt";
                ALTER TABLE "WritingSubmissions" DROP COLUMN IF EXISTS "FailureCode";
                ALTER TABLE "WritingSubmissions" DROP COLUMN IF EXISTS "FailureRetryable";
                """);
        }
    }
}
