using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using OetLearner.Api.Data;

#nullable disable

namespace OetLearner.Api.Data.Migrations
{
    // Owner directive 2026-07-18: per-user Video Library allocation.
    //
    // Adds UserVideoAccesses — the per-USER video allow-list that mirrors
    // UserMaterialFolderAccesses (see 20260727090000). When a learner has ANY
    // rows, their Video Library is RESTRICTED to those video ids; no rows == the
    // module's full grant unchanged (fail-open). Enforced in
    // VideoLibraryLearnerService (listing) + VideoEntitlementService (playback gate).
    //
    // HAND-AUTHORED (repo convention): `dotnet ef migrations add` diffs against the
    // intentionally-stale LearnerDbContextModelSnapshot and would re-emit unrelated
    // already-shipped schema. This migration contains ONLY the new table. The model
    // snapshot is left as-is; the runtime model comes from the entity classes.
    // Future-dated id (…0803) keeps it ordered AFTER 20260801090000.
    //
    // SAFETY: purely additive — one brand-new table. No backfill, no destructive
    // change; forward-only but Down() is provided.
    [DbContext(typeof(LearnerDbContext))]
    [Migration("20260803090000_AddUserVideoAccess")]
    public partial class AddUserVideoAccess : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "UserVideoAccesses",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    UserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    VideoId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserVideoAccesses", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_UserVideoAccesses_UserId",
                table: "UserVideoAccesses",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_UserVideoAccesses_UserId_VideoId",
                table: "UserVideoAccesses",
                columns: new[] { "UserId", "VideoId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "UserVideoAccesses");
        }
    }
}
