using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OetLearner.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class ExactAuthSocialPort : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                ALTER TABLE "ExpertReviewDrafts"
                ADD COLUMN IF NOT EXISTS "ChecklistItemsJson" text NOT NULL DEFAULT '';
                """);

            migrationBuilder.Sql(
                """
                ALTER TABLE "ExpertReviewDrafts"
                ADD COLUMN IF NOT EXISTS "ScratchpadJson" text NOT NULL DEFAULT '';
                """);

            migrationBuilder.Sql(
                """
                ALTER TABLE "Criteria"
                ADD COLUMN IF NOT EXISTS "Status" character varying(16) NOT NULL DEFAULT '';
                """);

            migrationBuilder.Sql(
                """
                ALTER TABLE "ApplicationUserAccounts"
                ADD COLUMN IF NOT EXISTS "ProtectedAuthenticatorSecret" character varying(1024);
                """);

            migrationBuilder.CreateTable(
                name: "ExternalIdentityLinks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ApplicationUserAccountId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Provider = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ProviderSubject = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    FirstName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    LastName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastSignedInAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExternalIdentityLinks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ExternalIdentityLinks_ApplicationUserAccounts_ApplicationUs~",
                        column: x => x.ApplicationUserAccountId,
                        principalTable: "ApplicationUserAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "LearnerRegistrationProfiles",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ApplicationUserAccountId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    LearnerUserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    FirstName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    LastName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ExamTypeId = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ProfessionId = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    SessionId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CountryTarget = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    MobileNumber = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    AgreeToTerms = table.Column<bool>(type: "boolean", nullable: false),
                    AgreeToPrivacy = table.Column<bool>(type: "boolean", nullable: false),
                    MarketingOptIn = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LearnerRegistrationProfiles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LearnerRegistrationProfiles_ApplicationUserAccounts_Applica~",
                        column: x => x.ApplicationUserAccountId,
                        principalTable: "ApplicationUserAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_LearnerRegistrationProfiles_Users_LearnerUserId",
                        column: x => x.LearnerUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SignupExamTypeCatalog",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Label = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Code = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Description = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SignupExamTypeCatalog", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SignupProfessionCatalog",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Label = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    CountryTargetsJson = table.Column<string>(type: "text", nullable: false),
                    ExamTypeIdsJson = table.Column<string>(type: "text", nullable: false),
                    Description = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SignupProfessionCatalog", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SignupSessionCatalog",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Name = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    ExamTypeId = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ProfessionIdsJson = table.Column<string>(type: "text", nullable: false),
                    PriceLabel = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    StartDate = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    EndDate = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    DeliveryMode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Capacity = table.Column<int>(type: "integer", nullable: false),
                    SeatsRemaining = table.Column<int>(type: "integer", nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SignupSessionCatalog", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ExternalIdentityLinks_ApplicationUserAccountId_Provider",
                table: "ExternalIdentityLinks",
                columns: new[] { "ApplicationUserAccountId", "Provider" });

            migrationBuilder.CreateIndex(
                name: "IX_ExternalIdentityLinks_Provider_ProviderSubject",
                table: "ExternalIdentityLinks",
                columns: new[] { "Provider", "ProviderSubject" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LearnerRegistrationProfiles_ApplicationUserAccountId",
                table: "LearnerRegistrationProfiles",
                column: "ApplicationUserAccountId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LearnerRegistrationProfiles_LearnerUserId",
                table: "LearnerRegistrationProfiles",
                column: "LearnerUserId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SignupExamTypeCatalog_IsActive_SortOrder",
                table: "SignupExamTypeCatalog",
                columns: new[] { "IsActive", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_SignupProfessionCatalog_IsActive_SortOrder",
                table: "SignupProfessionCatalog",
                columns: new[] { "IsActive", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_SignupSessionCatalog_IsActive_SortOrder",
                table: "SignupSessionCatalog",
                columns: new[] { "IsActive", "SortOrder" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ExternalIdentityLinks");

            migrationBuilder.DropTable(
                name: "LearnerRegistrationProfiles");

            migrationBuilder.DropTable(
                name: "SignupExamTypeCatalog");

            migrationBuilder.DropTable(
                name: "SignupProfessionCatalog");

            migrationBuilder.DropTable(
                name: "SignupSessionCatalog");

            migrationBuilder.DropColumn(
                name: "ChecklistItemsJson",
                table: "ExpertReviewDrafts");

            migrationBuilder.DropColumn(
                name: "ScratchpadJson",
                table: "ExpertReviewDrafts");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "Criteria");

            migrationBuilder.DropColumn(
                name: "ProtectedAuthenticatorSecret",
                table: "ApplicationUserAccounts");
        }
    }
}
