using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OetLearner.Api.Data.Migrations
{
    /// <inheritdoc />
    /// <remarks>
    /// Review module enhancements (docs/REVIEW-MODULE.md):
    ///   - ReviewItems: widen SourceId (64 → 128), add Title, PromptKind,
    ///     RichContentJson, LastQuality, SuspendedAt, SuspendedReason.
    ///   - Add unique index (UserId, SourceType, SourceId) for seeder idempotency.
    ///   - New table ReviewItemTransitions for undo support.
    ///
    /// Safe to apply on existing data: all added columns are nullable. The
    /// unique index tolerates existing rows because the current seeding path
    /// (Grammar) already enforced idempotency in code.
    /// </remarks>
    public partial class AddReviewModuleEnhancements : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ── Widen SourceId so composite keys like `{evaluationId}:{feedbackItemId}` fit ──
            migrationBuilder.AlterColumn<string>(
                name: "SourceId",
                table: "ReviewItems",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(64)",
                oldMaxLength: 64,
                oldNullable: true);

            // ── New ReviewItem columns ─────────────────────────────────
            migrationBuilder.AddColumn<string>(
                name: "Title",
                table: "ReviewItems",
                type: "character varying(180)",
                maxLength: 180,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PromptKind",
                table: "ReviewItems",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RichContentJson",
                table: "ReviewItems",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "LastQuality",
                table: "ReviewItems",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "SuspendedAt",
                table: "ReviewItems",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SuspendedReason",
                table: "ReviewItems",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true);

            // ── Idempotency unique index ────────────────────────────────
            migrationBuilder.CreateIndex(
                name: "IX_ReviewItems_UserId_SourceType_SourceId",
                table: "ReviewItems",
                columns: new[] { "UserId", "SourceType", "SourceId" },
                unique: true);

            // ── Transition log (undo) ──────────────────────────────────
            migrationBuilder.CreateTable(
                name: "ReviewItemTransitions",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ReviewItemId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    UserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    PrevEaseFactor = table.Column<double>(type: "double precision", nullable: false),
                    PrevIntervalDays = table.Column<int>(type: "integer", nullable: false),
                    PrevReviewCount = table.Column<int>(type: "integer", nullable: false),
                    PrevConsecutiveCorrect = table.Column<int>(type: "integer", nullable: false),
                    PrevDueDate = table.Column<DateOnly>(type: "date", nullable: false),
                    PrevLastReviewedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    PrevLastQuality = table.Column<int>(type: "integer", nullable: true),
                    PrevStatus = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    AppliedQuality = table.Column<int>(type: "integer", nullable: false),
                    AppliedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReviewItemTransitions", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ReviewItemTransitions_ReviewItemId_AppliedAt",
                table: "ReviewItemTransitions",
                columns: new[] { "ReviewItemId", "AppliedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ReviewItemTransitions_UserId_AppliedAt",
                table: "ReviewItemTransitions",
                columns: new[] { "UserId", "AppliedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "ReviewItemTransitions");

            migrationBuilder.DropIndex(
                name: "IX_ReviewItems_UserId_SourceType_SourceId",
                table: "ReviewItems");

            migrationBuilder.DropColumn(name: "SuspendedReason", table: "ReviewItems");
            migrationBuilder.DropColumn(name: "SuspendedAt", table: "ReviewItems");
            migrationBuilder.DropColumn(name: "LastQuality", table: "ReviewItems");
            migrationBuilder.DropColumn(name: "RichContentJson", table: "ReviewItems");
            migrationBuilder.DropColumn(name: "PromptKind", table: "ReviewItems");
            migrationBuilder.DropColumn(name: "Title", table: "ReviewItems");

            migrationBuilder.AlterColumn<string>(
                name: "SourceId",
                table: "ReviewItems",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(128)",
                oldMaxLength: 128,
                oldNullable: true);
        }
    }
}
