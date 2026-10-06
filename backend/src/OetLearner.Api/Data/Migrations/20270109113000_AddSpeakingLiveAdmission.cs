using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using OetLearner.Api.Data;

#nullable disable

namespace OetLearner.Api.Data.Migrations
{
    /// <summary>
    /// Live AI Speaking admission control (owner decision 5 Oct 2026): the FIFO line and head-count of live AI
    /// patient sessions, and the singleton row holding the owner-tunable cap and kill switch. Two additive,
    /// idempotent tables; the blue/green slots overlap and neither the old nor the new slot is affected by a
    /// table it never reads. No data is moved or deleted.
    ///
    /// HAND-AUTHORED (repo convention, ADR 0001): inline <c>[Migration]</c>/<c>[DbContext]</c>, no Designer file;
    /// the matching entities are in <c>LearnerDbContextModelSnapshot</c>.
    /// </summary>
    [DbContext(typeof(LearnerDbContext))]
    [Migration("20270109113000_AddSpeakingLiveAdmission")]
    public partial class AddSpeakingLiveAdmission : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
CREATE TABLE IF NOT EXISTS ""SpeakingLiveAdmissions"" (
    ""Id"" character varying(96) NOT NULL,
    ""AdmittedAt"" timestamp with time zone NULL,
    ""EndedAt"" timestamp with time zone NULL,
    ""EnqueuedAt"" timestamp with time zone NOT NULL,
    ""ExpiresAt"" timestamp with time zone NULL,
    ""LastSeenAt"" timestamp with time zone NOT NULL,
    ""Seq"" bigint NOT NULL,
    ""State"" integer NOT NULL,
    ""SubjectId"" character varying(64) NOT NULL,
    ""SubjectKind"" character varying(16) NOT NULL,
    ""UpdatedAt"" timestamp with time zone NOT NULL,
    ""UserId"" character varying(64) NOT NULL,
    CONSTRAINT ""PK_SpeakingLiveAdmissions"" PRIMARY KEY (""Id"")
);");
            migrationBuilder.Sql(
                "CREATE UNIQUE INDEX IF NOT EXISTS \"IX_SpeakingLiveAdmissions_Seq\" " +
                "ON \"SpeakingLiveAdmissions\" (\"Seq\");");
            migrationBuilder.Sql(
                "CREATE INDEX IF NOT EXISTS \"IX_SpeakingLiveAdmissions_State_Seq\" " +
                "ON \"SpeakingLiveAdmissions\" (\"State\", \"Seq\");");
            migrationBuilder.Sql(
                "CREATE INDEX IF NOT EXISTS \"IX_SpeakingLiveAdmissions_UserId\" " +
                "ON \"SpeakingLiveAdmissions\" (\"UserId\");");
            migrationBuilder.Sql(@"
CREATE TABLE IF NOT EXISTS ""SpeakingLiveAdmissionSettings"" (
    ""Id"" character varying(32) NOT NULL,
    ""Enabled"" boolean NOT NULL,
    ""MaxConcurrent"" integer NOT NULL,
    ""UpdatedAt"" timestamp with time zone NOT NULL,
    ""UpdatedById"" character varying(64) NULL,
    CONSTRAINT ""PK_SpeakingLiveAdmissionSettings"" PRIMARY KEY (""Id"")
);");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TABLE IF EXISTS \"SpeakingLiveAdmissionSettings\";");
            migrationBuilder.Sql("DROP TABLE IF EXISTS \"SpeakingLiveAdmissions\";");
        }
    }
}
