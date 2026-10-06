using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OetLearner.Api.Data.Migrations
{
    /// <inheritdoc />
    /// <remarks>
    /// SAMI Wave 1 (hand-authored): the companion memory spine — three-layer
    /// memory entries (F-041/042/043), Error DNA entries with spaced review
    /// scheduling (F-044/046), exam journeys (F-012/043) and the study
    /// availability model (F-009) the planner and next-best-action engine read.
    /// </remarks>
    public partial class AddCompanionMemoryErrorDna : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CompanionMemoryEntries",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    UserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Layer = table.Column<int>(type: "integer", nullable: false),
                    Kind = table.Column<string>(type: "character varying(48)", maxLength: 48, nullable: false),
                    Subtest = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Content = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    DataJson = table.Column<string>(type: "text", nullable: true),
                    ProvenanceType = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    ProvenanceId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ConfirmedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    SupersededAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    JourneyId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    RecordedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CompanionMemoryEntries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ErrorDnaEntries",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    UserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Category = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    Pattern = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    PatternKey = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Subtest = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    EvidenceCount = table.Column<int>(type: "integer", nullable: false),
                    FirstSeenAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastSeenAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    MasteryScore = table.Column<int>(type: "integer", nullable: false),
                    ReviewCount = table.Column<int>(type: "integer", nullable: false),
                    NextReviewAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    SourceKind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    SourceId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ErrorDnaEntries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CompanionJourneys",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    UserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Label = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    TargetExamDate = table.Column<DateOnly>(type: "date", nullable: true),
                    FocusSubtestsJson = table.Column<string>(type: "text", nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    EndedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Outcome = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CompanionJourneys", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CompanionAvailabilities",
                columns: table => new
                {
                    UserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    DailyMinutesJson = table.Column<string>(type: "text", nullable: false),
                    NightShiftDaysJson = table.Column<string>(type: "text", nullable: false),
                    LongDayShiftDaysJson = table.Column<string>(type: "text", nullable: false),
                    TravelMode = table.Column<bool>(type: "boolean", nullable: false),
                    TravelModeMinutesPerDay = table.Column<int>(type: "integer", nullable: false),
                    TravelUntil = table.Column<DateOnly>(type: "date", nullable: true),
                    PreferredStudyTime = table.Column<string>(type: "character varying(5)", maxLength: 5, nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CompanionAvailabilities", x => x.UserId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CompanionMemoryEntries_UserId_Layer_Kind_Subtest",
                table: "CompanionMemoryEntries",
                columns: new[] { "UserId", "Layer", "Kind", "Subtest" });

            migrationBuilder.CreateIndex(
                name: "IX_CompanionMemoryEntries_UserId_JourneyId",
                table: "CompanionMemoryEntries",
                columns: new[] { "UserId", "JourneyId" });

            migrationBuilder.CreateIndex(
                name: "IX_CompanionMemoryEntries_ConfirmedAt",
                table: "CompanionMemoryEntries",
                column: "ConfirmedAt");

            migrationBuilder.CreateIndex(
                name: "IX_ErrorDnaEntries_UserId_PatternKey",
                table: "ErrorDnaEntries",
                columns: new[] { "UserId", "PatternKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ErrorDnaEntries_UserId_NextReviewAt",
                table: "ErrorDnaEntries",
                columns: new[] { "UserId", "NextReviewAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ErrorDnaEntries_UserId_Category",
                table: "ErrorDnaEntries",
                columns: new[] { "UserId", "Category" });

            migrationBuilder.CreateIndex(
                name: "IX_CompanionJourneys_UserId_IsActive",
                table: "CompanionJourneys",
                columns: new[] { "UserId", "IsActive" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "CompanionMemoryEntries");
            migrationBuilder.DropTable(name: "ErrorDnaEntries");
            migrationBuilder.DropTable(name: "CompanionJourneys");
            migrationBuilder.DropTable(name: "CompanionAvailabilities");
        }
    }
}
