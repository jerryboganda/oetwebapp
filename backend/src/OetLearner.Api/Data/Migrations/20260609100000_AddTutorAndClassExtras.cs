using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using OetLearner.Api.Data;

#nullable disable

namespace OetLearner.Api.Data.Migrations
{
    /// <summary>
    /// Wave A1 — Zoom tutor stack: principal Tutor records, weekly availability
    /// schedule, per-class/per-session materials catalogue, and learner-submitted
    /// feedback rows. See OET_ZOOM_INTEGRATION_PLAN.md §7-§9.
    /// </summary>
    [DbContext(typeof(LearnerDbContext))]
    [Migration("20260609100000_AddTutorAndClassExtras")]
    public partial class AddTutorAndClassExtras : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                CREATE TABLE IF NOT EXISTS ""Tutors"" (
                    ""Id"" varchar(64) NOT NULL PRIMARY KEY,
                    ""UserId"" varchar(64) NOT NULL,
                    ""DisplayName"" varchar(128) NOT NULL,
                    ""DisplayNameAr"" varchar(128) NULL,
                    ""Bio"" varchar(4096) NOT NULL DEFAULT '',
                    ""BioAr"" varchar(4096) NULL,
                    ""AvatarUrl"" varchar(512) NULL,
                    ""SpecialtiesJson"" varchar(1024) NOT NULL DEFAULT '[]',
                    ""LanguagesJson"" varchar(512) NOT NULL DEFAULT '[]',
                    ""HourlyRateUsd"" numeric(10,2) NULL,
                    ""TimeZone"" varchar(64) NOT NULL DEFAULT 'UTC',
                    ""ZoomUserId"" varchar(128) NULL,
                    ""IsActive"" boolean NOT NULL DEFAULT true,
                    ""CreatedAt"" timestamp with time zone NOT NULL,
                    ""UpdatedAt"" timestamp with time zone NOT NULL
                );

                CREATE UNIQUE INDEX IF NOT EXISTS ""IX_Tutors_UserId"" ON ""Tutors"" (""UserId"");
                CREATE INDEX IF NOT EXISTS ""IX_Tutors_IsActive"" ON ""Tutors"" (""IsActive"");

                CREATE TABLE IF NOT EXISTS ""TutorAvailabilities"" (
                    ""Id"" varchar(64) NOT NULL PRIMARY KEY,
                    ""TutorId"" varchar(64) NOT NULL,
                    ""DayOfWeek"" integer NOT NULL,
                    ""StartTime"" time without time zone NOT NULL,
                    ""EndTime"" time without time zone NOT NULL,
                    ""IsActive"" boolean NOT NULL DEFAULT true,
                    CONSTRAINT ""FK_TutorAvailabilities_Tutors_TutorId""
                        FOREIGN KEY (""TutorId"") REFERENCES ""Tutors"" (""Id"") ON DELETE CASCADE
                );

                CREATE INDEX IF NOT EXISTS ""IX_TutorAvailabilities_TutorId"" ON ""TutorAvailabilities"" (""TutorId"");
                CREATE INDEX IF NOT EXISTS ""IX_TutorAvailabilities_TutorId_DayOfWeek"" ON ""TutorAvailabilities"" (""TutorId"", ""DayOfWeek"");

                CREATE TABLE IF NOT EXISTS ""ClassMaterials"" (
                    ""Id"" varchar(64) NOT NULL PRIMARY KEY,
                    ""LiveClassId"" varchar(64) NOT NULL,
                    ""ClassSessionId"" varchar(64) NULL,
                    ""Title"" varchar(180) NOT NULL,
                    ""FileUrl"" varchar(1024) NOT NULL,
                    ""MimeType"" varchar(128) NULL,
                    ""Visibility"" integer NOT NULL DEFAULT 0,
                    ""CreatedAt"" timestamp with time zone NOT NULL
                );

                CREATE INDEX IF NOT EXISTS ""IX_ClassMaterials_LiveClassId"" ON ""ClassMaterials"" (""LiveClassId"");
                CREATE INDEX IF NOT EXISTS ""IX_ClassMaterials_ClassSessionId"" ON ""ClassMaterials"" (""ClassSessionId"");
                CREATE INDEX IF NOT EXISTS ""IX_ClassMaterials_LiveClassId_ClassSessionId"" ON ""ClassMaterials"" (""LiveClassId"", ""ClassSessionId"");

                CREATE TABLE IF NOT EXISTS ""ClassFeedbacks"" (
                    ""Id"" varchar(64) NOT NULL PRIMARY KEY,
                    ""ClassSessionId"" varchar(64) NOT NULL,
                    ""UserId"" varchar(64) NOT NULL,
                    ""Rating"" integer NOT NULL,
                    ""Comment"" varchar(4096) NULL,
                    ""RecommendToFriend"" boolean NOT NULL DEFAULT false,
                    ""CreatedAt"" timestamp with time zone NOT NULL,
                    ""UpdatedAt"" timestamp with time zone NOT NULL
                );

                CREATE INDEX IF NOT EXISTS ""IX_ClassFeedbacks_ClassSessionId"" ON ""ClassFeedbacks"" (""ClassSessionId"");
                CREATE INDEX IF NOT EXISTS ""IX_ClassFeedbacks_UserId"" ON ""ClassFeedbacks"" (""UserId"");
                CREATE UNIQUE INDEX IF NOT EXISTS ""IX_ClassFeedbacks_ClassSessionId_UserId"" ON ""ClassFeedbacks"" (""ClassSessionId"", ""UserId"");
            ");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                DROP TABLE IF EXISTS ""ClassFeedbacks"";
                DROP TABLE IF EXISTS ""ClassMaterials"";
                DROP TABLE IF EXISTS ""TutorAvailabilities"";
                DROP TABLE IF EXISTS ""Tutors"";
            ");
        }
    }
}
