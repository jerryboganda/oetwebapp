using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OetWithDrHesham.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddListeningPathwayPhase2Through5Schema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DictationDrills",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DrillType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    AudioAssetUrl = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    DurationSeconds = table.Column<int>(type: "integer", nullable: false),
                    TranscriptText = table.Column<string>(type: "text", nullable: false),
                    AcceptableVariantsJson = table.Column<string>(type: "text", nullable: false),
                    Accent = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Difficulty = table.Column<int>(type: "integer", nullable: false),
                    ProfessionRelevanceJson = table.Column<string>(type: "text", nullable: false),
                    IsPublished = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DictationDrills", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "LearnerDictationProgresses",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    DictationDrillId = table.Column<Guid>(type: "uuid", nullable: false),
                    LearnerAnswer = table.Column<string>(type: "text", nullable: true),
                    IsCorrect = table.Column<bool>(type: "boolean", nullable: false),
                    Attempts = table.Column<int>(type: "integer", nullable: false),
                    NextReviewAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastAttemptedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LearnerDictationProgresses", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "LearnerListeningLessonProgresses",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    LessonId = table.Column<Guid>(type: "uuid", nullable: false),
                    VideoWatched = table.Column<bool>(type: "boolean", nullable: false),
                    BodyRead = table.Column<bool>(type: "boolean", nullable: false),
                    Drill1Completed = table.Column<bool>(type: "boolean", nullable: false),
                    Drill2Completed = table.Column<bool>(type: "boolean", nullable: false),
                    Drill3Completed = table.Column<bool>(type: "boolean", nullable: false),
                    QuizScore = table.Column<int>(type: "integer", nullable: true),
                    QuizAttempts = table.Column<int>(type: "integer", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LearnerListeningLessonProgresses", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "LearnerListeningStrategyProgresses",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    StrategyId = table.Column<Guid>(type: "uuid", nullable: false),
                    MarkedAsRead = table.Column<bool>(type: "boolean", nullable: false),
                    Favorited = table.Column<bool>(type: "boolean", nullable: false),
                    ReadAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LearnerListeningStrategyProgresses", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "LearnerPronunciationCards",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    PronunciationCardId = table.Column<Guid>(type: "uuid", nullable: false),
                    Source = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Easiness = table.Column<decimal>(type: "numeric", nullable: false),
                    IntervalDays = table.Column<int>(type: "integer", nullable: false),
                    Repetitions = table.Column<int>(type: "integer", nullable: false),
                    RetentionScore = table.Column<int>(type: "integer", nullable: false),
                    NextReviewAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastReviewedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    AddedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LearnerPronunciationCards", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ListeningDailyPlanItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    PlanDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Ordinal = table.Column<int>(type: "integer", nullable: false),
                    ItemType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    FocusSkill = table.Column<string>(type: "character varying(4)", maxLength: 4, nullable: true),
                    FocusAccent = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    EstimatedMinutes = table.Column<int>(type: "integer", nullable: false),
                    PayloadJson = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ListeningDailyPlanItems", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ListeningLessons",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Slug = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Title = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    TitleAr = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    SkillCode = table.Column<string>(type: "character varying(4)", maxLength: 4, nullable: false),
                    OrderIndex = table.Column<int>(type: "integer", nullable: false),
                    EstimatedMinutes = table.Column<int>(type: "integer", nullable: false),
                    VideoUrl = table.Column<string>(type: "text", nullable: true),
                    BodyMarkdownEn = table.Column<string>(type: "text", nullable: false),
                    BodyMarkdownAr = table.Column<string>(type: "text", nullable: false),
                    DrillQuestionIdsJson = table.Column<string>(type: "text", nullable: false),
                    QuizQuestionIdsJson = table.Column<string>(type: "text", nullable: false),
                    PrerequisiteLessonId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsPublished = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ListeningLessons", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ListeningMockTemplates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Title = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Difficulty = table.Column<int>(type: "integer", nullable: false),
                    QuestionIdsJson = table.Column<string>(type: "text", nullable: false),
                    DurationSeconds = table.Column<int>(type: "integer", nullable: false),
                    IsPublished = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ListeningMockTemplates", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ListeningStrategies",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Slug = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Title = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    TitleAr = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Category = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ApplicablePartsJson = table.Column<string>(type: "text", nullable: false),
                    EstimatedReadMinutes = table.Column<int>(type: "integer", nullable: false),
                    BodyMarkdownEn = table.Column<string>(type: "text", nullable: false),
                    BodyMarkdownAr = table.Column<string>(type: "text", nullable: false),
                    VideoUrl = table.Column<string>(type: "text", nullable: true),
                    AudioUrl = table.Column<string>(type: "text", nullable: true),
                    LinkedDrillId = table.Column<Guid>(type: "uuid", nullable: true),
                    UnlockStage = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Difficulty = table.Column<int>(type: "integer", nullable: false),
                    IsPublished = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ListeningStrategies", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PronunciationCards",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Word = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    PronunciationIpa = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    BritishIpa = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    AustralianIpa = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    AmericanIpa = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    AudioBritishUrl = table.Column<string>(type: "text", nullable: true),
                    AudioAustralianUrl = table.Column<string>(type: "text", nullable: true),
                    AudioAmericanUrl = table.Column<string>(type: "text", nullable: true),
                    DefinitionEn = table.Column<string>(type: "text", nullable: false),
                    DefinitionAr = table.Column<string>(type: "text", nullable: false),
                    SyllableCount = table.Column<int>(type: "integer", nullable: false),
                    StressPattern = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CommonMispronunciationsJson = table.Column<string>(type: "text", nullable: false),
                    SimilarSoundingTrapsJson = table.Column<string>(type: "text", nullable: false),
                    Difficulty = table.Column<int>(type: "integer", nullable: false),
                    ProfessionRelevanceJson = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PronunciationCards", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DictationDrills_DrillType_Accent_IsPublished",
                table: "DictationDrills",
                columns: new[] { "DrillType", "Accent", "IsPublished" });

            migrationBuilder.CreateIndex(
                name: "IX_LearnerDictationProgresses_UserId_DictationDrillId",
                table: "LearnerDictationProgresses",
                columns: new[] { "UserId", "DictationDrillId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LearnerDictationProgresses_UserId_NextReviewAt",
                table: "LearnerDictationProgresses",
                columns: new[] { "UserId", "NextReviewAt" });

            migrationBuilder.CreateIndex(
                name: "IX_LearnerListeningLessonProgresses_UserId_LessonId",
                table: "LearnerListeningLessonProgresses",
                columns: new[] { "UserId", "LessonId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LearnerListeningStrategyProgresses_UserId_StrategyId",
                table: "LearnerListeningStrategyProgresses",
                columns: new[] { "UserId", "StrategyId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LearnerPronunciationCards_UserId_NextReviewAt",
                table: "LearnerPronunciationCards",
                columns: new[] { "UserId", "NextReviewAt" });

            migrationBuilder.CreateIndex(
                name: "IX_LearnerPronunciationCards_UserId_PronunciationCardId",
                table: "LearnerPronunciationCards",
                columns: new[] { "UserId", "PronunciationCardId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ListeningDailyPlanItems_UserId_PlanDate",
                table: "ListeningDailyPlanItems",
                columns: new[] { "UserId", "PlanDate" });

            migrationBuilder.CreateIndex(
                name: "IX_ListeningLessons_SkillCode_OrderIndex",
                table: "ListeningLessons",
                columns: new[] { "SkillCode", "OrderIndex" });

            migrationBuilder.CreateIndex(
                name: "IX_ListeningLessons_Slug",
                table: "ListeningLessons",
                column: "Slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ListeningStrategies_Slug",
                table: "ListeningStrategies",
                column: "Slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PronunciationCards_Word",
                table: "PronunciationCards",
                column: "Word",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DictationDrills");

            migrationBuilder.DropTable(
                name: "LearnerDictationProgresses");

            migrationBuilder.DropTable(
                name: "LearnerListeningLessonProgresses");

            migrationBuilder.DropTable(
                name: "LearnerListeningStrategyProgresses");

            migrationBuilder.DropTable(
                name: "LearnerPronunciationCards");

            migrationBuilder.DropTable(
                name: "ListeningDailyPlanItems");

            migrationBuilder.DropTable(
                name: "ListeningLessons");

            migrationBuilder.DropTable(
                name: "ListeningMockTemplates");

            migrationBuilder.DropTable(
                name: "ListeningStrategies");

            migrationBuilder.DropTable(
                name: "PronunciationCards");
        }
    }
}
