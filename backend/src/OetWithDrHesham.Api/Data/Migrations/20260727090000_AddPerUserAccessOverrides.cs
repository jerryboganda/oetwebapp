using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using OetWithDrHesham.Api.Data;

#nullable disable

namespace OetWithDrHesham.Api.Data.Migrations
{
    // Owner directive 2026-07-12: admin "Add User" + per-user allocation.
    //
    // Adds the schema for per-USER access control that layers on top of the
    // existing per-PLAN entitlements:
    //   * Users.AccessExpiresAt — master login-expiry gate (null = never).
    //   * UserModuleOverrides — per-user enable/disable of the 4 dashboard
    //     modules (Recalls / MaterialsLibrary / VideoLibrary / Mocks).
    //   * UserMaterialFolderAccesses — per-user Materials folder allow-list.
    //   * UserRecallSetAccesses — per-user Recall-set allow-list.
    //
    // HAND-AUTHORED (repo convention, see 20260726090000): `dotnet ef migrations
    // add` diffs against the intentionally-stale LearnerDbContextModelSnapshot and
    // would re-emit unrelated already-shipped schema (video library, Bunny
    // settings, ContextIntro, ...). This migration therefore contains ONLY the new
    // objects. The model snapshot is left as-is (same as prior hand-authored
    // migrations); the runtime model comes from the entity classes, not the
    // snapshot. Future-dated id (…0727) keeps it ordered AFTER 20260726090000.
    //
    // SAFETY: purely additive — one nullable column + three brand-new tables. No
    // backfill, no destructive change; forward-only but Down() is provided.
    [DbContext(typeof(LearnerDbContext))]
    [Migration("20260727090000_AddPerUserAccessOverrides")]
    public partial class AddPerUserAccessOverrides : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "AccessExpiresAt",
                table: "Users",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "UserModuleOverrides",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    UserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ModuleKey = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserModuleOverrides", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "UserMaterialFolderAccesses",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    UserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    FolderId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserMaterialFolderAccesses", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "UserRecallSetAccesses",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    UserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    RecallSetCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserRecallSetAccesses", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_UserModuleOverrides_UserId",
                table: "UserModuleOverrides",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_UserModuleOverrides_UserId_ModuleKey",
                table: "UserModuleOverrides",
                columns: new[] { "UserId", "ModuleKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserMaterialFolderAccesses_UserId",
                table: "UserMaterialFolderAccesses",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_UserMaterialFolderAccesses_UserId_FolderId",
                table: "UserMaterialFolderAccesses",
                columns: new[] { "UserId", "FolderId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserRecallSetAccesses_UserId",
                table: "UserRecallSetAccesses",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_UserRecallSetAccesses_UserId_RecallSetCode",
                table: "UserRecallSetAccesses",
                columns: new[] { "UserId", "RecallSetCode" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "UserModuleOverrides");
            migrationBuilder.DropTable(name: "UserMaterialFolderAccesses");
            migrationBuilder.DropTable(name: "UserRecallSetAccesses");

            migrationBuilder.DropColumn(
                name: "AccessExpiresAt",
                table: "Users");
        }
    }
}
