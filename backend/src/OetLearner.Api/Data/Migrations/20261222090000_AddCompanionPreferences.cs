using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using OetLearner.Api.Data;

#nullable disable

namespace OetLearner.Api.Data.Migrations
{
    /// <summary>
    /// AI Learning Companion teaching-style preferences (docs/ai-learning-companion/):
    /// F-011 teaching style, F-050 Socratic mode, F-052 coach mode, F-055 English-only.
    ///
    /// One row per learner, keyed by user id. Absent means every default, so the companion
    /// works for a learner who has never opened the settings — the row is created on first
    /// save and never required.
    ///
    /// Purely additive and idempotent (<c>CREATE TABLE IF NOT EXISTS</c>); no existing table
    /// is touched. <c>Down</c> drops only this table.
    ///
    /// ⚠ Postgres-only SQL. The test suite uses SQLite via EnsureCreatedAsync() and builds
    /// from the model, bypassing migrations entirely.
    /// </summary>
    [DbContext(typeof(LearnerDbContext))]
    [Migration("20261222090000_AddCompanionPreferences")]
    public partial class AddCompanionPreferences : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                CREATE TABLE IF NOT EXISTS ""CompanionPreferences"" (
                    ""UserId""               character varying(64) NOT NULL,
                    ""TeachingStyle""        integer NOT NULL DEFAULT 0,
                    ""Depth""                integer NOT NULL DEFAULT 1,
                    ""EnglishOnly""          boolean NOT NULL DEFAULT FALSE,
                    ""PreferWorkedExamples"" boolean NOT NULL DEFAULT TRUE,
                    ""UpdatedAt""            timestamp with time zone NOT NULL DEFAULT NOW(),
                    CONSTRAINT ""PK_CompanionPreferences"" PRIMARY KEY (""UserId"")
                );
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"DROP TABLE IF EXISTS ""CompanionPreferences"";");
        }
    }
}
