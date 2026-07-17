using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OetWithDrHesham.Api.Data.Migrations
{
    /// <summary>
    /// Buddy System schema (Writing Module spec §23.5). Adds the
    /// <c>OptInBuddy</c> opt-in column on <c>LearnerWritingProfiles</c>
    /// plus three new tables: <c>WritingBuddyPairs</c>,
    /// <c>WritingBuddyMessages</c>, <c>WritingBuddyCheckIns</c>.
    ///
    /// Anonymous learner-to-learner pairing at ±1 band, same profession,
    /// with a 500-character message cap and a 10/day per-sender rate
    /// limit enforced by <c>WritingBuddyService</c>.
    /// </summary>
    public partial class AddWritingBuddySchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Additive opt-in column on the existing profile table.
            migrationBuilder.AddColumn<bool>(
                name: "OptInBuddy",
                table: "LearnerWritingProfiles",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            // ── WritingBuddyPairs ────────────────────────────────────────
            migrationBuilder.CreateTable(
                name: "WritingBuddyPairs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserAId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    UserBId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    EndedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    EndedReason = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Profession = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    MatchedAtBand = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false)
                },
                constraints: table => table.PrimaryKey("PK_WritingBuddyPairs", x => x.Id));

            migrationBuilder.CreateIndex(
                name: "IX_WritingBuddyPairs_UserAId",
                table: "WritingBuddyPairs",
                column: "UserAId");

            migrationBuilder.CreateIndex(
                name: "IX_WritingBuddyPairs_UserBId",
                table: "WritingBuddyPairs",
                column: "UserBId");

            migrationBuilder.CreateIndex(
                name: "IX_WritingBuddyPairs_Profession_Status_MatchedAtBand",
                table: "WritingBuddyPairs",
                columns: new[] { "Profession", "Status", "MatchedAtBand" });

            // Partial unique — at most one active row for any ordered pair.
            migrationBuilder.CreateIndex(
                name: "IX_WritingBuddyPairs_UserAId_UserBId",
                table: "WritingBuddyPairs",
                columns: new[] { "UserAId", "UserBId" },
                unique: true,
                filter: "\"Status\" = 'active'");

            // ── WritingBuddyMessages ────────────────────────────────────
            migrationBuilder.CreateTable(
                name: "WritingBuddyMessages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PairId = table.Column<Guid>(type: "uuid", nullable: false),
                    FromUserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    BodyMarkdown = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    SentAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ReadAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table => table.PrimaryKey("PK_WritingBuddyMessages", x => x.Id));

            migrationBuilder.CreateIndex(
                name: "IX_WritingBuddyMessage_Pair_SentAt_Desc",
                table: "WritingBuddyMessages",
                columns: new[] { "PairId", "SentAt" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "IX_WritingBuddyMessages_FromUserId_SentAt",
                table: "WritingBuddyMessages",
                columns: new[] { "FromUserId", "SentAt" });

            // ── WritingBuddyCheckIns ────────────────────────────────────
            migrationBuilder.CreateTable(
                name: "WritingBuddyCheckIns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PairId = table.Column<Guid>(type: "uuid", nullable: false),
                    WeekStartDate = table.Column<DateOnly>(type: "date", nullable: false),
                    UserAReportJson = table.Column<string>(type: "jsonb", nullable: true),
                    UserBReportJson = table.Column<string>(type: "jsonb", nullable: true),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table => table.PrimaryKey("PK_WritingBuddyCheckIns", x => x.Id));

            migrationBuilder.CreateIndex(
                name: "IX_WritingBuddyCheckIns_PairId_WeekStartDate",
                table: "WritingBuddyCheckIns",
                columns: new[] { "PairId", "WeekStartDate" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "WritingBuddyCheckIns");
            migrationBuilder.DropTable(name: "WritingBuddyMessages");
            migrationBuilder.DropTable(name: "WritingBuddyPairs");
            migrationBuilder.DropColumn(name: "OptInBuddy", table: "LearnerWritingProfiles");
        }
    }
}
