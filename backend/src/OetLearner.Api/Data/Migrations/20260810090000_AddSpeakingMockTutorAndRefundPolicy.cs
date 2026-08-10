using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OetLearner.Api.Data.Migrations;

/// <summary>
/// Stores the canonical tutor-calendar assignment and the auditable refund
/// decision for Full Mock Speaking bookings.
/// </summary>
public partial class AddSpeakingMockTutorAndRefundPolicy : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
            migrationBuilder.AddColumn<string>(
                name: "TutorProfileId",
                table: "MockBookings",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EntitlementReferenceId",
                table: "MockBookings",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EntitlementSource",
                table: "MockBookings",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "RefundDecision",
            table: "MockBookings",
            type: "character varying(32)",
            maxLength: 32,
            nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "RefundIssued",
                table: "MockBookings",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ReversedAt",
                table: "MockEntitlementLedgers",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReversalReferenceId",
                table: "MockEntitlementLedgers",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

        migrationBuilder.CreateIndex(
            name: "IX_MockBookings_TutorProfileId_ScheduledStartAt",
            table: "MockBookings",
            columns: new[] { "TutorProfileId", "ScheduledStartAt" });

        migrationBuilder.Sql(
            "UPDATE \"PrivateSpeakingConfigs\" SET \"CancellationWindowHours\" = 24, \"CancellationPolicyText\" = 'You may cancel your Speaking session with a full refund if the cancellation is made more than 24 hours before the scheduled start time. If you cancel 24 hours or less before the session, a full refund is not available.', \"BookingPolicyText\" = 'You may reschedule your Speaking session any time before it starts, subject to an alternative slot currently available in the tutor calendar.' WHERE \"Id\" = 'ps-config-singleton';");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_MockBookings_TutorProfileId_ScheduledStartAt",
            table: "MockBookings");

        migrationBuilder.DropColumn(name: "TutorProfileId", table: "MockBookings");
        migrationBuilder.DropColumn(name: "EntitlementReferenceId", table: "MockBookings");
        migrationBuilder.DropColumn(name: "EntitlementSource", table: "MockBookings");
        migrationBuilder.DropColumn(name: "RefundDecision", table: "MockBookings");
        migrationBuilder.DropColumn(name: "RefundIssued", table: "MockBookings");
        migrationBuilder.DropColumn(name: "ReversedAt", table: "MockEntitlementLedgers");
        migrationBuilder.DropColumn(name: "ReversalReferenceId", table: "MockEntitlementLedgers");
    }
}
