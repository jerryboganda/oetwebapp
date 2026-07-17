using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OetWithDrHesham.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSpeakingSharedResources : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SpeakingSharedResources",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ProfessionId = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    MediaAssetId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    PublishedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    EffectiveFrom = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    UploadedByUserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SpeakingSharedResources", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SpeakingSharedResources_MediaAssets_MediaAssetId",
                        column: x => x.MediaAssetId,
                        principalTable: "MediaAssets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SpeakingSharedResources_Kind_Status",
                table: "SpeakingSharedResources",
                columns: new[] { "Kind", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_SpeakingSharedResources_MediaAssetId",
                table: "SpeakingSharedResources",
                column: "MediaAssetId");

            migrationBuilder.CreateIndex(
                name: "IX_SpeakingSharedResources_ProfessionId_Kind",
                table: "SpeakingSharedResources",
                columns: new[] { "ProfessionId", "Kind" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SpeakingSharedResources");
        }
    }
}
