using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OetLearner.Api.Data.Migrations
{
    /// <inheritdoc />
    /// <remarks>
    /// Phase 2 pronunciation subsystem:
    ///   - Adds new columns to PronunciationDrill (Profession, Focus, PrimaryRuleId,
    ///     AudioModelAssetId, OrderIndex, CreatedAt, UpdatedAt) and widens TipsHtml.
    ///   - Adds new columns to PronunciationAssessment (DrillId, ProjectedSpeakingScaled,
    ///     ProjectedSpeakingGrade, Provider, RulebookVersion, FindingsJson, FeedbackJson).
    ///   - Adds new columns to LearnerPronunciationProgress (NextDueAt, IntervalDays, Ease).
    ///   - Creates PronunciationAttempts table for upload / ASR lifecycle tracking.
    ///   - Creates LearnerPronunciationDiscriminationAttempts table for minimal-pair game.
    ///   - Adds requisite indexes.
    /// Safe to apply on top of existing data: all added columns are nullable or have defaults.
    /// </remarks>
    public partial class AddPronunciationV2 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ── PronunciationDrill: new columns + widen TipsHtml ─────────────
            migrationBuilder.AddColumn<string>(
                name: "Profession",
                table: "PronunciationDrills",
                type: "character varying(48)",
                maxLength: 48,
                nullable: false,
                defaultValue: "all");

            migrationBuilder.AddColumn<string>(
                name: "Focus",
                table: "PronunciationDrills",
                type: "character varying(24)",
                maxLength: 24,
                nullable: false,
                defaultValue: "phoneme");

            migrationBuilder.AddColumn<string>(
                name: "PrimaryRuleId",
                table: "PronunciationDrills",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AudioModelAssetId",
                table: "PronunciationDrills",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "OrderIndex",
                table: "PronunciationDrills",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "CreatedAt",
                table: "PronunciationDrills",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: DateTimeOffset.MinValue);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "UpdatedAt",
                table: "PronunciationDrills",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: DateTimeOffset.MinValue);

            migrationBuilder.AlterColumn<string>(
                name: "TipsHtml",
                table: "PronunciationDrills",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(512)",
                oldMaxLength: 512);

            migrationBuilder.AlterColumn<string>(
                name: "AudioModelUrl",
                table: "PronunciationDrills",
                type: "character varying(512)",
                maxLength: 512,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(256)",
                oldMaxLength: 256,
                oldNullable: true);

            // ── PronunciationAssessment: new columns ─────────────────────────
            migrationBuilder.AddColumn<string>(
                name: "DrillId",
                table: "PronunciationAssessments",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ProjectedSpeakingScaled",
                table: "PronunciationAssessments",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "ProjectedSpeakingGrade",
                table: "PronunciationAssessments",
                type: "character varying(4)",
                maxLength: 4,
                nullable: false,
                defaultValue: "B");

            migrationBuilder.AddColumn<string>(
                name: "Provider",
                table: "PronunciationAssessments",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "mock");

            migrationBuilder.AddColumn<string>(
                name: "RulebookVersion",
                table: "PronunciationAssessments",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "1.0.0");

            migrationBuilder.AddColumn<string>(
                name: "FindingsJson",
                table: "PronunciationAssessments",
                type: "text",
                nullable: false,
                defaultValue: "[]");

            migrationBuilder.AddColumn<string>(
                name: "FeedbackJson",
                table: "PronunciationAssessments",
                type: "text",
                nullable: false,
                defaultValue: "{}");

            migrationBuilder.CreateIndex(
                name: "IX_PronunciationAssessments_DrillId",
                table: "PronunciationAssessments",
                column: "DrillId");

            migrationBuilder.CreateIndex(
                name: "IX_PronunciationAssessments_UserId_CreatedAt",
                table: "PronunciationAssessments",
                columns: new[] { "UserId", "CreatedAt" });

            // ── LearnerPronunciationProgress: spaced-rep columns ─────────────
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "NextDueAt",
                table: "LearnerPronunciationProgress",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "IntervalDays",
                table: "LearnerPronunciationProgress",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<double>(
                name: "Ease",
                table: "LearnerPronunciationProgress",
                type: "double precision",
                nullable: false,
                defaultValue: 2.5);

            migrationBuilder.CreateIndex(
                name: "IX_LearnerPronunciationProgress_UserId_AverageScore",
                table: "LearnerPronunciationProgress",
                columns: new[] { "UserId", "AverageScore" });

            // Tighten existing composite index to unique.
            // Drop the legacy non-unique index if it exists, then add unique.
            migrationBuilder.Sql(@"DROP INDEX IF EXISTS ""IX_LearnerPronunciationProgress_UserId_PhonemeCode"";");
            migrationBuilder.CreateIndex(
                name: "IX_LearnerPronunciationProgress_UserId_PhonemeCode",
                table: "LearnerPronunciationProgress",
                columns: new[] { "UserId", "PhonemeCode" },
                unique: true);

            // ── PronunciationAttempts (new table) ────────────────────────────
            migrationBuilder.CreateTable(
                name: "PronunciationAttempts",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    UserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    DrillId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    AudioStorageKey = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    AudioSha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    AudioBytes = table.Column<long>(type: "bigint", nullable: true),
                    AudioMimeType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    AudioDurationMs = table.Column<int>(type: "integer", nullable: true),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    AssessmentId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ErrorCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ErrorMessage = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    Provider = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    AudioReapAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PronunciationAttempts", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PronunciationAttempts_UserId_CreatedAt",
                table: "PronunciationAttempts",
                columns: new[] { "UserId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_PronunciationAttempts_DrillId_CreatedAt",
                table: "PronunciationAttempts",
                columns: new[] { "DrillId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_PronunciationAttempts_UserId_DrillId_CreatedAt",
                table: "PronunciationAttempts",
                columns: new[] { "UserId", "DrillId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_PronunciationAttempts_Status",
                table: "PronunciationAttempts",
                column: "Status");

            // ── LearnerPronunciationDiscriminationAttempts (new table) ───────
            migrationBuilder.CreateTable(
                name: "LearnerPronunciationDiscriminationAttempts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    DrillId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    TargetPhoneme = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    RoundsTotal = table.Column<int>(type: "integer", nullable: false),
                    RoundsCorrect = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LearnerPronunciationDiscriminationAttempts", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_LearnerPronunciationDiscriminationAttempts_UserId_CreatedAt",
                table: "LearnerPronunciationDiscriminationAttempts",
                columns: new[] { "UserId", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "PronunciationAttempts");
            migrationBuilder.DropTable(name: "LearnerPronunciationDiscriminationAttempts");

            migrationBuilder.DropIndex(name: "IX_LearnerPronunciationProgress_UserId_AverageScore", table: "LearnerPronunciationProgress");
            migrationBuilder.DropColumn(name: "NextDueAt", table: "LearnerPronunciationProgress");
            migrationBuilder.DropColumn(name: "IntervalDays", table: "LearnerPronunciationProgress");
            migrationBuilder.DropColumn(name: "Ease", table: "LearnerPronunciationProgress");

            migrationBuilder.DropIndex(name: "IX_PronunciationAssessments_DrillId", table: "PronunciationAssessments");
            migrationBuilder.DropIndex(name: "IX_PronunciationAssessments_UserId_CreatedAt", table: "PronunciationAssessments");
            migrationBuilder.DropColumn(name: "DrillId", table: "PronunciationAssessments");
            migrationBuilder.DropColumn(name: "ProjectedSpeakingScaled", table: "PronunciationAssessments");
            migrationBuilder.DropColumn(name: "ProjectedSpeakingGrade", table: "PronunciationAssessments");
            migrationBuilder.DropColumn(name: "Provider", table: "PronunciationAssessments");
            migrationBuilder.DropColumn(name: "RulebookVersion", table: "PronunciationAssessments");
            migrationBuilder.DropColumn(name: "FindingsJson", table: "PronunciationAssessments");
            migrationBuilder.DropColumn(name: "FeedbackJson", table: "PronunciationAssessments");

            migrationBuilder.DropColumn(name: "Profession", table: "PronunciationDrills");
            migrationBuilder.DropColumn(name: "Focus", table: "PronunciationDrills");
            migrationBuilder.DropColumn(name: "PrimaryRuleId", table: "PronunciationDrills");
            migrationBuilder.DropColumn(name: "AudioModelAssetId", table: "PronunciationDrills");
            migrationBuilder.DropColumn(name: "OrderIndex", table: "PronunciationDrills");
            migrationBuilder.DropColumn(name: "CreatedAt", table: "PronunciationDrills");
            migrationBuilder.DropColumn(name: "UpdatedAt", table: "PronunciationDrills");
        }
    }
}
