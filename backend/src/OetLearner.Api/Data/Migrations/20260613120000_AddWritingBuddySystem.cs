using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OetLearner.Api.Data.Migrations
{
    /// <summary>
    /// Writing Buddy System (OET_WRITING_MODULE_PATHWAY.md §23.5).
    /// Tables: WritingBuddyOptIns, WritingBuddyPairs, WritingBuddyCheckIns.
    /// Hand-written; do not regenerate from EF tooling. See
    /// <see cref="OetLearner.Api.Domain.WritingBuddyPair"/> for column intent.
    /// </summary>
    public partial class AddWritingBuddySystem : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "WritingBuddyOptIns",
                columns: table => new
                {
                    UserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    OptedInAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    AnonymousDisplayName = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    PreferredProfession = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    PreferredTargetBand = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                },
                constraints: table => table.PrimaryKey("PK_WritingBuddyOptIns", x => x.UserId));

            migrationBuilder.CreateIndex(
                name: "IX_WritingBuddyOptIns_PreferredProfession_PreferredTargetBand",
                table: "WritingBuddyOptIns",
                columns: new[] { "PreferredProfession", "PreferredTargetBand" });

            migrationBuilder.CreateTable(
                name: "WritingBuddyPairs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    LearnerAUserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    LearnerBUserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    MatchedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false, defaultValue: "active"),
                    MatchScore = table.Column<decimal>(type: "numeric(3,2)", nullable: false, defaultValue: 0m),
                    LastInteractionAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DissolvedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DissolveReason = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                },
                constraints: table => table.PrimaryKey("PK_WritingBuddyPairs", x => x.Id));

            migrationBuilder.CreateIndex(
                name: "IX_WritingBuddyPairs_LearnerAUserId_Status",
                table: "WritingBuddyPairs",
                columns: new[] { "LearnerAUserId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_WritingBuddyPairs_LearnerBUserId_Status",
                table: "WritingBuddyPairs",
                columns: new[] { "LearnerBUserId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_WritingBuddyPairs_MatchedAt",
                table: "WritingBuddyPairs",
                column: "MatchedAt",
                descending: new[] { true });

            migrationBuilder.CreateTable(
                name: "WritingBuddyCheckIns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PairId = table.Column<Guid>(type: "uuid", nullable: false),
                    FromUserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Message = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    SentAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ReadAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                },
                constraints: table => table.PrimaryKey("PK_WritingBuddyCheckIns", x => x.Id));

            migrationBuilder.CreateIndex(
                name: "IX_WritingBuddyCheckIns_Pair_SentAt",
                table: "WritingBuddyCheckIns",
                columns: new[] { "PairId", "SentAt" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "IX_WritingBuddyCheckIns_PairId_ReadAt",
                table: "WritingBuddyCheckIns",
                columns: new[] { "PairId", "ReadAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "WritingBuddyCheckIns");
            migrationBuilder.DropTable(name: "WritingBuddyPairs");
            migrationBuilder.DropTable(name: "WritingBuddyOptIns");
        }
    }
}
