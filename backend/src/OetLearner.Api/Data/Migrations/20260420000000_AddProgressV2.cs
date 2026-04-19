using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OetLearner.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddProgressV2 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ProgressPolicies",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ExamFamilyCode = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    DefaultTimeRange = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    SmoothingWindow = table.Column<int>(type: "integer", nullable: false),
                    MinCohortSize = table.Column<int>(type: "integer", nullable: false),
                    MockDistinctStyle = table.Column<bool>(type: "boolean", nullable: false),
                    ShowScoreGuaranteeStrip = table.Column<bool>(type: "boolean", nullable: false),
                    ShowCriterionConfidenceBand = table.Column<bool>(type: "boolean", nullable: false),
                    MinEvaluationsForTrend = table.Column<int>(type: "integer", nullable: false),
                    ExportPdfEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    UpdatedByAdminId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table => table.PrimaryKey("PK_ProgressPolicies", x => x.Id));

            migrationBuilder.CreateIndex(
                name: "IX_ProgressPolicies_ExamFamilyCode",
                table: "ProgressPolicies",
                column: "ExamFamilyCode",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "ProgressPolicies");
        }
    }
}
