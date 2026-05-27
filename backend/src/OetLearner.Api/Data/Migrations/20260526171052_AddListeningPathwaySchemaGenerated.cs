using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OetLearner.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddListeningPathwaySchemaGenerated : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Accent",
                table: "ListeningQuestions",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SubSkillTagsCsv",
                table: "ListeningQuestions",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "RecoveryEmailSentAt",
                table: "Carts",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ClassFeedbacks",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ClassSessionId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    UserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Rating = table.Column<int>(type: "integer", nullable: false),
                    Comment = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: true),
                    RecommendToFriend = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClassFeedbacks", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ClassMaterials",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    LiveClassId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ClassSessionId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Title = table.Column<string>(type: "character varying(180)", maxLength: 180, nullable: false),
                    FileUrl = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    MimeType = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    Visibility = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClassMaterials", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ClassRecordingEmbeddings",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ClassRecordingId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ChunkIndex = table.Column<int>(type: "integer", nullable: false),
                    ChunkText = table.Column<string>(type: "text", nullable: false),
                    EmbeddingJson = table.Column<string>(type: "text", nullable: false),
                    EmbeddingModel = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    StartTimeSeconds = table.Column<int>(type: "integer", nullable: false),
                    EndTimeSeconds = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClassRecordingEmbeddings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ClassRecordingEmbeddings_LiveClassRecordings_ClassRecording~",
                        column: x => x.ClassRecordingId,
                        principalTable: "LiveClassRecordings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "DunningAttempts",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SubscriptionId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    InvoiceId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    UserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    AttemptNumber = table.Column<int>(type: "integer", nullable: false),
                    ScheduledAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExecutedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Outcome = table.Column<int>(type: "integer", nullable: false),
                    StripeFailureCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    FailureReason = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DunningAttempts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "LearnerAccentProgresses",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Accent = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    AccuracyPercentage = table.Column<decimal>(type: "numeric", nullable: false),
                    QuestionsAttempted = table.Column<int>(type: "integer", nullable: false),
                    QuestionsCorrect = table.Column<int>(type: "integer", nullable: false),
                    MinutesListened = table.Column<int>(type: "integer", nullable: false),
                    SelfConfidenceRating = table.Column<int>(type: "integer", nullable: false),
                    LastPracticedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LearnerAccentProgresses", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "LearnerListeningPathways",
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
                    table.PrimaryKey("PK_LearnerListeningPathways", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "LearnerListeningProfiles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    TargetBand = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    ExamDate = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    HoursPerWeek = table.Column<int>(type: "integer", nullable: false),
                    Profession = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    EnglishExposureSource = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ComfortBritish = table.Column<int>(type: "integer", nullable: false),
                    ComfortAustralian = table.Column<int>(type: "integer", nullable: false),
                    ComfortVarious = table.Column<int>(type: "integer", nullable: false),
                    HasTakenBefore = table.Column<bool>(type: "boolean", nullable: false),
                    PreviousScore = table.Column<int>(type: "integer", nullable: true),
                    SelfRatedSpeed = table.Column<int>(type: "integer", nullable: false),
                    SelfRatedNoteTaking = table.Column<int>(type: "integer", nullable: false),
                    SelfRatedSpelling = table.Column<int>(type: "integer", nullable: false),
                    CurrentStage = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CurrentReadinessScore = table.Column<int>(type: "integer", nullable: true),
                    PredictedScore = table.Column<int>(type: "integer", nullable: true),
                    OnboardingCompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    AudioCheckPassedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    PathwayGeneratedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LearnerListeningProfiles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "LearnerListeningSkillScores",
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
                    table.PrimaryKey("PK_LearnerListeningSkillScores", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ListeningPracticeNotes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    PracticeSessionId = table.Column<Guid>(type: "uuid", nullable: true),
                    ListeningQuestionId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    NoteMarkdown = table.Column<string>(type: "text", nullable: false),
                    CharacterCount = table.Column<int>(type: "integer", nullable: false),
                    LastSavedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ListeningPracticeNotes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ListeningPracticeSessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SessionType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    FocusSkill = table.Column<string>(type: "character varying(4)", maxLength: 4, nullable: true),
                    FocusAccent = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    QuestionIdsJson = table.Column<string>(type: "text", nullable: false),
                    AudioAssetIdsJson = table.Column<string>(type: "text", nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DurationSeconds = table.Column<int>(type: "integer", nullable: true),
                    Score = table.Column<int>(type: "integer", nullable: true),
                    TotalQuestions = table.Column<int>(type: "integer", nullable: true),
                    MetadataJson = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ListeningPracticeSessions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ListeningQuestionAttempts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ListeningQuestionId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    PracticeSessionId = table.Column<Guid>(type: "uuid", nullable: true),
                    AudioAssetId = table.Column<Guid>(type: "uuid", nullable: true),
                    SelectedOption = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    LearnerAnswer = table.Column<string>(type: "text", nullable: true),
                    IsCorrect = table.Column<bool>(type: "boolean", nullable: false),
                    IsUnknown = table.Column<bool>(type: "boolean", nullable: false),
                    IsSpellingCorrectMeaningWrong = table.Column<bool>(type: "boolean", nullable: false),
                    IsMeaningCorrectSpellingWrong = table.Column<bool>(type: "boolean", nullable: false),
                    ReplaysUsed = table.Column<int>(type: "integer", nullable: false),
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
                    table.PrimaryKey("PK_ListeningQuestionAttempts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Tutors",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    UserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    DisplayNameAr = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    Bio = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: false),
                    BioAr = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: true),
                    AvatarUrl = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    SpecialtiesJson = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    LanguagesJson = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    HourlyRateUsd = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: true),
                    TimeZone = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ZoomUserId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Tutors", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Tutors_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TutorAvailabilities",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    TutorId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    DayOfWeek = table.Column<int>(type: "integer", nullable: false),
                    StartTime = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    EndTime = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TutorAvailabilities", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TutorAvailabilities_Tutors_TutorId",
                        column: x => x.TutorId,
                        principalTable: "Tutors",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ClassFeedbacks_ClassSessionId",
                table: "ClassFeedbacks",
                column: "ClassSessionId");

            migrationBuilder.CreateIndex(
                name: "IX_ClassFeedbacks_ClassSessionId_UserId",
                table: "ClassFeedbacks",
                columns: new[] { "ClassSessionId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ClassFeedbacks_UserId",
                table: "ClassFeedbacks",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_ClassMaterials_ClassSessionId",
                table: "ClassMaterials",
                column: "ClassSessionId");

            migrationBuilder.CreateIndex(
                name: "IX_ClassMaterials_LiveClassId",
                table: "ClassMaterials",
                column: "LiveClassId");

            migrationBuilder.CreateIndex(
                name: "IX_ClassMaterials_LiveClassId_ClassSessionId",
                table: "ClassMaterials",
                columns: new[] { "LiveClassId", "ClassSessionId" });

            migrationBuilder.CreateIndex(
                name: "IX_ClassRecordingEmbeddings_ClassRecordingId",
                table: "ClassRecordingEmbeddings",
                column: "ClassRecordingId");

            migrationBuilder.CreateIndex(
                name: "IX_ClassRecordingEmbeddings_ClassRecordingId_ChunkIndex",
                table: "ClassRecordingEmbeddings",
                columns: new[] { "ClassRecordingId", "ChunkIndex" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DunningAttempts_InvoiceId_AttemptNumber",
                table: "DunningAttempts",
                columns: new[] { "InvoiceId", "AttemptNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DunningAttempts_Outcome_ScheduledAt",
                table: "DunningAttempts",
                columns: new[] { "Outcome", "ScheduledAt" });

            migrationBuilder.CreateIndex(
                name: "IX_DunningAttempts_SubscriptionId_InvoiceId",
                table: "DunningAttempts",
                columns: new[] { "SubscriptionId", "InvoiceId" });

            migrationBuilder.CreateIndex(
                name: "IX_LearnerAccentProgresses_UserId_Accent",
                table: "LearnerAccentProgresses",
                columns: new[] { "UserId", "Accent" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LearnerListeningProfiles_UserId",
                table: "LearnerListeningProfiles",
                column: "UserId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LearnerListeningSkillScores_UserId_SkillCode",
                table: "LearnerListeningSkillScores",
                columns: new[] { "UserId", "SkillCode" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ListeningPracticeNotes_UserId_PracticeSessionId_ListeningQu~",
                table: "ListeningPracticeNotes",
                columns: new[] { "UserId", "PracticeSessionId", "ListeningQuestionId" });

            migrationBuilder.CreateIndex(
                name: "IX_ListeningPracticeSessions_UserId_StartedAt",
                table: "ListeningPracticeSessions",
                columns: new[] { "UserId", "StartedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ListeningQuestionAttempts_UserId_AttemptedAt",
                table: "ListeningQuestionAttempts",
                columns: new[] { "UserId", "AttemptedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ListeningQuestionAttempts_UserId_InReviewQueue_NextReviewAt",
                table: "ListeningQuestionAttempts",
                columns: new[] { "UserId", "InReviewQueue", "NextReviewAt" });

            migrationBuilder.CreateIndex(
                name: "IX_TutorAvailabilities_TutorId",
                table: "TutorAvailabilities",
                column: "TutorId");

            migrationBuilder.CreateIndex(
                name: "IX_TutorAvailabilities_TutorId_DayOfWeek",
                table: "TutorAvailabilities",
                columns: new[] { "TutorId", "DayOfWeek" });

            migrationBuilder.CreateIndex(
                name: "IX_Tutors_IsActive",
                table: "Tutors",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "IX_Tutors_UserId",
                table: "Tutors",
                column: "UserId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ClassFeedbacks");

            migrationBuilder.DropTable(
                name: "ClassMaterials");

            migrationBuilder.DropTable(
                name: "ClassRecordingEmbeddings");

            migrationBuilder.DropTable(
                name: "DunningAttempts");

            migrationBuilder.DropTable(
                name: "LearnerAccentProgresses");

            migrationBuilder.DropTable(
                name: "LearnerListeningPathways");

            migrationBuilder.DropTable(
                name: "LearnerListeningProfiles");

            migrationBuilder.DropTable(
                name: "LearnerListeningSkillScores");

            migrationBuilder.DropTable(
                name: "ListeningPracticeNotes");

            migrationBuilder.DropTable(
                name: "ListeningPracticeSessions");

            migrationBuilder.DropTable(
                name: "ListeningQuestionAttempts");

            migrationBuilder.DropTable(
                name: "TutorAvailabilities");

            migrationBuilder.DropTable(
                name: "Tutors");

            migrationBuilder.DropColumn(
                name: "Accent",
                table: "ListeningQuestions");

            migrationBuilder.DropColumn(
                name: "SubSkillTagsCsv",
                table: "ListeningQuestions");

            migrationBuilder.DropColumn(
                name: "RecoveryEmailSentAt",
                table: "Carts");
        }
    }
}
