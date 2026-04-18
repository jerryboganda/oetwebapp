using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OetLearner.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddAdminUploadSessions : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AdminUploadSessions",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    AdminUserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    OriginalFilename = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Extension = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    DeclaredMimeType = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    DeclaredSizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    ReceivedBytes = table.Column<long>(type: "bigint", nullable: false),
                    TotalParts = table.Column<int>(type: "integer", nullable: false),
                    PartsReceived = table.Column<int>(type: "integer", nullable: false),
                    IntendedRole = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    State = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    MediaAssetId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AdminUploadSessions", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AdminUploadSessions_AdminUserId_ExpiresAt",
                table: "AdminUploadSessions",
                columns: new[] { "AdminUserId", "ExpiresAt" });

            migrationBuilder.CreateIndex(
                name: "IX_AdminUploadSessions_State_ExpiresAt",
                table: "AdminUploadSessions",
                columns: new[] { "State", "ExpiresAt" });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "AdminUploadSessions");
        }
    }
}
