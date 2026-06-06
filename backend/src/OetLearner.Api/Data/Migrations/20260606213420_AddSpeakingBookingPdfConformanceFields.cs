using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OetLearner.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSpeakingBookingPdfConformanceFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ReminderOffsetsMinutesJson",
                table: "PrivateSpeakingConfigs",
                type: "character varying(256)",
                maxLength: 256,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "RescheduleFreeWindowHours",
                table: "PrivateSpeakingConfigs",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "RescheduleSameDayPenaltyPercent",
                table: "PrivateSpeakingConfigs",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "AiFeedbackStatus",
                table: "PrivateSpeakingBookings",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "AttendanceJoinedAt",
                table: "PrivateSpeakingBookings",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "AttendanceLeftAt",
                table: "PrivateSpeakingBookings",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "AttendanceVerified",
                table: "PrivateSpeakingBookings",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "PenaltyAmountMinorUnits",
                table: "PrivateSpeakingBookings",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProfessionTrack",
                table: "PrivateSpeakingBookings",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RecordingStatus",
                table: "PrivateSpeakingBookings",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RecordingUrl",
                table: "PrivateSpeakingBookings",
                type: "character varying(1024)",
                maxLength: 1024,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RefundAmountMinorUnits",
                table: "PrivateSpeakingBookings",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "RefundIssued",
                table: "PrivateSpeakingBookings",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "StripeRefundId",
                table: "PrivateSpeakingBookings",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ReminderOffsetsMinutesJson",
                table: "PrivateSpeakingConfigs");

            migrationBuilder.DropColumn(
                name: "RescheduleFreeWindowHours",
                table: "PrivateSpeakingConfigs");

            migrationBuilder.DropColumn(
                name: "RescheduleSameDayPenaltyPercent",
                table: "PrivateSpeakingConfigs");

            migrationBuilder.DropColumn(
                name: "AiFeedbackStatus",
                table: "PrivateSpeakingBookings");

            migrationBuilder.DropColumn(
                name: "AttendanceJoinedAt",
                table: "PrivateSpeakingBookings");

            migrationBuilder.DropColumn(
                name: "AttendanceLeftAt",
                table: "PrivateSpeakingBookings");

            migrationBuilder.DropColumn(
                name: "AttendanceVerified",
                table: "PrivateSpeakingBookings");

            migrationBuilder.DropColumn(
                name: "PenaltyAmountMinorUnits",
                table: "PrivateSpeakingBookings");

            migrationBuilder.DropColumn(
                name: "ProfessionTrack",
                table: "PrivateSpeakingBookings");

            migrationBuilder.DropColumn(
                name: "RecordingStatus",
                table: "PrivateSpeakingBookings");

            migrationBuilder.DropColumn(
                name: "RecordingUrl",
                table: "PrivateSpeakingBookings");

            migrationBuilder.DropColumn(
                name: "RefundAmountMinorUnits",
                table: "PrivateSpeakingBookings");

            migrationBuilder.DropColumn(
                name: "RefundIssued",
                table: "PrivateSpeakingBookings");

            migrationBuilder.DropColumn(
                name: "StripeRefundId",
                table: "PrivateSpeakingBookings");
        }
    }
}
