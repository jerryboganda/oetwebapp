using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OetLearner.Api.Data.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(LearnerDbContext))]
    [Migration("20260429130000_AddNotificationConsentSuppression")]
    public partial class AddNotificationConsentSuppression : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CreatedByAdminId",
                table: "NotificationTemplates",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Description",
                table: "NotificationTemplates",
                type: "character varying(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "HtmlTemplate",
                table: "NotificationTemplates",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Locale",
                table: "NotificationTemplates",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "en");

            migrationBuilder.AddColumn<string>(
                name: "MetadataJson",
                table: "NotificationTemplates",
                type: "jsonb",
                nullable: false,
                defaultValue: "{}");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "PublishedAt",
                table: "NotificationTemplates",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TextTemplate",
                table: "NotificationTemplates",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UpdatedByAdminId",
                table: "NotificationTemplates",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Version",
                table: "NotificationTemplates",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.CreateTable(
                name: "NotificationConsents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AuthAccountId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Channel = table.Column<int>(type: "integer", nullable: false),
                    Category = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    IsGranted = table.Column<bool>(type: "boolean", nullable: false),
                    Source = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Reason = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    UpdatedByAdminId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    UpdatedByAdminName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    GrantedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RevokedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NotificationConsents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "NotificationSuppressions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AuthAccountId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Channel = table.Column<int>(type: "integer", nullable: false),
                    EventKey = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    ReasonCode = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Reason = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    CreatedByAdminId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreatedByAdminName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ReleasedByAdminId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ReleasedByAdminName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    StartsAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ReleasedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NotificationSuppressions", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_NotificationConsents_AuthAccount_Channel_Category",
                table: "NotificationConsents",
                columns: new[] { "AuthAccountId", "Channel", "Category" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_NotificationConsents_Channel_IsGranted_UpdatedAt",
                table: "NotificationConsents",
                columns: new[] { "Channel", "IsGranted", "UpdatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_NotificationSuppressions_Account_Channel_Event_Active",
                table: "NotificationSuppressions",
                columns: new[] { "AuthAccountId", "Channel", "EventKey", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_NotificationSuppressions_Channel_Active_ExpiresAt",
                table: "NotificationSuppressions",
                columns: new[] { "Channel", "IsActive", "ExpiresAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "NotificationConsents");
            migrationBuilder.DropTable(name: "NotificationSuppressions");

            migrationBuilder.DropColumn(name: "CreatedByAdminId", table: "NotificationTemplates");
            migrationBuilder.DropColumn(name: "Description", table: "NotificationTemplates");
            migrationBuilder.DropColumn(name: "HtmlTemplate", table: "NotificationTemplates");
            migrationBuilder.DropColumn(name: "Locale", table: "NotificationTemplates");
            migrationBuilder.DropColumn(name: "MetadataJson", table: "NotificationTemplates");
            migrationBuilder.DropColumn(name: "PublishedAt", table: "NotificationTemplates");
            migrationBuilder.DropColumn(name: "TextTemplate", table: "NotificationTemplates");
            migrationBuilder.DropColumn(name: "UpdatedByAdminId", table: "NotificationTemplates");
            migrationBuilder.DropColumn(name: "Version", table: "NotificationTemplates");
        }
    }
}