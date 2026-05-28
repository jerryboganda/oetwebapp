using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Infrastructure;
using OetLearner.Api.Data;

#nullable disable

namespace OetLearner.Api.Data.Migrations
{
    [DbContext(typeof(LearnerDbContext))]
    [Migration("20260613123000_AddPrivateSpeakingEntitlementsAndCalendar")]
    public partial class AddPrivateSpeakingEntitlementsAndCalendar : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "EntitlementConsumed",
                table: "PrivateSpeakingBookings",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "EntitlementConsumedAt",
                table: "PrivateSpeakingBookings",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EntitlementRestorationReason",
                table: "PrivateSpeakingBookings",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "EntitlementRestoredAt",
                table: "PrivateSpeakingBookings",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EntitlementSubscriptionId",
                table: "PrivateSpeakingBookings",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "GoogleCalendarEventId",
                table: "PrivateSpeakingBookings",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "GoogleCalendarSyncedAt",
                table: "PrivateSpeakingBookings",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "GoogleCalendarSyncError",
                table: "PrivateSpeakingBookings",
                type: "character varying(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "GoogleCalendarSyncStatus",
                table: "PrivateSpeakingBookings",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RescheduledToBookingId",
                table: "PrivateSpeakingBookings",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "PrivateSpeakingTutorCalendarConnections",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    TutorProfileId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ExpertUserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Provider = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CalendarId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    ConnectedEmail = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    RefreshTokenEncrypted = table.Column<string>(type: "text", nullable: true),
                    Scopes = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    ConnectedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    DisconnectedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastCheckedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastSyncedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastError = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PrivateSpeakingTutorCalendarConnections", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PrivateSpeakingTutorCalendarConnections_PrivateSpeakingTutorProfiles_TutorProfileId",
                        column: x => x.TutorProfileId,
                        principalTable: "PrivateSpeakingTutorProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PrivateSpeakingBookings_EntitlementSubscriptionId",
                table: "PrivateSpeakingBookings",
                column: "EntitlementSubscriptionId");

            migrationBuilder.CreateIndex(
                name: "IX_PrivateSpeakingBookings_GoogleCalendarEventId",
                table: "PrivateSpeakingBookings",
                column: "GoogleCalendarEventId");

            migrationBuilder.CreateIndex(
                name: "IX_PrivateSpeakingTutorCalendarConnections_ExpertUserId",
                table: "PrivateSpeakingTutorCalendarConnections",
                column: "ExpertUserId");

            migrationBuilder.CreateIndex(
                name: "IX_PrivateSpeakingTutorCalendarConnections_TutorProfileId",
                table: "PrivateSpeakingTutorCalendarConnections",
                column: "TutorProfileId",
                unique: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "PrivateSpeakingTutorCalendarConnections");

            migrationBuilder.DropIndex(
                name: "IX_PrivateSpeakingBookings_EntitlementSubscriptionId",
                table: "PrivateSpeakingBookings");

            migrationBuilder.DropIndex(
                name: "IX_PrivateSpeakingBookings_GoogleCalendarEventId",
                table: "PrivateSpeakingBookings");

            migrationBuilder.DropColumn(name: "EntitlementConsumed", table: "PrivateSpeakingBookings");
            migrationBuilder.DropColumn(name: "EntitlementConsumedAt", table: "PrivateSpeakingBookings");
            migrationBuilder.DropColumn(name: "EntitlementRestorationReason", table: "PrivateSpeakingBookings");
            migrationBuilder.DropColumn(name: "EntitlementRestoredAt", table: "PrivateSpeakingBookings");
            migrationBuilder.DropColumn(name: "EntitlementSubscriptionId", table: "PrivateSpeakingBookings");
            migrationBuilder.DropColumn(name: "GoogleCalendarEventId", table: "PrivateSpeakingBookings");
            migrationBuilder.DropColumn(name: "GoogleCalendarSyncedAt", table: "PrivateSpeakingBookings");
            migrationBuilder.DropColumn(name: "GoogleCalendarSyncError", table: "PrivateSpeakingBookings");
            migrationBuilder.DropColumn(name: "GoogleCalendarSyncStatus", table: "PrivateSpeakingBookings");
            migrationBuilder.DropColumn(name: "RescheduledToBookingId", table: "PrivateSpeakingBookings");
        }
    }
}
