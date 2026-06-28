using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Infrastructure;
using OetLearner.Api.Data;

#nullable disable

namespace OetLearner.Api.Data.Migrations
{
    [DbContext(typeof(LearnerDbContext))]
    [Migration("20260713090000_PurgeSeededWritingContent")]
    public partial class PurgeSeededWritingContent : Migration
    {
        // One-shot permanent purge of auto-seeded Writing tasks.
        //
        // Context: the WritingV2ContentSeeder (diagnostic scenarios + mocks) and
        // WritingSampleSeeder (sample papers) re-created admin-deleted Writing
        // tasks on every deploy, because their idempotency check was "skip if the
        // row exists" — deleting a row made it eligible for re-insertion. Both
        // seeders have now been removed from the codebase; this migration deletes
        // the rows they already created so the admin's deletions finally stick.
        //
        // SCOPING — this NEVER touches admin-authored tasks:
        //   • Seeded scenarios carry a sentinel AuthorId: 'system:seed',
        //     'system:seed-v2', 'system:seed-v2-round2', 'system:seed-v3',
        //     'system:seed:mock' (WritingV2ContentSeeder) or 'system:writing-seed'
        //     (sample-paper projection). All of these match the predicate below.
        //   • Admin-authored tasks are created via WritingTaskAuthoringService with
        //     AuthorId = the admin's user id (or the bare fallback 'system', which
        //     does NOT match 'system:seed%'). They are therefore untouched.
        //
        // The delete order mirrors WritingTaskAuthoringService.HardDeleteAsync's
        // force-delete cascade (children → learner data → scenario) so no orphaned
        // submissions are left behind (orphans previously 500'd the Submissions
        // module). There are no DB-level FK constraints between these tables, so
        // ordering is for cleanliness, not correctness.
        //
        // Irreversible by design: Down() is a no-op. The deleted rows were
        // placeholder/demo content with deterministic seed ids and cannot be
        // meaningfully restored (re-running the removed seeders is the only way,
        // which is exactly the behaviour being retired).

        private const string SeededScenarioPredicate =
            @"""AuthorId"" LIKE 'system:seed%' OR ""AuthorId"" = 'system:writing-seed'";

        protected override void Up(MigrationBuilder migrationBuilder)
        {
            var seededScenarios = $@"SELECT ""Id"" FROM ""WritingScenarios"" WHERE {SeededScenarioPredicate}";
            var seededSubmissions =
                $@"SELECT ""Id"" FROM ""WritingSubmissions"" WHERE ""ScenarioId"" IN ({seededScenarios})";

            // 1. Submission-keyed learner data.
            migrationBuilder.Sql(
                $@"DELETE FROM ""WritingFeedbackAnnotations"" WHERE ""SubmissionId"" IN ({seededSubmissions});");
            migrationBuilder.Sql(
                $@"DELETE FROM ""WritingModerations"" WHERE ""SubmissionId"" IN ({seededSubmissions});");
            migrationBuilder.Sql(
                $@"DELETE FROM ""WritingScoreAppeals"" WHERE ""SubmissionId"" IN ({seededSubmissions});");
            migrationBuilder.Sql(
                $@"DELETE FROM ""WritingGrades"" WHERE ""SubmissionId"" IN ({seededSubmissions});");

            // 2. Scenario-keyed learner data.
            migrationBuilder.Sql(
                $@"DELETE FROM ""WritingSubmissions"" WHERE ""ScenarioId"" IN ({seededScenarios});");
            migrationBuilder.Sql(
                $@"DELETE FROM ""WritingAttemptEvents"" WHERE ""ScenarioId"" IN ({seededScenarios});");

            // 3. Scenario-owned authoring children + per-scenario visibility config.
            migrationBuilder.Sql(
                $@"DELETE FROM ""WritingScenarioStructuredSentences"" WHERE ""ScenarioId"" IN ({seededScenarios});");
            migrationBuilder.Sql(
                $@"DELETE FROM ""WritingScenarioEmbeddings"" WHERE ""ScenarioId"" IN ({seededScenarios});");
            migrationBuilder.Sql(
                $@"DELETE FROM ""WritingResultVisibilityConfigs"" WHERE ""ScenarioId"" IN ({seededScenarios});");

            // 4. Mocks that point at a seeded scenario (the 6 demo mocks).
            migrationBuilder.Sql(
                $@"DELETE FROM ""WritingMocks"" WHERE ""ScenarioId"" IN ({seededScenarios});");

            // 5. The seeded scenarios themselves.
            migrationBuilder.Sql(
                $@"DELETE FROM ""WritingScenarios"" WHERE {SeededScenarioPredicate};");

            // 6. Sample-paper rows created by the removed WritingSampleSeeder
            //    (ContentItem mirrors the ContentPaper id). Both carry the
            //    'system:writing-seed' sentinel and never collide with admin work.
            migrationBuilder.Sql(
                @"DELETE FROM ""ContentItems"" WHERE ""Id"" IN (
                      SELECT ""Id"" FROM ""ContentPapers"" WHERE ""CreatedByAdminId"" = 'system:writing-seed');");
            migrationBuilder.Sql(
                @"DELETE FROM ""ContentPapers"" WHERE ""CreatedByAdminId"" = 'system:writing-seed';");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Intentionally irreversible — the purged rows were auto-seeded demo
            // content. Restoring them would re-introduce the resurrection bug.
        }
    }
}
