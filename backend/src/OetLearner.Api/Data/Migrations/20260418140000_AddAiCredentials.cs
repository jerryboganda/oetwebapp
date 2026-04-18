using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OetLearner.Api.Data.Migrations
{
    /// <inheritdoc />
    /// <remarks>
    /// Slice 3 of the AI Usage Management subsystem. Adds BYOK credential
    /// storage (encrypted via ASP.NET Data Protection) and learner
    /// preferences. See <c>docs/AI-USAGE-POLICY.md</c> §1 and §8.
    /// </remarks>
    public partial class AddAiCredentials : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "UserAiCredentials",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    UserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    AuthAccountId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ProviderCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    EncryptedKey = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: false),
                    KeyHint = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    ModelAllowlistCsv = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastUsedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastErrorAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastErrorCode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    CooldownUntil = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserAiCredentials", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserAiCredentials_ApplicationUserAccounts_AuthAccountId",
                        column: x => x.AuthAccountId,
                        principalTable: "ApplicationUserAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_UserAiCredentials_UserId_ProviderCode",
                table: "UserAiCredentials",
                columns: new[] { "UserId", "ProviderCode" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserAiCredentials_AuthAccountId",
                table: "UserAiCredentials",
                column: "AuthAccountId");

            migrationBuilder.CreateTable(
                name: "UserAiPreferences",
                columns: table => new
                {
                    UserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Mode = table.Column<int>(type: "integer", nullable: false),
                    AllowPlatformFallback = table.Column<bool>(type: "boolean", nullable: false),
                    PerFeatureOverridesJson = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserAiPreferences", x => x.UserId);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "UserAiPreferences");
            migrationBuilder.DropTable(name: "UserAiCredentials");
        }
    }
}
