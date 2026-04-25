using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OetLearner.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddExpertOnboardingProgress : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Idempotent: safe to re-run on environments where the table was already
            // created out-of-band (mirrors the pattern used by the recovery migrations).
            migrationBuilder.Sql(@"
CREATE TABLE IF NOT EXISTS ""ExpertOnboardingProgresses"" (
    ""ExpertUserId"" character varying(64) NOT NULL,
    ""ProfileJson"" text NOT NULL,
    ""QualificationsJson"" text NOT NULL,
    ""RatesJson"" text NOT NULL,
    ""CompletedStepsJson"" text NOT NULL,
    ""IsComplete"" boolean NOT NULL,
    ""CompletedAt"" timestamp with time zone NULL,
    ""UpdatedAt"" timestamp with time zone NOT NULL,
    CONSTRAINT ""PK_ExpertOnboardingProgresses"" PRIMARY KEY (""ExpertUserId"")
);
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ExpertOnboardingProgresses");
        }
    }
}
