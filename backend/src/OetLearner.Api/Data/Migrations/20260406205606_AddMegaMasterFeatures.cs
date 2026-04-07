using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OetLearner.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddMegaMasterFeatures : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ActiveExamTypeCode",
                table: "Users",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ExamTypeCode",
                table: "StudyPlans",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ExamTypeCode",
                table: "MockAttempts",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ExamTypeCode",
                table: "Goals",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ExamTypeCode",
                table: "Evaluations",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ExamTypeCode",
                table: "DiagnosticSessions",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ExamTypeCode",
                table: "Criteria",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "DifficultyRating",
                table: "ContentItems",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "ExamTypeCode",
                table: "ContentItems",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ExamTypeCode",
                table: "Attempts",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "AccountFreezeEntitlements",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    UserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    FreezeRecordId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ConsumedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ResetAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ResetByAdminId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ResetByAdminName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    ResetReason = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AccountFreezeEntitlements", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AccountFreezePolicies",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    IsEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    SelfServiceEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    ApprovalMode = table.Column<int>(type: "integer", nullable: false),
                    MinDurationDays = table.Column<int>(type: "integer", nullable: false),
                    MaxDurationDays = table.Column<int>(type: "integer", nullable: false),
                    AllowScheduling = table.Column<bool>(type: "boolean", nullable: false),
                    AccessMode = table.Column<int>(type: "integer", nullable: false),
                    EntitlementPauseMode = table.Column<int>(type: "integer", nullable: false),
                    RequireReason = table.Column<bool>(type: "boolean", nullable: false),
                    RequireInternalNotes = table.Column<bool>(type: "boolean", nullable: false),
                    AllowActivePaid = table.Column<bool>(type: "boolean", nullable: false),
                    AllowGracePeriod = table.Column<bool>(type: "boolean", nullable: false),
                    AllowTrial = table.Column<bool>(type: "boolean", nullable: false),
                    AllowComplimentary = table.Column<bool>(type: "boolean", nullable: false),
                    AllowCancelled = table.Column<bool>(type: "boolean", nullable: false),
                    AllowExpired = table.Column<bool>(type: "boolean", nullable: false),
                    AllowReviewOnly = table.Column<bool>(type: "boolean", nullable: false),
                    AllowPastDue = table.Column<bool>(type: "boolean", nullable: false),
                    AllowSuspended = table.Column<bool>(type: "boolean", nullable: false),
                    PolicyNotes = table.Column<string>(type: "text", nullable: false),
                    EligibilityReasonCodesJson = table.Column<string>(type: "text", nullable: false),
                    UpdatedByAdminId = table.Column<string>(type: "text", nullable: true),
                    UpdatedByAdminName = table.Column<string>(type: "text", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AccountFreezePolicies", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AccountFreezeRecords",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    UserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    RequestedByLearnerId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    RequestedByAdminId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    RequestedByAdminName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    ApprovedByAdminId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ApprovedByAdminName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    RejectedByAdminId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    RejectedByAdminName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    EndedByAdminId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    EndedByAdminName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    IsCurrent = table.Column<bool>(type: "boolean", nullable: false),
                    IsSelfService = table.Column<bool>(type: "boolean", nullable: false),
                    EntitlementConsumed = table.Column<bool>(type: "boolean", nullable: false),
                    EntitlementReset = table.Column<bool>(type: "boolean", nullable: false),
                    IsOverride = table.Column<bool>(type: "boolean", nullable: false),
                    RequestedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ScheduledStartAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    EndedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DurationDays = table.Column<int>(type: "integer", nullable: false),
                    Reason = table.Column<string>(type: "text", nullable: false),
                    InternalNotes = table.Column<string>(type: "text", nullable: true),
                    PolicySnapshotJson = table.Column<string>(type: "text", nullable: false),
                    PolicyVersionSnapshot = table.Column<int>(type: "integer", nullable: false),
                    EligibilitySnapshotJson = table.Column<string>(type: "text", nullable: false),
                    RejectionReason = table.Column<string>(type: "text", nullable: true),
                    EndReason = table.Column<string>(type: "text", nullable: true),
                    CancellationReason = table.Column<string>(type: "text", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AccountFreezeRecords", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Achievements",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Label = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Description = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    Category = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    IconUrl = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    XPReward = table.Column<int>(type: "integer", nullable: false),
                    CriteriaJson = table.Column<string>(type: "text", nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Achievements", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Certificates",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    UserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    UserDisplayName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Title = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Description = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    DataJson = table.Column<string>(type: "text", nullable: false),
                    PdfUrl = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    VerificationCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    IssuedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Certificates", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CohortMembers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CohortId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    LearnerId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    EnrolledAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CohortMembers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Cohorts",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SponsorId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ExamTypeCode = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    StartDate = table.Column<DateOnly>(type: "date", nullable: true),
                    EndDate = table.Column<DateOnly>(type: "date", nullable: true),
                    MaxSeats = table.Column<int>(type: "integer", nullable: false),
                    EnrolledCount = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Cohorts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ContentContributors",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    AuthAccountId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Bio = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    IsVerified = table.Column<bool>(type: "boolean", nullable: false),
                    PublishedCount = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ContentContributors", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ContentGenerationJobs",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    RequestedBy = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ExamTypeCode = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    SubtestCode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    TaskTypeId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ProfessionId = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    Difficulty = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    RequestedCount = table.Column<int>(type: "integer", nullable: false),
                    GeneratedCount = table.Column<int>(type: "integer", nullable: false),
                    PromptConfigJson = table.Column<string>(type: "text", nullable: false),
                    GeneratedContentIdsJson = table.Column<string>(type: "text", nullable: false),
                    State = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ErrorMessage = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ContentGenerationJobs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ContentSubmissions",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ContributorId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ExamTypeCode = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    SubtestCode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    TaskTypeId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ContentPayloadJson = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ReviewNotesJson = table.Column<string>(type: "text", nullable: true),
                    PublishedContentId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    SubmittedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ReviewedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ContentSubmissions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ConversationSessions",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    UserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ContentId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ExamTypeCode = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    SubtestCode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    TaskTypeCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ScenarioJson = table.Column<string>(type: "text", nullable: false),
                    State = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    TurnCount = table.Column<int>(type: "integer", nullable: false),
                    DurationSeconds = table.Column<int>(type: "integer", nullable: false),
                    TranscriptJson = table.Column<string>(type: "text", nullable: false),
                    EvaluationId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConversationSessions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ConversationTurns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SessionId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    TurnNumber = table.Column<int>(type: "integer", nullable: false),
                    Role = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Content = table.Column<string>(type: "text", nullable: false),
                    AudioUrl = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    DurationMs = table.Column<int>(type: "integer", nullable: false),
                    TimestampMs = table.Column<int>(type: "integer", nullable: false),
                    ConfidenceScore = table.Column<double>(type: "double precision", nullable: true),
                    AnalysisJson = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConversationTurns", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ExamBookings",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    UserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ExamTypeCode = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    ExamDate = table.Column<DateOnly>(type: "date", nullable: false),
                    BookingReference = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    ExternalUrl = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    TestCenter = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExamBookings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ExamTypes",
                columns: table => new
                {
                    Code = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Label = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Description = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    SubtestDefinitionsJson = table.Column<string>(type: "text", nullable: false),
                    ScoringSystemJson = table.Column<string>(type: "text", nullable: false),
                    TimingsJson = table.Column<string>(type: "text", nullable: false),
                    ProfessionIdsJson = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExamTypes", x => x.Code);
                });

            migrationBuilder.CreateTable(
                name: "ForumCategories",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ExamTypeCode = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    Name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Description = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ForumCategories", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ForumReplies",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ThreadId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    AuthorUserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    AuthorDisplayName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    AuthorRole = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Body = table.Column<string>(type: "text", nullable: false),
                    IsExpertVerified = table.Column<bool>(type: "boolean", nullable: false),
                    LikeCount = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    EditedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ForumReplies", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ForumThreads",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CategoryId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    AuthorUserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    AuthorDisplayName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    AuthorRole = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Title = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Body = table.Column<string>(type: "text", nullable: false),
                    IsPinned = table.Column<bool>(type: "boolean", nullable: false),
                    IsLocked = table.Column<bool>(type: "boolean", nullable: false),
                    ReplyCount = table.Column<int>(type: "integer", nullable: false),
                    ViewCount = table.Column<int>(type: "integer", nullable: false),
                    LikeCount = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastActivityAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ForumThreads", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "GrammarLessons",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ExamTypeCode = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Title = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Description = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    Category = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Level = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    ContentHtml = table.Column<string>(type: "text", nullable: false),
                    ExercisesJson = table.Column<string>(type: "text", nullable: false),
                    EstimatedMinutes = table.Column<int>(type: "integer", nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    PrerequisiteLessonId = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GrammarLessons", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "LeaderboardEntries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ExamTypeCode = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Period = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    PeriodStart = table.Column<DateOnly>(type: "date", nullable: false),
                    XP = table.Column<long>(type: "bigint", nullable: false),
                    Rank = table.Column<int>(type: "integer", nullable: false),
                    OptedIn = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LeaderboardEntries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "LearnerAchievements",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    AchievementId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    UnlockedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Notified = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LearnerAchievements", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "LearnerGrammarProgress",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    LessonId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    ExerciseScore = table.Column<int>(type: "integer", nullable: true),
                    AnswersJson = table.Column<string>(type: "text", nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LearnerGrammarProgress", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "LearnerPronunciationProgress",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    PhonemeCode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    AverageScore = table.Column<double>(type: "double precision", nullable: false),
                    AttemptCount = table.Column<int>(type: "integer", nullable: false),
                    ScoreHistoryJson = table.Column<string>(type: "text", nullable: false),
                    LastPracticedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LearnerPronunciationProgress", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "LearnerSkillProfiles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ExamTypeCode = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    SubtestCode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CriterionCode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CurrentRating = table.Column<double>(type: "double precision", nullable: false),
                    ConfidenceLevel = table.Column<int>(type: "integer", nullable: false),
                    EvidenceCount = table.Column<int>(type: "integer", nullable: false),
                    RecentScoresJson = table.Column<string>(type: "text", nullable: false),
                    LastUpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LearnerSkillProfiles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "LearnerStreaks",
                columns: table => new
                {
                    UserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CurrentStreak = table.Column<int>(type: "integer", nullable: false),
                    LongestStreak = table.Column<int>(type: "integer", nullable: false),
                    LastActiveDate = table.Column<DateOnly>(type: "date", nullable: false),
                    StreakFreezeCount = table.Column<int>(type: "integer", nullable: false),
                    StreakFreezeUsedCount = table.Column<int>(type: "integer", nullable: false),
                    LastFreezeUsedDate = table.Column<DateOnly>(type: "date", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LearnerStreaks", x => x.UserId);
                });

            migrationBuilder.CreateTable(
                name: "LearnerVideoProgress",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    VideoLessonId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    WatchedSeconds = table.Column<int>(type: "integer", nullable: false),
                    Completed = table.Column<bool>(type: "boolean", nullable: false),
                    LastWatchedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LearnerVideoProgress", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "LearnerVocabularies",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    TermId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Mastery = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    EaseFactor = table.Column<double>(type: "double precision", nullable: false),
                    IntervalDays = table.Column<int>(type: "integer", nullable: false),
                    ReviewCount = table.Column<int>(type: "integer", nullable: false),
                    CorrectCount = table.Column<int>(type: "integer", nullable: false),
                    NextReviewDate = table.Column<DateOnly>(type: "date", nullable: true),
                    LastReviewedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    AddedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LearnerVocabularies", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "LearnerXPs",
                columns: table => new
                {
                    UserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    TotalXP = table.Column<long>(type: "bigint", nullable: false),
                    WeeklyXP = table.Column<long>(type: "bigint", nullable: false),
                    MonthlyXP = table.Column<long>(type: "bigint", nullable: false),
                    Level = table.Column<int>(type: "integer", nullable: false),
                    WeekStartDate = table.Column<DateOnly>(type: "date", nullable: false),
                    MonthStartDate = table.Column<DateOnly>(type: "date", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LearnerXPs", x => x.UserId);
                });

            migrationBuilder.CreateTable(
                name: "PredictionSnapshots",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    UserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ExamTypeCode = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    SubtestCode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    PredictedScoreLow = table.Column<int>(type: "integer", nullable: false),
                    PredictedScoreHigh = table.Column<int>(type: "integer", nullable: false),
                    PredictedScoreMid = table.Column<int>(type: "integer", nullable: false),
                    ConfidenceLevel = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    FactorsJson = table.Column<string>(type: "text", nullable: false),
                    TrendJson = table.Column<string>(type: "text", nullable: false),
                    EvaluationCount = table.Column<int>(type: "integer", nullable: false),
                    ComputedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PredictionSnapshots", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PronunciationAssessments",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    UserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    AttemptId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ConversationSessionId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    AccuracyScore = table.Column<double>(type: "double precision", nullable: false),
                    FluencyScore = table.Column<double>(type: "double precision", nullable: false),
                    CompletenessScore = table.Column<double>(type: "double precision", nullable: false),
                    ProsodyScore = table.Column<double>(type: "double precision", nullable: false),
                    OverallScore = table.Column<double>(type: "double precision", nullable: false),
                    WordScoresJson = table.Column<string>(type: "text", nullable: false),
                    ProblematicPhonemesJson = table.Column<string>(type: "text", nullable: false),
                    FluencyMarkersJson = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PronunciationAssessments", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PronunciationDrills",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    TargetPhoneme = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Label = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ExampleWordsJson = table.Column<string>(type: "text", nullable: false),
                    MinimalPairsJson = table.Column<string>(type: "text", nullable: false),
                    SentencesJson = table.Column<string>(type: "text", nullable: false),
                    AudioModelUrl = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    TipsHtml = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    Difficulty = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PronunciationDrills", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ReferralCodes",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    UserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Code = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    TotalReferrals = table.Column<int>(type: "integer", nullable: false),
                    ConvertedReferrals = table.Column<int>(type: "integer", nullable: false),
                    TotalCreditsEarned = table.Column<decimal>(type: "numeric", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReferralCodes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Referrals",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ReferrerUserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ReferredUserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ReferredEmail = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CreditAmount = table.Column<decimal>(type: "numeric", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RegisteredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ConvertedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreditedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Referrals", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ReviewItems",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    UserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ExamTypeCode = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    SourceType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    SourceId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    SubtestCode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CriterionCode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    QuestionJson = table.Column<string>(type: "text", nullable: false),
                    AnswerJson = table.Column<string>(type: "text", nullable: false),
                    EaseFactor = table.Column<double>(type: "double precision", nullable: false),
                    IntervalDays = table.Column<int>(type: "integer", nullable: false),
                    ReviewCount = table.Column<int>(type: "integer", nullable: false),
                    ConsecutiveCorrect = table.Column<int>(type: "integer", nullable: false),
                    DueDate = table.Column<DateOnly>(type: "date", nullable: false),
                    LastReviewedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReviewItems", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SponsorAccounts",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    AuthAccountId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ContactEmail = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    OrganizationName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SponsorAccounts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SponsorLearnerLinks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SponsorId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    LearnerId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    LearnerConsented = table.Column<bool>(type: "boolean", nullable: false),
                    LinkedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ConsentedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SponsorLearnerLinks", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "StrategyGuides",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ExamTypeCode = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    SubtestCode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Summary = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    ContentHtml = table.Column<string>(type: "text", nullable: false),
                    Category = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ReadingTimeMinutes = table.Column<int>(type: "integer", nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    PublishedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StrategyGuides", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "StudyGroupMembers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    GroupId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    UserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Role = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    JoinedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StudyGroupMembers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "StudyGroups",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Description = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    ExamTypeCode = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    CreatorUserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    MaxMembers = table.Column<int>(type: "integer", nullable: false),
                    MemberCount = table.Column<int>(type: "integer", nullable: false),
                    IsPublic = table.Column<bool>(type: "boolean", nullable: false),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StudyGroups", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TaskTypes",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ExamTypeCode = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    SubtestCode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Label = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Description = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    ConfigJson = table.Column<string>(type: "text", nullable: false),
                    CriteriaIdsJson = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TaskTypes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TutoringAvailabilities",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ExpertUserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    DayOfWeek = table.Column<int>(type: "integer", nullable: false),
                    StartTime = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    EndTime = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    Timezone = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TutoringAvailabilities", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TutoringSessions",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    LearnerUserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ExpertUserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ExamTypeCode = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    SubtestFocus = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    ScheduledAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    DurationMinutes = table.Column<int>(type: "integer", nullable: false),
                    State = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    RoomUrl = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    LearnerNotes = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    ExpertNotes = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    Price = table.Column<decimal>(type: "numeric", nullable: false),
                    PaymentSource = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    LearnerRating = table.Column<int>(type: "integer", nullable: true),
                    LearnerFeedback = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TutoringSessions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "VideoLessons",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ExamTypeCode = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    SubtestCode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    Title = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Description = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    VideoUrl = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    ThumbnailUrl = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    DurationSeconds = table.Column<int>(type: "integer", nullable: false),
                    Category = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    InstructorName = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    ChaptersJson = table.Column<string>(type: "text", nullable: false),
                    ResourcesJson = table.Column<string>(type: "text", nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    PublishedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VideoLessons", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "VocabularyQuizResults",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    TermsQuizzed = table.Column<int>(type: "integer", nullable: false),
                    CorrectCount = table.Column<int>(type: "integer", nullable: false),
                    DurationSeconds = table.Column<int>(type: "integer", nullable: false),
                    ResultsJson = table.Column<string>(type: "text", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VocabularyQuizResults", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "VocabularyTerms",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Term = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Definition = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    ExampleSentence = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    ContextNotes = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    ExamTypeCode = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    ProfessionId = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    Category = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Difficulty = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    AudioUrl = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    ImageUrl = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    SynonymsJson = table.Column<string>(type: "text", nullable: false),
                    CollocationsJson = table.Column<string>(type: "text", nullable: false),
                    RelatedTermsJson = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VocabularyTerms", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "WritingCoachSessions",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    AttemptId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    UserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SuggestionsGenerated = table.Column<int>(type: "integer", nullable: false),
                    SuggestionsAccepted = table.Column<int>(type: "integer", nullable: false),
                    SuggestionsDismissed = table.Column<int>(type: "integer", nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WritingCoachSessions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "WritingCoachSuggestions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SessionId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    AttemptId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SuggestionType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    OriginalText = table.Column<string>(type: "text", nullable: false),
                    SuggestedText = table.Column<string>(type: "text", nullable: false),
                    Explanation = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    StartOffset = table.Column<int>(type: "integer", nullable: false),
                    EndOffset = table.Column<int>(type: "integer", nullable: false),
                    Resolution = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WritingCoachSuggestions", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ContentItems_ExamTypeCode",
                table: "ContentItems",
                column: "ExamTypeCode");

            migrationBuilder.CreateIndex(
                name: "IX_Attempts_ExamTypeCode",
                table: "Attempts",
                column: "ExamTypeCode");

            migrationBuilder.CreateIndex(
                name: "IX_AccountFreezeEntitlements_UserId",
                table: "AccountFreezeEntitlements",
                column: "UserId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AccountFreezePolicies_Version",
                table: "AccountFreezePolicies",
                column: "Version");

            migrationBuilder.CreateIndex(
                name: "IX_AccountFreezeRecords_Status_EndedAt",
                table: "AccountFreezeRecords",
                columns: new[] { "Status", "EndedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_AccountFreezeRecords_Status_ScheduledStartAt",
                table: "AccountFreezeRecords",
                columns: new[] { "Status", "ScheduledStartAt" });

            migrationBuilder.CreateIndex(
                name: "IX_AccountFreezeRecords_UserId",
                table: "AccountFreezeRecords",
                column: "UserId",
                unique: true,
                filter: "\"IsCurrent\" = TRUE");

            migrationBuilder.CreateIndex(
                name: "IX_AccountFreezeRecords_UserId_Status",
                table: "AccountFreezeRecords",
                columns: new[] { "UserId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_Achievements_Status_SortOrder",
                table: "Achievements",
                columns: new[] { "Status", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_Certificates_UserId",
                table: "Certificates",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_Certificates_VerificationCode",
                table: "Certificates",
                column: "VerificationCode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CohortMembers_CohortId_LearnerId",
                table: "CohortMembers",
                columns: new[] { "CohortId", "LearnerId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ContentGenerationJobs_RequestedBy",
                table: "ContentGenerationJobs",
                column: "RequestedBy");

            migrationBuilder.CreateIndex(
                name: "IX_ContentGenerationJobs_State_CreatedAt",
                table: "ContentGenerationJobs",
                columns: new[] { "State", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ContentSubmissions_ContributorId_Status",
                table: "ContentSubmissions",
                columns: new[] { "ContributorId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_ConversationSessions_UserId_State",
                table: "ConversationSessions",
                columns: new[] { "UserId", "State" });

            migrationBuilder.CreateIndex(
                name: "IX_ConversationTurns_SessionId_TurnNumber",
                table: "ConversationTurns",
                columns: new[] { "SessionId", "TurnNumber" });

            migrationBuilder.CreateIndex(
                name: "IX_ExamBookings_UserId_ExamDate",
                table: "ExamBookings",
                columns: new[] { "UserId", "ExamDate" });

            migrationBuilder.CreateIndex(
                name: "IX_ExamTypes_Status_SortOrder",
                table: "ExamTypes",
                columns: new[] { "Status", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_ForumReplies_ThreadId",
                table: "ForumReplies",
                column: "ThreadId");

            migrationBuilder.CreateIndex(
                name: "IX_ForumThreads_CategoryId_LastActivityAt",
                table: "ForumThreads",
                columns: new[] { "CategoryId", "LastActivityAt" });

            migrationBuilder.CreateIndex(
                name: "IX_GrammarLessons_ExamTypeCode_Category_Status",
                table: "GrammarLessons",
                columns: new[] { "ExamTypeCode", "Category", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_LeaderboardEntries_ExamTypeCode_Period_PeriodStart_Rank",
                table: "LeaderboardEntries",
                columns: new[] { "ExamTypeCode", "Period", "PeriodStart", "Rank" });

            migrationBuilder.CreateIndex(
                name: "IX_LeaderboardEntries_UserId_Period",
                table: "LeaderboardEntries",
                columns: new[] { "UserId", "Period" });

            migrationBuilder.CreateIndex(
                name: "IX_LearnerAchievements_UserId",
                table: "LearnerAchievements",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_LearnerAchievements_UserId_AchievementId",
                table: "LearnerAchievements",
                columns: new[] { "UserId", "AchievementId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LearnerGrammarProgress_UserId_LessonId",
                table: "LearnerGrammarProgress",
                columns: new[] { "UserId", "LessonId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LearnerPronunciationProgress_UserId_PhonemeCode",
                table: "LearnerPronunciationProgress",
                columns: new[] { "UserId", "PhonemeCode" });

            migrationBuilder.CreateIndex(
                name: "IX_LearnerSkillProfiles_UserId_ExamTypeCode_SubtestCode",
                table: "LearnerSkillProfiles",
                columns: new[] { "UserId", "ExamTypeCode", "SubtestCode" });

            migrationBuilder.CreateIndex(
                name: "IX_LearnerVideoProgress_UserId_VideoLessonId",
                table: "LearnerVideoProgress",
                columns: new[] { "UserId", "VideoLessonId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LearnerVocabularies_UserId_NextReviewDate",
                table: "LearnerVocabularies",
                columns: new[] { "UserId", "NextReviewDate" });

            migrationBuilder.CreateIndex(
                name: "IX_LearnerVocabularies_UserId_TermId",
                table: "LearnerVocabularies",
                columns: new[] { "UserId", "TermId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PredictionSnapshots_UserId_ExamTypeCode_SubtestCode_Compute~",
                table: "PredictionSnapshots",
                columns: new[] { "UserId", "ExamTypeCode", "SubtestCode", "ComputedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_PronunciationAssessments_UserId",
                table: "PronunciationAssessments",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_ReferralCodes_Code",
                table: "ReferralCodes",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ReferralCodes_UserId",
                table: "ReferralCodes",
                column: "UserId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Referrals_ReferrerUserId",
                table: "Referrals",
                column: "ReferrerUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ReviewItems_UserId_DueDate_Status",
                table: "ReviewItems",
                columns: new[] { "UserId", "DueDate", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_ReviewItems_UserId_ExamTypeCode_Status",
                table: "ReviewItems",
                columns: new[] { "UserId", "ExamTypeCode", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_SponsorLearnerLinks_SponsorId_LearnerId",
                table: "SponsorLearnerLinks",
                columns: new[] { "SponsorId", "LearnerId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StrategyGuides_ExamTypeCode_Category_Status",
                table: "StrategyGuides",
                columns: new[] { "ExamTypeCode", "Category", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_StudyGroupMembers_GroupId_UserId",
                table: "StudyGroupMembers",
                columns: new[] { "GroupId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StudyGroupMembers_UserId",
                table: "StudyGroupMembers",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_TaskTypes_ExamTypeCode_SubtestCode_Status",
                table: "TaskTypes",
                columns: new[] { "ExamTypeCode", "SubtestCode", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_TutoringAvailabilities_ExpertUserId",
                table: "TutoringAvailabilities",
                column: "ExpertUserId");

            migrationBuilder.CreateIndex(
                name: "IX_TutoringSessions_ExpertUserId_ScheduledAt",
                table: "TutoringSessions",
                columns: new[] { "ExpertUserId", "ScheduledAt" });

            migrationBuilder.CreateIndex(
                name: "IX_TutoringSessions_LearnerUserId_ScheduledAt",
                table: "TutoringSessions",
                columns: new[] { "LearnerUserId", "ScheduledAt" });

            migrationBuilder.CreateIndex(
                name: "IX_VideoLessons_ExamTypeCode_Category_Status",
                table: "VideoLessons",
                columns: new[] { "ExamTypeCode", "Category", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_VocabularyTerms_ExamTypeCode_Status_Category",
                table: "VocabularyTerms",
                columns: new[] { "ExamTypeCode", "Status", "Category" });

            migrationBuilder.CreateIndex(
                name: "IX_WritingCoachSessions_AttemptId",
                table: "WritingCoachSessions",
                column: "AttemptId");

            migrationBuilder.CreateIndex(
                name: "IX_WritingCoachSuggestions_AttemptId_Resolution",
                table: "WritingCoachSuggestions",
                columns: new[] { "AttemptId", "Resolution" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AccountFreezeEntitlements");

            migrationBuilder.DropTable(
                name: "AccountFreezePolicies");

            migrationBuilder.DropTable(
                name: "AccountFreezeRecords");

            migrationBuilder.DropTable(
                name: "Achievements");

            migrationBuilder.DropTable(
                name: "Certificates");

            migrationBuilder.DropTable(
                name: "CohortMembers");

            migrationBuilder.DropTable(
                name: "Cohorts");

            migrationBuilder.DropTable(
                name: "ContentContributors");

            migrationBuilder.DropTable(
                name: "ContentGenerationJobs");

            migrationBuilder.DropTable(
                name: "ContentSubmissions");

            migrationBuilder.DropTable(
                name: "ConversationSessions");

            migrationBuilder.DropTable(
                name: "ConversationTurns");

            migrationBuilder.DropTable(
                name: "ExamBookings");

            migrationBuilder.DropTable(
                name: "ExamTypes");

            migrationBuilder.DropTable(
                name: "ForumCategories");

            migrationBuilder.DropTable(
                name: "ForumReplies");

            migrationBuilder.DropTable(
                name: "ForumThreads");

            migrationBuilder.DropTable(
                name: "GrammarLessons");

            migrationBuilder.DropTable(
                name: "LeaderboardEntries");

            migrationBuilder.DropTable(
                name: "LearnerAchievements");

            migrationBuilder.DropTable(
                name: "LearnerGrammarProgress");

            migrationBuilder.DropTable(
                name: "LearnerPronunciationProgress");

            migrationBuilder.DropTable(
                name: "LearnerSkillProfiles");

            migrationBuilder.DropTable(
                name: "LearnerStreaks");

            migrationBuilder.DropTable(
                name: "LearnerVideoProgress");

            migrationBuilder.DropTable(
                name: "LearnerVocabularies");

            migrationBuilder.DropTable(
                name: "LearnerXPs");

            migrationBuilder.DropTable(
                name: "PredictionSnapshots");

            migrationBuilder.DropTable(
                name: "PronunciationAssessments");

            migrationBuilder.DropTable(
                name: "PronunciationDrills");

            migrationBuilder.DropTable(
                name: "ReferralCodes");

            migrationBuilder.DropTable(
                name: "Referrals");

            migrationBuilder.DropTable(
                name: "ReviewItems");

            migrationBuilder.DropTable(
                name: "SponsorAccounts");

            migrationBuilder.DropTable(
                name: "SponsorLearnerLinks");

            migrationBuilder.DropTable(
                name: "StrategyGuides");

            migrationBuilder.DropTable(
                name: "StudyGroupMembers");

            migrationBuilder.DropTable(
                name: "StudyGroups");

            migrationBuilder.DropTable(
                name: "TaskTypes");

            migrationBuilder.DropTable(
                name: "TutoringAvailabilities");

            migrationBuilder.DropTable(
                name: "TutoringSessions");

            migrationBuilder.DropTable(
                name: "VideoLessons");

            migrationBuilder.DropTable(
                name: "VocabularyQuizResults");

            migrationBuilder.DropTable(
                name: "VocabularyTerms");

            migrationBuilder.DropTable(
                name: "WritingCoachSessions");

            migrationBuilder.DropTable(
                name: "WritingCoachSuggestions");

            migrationBuilder.DropIndex(
                name: "IX_ContentItems_ExamTypeCode",
                table: "ContentItems");

            migrationBuilder.DropIndex(
                name: "IX_Attempts_ExamTypeCode",
                table: "Attempts");

            migrationBuilder.DropColumn(
                name: "ActiveExamTypeCode",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "ExamTypeCode",
                table: "StudyPlans");

            migrationBuilder.DropColumn(
                name: "ExamTypeCode",
                table: "MockAttempts");

            migrationBuilder.DropColumn(
                name: "ExamTypeCode",
                table: "Goals");

            migrationBuilder.DropColumn(
                name: "ExamTypeCode",
                table: "Evaluations");

            migrationBuilder.DropColumn(
                name: "ExamTypeCode",
                table: "DiagnosticSessions");

            migrationBuilder.DropColumn(
                name: "ExamTypeCode",
                table: "Criteria");

            migrationBuilder.DropColumn(
                name: "DifficultyRating",
                table: "ContentItems");

            migrationBuilder.DropColumn(
                name: "ExamTypeCode",
                table: "ContentItems");

            migrationBuilder.DropColumn(
                name: "ExamTypeCode",
                table: "Attempts");
        }
    }
}
