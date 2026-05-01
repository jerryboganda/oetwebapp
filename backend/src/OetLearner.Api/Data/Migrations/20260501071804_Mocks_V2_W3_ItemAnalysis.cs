using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OetLearner.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class Mocks_V2_W3_ItemAnalysis : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MockItemAnalysisSnapshots",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    MockBundleId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ItemId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SubtestCode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Label = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: true),
                    TotalAttempts = table.Column<int>(type: "integer", nullable: false),
                    CorrectCount = table.Column<int>(type: "integer", nullable: false),
                    Difficulty = table.Column<double>(type: "double precision", nullable: false),
                    DistractorJson = table.Column<string>(type: "text", nullable: false),
                    Flag = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    GeneratedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MockItemAnalysisSnapshots", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MockItemAnalysisSnapshots_MockBundleId",
                table: "MockItemAnalysisSnapshots",
                column: "MockBundleId");

            migrationBuilder.CreateIndex(
                name: "IX_MockItemAnalysisSnapshots_MockBundleId_SubtestCode",
                table: "MockItemAnalysisSnapshots",
                columns: new[] { "MockBundleId", "SubtestCode" });

            migrationBuilder.CreateIndex(
                name: "UX_MockItemAnalysis_Bundle_Item",
                table: "MockItemAnalysisSnapshots",
                columns: new[] { "MockBundleId", "ItemId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MockItemAnalysisSnapshots");
        }
    }
}
