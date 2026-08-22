using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using OetLearner.Api.Data;

#nullable disable

namespace OetLearner.Api.Data.Migrations;

[DbContext(typeof(LearnerDbContext))]
[Migration("20260904140000_AddAssessmentAnswerKeyReports")]
public partial class AddAssessmentAnswerKeyReports : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "AssessmentAnswerKeyReports",
            columns: table => new
            {
                Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                Assessment = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                AttemptId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                PaperId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                QuestionId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                QuestionNumber = table.Column<int>(type: "integer", nullable: false),
                PartCode = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                QuestionStemSnapshot = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                PaperTitleSnapshot = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                ReporterUserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                LearnerAnswerSnapshot = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                OfficialAnswerSnapshot = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                ReasonCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                Details = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                ResolutionNote = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                ResolvedByAdminId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                ResolvedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_AssessmentAnswerKeyReports", x => x.Id);
            });

        migrationBuilder.CreateIndex(
            name: "IX_AssessmentAnswerKeyReports_Status_CreatedAt",
            table: "AssessmentAnswerKeyReports",
            columns: new[] { "Status", "CreatedAt" });

        migrationBuilder.CreateIndex(
            name: "IX_AssessmentAnswerKeyReports_Assessment_Status",
            table: "AssessmentAnswerKeyReports",
            columns: new[] { "Assessment", "Status" });

        migrationBuilder.CreateIndex(
            name: "IX_AssessmentAnswerKeyReports_Reporter_Assessment_Question_Attempt",
            table: "AssessmentAnswerKeyReports",
            columns: new[] { "ReporterUserId", "Assessment", "QuestionId", "AttemptId" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "AssessmentAnswerKeyReports");
    }
}
