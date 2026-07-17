using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OetWithDrHesham.Api.Data.Migrations;

/// <inheritdoc />
// 2026-05-28 — restored EF recognition. This migration was missing its
// [DbContext]/[Migration] attributes (no companion .Designer.cs), so EF's
// migration scanner ignored it entirely. Re-added inline (matching the
// AddSpeakingFullRuntimeSettings pattern) so it is applied via the normal
// pipeline.
[DbContext(typeof(LearnerDbContext))]
[Migration("20260523120000_DropRecallDocuments")]
public partial class DropRecallDocuments : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // 2026-05-28 — idempotent drop. In some environments the
        // RecallDocuments table is already absent (recalls seeders were
        // permanently disabled and the table was removed via an EnsureCreated /
        // manual-apply divergence). A bare DropTable would throw "table does
        // not exist" and crash AutoMigrate on boot. DROP TABLE IF EXISTS does
        // the right thing everywhere — drops it where present (the original
        // intent), no-ops where already gone — and is safe for the future
        // production deploy too.
        migrationBuilder.Sql("DROP TABLE IF EXISTS \"RecallDocuments\";");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "RecallDocuments",
            columns: table => new
            {
                Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                ArchivedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                DescriptionMarkdown = table.Column<string>(type: "text", nullable: true),
                MediaAssetId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                PeriodLabel = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                ProfessionId = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                PublishedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                SortOrder = table.Column<int>(type: "integer", nullable: false),
                Status = table.Column<int>(type: "integer", nullable: false),
                SubtestCode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                UploadedByUserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_RecallDocuments", x => x.Id);
                table.ForeignKey(
                    name: "FK_RecallDocuments_MediaAssets_MediaAssetId",
                    column: x => x.MediaAssetId,
                    principalTable: "MediaAssets",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(name: "IX_RecallDocuments_MediaAssetId", table: "RecallDocuments", column: "MediaAssetId");
        migrationBuilder.CreateIndex(name: "IX_RecallDocuments_ProfessionId_Status", table: "RecallDocuments", columns: new[] { "ProfessionId", "Status" });
        migrationBuilder.CreateIndex(name: "IX_RecallDocuments_Status_SortOrder", table: "RecallDocuments", columns: new[] { "Status", "SortOrder" });
        migrationBuilder.CreateIndex(name: "IX_RecallDocuments_SubtestCode_Status", table: "RecallDocuments", columns: new[] { "SubtestCode", "Status" });
    }
}
