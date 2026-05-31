using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using OetLearner.Api.Data;

#nullable disable

namespace OetLearner.Api.Data.Migrations
{
    /// <inheritdoc />
    // Hand-authored migration (see docs/onboarding-project-discovery-report.md). The
    // [DbContext]/[Migration] attributes — normally emitted into the *.Designer.cs by
    // `dotnet ef migrations add` — are placed here so EF discovers and applies the
    // migration without a designer file. The matching LearnerDbContextModelSnapshot
    // entries are updated by hand so a future `dotnet ef migrations add` reports no
    // pending model changes. Verify in Docker: `dotnet ef migrations add Verify` must
    // be a no-op, then `dotnet ef database update`.
    [DbContext(typeof(LearnerDbContext))]
    [Migration("20260619000000_AddOnboardingTourAndGoalFields")]
    public partial class AddOnboardingTourAndGoalFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Idempotent (mirrors AddExpertOnboardingProgress + the recovery migrations)
            // so it is safe to re-run on environments where the schema was created
            // out-of-band or where AutoMigrate already applied an equivalent change.
            migrationBuilder.Sql(@"
CREATE TABLE IF NOT EXISTS ""LearnerOnboardingTours"" (
    ""UserId"" character varying(64) NOT NULL,
    ""Role"" character varying(32) NOT NULL,
    ""OnboardingVersion"" integer NOT NULL,
    ""CompletedIntro"" boolean NOT NULL,
    ""CompletedDashboardTour"" boolean NOT NULL,
    ""CompletedListeningTour"" boolean NOT NULL,
    ""CompletedReadingTour"" boolean NOT NULL,
    ""CompletedWritingTour"" boolean NOT NULL,
    ""CompletedSpeakingTour"" boolean NOT NULL,
    ""CompletedAdminTour"" boolean NOT NULL,
    ""CompletedExpertTour"" boolean NOT NULL,
    ""SkippedToursJson"" text NOT NULL,
    ""DismissedTipsJson"" text NOT NULL,
    ""LastSeenTourVersion"" integer NOT NULL,
    ""CreatedAt"" timestamp with time zone NOT NULL,
    ""UpdatedAt"" timestamp with time zone NOT NULL,
    CONSTRAINT ""PK_LearnerOnboardingTours"" PRIMARY KEY (""UserId"")
);
");
            migrationBuilder.Sql(@"ALTER TABLE ""Goals"" ADD COLUMN IF NOT EXISTS ""TargetExamMode"" character varying(16);");
            migrationBuilder.Sql(@"ALTER TABLE ""Goals"" ADD COLUMN IF NOT EXISTS ""ConfidenceLevel"" character varying(16);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LearnerOnboardingTours");
            migrationBuilder.DropColumn(
                name: "ConfidenceLevel",
                table: "Goals");
            migrationBuilder.DropColumn(
                name: "TargetExamMode",
                table: "Goals");
        }
    }
}
