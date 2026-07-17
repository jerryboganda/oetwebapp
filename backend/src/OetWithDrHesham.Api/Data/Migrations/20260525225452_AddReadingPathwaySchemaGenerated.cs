using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OetWithDrHesham.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddReadingPathwaySchemaGenerated : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "LearnerBadges",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    BadgeCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    EarnedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LearnerBadges", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "LearnerLessonProgresses",
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
                    table.PrimaryKey("PK_LearnerLessonProgresses", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "LearnerReadingPathways",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    TotalWeeks = table.Column<int>(type: "integer", nullable: false),
                    GeneratedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    WeeksJson = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LearnerReadingPathways", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "LearnerReadingProfiles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    TargetBand = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    ExamDate = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    HoursPerWeek = table.Column<int>(type: "integer", nullable: false),
                    Profession = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    HasTakenBefore = table.Column<bool>(type: "boolean", nullable: false),
                    PreviousScore = table.Column<int>(type: "integer", nullable: true),
                    SelfRatedSpeed = table.Column<int>(type: "integer", nullable: false),
                    SelfRatedVocabulary = table.Column<int>(type: "integer", nullable: false),
                    CurrentStage = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CurrentReadinessScore = table.Column<int>(type: "integer", nullable: true),
                    PredictedScore = table.Column<int>(type: "integer", nullable: true),
                    OnboardingCompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    PathwayGeneratedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LearnerReadingProfiles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "LearnerSkillScores",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SkillCode = table.Column<string>(type: "character varying(4)", maxLength: 4, nullable: false),
                    CurrentScore = table.Column<decimal>(type: "numeric", nullable: false),
                    DiagnosticScore = table.Column<decimal>(type: "numeric", nullable: false),
                    QuestionsAttempted = table.Column<int>(type: "integer", nullable: false),
                    QuestionsCorrect = table.Column<int>(type: "integer", nullable: false),
                    LastPracticedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LearnerSkillScores", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "LearnerVocabularyItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    VocabularyWordId = table.Column<Guid>(type: "uuid", nullable: false),
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
                    table.PrimaryKey("PK_LearnerVocabularyItems", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "LearnerXps",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    TotalXp = table.Column<int>(type: "integer", nullable: false),
                    CurrentLevel = table.Column<int>(type: "integer", nullable: false),
                    XpToNextLevel = table.Column<int>(type: "integer", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LearnerXps", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ReadingDailyPlanItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    PlanDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Ordinal = table.Column<int>(type: "integer", nullable: false),
                    ItemType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    FocusSkill = table.Column<string>(type: "character varying(4)", maxLength: 4, nullable: true),
                    EstimatedMinutes = table.Column<int>(type: "integer", nullable: false),
                    PayloadJson = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReadingDailyPlanItems", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ReadingLessons",
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
                    table.PrimaryKey("PK_ReadingLessons", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ReadingMockTemplates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Title = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Difficulty = table.Column<int>(type: "integer", nullable: false),
                    QuestionIdsJson = table.Column<string>(type: "text", nullable: false),
                    IsPublished = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReadingMockTemplates", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ReadingPracticeSessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SessionType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    FocusSkill = table.Column<string>(type: "character varying(4)", maxLength: 4, nullable: true),
                    QuestionIdsJson = table.Column<string>(type: "text", nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DurationSeconds = table.Column<int>(type: "integer", nullable: true),
                    Score = table.Column<int>(type: "integer", nullable: true),
                    TotalQuestions = table.Column<int>(type: "integer", nullable: true),
                    MetadataJson = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReadingPracticeSessions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ReadingQuestionAttempts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ReadingQuestionId = table.Column<Guid>(type: "uuid", nullable: false),
                    PracticeSessionId = table.Column<Guid>(type: "uuid", nullable: true),
                    SelectedOption = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    IsCorrect = table.Column<bool>(type: "boolean", nullable: false),
                    IsUnknown = table.Column<bool>(type: "boolean", nullable: false),
                    TimeSpentSeconds = table.Column<int>(type: "integer", nullable: false),
                    MarkedForReview = table.Column<bool>(type: "boolean", nullable: false),
                    NoteText = table.Column<string>(type: "text", nullable: true),
                    AttemptedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    InReviewQueue = table.Column<bool>(type: "boolean", nullable: false),
                    NextReviewAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ReviewIntervalIndex = table.Column<int>(type: "integer", nullable: false),
                    ConsecutiveCorrect = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReadingQuestionAttempts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ReadingQuestionDiscussionComments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ReadingQuestionId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Body = table.Column<string>(type: "text", nullable: false),
                    Upvotes = table.Column<int>(type: "integer", nullable: false),
                    IsFromTutor = table.Column<bool>(type: "boolean", nullable: false),
                    IsHidden = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReadingQuestionDiscussionComments", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ReadingStrategies",
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
                    LinkedDrillId = table.Column<Guid>(type: "uuid", nullable: true),
                    RelatedStrategyIdsJson = table.Column<string>(type: "text", nullable: false),
                    UnlockStage = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Difficulty = table.Column<int>(type: "integer", nullable: false),
                    IsPublished = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReadingStrategies", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ReadingStrategyProgresses",
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
                    table.PrimaryKey("PK_ReadingStrategyProgresses", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "StreakRecords",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    HasActivity = table.Column<bool>(type: "boolean", nullable: false),
                    QuestionsAnsweredToday = table.Column<int>(type: "integer", nullable: false),
                    CurrentStreak = table.Column<int>(type: "integer", nullable: false),
                    LongestStreak = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StreakRecords", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "VocabularyLists",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Slug = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    NameAr = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Description = table.Column<string>(type: "text", nullable: false),
                    WordIdsJson = table.Column<string>(type: "text", nullable: false),
                    IsPublished = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VocabularyLists", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "VocabularyWords",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Word = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    PartOfSpeech = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    DefinitionEn = table.Column<string>(type: "text", nullable: false),
                    DefinitionAr = table.Column<string>(type: "text", nullable: false),
                    PronunciationIpa = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    AudioUrl = table.Column<string>(type: "text", nullable: true),
                    ExampleEn = table.Column<string>(type: "text", nullable: false),
                    ExampleAr = table.Column<string>(type: "text", nullable: false),
                    HealthcareContext = table.Column<string>(type: "text", nullable: false),
                    ProfessionRelevanceJson = table.Column<string>(type: "text", nullable: false),
                    Difficulty = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VocabularyWords", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_LearnerBadges_UserId_BadgeCode",
                table: "LearnerBadges",
                columns: new[] { "UserId", "BadgeCode" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LearnerLessonProgresses_UserId_LessonId",
                table: "LearnerLessonProgresses",
                columns: new[] { "UserId", "LessonId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LearnerReadingProfiles_UserId",
                table: "LearnerReadingProfiles",
                column: "UserId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LearnerSkillScores_UserId_SkillCode",
                table: "LearnerSkillScores",
                columns: new[] { "UserId", "SkillCode" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LearnerVocabularyItems_UserId_NextReviewAt",
                table: "LearnerVocabularyItems",
                columns: new[] { "UserId", "NextReviewAt" });

            migrationBuilder.CreateIndex(
                name: "IX_LearnerVocabularyItems_UserId_VocabularyWordId",
                table: "LearnerVocabularyItems",
                columns: new[] { "UserId", "VocabularyWordId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LearnerXps_UserId",
                table: "LearnerXps",
                column: "UserId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ReadingDailyPlanItems_UserId_PlanDate",
                table: "ReadingDailyPlanItems",
                columns: new[] { "UserId", "PlanDate" });

            migrationBuilder.CreateIndex(
                name: "IX_ReadingQuestionAttempts_UserId_AttemptedAt",
                table: "ReadingQuestionAttempts",
                columns: new[] { "UserId", "AttemptedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ReadingQuestionAttempts_UserId_InReviewQueue_NextReviewAt",
                table: "ReadingQuestionAttempts",
                columns: new[] { "UserId", "InReviewQueue", "NextReviewAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ReadingQuestionDiscussionComments_ReadingQuestionId",
                table: "ReadingQuestionDiscussionComments",
                column: "ReadingQuestionId");

            migrationBuilder.CreateIndex(
                name: "IX_ReadingStrategyProgresses_UserId_StrategyId",
                table: "ReadingStrategyProgresses",
                columns: new[] { "UserId", "StrategyId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StreakRecords_UserId_Date",
                table: "StreakRecords",
                columns: new[] { "UserId", "Date" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LearnerBadges");

            migrationBuilder.DropTable(
                name: "LearnerLessonProgresses");

            migrationBuilder.DropTable(
                name: "LearnerReadingPathways");

            migrationBuilder.DropTable(
                name: "LearnerReadingProfiles");

            migrationBuilder.DropTable(
                name: "LearnerSkillScores");

            migrationBuilder.DropTable(
                name: "LearnerVocabularyItems");

            migrationBuilder.DropTable(
                name: "LearnerXps");

            migrationBuilder.DropTable(
                name: "ReadingDailyPlanItems");

            migrationBuilder.DropTable(
                name: "ReadingLessons");

            migrationBuilder.DropTable(
                name: "ReadingMockTemplates");

            migrationBuilder.DropTable(
                name: "ReadingPracticeSessions");

            migrationBuilder.DropTable(
                name: "ReadingQuestionAttempts");

            migrationBuilder.DropTable(
                name: "ReadingQuestionDiscussionComments");

            migrationBuilder.DropTable(
                name: "ReadingStrategies");

            migrationBuilder.DropTable(
                name: "ReadingStrategyProgresses");

            migrationBuilder.DropTable(
                name: "StreakRecords");

            migrationBuilder.DropTable(
                name: "VocabularyLists");

            migrationBuilder.DropTable(
                name: "VocabularyWords");
        }
    }
}
