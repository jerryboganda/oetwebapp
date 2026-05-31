using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OetLearner.Api.Data.Migrations
{
    /// <summary>
    /// 50-letter calibration harness (OET_WRITING_MODULE_PATHWAY.md §33).
    /// Tables: WritingCalibrationLetters (gold-standard fixtures) +
    /// WritingCalibrationRuns (per-run aggregate metrics + raw per-letter
    /// deltas in <c>ResultsJson</c>). Hand-written; do not regenerate.
    /// </summary>
    public partial class AddWritingCalibration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "WritingCalibrationLetters",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ScenarioId = table.Column<Guid>(type: "uuid", nullable: false),
                    LetterContent = table.Column<string>(type: "text", nullable: false),
                    ExpectedC1 = table.Column<short>(type: "smallint", nullable: false),
                    ExpectedC2 = table.Column<short>(type: "smallint", nullable: false),
                    ExpectedC3 = table.Column<short>(type: "smallint", nullable: false),
                    ExpectedC4 = table.Column<short>(type: "smallint", nullable: false),
                    ExpectedC5 = table.Column<short>(type: "smallint", nullable: false),
                    ExpectedC6 = table.Column<short>(type: "smallint", nullable: false),
                    ExpectedRawTotal = table.Column<short>(type: "smallint", nullable: false),
                    ExpectedBandLabel = table.Column<string>(type: "character varying(4)", maxLength: 4, nullable: false),
                    DifficultyRating = table.Column<int>(type: "integer", nullable: false),
                    AuthoredById = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                },
                constraints: table => table.PrimaryKey("PK_WritingCalibrationLetters", x => x.Id));

            migrationBuilder.CreateIndex(
                name: "IX_WritingCalibrationLetters_ScenarioId",
                table: "WritingCalibrationLetters",
                column: "ScenarioId");

            migrationBuilder.CreateIndex(
                name: "IX_WritingCalibrationLetters_DifficultyRating",
                table: "WritingCalibrationLetters",
                column: "DifficultyRating");

            migrationBuilder.CreateTable(
                name: "WritingCalibrationRuns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RunByUserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    TotalLetters = table.Column<int>(type: "integer", nullable: false),
                    AgreementCount = table.Column<int>(type: "integer", nullable: false),
                    MeanAbsoluteError = table.Column<decimal>(type: "numeric(4,2)", nullable: false, defaultValue: 0m),
                    CorrelationCoefficient = table.Column<decimal>(type: "numeric(4,3)", nullable: false, defaultValue: 0m),
                    ResultsJson = table.Column<string>(type: "text", nullable: false, defaultValue: "[]"),
                },
                constraints: table => table.PrimaryKey("PK_WritingCalibrationRuns", x => x.Id));

            migrationBuilder.CreateIndex(
                name: "IX_WritingCalibrationRuns_StartedAt",
                table: "WritingCalibrationRuns",
                column: "StartedAt",
                descending: new[] { true });

            migrationBuilder.CreateIndex(
                name: "IX_WritingCalibrationRuns_RunByUserId",
                table: "WritingCalibrationRuns",
                column: "RunByUserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "WritingCalibrationRuns");
            migrationBuilder.DropTable(name: "WritingCalibrationLetters");
        }
    }
}
