using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OetLearner.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddReadingExtractionDraft : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ReadingExtractionDrafts",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    PaperId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    MediaAssetId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    ExtractedManifestJson = table.Column<string>(type: "text", nullable: true),
                    RawAiResponseJson = table.Column<string>(type: "character varying(65536)", maxLength: 65536, nullable: true),
                    Notes = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    IsStub = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedByAdminId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ResolvedByAdminId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ResolvedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReadingExtractionDrafts", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ReadingExtractionDrafts_CreatedAt",
                table: "ReadingExtractionDrafts",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_ReadingExtractionDrafts_PaperId_Status",
                table: "ReadingExtractionDrafts",
                columns: new[] { "PaperId", "Status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ReadingExtractionDrafts");
        }
    }
}
