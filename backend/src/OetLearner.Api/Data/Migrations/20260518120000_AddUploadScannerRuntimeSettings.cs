using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OetLearner.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddUploadScannerRuntimeSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "UploadScannerFailClosedOnError",
                table: "RuntimeSettings",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UploadScannerHost",
                table: "RuntimeSettings",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "UploadScannerPort",
                table: "RuntimeSettings",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UploadScannerProvider",
                table: "RuntimeSettings",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "UploadScannerTimeoutSeconds",
                table: "RuntimeSettings",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "UploadScannerFailClosedOnError",
                table: "RuntimeSettings");

            migrationBuilder.DropColumn(
                name: "UploadScannerHost",
                table: "RuntimeSettings");

            migrationBuilder.DropColumn(
                name: "UploadScannerPort",
                table: "RuntimeSettings");

            migrationBuilder.DropColumn(
                name: "UploadScannerProvider",
                table: "RuntimeSettings");

            migrationBuilder.DropColumn(
                name: "UploadScannerTimeoutSeconds",
                table: "RuntimeSettings");
        }
    }
}
