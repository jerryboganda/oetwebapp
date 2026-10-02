using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using OetLearner.Api.Data;

#nullable disable

namespace OetLearner.Api.Data.Migrations
{
    /// <summary>
    /// WAI-06 — zero-loss Writing drafts: a compare-and-set Version, the
    /// active/submitted lifecycle with the consuming submission, and the
    /// pause-while-away exam clock (phase + seconds left per window).
    ///
    /// HAND-AUTHORED (repo convention, see 20261203090000): idempotent raw SQL,
    /// no Designer file, ModelSnapshot updated by hand. Defaults are required
    /// because blue/green slots overlap: the old slot inserts rows without
    /// these columns.
    ///
    /// Backfill (legacy rows only, re-runnable, never deletes): a draft whose
    /// (user, task, mode) has a non-revision submission created at or after
    /// its last save — minus one minute for an autosave that landed just after
    /// the submit — was consumed by the earliest such submission. New attempts
    /// only begin after a reading window, so they are never caught by it.
    /// Rollback: UPDATE "WritingDraftsV2" SET "Status" = 'active', "SubmissionId" = NULL.
    /// </summary>
    [DbContext(typeof(LearnerDbContext))]
    [Migration("20270104090100_AddWritingDraftResumeState")]
    public partial class AddWritingDraftResumeState : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                ALTER TABLE "WritingDraftsV2" ADD COLUMN IF NOT EXISTS "Version" integer NOT NULL DEFAULT 1;
                ALTER TABLE "WritingDraftsV2" ADD COLUMN IF NOT EXISTS "Status" character varying(16) NOT NULL DEFAULT 'active';
                ALTER TABLE "WritingDraftsV2" ADD COLUMN IF NOT EXISTS "SubmissionId" uuid;
                ALTER TABLE "WritingDraftsV2" ADD COLUMN IF NOT EXISTS "Phase" character varying(16);
                ALTER TABLE "WritingDraftsV2" ADD COLUMN IF NOT EXISTS "ReadingSecondsRemaining" integer;
                ALTER TABLE "WritingDraftsV2" ADD COLUMN IF NOT EXISTS "WritingSecondsRemaining" integer;
                ALTER TABLE "WritingDraftsV2" ADD COLUMN IF NOT EXISTS "AttemptStartedAt" timestamp with time zone;

                UPDATE "WritingDraftsV2" d
                SET "Status" = 'submitted',
                    "SubmissionId" = (
                        SELECT s."Id" FROM "WritingSubmissions" s
                        WHERE s."UserId" = d."UserId"
                          AND s."ScenarioId" = d."ScenarioId"
                          AND s."Mode" = d."Mode"
                          AND NOT s."IsRevision"
                          AND s."CreatedAt" >= d."LastSavedAt" - interval '1 minute'
                        ORDER BY s."CreatedAt", s."Id"
                        LIMIT 1)
                WHERE d."Status" = 'active'
                  AND d."SubmissionId" IS NULL
                  AND d."AttemptStartedAt" IS NULL
                  AND EXISTS (
                        SELECT 1 FROM "WritingSubmissions" s
                        WHERE s."UserId" = d."UserId"
                          AND s."ScenarioId" = d."ScenarioId"
                          AND s."Mode" = d."Mode"
                          AND NOT s."IsRevision"
                          AND s."CreatedAt" >= d."LastSavedAt" - interval '1 minute');
                """);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                ALTER TABLE "WritingDraftsV2" DROP COLUMN IF EXISTS "Version";
                ALTER TABLE "WritingDraftsV2" DROP COLUMN IF EXISTS "Status";
                ALTER TABLE "WritingDraftsV2" DROP COLUMN IF EXISTS "SubmissionId";
                ALTER TABLE "WritingDraftsV2" DROP COLUMN IF EXISTS "Phase";
                ALTER TABLE "WritingDraftsV2" DROP COLUMN IF EXISTS "ReadingSecondsRemaining";
                ALTER TABLE "WritingDraftsV2" DROP COLUMN IF EXISTS "WritingSecondsRemaining";
                ALTER TABLE "WritingDraftsV2" DROP COLUMN IF EXISTS "AttemptStartedAt";
                """);
        }
    }
}
