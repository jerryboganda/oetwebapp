using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OetLearner.Api.Data.Migrations
{
    /// <summary>
    /// 50-letter calibration harness schema (Writing Module spec §33).
    /// Adds three tables — <c>WritingCalibrationLetters</c>,
    /// <c>WritingCalibrationRuns</c>, <c>WritingCalibrationResults</c> —
    /// used by <c>WritingCalibrationService</c> to measure AI agreement
    /// (±2 raw points) against Dr Ahmed's reference grades before a new
    /// model build is promoted.
    /// </summary>
    public partial class AddWritingCalibration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ── WritingCalibrationLetters ───────────────────────────────
            migrationBuilder.CreateTable(
                name: "WritingCalibrationLetters",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ScenarioId = table.Column<Guid>(type: "uuid", nullable: false),
                    LetterContent = table.Column<string>(type: "text", nullable: false),
                    AuthorTier = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    DrAhmedGradeJson = table.Column<string>(type: "jsonb", nullable: false, defaultValue: "{}"),
                    AddedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    AddedById = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table => table.PrimaryKey("PK_WritingCalibrationLetters", x => x.Id));

            migrationBuilder.CreateIndex(
                name: "IX_WritingCalibrationLetters_AuthorTier",
                table: "WritingCalibrationLetters",
                column: "AuthorTier");

            migrationBuilder.CreateIndex(
                name: "IX_WritingCalibrationLetters_ScenarioId",
                table: "WritingCalibrationLetters",
                column: "ScenarioId");

            // ── WritingCalibrationRuns ──────────────────────────────────
            migrationBuilder.CreateTable(
                name: "WritingCalibrationRuns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RunDate = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ModelVersion = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    TotalLetters = table.Column<int>(type: "integer", nullable: false),
                    Within2PointsCount = table.Column<int>(type: "integer", nullable: false),
                    MeanAbsError = table.Column<double>(type: "double precision", nullable: false),
                    BandAgreementCount = table.Column<int>(type: "integer", nullable: false),
                    NotesMarkdown = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table => table.PrimaryKey("PK_WritingCalibrationRuns", x => x.Id));

            migrationBuilder.CreateIndex(
                name: "IX_WritingCalibrationRuns_RunDate",
                table: "WritingCalibrationRuns",
                column: "RunDate");

            migrationBuilder.CreateIndex(
                name: "IX_WritingCalibrationRuns_ModelVersion",
                table: "WritingCalibrationRuns",
                column: "ModelVersion");

            // ── WritingCalibrationResults ──────────────────────────────
            migrationBuilder.CreateTable(
                name: "WritingCalibrationResults",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RunId = table.Column<Guid>(type: "uuid", nullable: false),
                    CalibrationLetterId = table.Column<Guid>(type: "uuid", nullable: false),
                    AiGradeJson = table.Column<string>(type: "jsonb", nullable: false, defaultValue: "{}"),
                    AbsErrorRaw = table.Column<int>(type: "integer", nullable: false),
                    BandMatch = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table => table.PrimaryKey("PK_WritingCalibrationResults", x => x.Id));

            migrationBuilder.CreateIndex(
                name: "IX_WritingCalibrationResults_RunId_AbsErrorRaw",
                table: "WritingCalibrationResults",
                columns: new[] { "RunId", "AbsErrorRaw" });

            migrationBuilder.CreateIndex(
                name: "IX_WritingCalibrationResults_CalibrationLetterId",
                table: "WritingCalibrationResults",
                column: "CalibrationLetterId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "WritingCalibrationResults");
            migrationBuilder.DropTable(name: "WritingCalibrationRuns");
            migrationBuilder.DropTable(name: "WritingCalibrationLetters");
        }
    }
}
