using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using OetLearner.Api.Data;

#nullable disable

namespace OetLearner.Api.Data.Migrations
{
    /// <summary>
    /// OET 2026 Tutor Book — admin-managed updates feed + audio scripts library.
    ///
    /// <para>Two tables: <c>TutorBookUpdates</c> (recall amendments, profession-
    /// targeted) and <c>TutorBookAudioScripts</c> (per-chapter MP3 references).
    /// Both surfaced under <c>/learner/tutor-book</c> reader tabs.</para>
    ///
    /// <para>Hand-written Postgres-only migration. SQLite tests pick the
    /// schema up via <c>EnsureCreatedAsync</c>.</para>
    /// </summary>
    [DbContext(typeof(LearnerDbContext))]
    [Migration("20260608002000_AddTutorBookTables")]
    public partial class AddTutorBookTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                CREATE TABLE IF NOT EXISTS ""TutorBookUpdates"" (
                    ""Id"" varchar(64) NOT NULL PRIMARY KEY,
                    ""Title"" varchar(200) NOT NULL,
                    ""BodyMarkdown"" text NOT NULL DEFAULT '',
                    ""Audience"" varchar(16) NOT NULL DEFAULT 'all',
                    ""PublishedAt"" timestamp with time zone NOT NULL,
                    ""IsPublished"" boolean NOT NULL DEFAULT true,
                    ""CreatedByAdminId"" varchar(64) NULL,
                    ""CreatedByAdminName"" varchar(128) NULL,
                    ""CreatedAt"" timestamp with time zone NOT NULL,
                    ""UpdatedAt"" timestamp with time zone NOT NULL
                );

                CREATE INDEX IF NOT EXISTS ""IX_TutorBookUpdates_PublishedAt""
                    ON ""TutorBookUpdates"" (""PublishedAt"" DESC);
                CREATE INDEX IF NOT EXISTS ""IX_TutorBookUpdates_Audience""
                    ON ""TutorBookUpdates"" (""Audience"");

                CREATE TABLE IF NOT EXISTS ""TutorBookAudioScripts"" (
                    ""Id"" varchar(64) NOT NULL PRIMARY KEY,
                    ""Chapter"" varchar(32) NOT NULL,
                    ""Title"" varchar(200) NOT NULL,
                    ""AudioUrl"" varchar(1024) NOT NULL,
                    ""TranscriptUrl"" varchar(1024) NULL,
                    ""DisplayOrder"" integer NOT NULL DEFAULT 0,
                    ""IsPublished"" boolean NOT NULL DEFAULT true,
                    ""CreatedByAdminId"" varchar(64) NULL,
                    ""CreatedByAdminName"" varchar(128) NULL,
                    ""CreatedAt"" timestamp with time zone NOT NULL,
                    ""UpdatedAt"" timestamp with time zone NOT NULL
                );
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                DROP TABLE IF EXISTS ""TutorBookAudioScripts"";
                DROP INDEX IF EXISTS ""IX_TutorBookUpdates_Audience"";
                DROP INDEX IF EXISTS ""IX_TutorBookUpdates_PublishedAt"";
                DROP TABLE IF EXISTS ""TutorBookUpdates"";
            ");
        }
    }
}
