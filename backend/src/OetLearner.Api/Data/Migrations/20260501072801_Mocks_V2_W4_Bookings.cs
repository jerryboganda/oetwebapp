using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OetLearner.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class Mocks_V2_W4_Bookings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Difficulty",
                table: "MockBundles",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "exam_ready");

            migrationBuilder.AddColumn<string>(
                name: "QualityStatus",
                table: "MockBundles",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "draft");

            migrationBuilder.AddColumn<bool>(
                name: "RandomiseQuestions",
                table: "MockBundles",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "ReleasePolicy",
                table: "MockBundles",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "instant");

            migrationBuilder.AddColumn<string>(
                name: "SkillTagsCsv",
                table: "MockBundles",
                type: "character varying(512)",
                maxLength: 512,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "SourceStatus",
                table: "MockBundles",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "needs_review");

            migrationBuilder.AddColumn<string>(
                name: "TopicTagsCsv",
                table: "MockBundles",
                type: "character varying(512)",
                maxLength: 512,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<bool>(
                name: "WatermarkEnabled",
                table: "MockBundles",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.CreateTable(
                name: "MockBookings",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    UserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    MockBundleId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    MockAttemptId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ScheduledStartAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    TimezoneIana = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    AssignedTutorId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    AssignedInterlocutorId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    RescheduleCount = table.Column<int>(type: "integer", nullable: false),
                    ConsentToRecording = table.Column<bool>(type: "boolean", nullable: false),
                    DeliveryMode = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    LiveRoomState = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ZoomMeetingId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    ZoomJoinUrl = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    ZoomStartUrl = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    ZoomMeetingPassword = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    LearnerNotes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CancelledAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MockBookings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MockBookings_MockAttempts_MockAttemptId",
                        column: x => x.MockAttemptId,
                        principalTable: "MockAttempts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_MockBookings_MockBundles_MockBundleId",
                        column: x => x.MockBundleId,
                        principalTable: "MockBundles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MockContentReviews",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    MockBundleId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    MockAttemptId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ReportedByUserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ReviewType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Severity = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ResolvedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ResolvedByAdminId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MockContentReviews", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MockContentReviews_MockAttempts_MockAttemptId",
                        column: x => x.MockAttemptId,
                        principalTable: "MockAttempts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_MockContentReviews_MockBundles_MockBundleId",
                        column: x => x.MockBundleId,
                        principalTable: "MockBundles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MockBookings_AssignedTutorId_ScheduledStartAt",
                table: "MockBookings",
                columns: new[] { "AssignedTutorId", "ScheduledStartAt" });

            migrationBuilder.CreateIndex(
                name: "IX_MockBookings_MockAttemptId",
                table: "MockBookings",
                column: "MockAttemptId");

            migrationBuilder.CreateIndex(
                name: "IX_MockBookings_MockBundleId",
                table: "MockBookings",
                column: "MockBundleId");

            migrationBuilder.CreateIndex(
                name: "IX_MockBookings_Status_ScheduledStartAt",
                table: "MockBookings",
                columns: new[] { "Status", "ScheduledStartAt" });

            migrationBuilder.CreateIndex(
                name: "IX_MockBookings_UserId_ScheduledStartAt",
                table: "MockBookings",
                columns: new[] { "UserId", "ScheduledStartAt" });

            migrationBuilder.CreateIndex(
                name: "IX_MockContentReviews_MockAttemptId",
                table: "MockContentReviews",
                column: "MockAttemptId");

            migrationBuilder.CreateIndex(
                name: "IX_MockContentReviews_MockBundleId",
                table: "MockContentReviews",
                column: "MockBundleId");

            migrationBuilder.CreateIndex(
                name: "IX_MockContentReviews_ReportedByUserId_CreatedAt",
                table: "MockContentReviews",
                columns: new[] { "ReportedByUserId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_MockContentReviews_Status_Severity",
                table: "MockContentReviews",
                columns: new[] { "Status", "Severity" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MockBookings");

            migrationBuilder.DropTable(
                name: "MockContentReviews");

            migrationBuilder.DropColumn(
                name: "Difficulty",
                table: "MockBundles");

            migrationBuilder.DropColumn(
                name: "QualityStatus",
                table: "MockBundles");

            migrationBuilder.DropColumn(
                name: "RandomiseQuestions",
                table: "MockBundles");

            migrationBuilder.DropColumn(
                name: "ReleasePolicy",
                table: "MockBundles");

            migrationBuilder.DropColumn(
                name: "SkillTagsCsv",
                table: "MockBundles");

            migrationBuilder.DropColumn(
                name: "SourceStatus",
                table: "MockBundles");

            migrationBuilder.DropColumn(
                name: "TopicTagsCsv",
                table: "MockBundles");

            migrationBuilder.DropColumn(
                name: "WatermarkEnabled",
                table: "MockBundles");
        }
    }
}
