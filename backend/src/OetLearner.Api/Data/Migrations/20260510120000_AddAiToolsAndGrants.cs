using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Infrastructure;
using OetLearner.Api.Data;

#nullable disable

namespace OetLearner.Api.Data.Migrations
{
    /// <summary>
    /// Phase 5 — Tool calling. Introduces the deny-by-default tool catalog
    /// (<c>AiTools</c>), per-feature opt-in grants
    /// (<c>AiFeatureToolGrants</c>), and the audit projection
    /// (<c>AiToolInvocations</c>) keyed back to <c>AiUsageRecords.Id</c>.
    /// See docs/AI-COPILOT-TOOLS-PRD.md.
    ///
    /// IMPORTANT: this migration class carries both [DbContext] and
    /// [Migration] attributes — without them EF discovery silently no-ops
    /// on production boot. See repo:memory/migration-drift-note.md for the
    /// 2026-05-09 incident that taught us this lesson.
    /// </summary>
    [DbContext(typeof(LearnerDbContext))]
    [Migration("20260510120000_AddAiToolsAndGrants")]
    public partial class AddAiToolsAndGrants : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AiTools",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Description = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false, defaultValue: ""),
                    Category = table.Column<int>(type: "integer", nullable: false),
                    JsonSchemaArgs = table.Column<string>(type: "character varying(8192)", maxLength: 8192, nullable: false, defaultValue: "{}"),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedByAdminId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AiTools", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AiTools_Code",
                table: "AiTools",
                column: "Code",
                unique: true);

            migrationBuilder.CreateTable(
                name: "AiFeatureToolGrants",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    FeatureCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ToolCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedByAdminId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AiFeatureToolGrants", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AiFeatureToolGrants_FeatureCode_ToolCode",
                table: "AiFeatureToolGrants",
                columns: new[] { "FeatureCode", "ToolCode" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AiFeatureToolGrants_FeatureCode",
                table: "AiFeatureToolGrants",
                column: "FeatureCode");

            migrationBuilder.CreateTable(
                name: "AiToolInvocations",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    AiUsageRecordId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    FeatureCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ToolCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Category = table.Column<int>(type: "integer", nullable: false),
                    UserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    TurnIndex = table.Column<int>(type: "integer", nullable: false),
                    ArgsHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false, defaultValue: ""),
                    ResultHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false, defaultValue: ""),
                    Outcome = table.Column<int>(type: "integer", nullable: false),
                    ErrorCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ErrorMessage = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    LatencyMs = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AiToolInvocations", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AiToolInvocations_AiUsageRecordId_TurnIndex",
                table: "AiToolInvocations",
                columns: new[] { "AiUsageRecordId", "TurnIndex" });

            migrationBuilder.CreateIndex(
                name: "IX_AiToolInvocations_FeatureCode_CreatedAt",
                table: "AiToolInvocations",
                columns: new[] { "FeatureCode", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_AiToolInvocations_ToolCode_CreatedAt",
                table: "AiToolInvocations",
                columns: new[] { "ToolCode", "CreatedAt" });

            // ── User-scoped item tables backing the two write tools ──
            migrationBuilder.CreateTable(
                name: "UserNotes",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    UserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Title = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    BodyMarkdown = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    Source = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false, defaultValue: "user"),
                    CreatedByFeatureCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserNotes", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_UserNotes_UserId_CreatedAt",
                table: "UserNotes",
                columns: new[] { "UserId", "CreatedAt" });

            migrationBuilder.CreateTable(
                name: "RecallBookmarks",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    UserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    VocabularyTermId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Source = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false, defaultValue: "user"),
                    CreatedByFeatureCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RecallBookmarks", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RecallBookmarks_UserId_VocabularyTermId",
                table: "RecallBookmarks",
                columns: new[] { "UserId", "VocabularyTermId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RecallBookmarks_UserId_CreatedAt",
                table: "RecallBookmarks",
                columns: new[] { "UserId", "CreatedAt" });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "RecallBookmarks");
            migrationBuilder.DropTable(name: "UserNotes");
            migrationBuilder.DropTable(name: "AiToolInvocations");
            migrationBuilder.DropTable(name: "AiFeatureToolGrants");
            migrationBuilder.DropTable(name: "AiTools");
        }
    }
}
