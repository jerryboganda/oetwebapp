using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OetLearner.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class PlacementIntegration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "PlacementEnabled",
                table: "RuntimeSettings",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "PlacementBetaOnly",
                table: "RuntimeSettings",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PlacementBetaEmails",
                table: "RuntimeSettings",
                type: "character varying(2048)",
                maxLength: 2048,
                nullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "ProfessionId",
                table: "LearnerRegistrationProfiles",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(32)",
                oldMaxLength: 32);

            migrationBuilder.AlterColumn<string>(
                name: "ExamTypeId",
                table: "LearnerRegistrationProfiles",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(32)",
                oldMaxLength: 32);

            migrationBuilder.AlterColumn<string>(
                name: "CountryTarget",
                table: "LearnerRegistrationProfiles",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(64)",
                oldMaxLength: 64);

            migrationBuilder.AddColumn<string>(
                name: "RegistrationPurpose",
                table: "LearnerRegistrationProfiles",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            // The four CompanionSources columns exist on environments where
            // the companion feature was already deployed (prod got them
            // before any migration carried them) — add each only when
            // missing so this migration is idempotent against both shapes.
            migrationBuilder.Sql(@"
ALTER TABLE ""CompanionSources"" ADD COLUMN IF NOT EXISTS ""PackageScope"" character varying(32);
ALTER TABLE ""CompanionSources"" ADD COLUMN IF NOT EXISTS ""SourceUrl"" character varying(1024);
ALTER TABLE ""CompanionSources"" ADD COLUMN IF NOT EXISTS ""VerifiedAt"" timestamp with time zone;
ALTER TABLE ""CompanionSources"" ADD COLUMN IF NOT EXISTS ""VerifiedByUserId"" character varying(64);");

            migrationBuilder.CreateTable(
                name: "PlacementResults",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    LearnerUserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SessionId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    RulesetVersion = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ResultJson = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlacementResults", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PlacementResults_LearnerUserId_CreatedAt",
                table: "PlacementResults",
                columns: new[] { "LearnerUserId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_PlacementResults_SessionId",
                table: "PlacementResults",
                column: "SessionId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PlacementResults");

            migrationBuilder.DropColumn(
                name: "PlacementEnabled",
                table: "RuntimeSettings");

            migrationBuilder.DropColumn(
                name: "PlacementBetaOnly",
                table: "RuntimeSettings");

            migrationBuilder.DropColumn(
                name: "PlacementBetaEmails",
                table: "RuntimeSettings");

            migrationBuilder.DropColumn(
                name: "RegistrationPurpose",
                table: "LearnerRegistrationProfiles");

            // CompanionSources columns are NOT dropped on downgrade: on
            // environments that already had them (pre-dating this migration)
            // dropping would destroy live data. The columns are nullable and
            // harmless when the feature is absent.

            migrationBuilder.AlterColumn<string>(
                name: "ProfessionId",
                table: "LearnerRegistrationProfiles",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "character varying(32)",
                oldMaxLength: 32,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "ExamTypeId",
                table: "LearnerRegistrationProfiles",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "character varying(32)",
                oldMaxLength: 32,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "CountryTarget",
                table: "LearnerRegistrationProfiles",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "character varying(64)",
                oldMaxLength: 64,
                oldNullable: true);
        }
    }
}
