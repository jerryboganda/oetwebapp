using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Infrastructure;
using OetLearner.Api.Data;

#nullable disable

namespace OetLearner.Api.Data.Migrations
{
    [DbContext(typeof(LearnerDbContext))]
    [Migration("20260411181309_AddPrivateSpeakingSchema")]
    public partial class AddPrivateSpeakingSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ── PrivateSpeakingConfigs ──────────────────────────────────────

            migrationBuilder.CreateTable(
                name: "PrivateSpeakingConfigs",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    IsEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    DefaultSlotDurationMinutes = table.Column<int>(type: "integer", nullable: false),
                    BufferMinutesBetweenSlots = table.Column<int>(type: "integer", nullable: false),
                    MinBookingLeadTimeHours = table.Column<int>(type: "integer", nullable: false),
                    MaxBookingAdvanceDays = table.Column<int>(type: "integer", nullable: false),
                    ReservationTimeoutMinutes = table.Column<int>(type: "integer", nullable: false),
                    DefaultPriceMinorUnits = table.Column<int>(type: "integer", nullable: false),
                    Currency = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    CancellationWindowHours = table.Column<int>(type: "integer", nullable: false),
                    AllowReschedule = table.Column<bool>(type: "boolean", nullable: false),
                    RescheduleWindowHours = table.Column<int>(type: "integer", nullable: false),
                    ReminderOffsetsHoursJson = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    DailyReminderEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    DailyReminderHourUtc = table.Column<int>(type: "integer", nullable: false),
                    CancellationPolicyText = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    BookingPolicyText = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PrivateSpeakingConfigs", x => x.Id);
                });

            // ── PrivateSpeakingTutorProfiles ───────────────────────────────

            migrationBuilder.CreateTable(
                name: "PrivateSpeakingTutorProfiles",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ExpertUserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Bio = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    Timezone = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    PriceOverrideMinorUnits = table.Column<int>(type: "integer", nullable: true),
                    SlotDurationOverrideMinutes = table.Column<int>(type: "integer", nullable: true),
                    SpecialtiesJson = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    TotalSessions = table.Column<int>(type: "integer", nullable: false),
                    AverageRating = table.Column<double>(type: "double precision", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PrivateSpeakingTutorProfiles", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PrivateSpeakingTutorProfiles_ExpertUserId",
                table: "PrivateSpeakingTutorProfiles",
                column: "ExpertUserId",
                unique: true);

            // ── PrivateSpeakingAvailabilityRules ───────────────────────────

            migrationBuilder.CreateTable(
                name: "PrivateSpeakingAvailabilityRules",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    TutorProfileId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    DayOfWeek = table.Column<int>(type: "integer", nullable: false),
                    StartTime = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    EndTime = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    EffectiveFrom = table.Column<DateOnly>(type: "date", nullable: true),
                    EffectiveTo = table.Column<DateOnly>(type: "date", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PrivateSpeakingAvailabilityRules", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PrivateSpeakingAvailabilityRules_PrivateSpeakingTutorProfiles_TutorProfileId",
                        column: x => x.TutorProfileId,
                        principalTable: "PrivateSpeakingTutorProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PrivateSpeakingAvailabilityRules_TutorProfileId_DayOfWeek",
                table: "PrivateSpeakingAvailabilityRules",
                columns: new[] { "TutorProfileId", "DayOfWeek" });

            // ── PrivateSpeakingAvailabilityOverrides ───────────────────────

            migrationBuilder.CreateTable(
                name: "PrivateSpeakingAvailabilityOverrides",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    TutorProfileId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    OverrideType = table.Column<int>(type: "integer", nullable: false),
                    StartTime = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: true),
                    EndTime = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: true),
                    Reason = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PrivateSpeakingAvailabilityOverrides", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PrivateSpeakingAvailabilityOverrides_PrivateSpeakingTutorProfiles_TutorProfileId",
                        column: x => x.TutorProfileId,
                        principalTable: "PrivateSpeakingTutorProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PrivateSpeakingAvailabilityOverrides_TutorProfileId_Date",
                table: "PrivateSpeakingAvailabilityOverrides",
                columns: new[] { "TutorProfileId", "Date" });

            // ── PrivateSpeakingBookings ────────────────────────────────────

            migrationBuilder.CreateTable(
                name: "PrivateSpeakingBookings",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    LearnerUserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    TutorProfileId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    SessionStartUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    DurationMinutes = table.Column<int>(type: "integer", nullable: false),
                    TutorTimezone = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    LearnerTimezone = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    PriceMinorUnits = table.Column<int>(type: "integer", nullable: false),
                    Currency = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    StripeCheckoutSessionId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    StripePaymentIntentId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    PaymentStatus = table.Column<int>(type: "integer", nullable: false),
                    PaymentConfirmedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ZoomMeetingId = table.Column<long>(type: "bigint", nullable: true),
                    ZoomJoinUrl = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    ZoomStartUrl = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    ZoomMeetingPassword = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ZoomStatus = table.Column<int>(type: "integer", nullable: false),
                    ZoomError = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    ZoomRetryCount = table.Column<int>(type: "integer", nullable: false),
                    ReservationExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    IdempotencyKey = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    LearnerNotes = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    TutorNotes = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    LearnerRating = table.Column<int>(type: "integer", nullable: true),
                    LearnerFeedback = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    CancelledBy = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    CancellationReason = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    CancelledAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RescheduledFromBookingId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    RemindersSentJson = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    DailyReminderSent = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PrivateSpeakingBookings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PrivateSpeakingBookings_PrivateSpeakingTutorProfiles_TutorProfileId",
                        column: x => x.TutorProfileId,
                        principalTable: "PrivateSpeakingTutorProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PrivateSpeakingBookings_LearnerUserId_Status",
                table: "PrivateSpeakingBookings",
                columns: new[] { "LearnerUserId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_PrivateSpeakingBookings_TutorProfileId_SessionStartUtc",
                table: "PrivateSpeakingBookings",
                columns: new[] { "TutorProfileId", "SessionStartUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_PrivateSpeakingBookings_StripeCheckoutSessionId",
                table: "PrivateSpeakingBookings",
                column: "StripeCheckoutSessionId");

            migrationBuilder.CreateIndex(
                name: "IX_PrivateSpeakingBookings_IdempotencyKey",
                table: "PrivateSpeakingBookings",
                column: "IdempotencyKey",
                unique: true);

            // ── PrivateSpeakingAuditLogs ───────────────────────────────────

            migrationBuilder.CreateTable(
                name: "PrivateSpeakingAuditLogs",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    BookingId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ActorId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ActorRole = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Action = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Details = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PrivateSpeakingAuditLogs", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PrivateSpeakingAuditLogs_BookingId",
                table: "PrivateSpeakingAuditLogs",
                column: "BookingId");

            migrationBuilder.CreateIndex(
                name: "IX_PrivateSpeakingAuditLogs_ActorId_CreatedAt",
                table: "PrivateSpeakingAuditLogs",
                columns: new[] { "ActorId", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "PrivateSpeakingAuditLogs");
            migrationBuilder.DropTable(name: "PrivateSpeakingBookings");
            migrationBuilder.DropTable(name: "PrivateSpeakingAvailabilityOverrides");
            migrationBuilder.DropTable(name: "PrivateSpeakingAvailabilityRules");
            migrationBuilder.DropTable(name: "PrivateSpeakingTutorProfiles");
            migrationBuilder.DropTable(name: "PrivateSpeakingConfigs");
        }
    }
}