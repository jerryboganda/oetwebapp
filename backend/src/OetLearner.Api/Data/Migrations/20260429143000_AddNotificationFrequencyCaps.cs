using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OetLearner.Api.Data.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(LearnerDbContext))]
    [Migration("20260429143000_AddNotificationFrequencyCaps")]
    public partial class AddNotificationFrequencyCaps : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "MaxDeliveriesPerDay",
                table: "NotificationPolicyOverrides",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "MaxDeliveriesPerHour",
                table: "NotificationPolicyOverrides",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_NotificationDeliveryAttempts_AuthAccountId_Channel_Status_AttemptedAt",
                table: "NotificationDeliveryAttempts",
                columns: new[] { "AuthAccountId", "Channel", "Status", "AttemptedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_NotificationDeliveryAttempts_AuthAccountId_Channel_Status_AttemptedAt",
                table: "NotificationDeliveryAttempts");

            migrationBuilder.DropColumn(name: "MaxDeliveriesPerDay", table: "NotificationPolicyOverrides");
            migrationBuilder.DropColumn(name: "MaxDeliveriesPerHour", table: "NotificationPolicyOverrides");
        }
    }
}