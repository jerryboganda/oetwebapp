using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OetLearner.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddRulebookReferencePdf : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ReferencePdfAssetId",
                table: "RulebookVersions",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_RulebookVersions_ReferencePdfAssetId",
                table: "RulebookVersions",
                column: "ReferencePdfAssetId");

            migrationBuilder.AddForeignKey(
                name: "FK_RulebookVersions_MediaAssets_ReferencePdfAssetId",
                table: "RulebookVersions",
                column: "ReferencePdfAssetId",
                principalTable: "MediaAssets",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_RulebookVersions_MediaAssets_ReferencePdfAssetId",
                table: "RulebookVersions");

            migrationBuilder.DropIndex(
                name: "IX_RulebookVersions_ReferencePdfAssetId",
                table: "RulebookVersions");

            migrationBuilder.DropColumn(
                name: "ReferencePdfAssetId",
                table: "RulebookVersions");
        }
    }
}
