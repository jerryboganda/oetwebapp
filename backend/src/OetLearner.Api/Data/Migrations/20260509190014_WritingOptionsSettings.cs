using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OetLearner.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class WritingOptionsSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ExpertCompensationRates",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ExpertId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SubtestCode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    RateMinorUnits = table.Column<long>(type: "bigint", nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    EffectiveFrom = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    EffectiveTo = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExpertCompensationRates", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ExpertEarnings",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ExpertId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ReviewRequestId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    AmountMinorUnits = table.Column<long>(type: "bigint", nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    EarnedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    PaidOutAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    PayoutId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExpertEarnings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ExpertMessageReplies",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ThreadId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    AuthorId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    AuthorRole = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    AuthorName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Body = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExpertMessageReplies", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ExpertMessageThreads",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ExpertId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    LinkedReviewRequestId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    LinkedCalibrationCaseId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    LinkedLearnerId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExpertMessageThreads", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ExpertPayouts",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ExpertId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    TotalAmountMinorUnits = table.Column<long>(type: "bigint", nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ApprovedByAdminId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ApprovedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExpertPayouts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ExpertReviewAmends",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ReviewRequestId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ReviewerId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    BeforeSnapshotJson = table.Column<string>(type: "text", nullable: false),
                    AfterSnapshotJson = table.Column<string>(type: "text", nullable: false),
                    AmendNumber = table.Column<int>(type: "integer", nullable: false),
                    AmendedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExpertReviewAmends", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ExpertSlaSnapshots",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ReviewRequestId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ExpertId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SlaDueAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    WasMet = table.Column<bool>(type: "boolean", nullable: false),
                    TurnaroundHours = table.Column<double>(type: "double precision", nullable: true),
                    SlaState = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExpertSlaSnapshots", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "WritingOptions",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    AiGradingEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    AiCoachEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    KillSwitchReason = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    FreeTierLimit = table.Column<int>(type: "integer", nullable: false),
                    FreeTierWindowDays = table.Column<int>(type: "integer", nullable: false),
                    FreeTierEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    PreferredGradingProvider = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    PreferredCoachProvider = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    PreferredDraftProvider = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedByAdminId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WritingOptions", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ExpertCompensationRates_ExpertId",
                table: "ExpertCompensationRates",
                column: "ExpertId");

            migrationBuilder.CreateIndex(
                name: "IX_ExpertEarnings_ExpertId",
                table: "ExpertEarnings",
                column: "ExpertId");

            migrationBuilder.CreateIndex(
                name: "IX_ExpertEarnings_ReviewRequestId",
                table: "ExpertEarnings",
                column: "ReviewRequestId");

            migrationBuilder.CreateIndex(
                name: "IX_ExpertMessageReplies_ThreadId",
                table: "ExpertMessageReplies",
                column: "ThreadId");

            migrationBuilder.CreateIndex(
                name: "IX_ExpertMessageThreads_ExpertId",
                table: "ExpertMessageThreads",
                column: "ExpertId");

            migrationBuilder.CreateIndex(
                name: "IX_ExpertPayouts_ExpertId",
                table: "ExpertPayouts",
                column: "ExpertId");

            migrationBuilder.CreateIndex(
                name: "IX_ExpertReviewAmends_ReviewRequestId",
                table: "ExpertReviewAmends",
                column: "ReviewRequestId");

            migrationBuilder.CreateIndex(
                name: "IX_ExpertSlaSnapshots_ReviewRequestId",
                table: "ExpertSlaSnapshots",
                column: "ReviewRequestId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "ExpertCompensationRates");
            migrationBuilder.DropTable(name: "ExpertEarnings");
            migrationBuilder.DropTable(name: "ExpertMessageReplies");
            migrationBuilder.DropTable(name: "ExpertMessageThreads");
            migrationBuilder.DropTable(name: "ExpertPayouts");
            migrationBuilder.DropTable(name: "ExpertReviewAmends");
            migrationBuilder.DropTable(name: "ExpertSlaSnapshots");
            migrationBuilder.DropTable(name: "WritingOptions");
        }
    }
}
