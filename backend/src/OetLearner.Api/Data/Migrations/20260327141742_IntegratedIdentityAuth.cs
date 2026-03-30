using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OetLearner.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class IntegratedIdentityAuth : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AccountStatus",
                table: "Users",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AuthAccountId",
                table: "Users",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AuthAccountId",
                table: "ExpertUsers",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AutosaveErrorState",
                table: "ExpertReviewDrafts",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ActorAuthAccountId",
                table: "AuditEvents",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ApplicationUserAccounts",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    NormalizedEmail = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    PasswordHash = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    Role = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    EmailVerifiedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    AuthenticatorEnabledAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastLoginAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApplicationUserAccounts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "EmailOtpChallenges",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ApplicationUserAccountId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Purpose = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CodeHash = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    AttemptCount = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    VerifiedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmailOtpChallenges", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EmailOtpChallenges_ApplicationUserAccounts_ApplicationUserA~",
                        column: x => x.ApplicationUserAccountId,
                        principalTable: "ApplicationUserAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MfaRecoveryCodes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ApplicationUserAccountId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CodeHash = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RedeemedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MfaRecoveryCodes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MfaRecoveryCodes_ApplicationUserAccounts_ApplicationUserAcc~",
                        column: x => x.ApplicationUserAccountId,
                        principalTable: "ApplicationUserAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RefreshTokenRecords",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ApplicationUserAccountId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    TokenHash = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RevokedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastUsedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RefreshTokenRecords", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RefreshTokenRecords_ApplicationUserAccounts_ApplicationUser~",
                        column: x => x.ApplicationUserAccountId,
                        principalTable: "ApplicationUserAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Users_AuthAccountId",
                table: "Users",
                column: "AuthAccountId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ExpertUsers_AuthAccountId",
                table: "ExpertUsers",
                column: "AuthAccountId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AuditEvents_ActorAuthAccountId",
                table: "AuditEvents",
                column: "ActorAuthAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_ApplicationUserAccounts_NormalizedEmail",
                table: "ApplicationUserAccounts",
                column: "NormalizedEmail",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ApplicationUserAccounts_NormalizedEmail_Role",
                table: "ApplicationUserAccounts",
                columns: new[] { "NormalizedEmail", "Role" });

            migrationBuilder.CreateIndex(
                name: "IX_EmailOtpChallenges_ApplicationUserAccountId_Purpose_Expires~",
                table: "EmailOtpChallenges",
                columns: new[] { "ApplicationUserAccountId", "Purpose", "ExpiresAt" });

            migrationBuilder.CreateIndex(
                name: "IX_MfaRecoveryCodes_ApplicationUserAccountId",
                table: "MfaRecoveryCodes",
                column: "ApplicationUserAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_RefreshTokenRecords_ApplicationUserAccountId_ExpiresAt",
                table: "RefreshTokenRecords",
                columns: new[] { "ApplicationUserAccountId", "ExpiresAt" });

            migrationBuilder.CreateIndex(
                name: "IX_RefreshTokenRecords_ApplicationUserAccountId_TokenHash",
                table: "RefreshTokenRecords",
                columns: new[] { "ApplicationUserAccountId", "TokenHash" },
                unique: true);

            migrationBuilder.Sql("UPDATE \"Users\" SET \"AccountStatus\" = 'active' WHERE \"AccountStatus\" IS NULL;");

            migrationBuilder.AlterColumn<string>(
                name: "AccountStatus",
                table: "Users",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(32)",
                oldMaxLength: 32,
                oldNullable: true);

            migrationBuilder.AddForeignKey(
                name: "FK_AuditEvents_ApplicationUserAccounts_ActorAuthAccountId",
                table: "AuditEvents",
                column: "ActorAuthAccountId",
                principalTable: "ApplicationUserAccounts",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_ExpertUsers_ApplicationUserAccounts_AuthAccountId",
                table: "ExpertUsers",
                column: "AuthAccountId",
                principalTable: "ApplicationUserAccounts",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_Users_ApplicationUserAccounts_AuthAccountId",
                table: "Users",
                column: "AuthAccountId",
                principalTable: "ApplicationUserAccounts",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AuditEvents_ApplicationUserAccounts_ActorAuthAccountId",
                table: "AuditEvents");

            migrationBuilder.DropForeignKey(
                name: "FK_ExpertUsers_ApplicationUserAccounts_AuthAccountId",
                table: "ExpertUsers");

            migrationBuilder.DropForeignKey(
                name: "FK_Users_ApplicationUserAccounts_AuthAccountId",
                table: "Users");

            migrationBuilder.DropTable(
                name: "EmailOtpChallenges");

            migrationBuilder.DropTable(
                name: "MfaRecoveryCodes");

            migrationBuilder.DropTable(
                name: "RefreshTokenRecords");

            migrationBuilder.DropTable(
                name: "ApplicationUserAccounts");

            migrationBuilder.DropIndex(
                name: "IX_Users_AuthAccountId",
                table: "Users");

            migrationBuilder.DropIndex(
                name: "IX_ExpertUsers_AuthAccountId",
                table: "ExpertUsers");

            migrationBuilder.DropIndex(
                name: "IX_AuditEvents_ActorAuthAccountId",
                table: "AuditEvents");

            migrationBuilder.DropColumn(
                name: "AccountStatus",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "AuthAccountId",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "AuthAccountId",
                table: "ExpertUsers");

            migrationBuilder.DropColumn(
                name: "AutosaveErrorState",
                table: "ExpertReviewDrafts");

            migrationBuilder.DropColumn(
                name: "ActorAuthAccountId",
                table: "AuditEvents");
        }
    }
}
