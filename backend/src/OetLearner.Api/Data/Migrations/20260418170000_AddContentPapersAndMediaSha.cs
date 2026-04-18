using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OetLearner.Api.Data.Migrations
{
    /// <inheritdoc />
    /// <remarks>
    /// Content Upload subsystem, Slice 1. Adds <c>ContentPapers</c> and
    /// <c>ContentPaperAssets</c> tables, and extends <c>MediaAssets</c>
    /// with SHA-256 and media-kind columns.
    ///
    /// See <c>docs/CONTENT-UPLOAD-PLAN.md</c>.
    /// </remarks>
    public partial class AddContentPapersAndMediaSha : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ── Extend MediaAssets ─────────────────────────────────────
            migrationBuilder.AddColumn<string>(
                name: "Sha256",
                table: "MediaAssets",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MediaKind",
                table: "MediaAssets",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_MediaAssets_Sha256",
                table: "MediaAssets",
                column: "Sha256");

            // ── ContentPapers ──────────────────────────────────────────
            migrationBuilder.CreateTable(
                name: "ContentPapers",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SubtestCode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Slug = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ProfessionId = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    AppliesToAllProfessions = table.Column<bool>(type: "boolean", nullable: false),
                    Difficulty = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    EstimatedDurationMinutes = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    PublishedRevisionId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    CardType = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    LetterType = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Priority = table.Column<int>(type: "integer", nullable: false),
                    TagsCsv = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    SourceProvenance = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    ExtractedTextJson = table.Column<string>(type: "text", nullable: false),
                    CreatedByAdminId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    PublishedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ArchivedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ContentPapers", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ContentPapers_SubtestCode_Status",
                table: "ContentPapers",
                columns: new[] { "SubtestCode", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_ContentPapers_ProfessionId_SubtestCode",
                table: "ContentPapers",
                columns: new[] { "ProfessionId", "SubtestCode" });

            migrationBuilder.CreateIndex(
                name: "IX_ContentPapers_CardType",
                table: "ContentPapers",
                column: "CardType");

            migrationBuilder.CreateIndex(
                name: "IX_ContentPapers_LetterType",
                table: "ContentPapers",
                column: "LetterType");

            migrationBuilder.CreateIndex(
                name: "IX_ContentPapers_Slug",
                table: "ContentPapers",
                column: "Slug",
                unique: true);

            // ── ContentPaperAssets ─────────────────────────────────────
            migrationBuilder.CreateTable(
                name: "ContentPaperAssets",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    PaperId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Role = table.Column<int>(type: "integer", nullable: false),
                    Part = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    MediaAssetId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    DisplayOrder = table.Column<int>(type: "integer", nullable: false),
                    IsPrimary = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ContentPaperAssets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ContentPaperAssets_ContentPapers_PaperId",
                        column: x => x.PaperId,
                        principalTable: "ContentPapers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ContentPaperAssets_MediaAssets_MediaAssetId",
                        column: x => x.MediaAssetId,
                        principalTable: "MediaAssets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ContentPaperAssets_PaperId_Role",
                table: "ContentPaperAssets",
                columns: new[] { "PaperId", "Role" });

            migrationBuilder.CreateIndex(
                name: "IX_ContentPaperAssets_MediaAssetId",
                table: "ContentPaperAssets",
                column: "MediaAssetId");

            // Partial-unique: only one primary per (Paper, Role, Part).
            // We express this on Postgres via a filtered index; in-memory /
            // SQLite providers skip this and rely on application-layer checks
            // in the paper service (Slice 3).
            migrationBuilder.Sql(
                "CREATE UNIQUE INDEX \"UX_PaperAsset_Primary_Per_RolePart\" " +
                "ON \"ContentPaperAssets\" (\"PaperId\", \"Role\", \"Part\") " +
                "WHERE \"IsPrimary\" = TRUE;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP INDEX IF EXISTS \"UX_PaperAsset_Primary_Per_RolePart\";");
            migrationBuilder.DropTable(name: "ContentPaperAssets");
            migrationBuilder.DropTable(name: "ContentPapers");
            migrationBuilder.DropIndex(name: "IX_MediaAssets_Sha256", table: "MediaAssets");
            migrationBuilder.DropColumn(name: "MediaKind", table: "MediaAssets");
            migrationBuilder.DropColumn(name: "Sha256", table: "MediaAssets");
        }
    }
}
