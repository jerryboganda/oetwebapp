using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OetLearner.Api.Data.Migrations
{
    /// <summary>
    /// Grammar Module v2 — topic taxonomy, structured content blocks,
    /// typed server-graded exercises, per-attempt analytics rows,
    /// recommendations driven by writing/speaking evaluation flags,
    /// and mastery aggregates.
    ///
    /// See docs/GRAMMAR.md for the canonical contract.
    /// </summary>
    public partial class AddGrammarV2 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ── Extend GrammarLessons ────────────────────────────────────
            migrationBuilder.AddColumn<string>(
                name: "TopicId",
                table: "GrammarLessons",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Version",
                table: "GrammarLessons",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<string>(
                name: "PublishState",
                table: "GrammarLessons",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "draft");

            migrationBuilder.AddColumn<string>(
                name: "PrerequisiteLessonIds",
                table: "GrammarLessons",
                type: "text",
                nullable: false,
                defaultValue: "[]");

            migrationBuilder.AddColumn<string>(
                name: "SourceProvenance",
                table: "GrammarLessons",
                type: "character varying(512)",
                maxLength: 512,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "PublishedAt",
                table: "GrammarLessons",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "CreatedAt",
                table: "GrammarLessons",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "UpdatedAt",
                table: "GrammarLessons",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));

            migrationBuilder.CreateIndex(
                name: "IX_GrammarLessons_TopicId_PublishState",
                table: "GrammarLessons",
                columns: new[] { "TopicId", "PublishState" });

            // ── Extend LearnerGrammarProgress ─────────────────────────
            migrationBuilder.AddColumn<int>(
                name: "MasteryScore",
                table: "LearnerGrammarProgress",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "AttemptCount",
                table: "LearnerGrammarProgress",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LastAttemptedAt",
                table: "LearnerGrammarProgress",
                type: "timestamp with time zone",
                nullable: true);

            // ── GrammarTopics ──────────────────────────────────────────
            migrationBuilder.CreateTable(
                name: "GrammarTopics",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ExamTypeCode = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Slug = table.Column<string>(type: "character varying(96)", maxLength: 96, nullable: false),
                    Name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Description = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    IconEmoji = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    LevelHint = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false, defaultValue: "all"),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false, defaultValue: "draft"),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table => table.PrimaryKey("PK_GrammarTopics", x => x.Id));

            migrationBuilder.CreateIndex(
                name: "IX_GrammarTopics_ExamTypeCode_Slug",
                table: "GrammarTopics",
                columns: new[] { "ExamTypeCode", "Slug" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GrammarTopics_ExamTypeCode_Status_SortOrder",
                table: "GrammarTopics",
                columns: new[] { "ExamTypeCode", "Status", "SortOrder" });

            // ── GrammarContentBlocks ───────────────────────────────────
            migrationBuilder.CreateTable(
                name: "GrammarContentBlocks",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    LessonId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    Type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false, defaultValue: "prose"),
                    ContentMarkdown = table.Column<string>(type: "text", nullable: false),
                    ContentJson = table.Column<string>(type: "text", nullable: false, defaultValue: "{}")
                },
                constraints: table => table.PrimaryKey("PK_GrammarContentBlocks", x => x.Id));

            migrationBuilder.CreateIndex(
                name: "IX_GrammarContentBlocks_LessonId_SortOrder",
                table: "GrammarContentBlocks",
                columns: new[] { "LessonId", "SortOrder" });

            // ── GrammarExercises ────────────────────────────────────────
            migrationBuilder.CreateTable(
                name: "GrammarExercises",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    LessonId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    Type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    PromptMarkdown = table.Column<string>(type: "text", nullable: false),
                    OptionsJson = table.Column<string>(type: "text", nullable: false, defaultValue: "[]"),
                    CorrectAnswerJson = table.Column<string>(type: "text", nullable: false, defaultValue: "[]"),
                    AcceptedAnswersJson = table.Column<string>(type: "text", nullable: false, defaultValue: "[]"),
                    ExplanationMarkdown = table.Column<string>(type: "text", nullable: false, defaultValue: ""),
                    Difficulty = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false, defaultValue: "intermediate"),
                    Points = table.Column<int>(type: "integer", nullable: false, defaultValue: 1)
                },
                constraints: table => table.PrimaryKey("PK_GrammarExercises", x => x.Id));

            migrationBuilder.CreateIndex(
                name: "IX_GrammarExercises_LessonId_SortOrder",
                table: "GrammarExercises",
                columns: new[] { "LessonId", "SortOrder" });

            // ── GrammarExerciseAttempts ─────────────────────────────────
            migrationBuilder.CreateTable(
                name: "GrammarExerciseAttempts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    LessonId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ExerciseId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    UserAnswerJson = table.Column<string>(type: "text", nullable: false, defaultValue: "{}"),
                    IsCorrect = table.Column<bool>(type: "boolean", nullable: false),
                    PointsEarned = table.Column<int>(type: "integer", nullable: false),
                    AttemptIndex = table.Column<int>(type: "integer", nullable: false, defaultValue: 1),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table => table.PrimaryKey("PK_GrammarExerciseAttempts", x => x.Id));

            migrationBuilder.CreateIndex(
                name: "IX_GrammarExerciseAttempts_UserId_LessonId_CreatedAt",
                table: "GrammarExerciseAttempts",
                columns: new[] { "UserId", "LessonId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_GrammarExerciseAttempts_ExerciseId_IsCorrect",
                table: "GrammarExerciseAttempts",
                columns: new[] { "ExerciseId", "IsCorrect" });

            // ── GrammarRecommendations ─────────────────────────────────
            migrationBuilder.CreateTable(
                name: "GrammarRecommendations",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    UserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    LessonId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Source = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    SourceRefId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    RuleId = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    Relevance = table.Column<double>(type: "double precision", nullable: false, defaultValue: 1.0),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    DismissedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ActedOnAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table => table.PrimaryKey("PK_GrammarRecommendations", x => x.Id));

            migrationBuilder.CreateIndex(
                name: "IX_GrammarRecommendations_UserId_DismissedAt",
                table: "GrammarRecommendations",
                columns: new[] { "UserId", "DismissedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_GrammarRecommendations_UserId_LessonId",
                table: "GrammarRecommendations",
                columns: new[] { "UserId", "LessonId" },
                unique: true);

            // ── LearnerGrammarMasterySummaries ──────────────────────────
            migrationBuilder.CreateTable(
                name: "LearnerGrammarMasterySummaries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    TopicId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    LessonsCompleted = table.Column<int>(type: "integer", nullable: false),
                    LessonsMastered = table.Column<int>(type: "integer", nullable: false),
                    AvgMasteryScore = table.Column<double>(type: "double precision", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table => table.PrimaryKey("PK_LearnerGrammarMasterySummaries", x => x.Id));

            migrationBuilder.CreateIndex(
                name: "IX_LearnerGrammarMasterySummaries_UserId_TopicId",
                table: "LearnerGrammarMasterySummaries",
                columns: new[] { "UserId", "TopicId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "LearnerGrammarMasterySummaries");
            migrationBuilder.DropTable(name: "GrammarRecommendations");
            migrationBuilder.DropTable(name: "GrammarExerciseAttempts");
            migrationBuilder.DropTable(name: "GrammarExercises");
            migrationBuilder.DropTable(name: "GrammarContentBlocks");
            migrationBuilder.DropTable(name: "GrammarTopics");

            migrationBuilder.DropColumn(name: "MasteryScore", table: "LearnerGrammarProgress");
            migrationBuilder.DropColumn(name: "AttemptCount", table: "LearnerGrammarProgress");
            migrationBuilder.DropColumn(name: "LastAttemptedAt", table: "LearnerGrammarProgress");

            migrationBuilder.DropIndex(name: "IX_GrammarLessons_TopicId_PublishState", table: "GrammarLessons");
            migrationBuilder.DropColumn(name: "TopicId", table: "GrammarLessons");
            migrationBuilder.DropColumn(name: "Version", table: "GrammarLessons");
            migrationBuilder.DropColumn(name: "PublishState", table: "GrammarLessons");
            migrationBuilder.DropColumn(name: "PrerequisiteLessonIds", table: "GrammarLessons");
            migrationBuilder.DropColumn(name: "SourceProvenance", table: "GrammarLessons");
            migrationBuilder.DropColumn(name: "PublishedAt", table: "GrammarLessons");
            migrationBuilder.DropColumn(name: "CreatedAt", table: "GrammarLessons");
            migrationBuilder.DropColumn(name: "UpdatedAt", table: "GrammarLessons");
        }
    }
}
