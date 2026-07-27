using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using OetLearner.Api.Data;

#nullable disable

namespace OetLearner.Api.Data.Migrations
{
    /// <summary>
    /// Full Mock Speaking booking hardening (2026-07-27):
    ///  * <c>MockBookings.MockSectionId</c> — the MockSectionAttempt the
    ///    booking was made for (forwarded by the Speaking Gateway), so the
    ///    completion evidence gate can match per section, not just per attempt.
    ///  * <c>MockBookings.ZoomStatus</c> / <c>ZoomError</c> / <c>ZoomRetryCount</c>
    ///    — real-Zoom provisioning bookkeeping (mirrors PrivateSpeakingBooking);
    ///    replaces the old sandbox-id stamping with actual meetings created by
    ///    the MockBookingZoomCreate background job.
    /// </summary>
    [DbContext(typeof(LearnerDbContext))]
    [Migration("20260821090000_AddMockBookingZoomProvisioningAndSection")]
    public partial class AddMockBookingZoomProvisioningAndSection : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "MockSectionId",
                table: "MockBookings",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ZoomStatus",
                table: "MockBookings",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ZoomError",
                table: "MockBookings",
                type: "character varying(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ZoomRetryCount",
                table: "MockBookings",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "MockSectionId", table: "MockBookings");
            migrationBuilder.DropColumn(name: "ZoomStatus", table: "MockBookings");
            migrationBuilder.DropColumn(name: "ZoomError", table: "MockBookings");
            migrationBuilder.DropColumn(name: "ZoomRetryCount", table: "MockBookings");
        }
    }
}
