using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using OetWithDrHesham.Api.Data;

#nullable disable

namespace OetWithDrHesham.Api.Data.Migrations
{
    /// <summary>
    /// Readiness rebuild. Replaces stub JSON-only snapshot with structured
    /// columns + weekly history rows. Adds:
    /// <list type="bullet">
    ///   <item>Structured columns on <c>ReadinessSnapshots</c>: per-sub-test
    ///   readiness, overall risk, target-date probability, weakest sub-test,
    ///   recommended study hours, confidence level, data-point count,
    ///   expiry.</item>
    ///   <item>New <c>ReadinessHistories</c> table — one row per learner per
    ///   ISO week for trend chart.</item>
    /// </list>
    ///
    /// Postgres-only SQL — uses <c>ADD COLUMN IF NOT EXISTS</c>. SQLite tests
    /// bypass migrations via <c>EnsureCreatedAsync()</c>.
    /// </summary>
    [DbContext(typeof(LearnerDbContext))]
    [Migration("20260527000000_ReadinessRebuild")]
    public partial class ReadinessRebuild : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                ALTER TABLE ""ReadinessSnapshots""
                    ADD COLUMN IF NOT EXISTS ""ExpiresAt"" timestamp with time zone NOT NULL DEFAULT (now() + interval '24 hours');
                ALTER TABLE ""ReadinessSnapshots""
                    ADD COLUMN IF NOT EXISTS ""OverallReadiness"" numeric NOT NULL DEFAULT 0;
                ALTER TABLE ""ReadinessSnapshots""
                    ADD COLUMN IF NOT EXISTS ""WritingReadiness"" numeric NOT NULL DEFAULT 0;
                ALTER TABLE ""ReadinessSnapshots""
                    ADD COLUMN IF NOT EXISTS ""SpeakingReadiness"" numeric NOT NULL DEFAULT 0;
                ALTER TABLE ""ReadinessSnapshots""
                    ADD COLUMN IF NOT EXISTS ""ReadingReadiness"" numeric NOT NULL DEFAULT 0;
                ALTER TABLE ""ReadinessSnapshots""
                    ADD COLUMN IF NOT EXISTS ""ListeningReadiness"" numeric NOT NULL DEFAULT 0;
                ALTER TABLE ""ReadinessSnapshots""
                    ADD COLUMN IF NOT EXISTS ""VocabularyReadiness"" numeric NOT NULL DEFAULT 0;
                ALTER TABLE ""ReadinessSnapshots""
                    ADD COLUMN IF NOT EXISTS ""OverallRisk"" varchar(16) NOT NULL DEFAULT 'Unknown';
                ALTER TABLE ""ReadinessSnapshots""
                    ADD COLUMN IF NOT EXISTS ""TargetDateProbability"" numeric NULL;
                ALTER TABLE ""ReadinessSnapshots""
                    ADD COLUMN IF NOT EXISTS ""WeakestSubtest"" varchar(32) NULL;
                ALTER TABLE ""ReadinessSnapshots""
                    ADD COLUMN IF NOT EXISTS ""RecommendedStudyHoursPerWeek"" integer NOT NULL DEFAULT 0;
                ALTER TABLE ""ReadinessSnapshots""
                    ADD COLUMN IF NOT EXISTS ""ConfidenceLevel"" varchar(16) NOT NULL DEFAULT 'Low';
                ALTER TABLE ""ReadinessSnapshots""
                    ADD COLUMN IF NOT EXISTS ""DataPointCount"" integer NOT NULL DEFAULT 0;

                CREATE INDEX IF NOT EXISTS ""IX_ReadinessSnapshots_UserId_ComputedAt""
                    ON ""ReadinessSnapshots"" (""UserId"", ""ComputedAt"" DESC);
                CREATE INDEX IF NOT EXISTS ""IX_ReadinessSnapshots_ExpiresAt""
                    ON ""ReadinessSnapshots"" (""ExpiresAt"");

                CREATE TABLE IF NOT EXISTS ""ReadinessHistories"" (
                    ""Id"" varchar(64) PRIMARY KEY,
                    ""UserId"" varchar(64) NOT NULL,
                    ""WeekStartDate"" date NOT NULL,
                    ""RecordedAt"" timestamp with time zone NOT NULL,
                    ""Overall"" numeric NOT NULL DEFAULT 0,
                    ""Writing"" numeric NOT NULL DEFAULT 0,
                    ""Speaking"" numeric NOT NULL DEFAULT 0,
                    ""Reading"" numeric NOT NULL DEFAULT 0,
                    ""Listening"" numeric NOT NULL DEFAULT 0,
                    ""Vocabulary"" numeric NOT NULL DEFAULT 0,
                    ""Risk"" varchar(16) NOT NULL DEFAULT 'Unknown',
                    ""TargetDateProbability"" numeric NULL
                );

                CREATE UNIQUE INDEX IF NOT EXISTS ""IX_ReadinessHistories_UserId_WeekStartDate""
                    ON ""ReadinessHistories"" (""UserId"", ""WeekStartDate"");
                CREATE INDEX IF NOT EXISTS ""IX_ReadinessHistories_RecordedAt""
                    ON ""ReadinessHistories"" (""RecordedAt"");
            ");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                DROP TABLE IF EXISTS ""ReadinessHistories"";

                DROP INDEX IF EXISTS ""IX_ReadinessSnapshots_UserId_ComputedAt"";
                DROP INDEX IF EXISTS ""IX_ReadinessSnapshots_ExpiresAt"";

                ALTER TABLE ""ReadinessSnapshots"" DROP COLUMN IF EXISTS ""ExpiresAt"";
                ALTER TABLE ""ReadinessSnapshots"" DROP COLUMN IF EXISTS ""OverallReadiness"";
                ALTER TABLE ""ReadinessSnapshots"" DROP COLUMN IF EXISTS ""WritingReadiness"";
                ALTER TABLE ""ReadinessSnapshots"" DROP COLUMN IF EXISTS ""SpeakingReadiness"";
                ALTER TABLE ""ReadinessSnapshots"" DROP COLUMN IF EXISTS ""ReadingReadiness"";
                ALTER TABLE ""ReadinessSnapshots"" DROP COLUMN IF EXISTS ""ListeningReadiness"";
                ALTER TABLE ""ReadinessSnapshots"" DROP COLUMN IF EXISTS ""VocabularyReadiness"";
                ALTER TABLE ""ReadinessSnapshots"" DROP COLUMN IF EXISTS ""OverallRisk"";
                ALTER TABLE ""ReadinessSnapshots"" DROP COLUMN IF EXISTS ""TargetDateProbability"";
                ALTER TABLE ""ReadinessSnapshots"" DROP COLUMN IF EXISTS ""WeakestSubtest"";
                ALTER TABLE ""ReadinessSnapshots"" DROP COLUMN IF EXISTS ""RecommendedStudyHoursPerWeek"";
                ALTER TABLE ""ReadinessSnapshots"" DROP COLUMN IF EXISTS ""ConfidenceLevel"";
                ALTER TABLE ""ReadinessSnapshots"" DROP COLUMN IF EXISTS ""DataPointCount"";
            ");
        }
    }
}
