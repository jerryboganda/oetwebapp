using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OetLearner.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddNotificationAndSignupSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "NotificationDeliveryAttempts",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    NotificationEventId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    AuthAccountId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Channel = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    SubscriptionId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Provider = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    MessageId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    ErrorCode = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    ErrorMessage = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    ResponsePayloadJson = table.Column<string>(type: "text", nullable: false),
                    AttemptedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NotificationDeliveryAttempts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "NotificationEvents",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    RecipientAuthAccountId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    RecipientRole = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    EventKey = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Category = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Title = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Body = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: false),
                    ActionUrl = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    Severity = table.Column<int>(type: "integer", nullable: false),
                    State = table.Column<int>(type: "integer", nullable: false),
                    EntityType = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    EntityId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    VersionOrDateBucket = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    DedupeKey = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    PayloadJson = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ProcessedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    FanoutAttempts = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NotificationEvents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "NotificationInboxItems",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    NotificationEventId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    AuthAccountId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    EventKey = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Category = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Title = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Body = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: false),
                    ActionUrl = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    Severity = table.Column<int>(type: "integer", nullable: false),
                    IsRead = table.Column<bool>(type: "boolean", nullable: false),
                    ChannelsJson = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ReadAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NotificationInboxItems", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "NotificationPolicyOverrides",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AudienceRole = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    EventKey = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    InAppEnabled = table.Column<bool>(type: "boolean", nullable: true),
                    EmailEnabled = table.Column<bool>(type: "boolean", nullable: true),
                    PushEnabled = table.Column<bool>(type: "boolean", nullable: true),
                    EmailMode = table.Column<int>(type: "integer", nullable: true),
                    UpdatedByAdminId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    UpdatedByAdminName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NotificationPolicyOverrides", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "NotificationPreferences",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AuthAccountId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Timezone = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    GlobalInAppEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    GlobalEmailEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    GlobalPushEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    QuietHoursEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    QuietHoursStartMinutes = table.Column<int>(type: "integer", nullable: true),
                    QuietHoursEndMinutes = table.Column<int>(type: "integer", nullable: true),
                    EventOverridesJson = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NotificationPreferences", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PushSubscriptions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AuthAccountId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Endpoint = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    P256dh = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    Auth = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    UserAgent = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    FailureReasonCode = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastSuccessfulAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastFailureAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PushSubscriptions", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_NotificationDeliveryAttempts_NotificationEventId_Channel_At~",
                table: "NotificationDeliveryAttempts",
                columns: new[] { "NotificationEventId", "Channel", "AttemptedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_NotificationDeliveryAttempts_Status_AttemptedAt",
                table: "NotificationDeliveryAttempts",
                columns: new[] { "Status", "AttemptedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_NotificationEvents_DedupeKey",
                table: "NotificationEvents",
                column: "DedupeKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_NotificationEvents_RecipientAuthAccountId_CreatedAt",
                table: "NotificationEvents",
                columns: new[] { "RecipientAuthAccountId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_NotificationEvents_State_CreatedAt",
                table: "NotificationEvents",
                columns: new[] { "State", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_NotificationInboxItems_AuthAccountId_IsRead_CreatedAt",
                table: "NotificationInboxItems",
                columns: new[] { "AuthAccountId", "IsRead", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_NotificationInboxItems_NotificationEventId",
                table: "NotificationInboxItems",
                column: "NotificationEventId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_NotificationPolicyOverrides_AudienceRole_EventKey",
                table: "NotificationPolicyOverrides",
                columns: new[] { "AudienceRole", "EventKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_NotificationPreferences_AuthAccountId",
                table: "NotificationPreferences",
                column: "AuthAccountId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PushSubscriptions_AuthAccountId_IsActive",
                table: "PushSubscriptions",
                columns: new[] { "AuthAccountId", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_PushSubscriptions_Endpoint",
                table: "PushSubscriptions",
                column: "Endpoint",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "NotificationDeliveryAttempts");

            migrationBuilder.DropTable(
                name: "NotificationEvents");

            migrationBuilder.DropTable(
                name: "NotificationInboxItems");

            migrationBuilder.DropTable(
                name: "NotificationPolicyOverrides");

            migrationBuilder.DropTable(
                name: "NotificationPreferences");

            migrationBuilder.DropTable(
                name: "PushSubscriptions");
        }
    }
}
