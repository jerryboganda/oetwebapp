using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OetLearner.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSpeakingCalibrationTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SpeakingCalibrationSamples",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreatedByAdminId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Title = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    Description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    SourceAttemptId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ProfessionId = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Difficulty = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    GoldScoresJson = table.Column<string>(type: "text", nullable: false),
                    CalibrationNotes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    PublishedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SpeakingCalibrationSamples", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SpeakingCalibrationScores",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SampleId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    TutorId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ScoresJson = table.Column<string>(type: "text", nullable: false),
                    TotalAbsoluteError = table.Column<double>(type: "double precision", nullable: false),
                    Notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    SubmittedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SpeakingCalibrationScores", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SpeakingFeedbackComments",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    AttemptId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ExpertId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    TranscriptLineIndex = table.Column<int>(type: "integer", nullable: false),
                    CriterionCode = table.Column<string>(type: "character varying(48)", maxLength: 48, nullable: false),
                    Body = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SpeakingFeedbackComments", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SpeakingCalibrationSamples_Status_PublishedAt",
                table: "SpeakingCalibrationSamples",
                columns: new[] { "Status", "PublishedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_SpeakingCalibrationScores_SampleId_TutorId",
                table: "SpeakingCalibrationScores",
                columns: new[] { "SampleId", "TutorId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SpeakingCalibrationScores_TutorId",
                table: "SpeakingCalibrationScores",
                column: "TutorId");

            migrationBuilder.CreateIndex(
                name: "IX_SpeakingFeedbackComments_AttemptId_TranscriptLineIndex",
                table: "SpeakingFeedbackComments",
                columns: new[] { "AttemptId", "TranscriptLineIndex" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "SpeakingCalibrationSamples");
            migrationBuilder.DropTable(name: "SpeakingCalibrationScores");
            migrationBuilder.DropTable(name: "SpeakingFeedbackComments");
        }
    }
}
