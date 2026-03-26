using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OetLearner.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AnalyticsEvents",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    UserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    EventName = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    PayloadJson = table.Column<string>(type: "text", nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AnalyticsEvents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Attempts",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    UserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ContentId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SubtestCode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Context = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Mode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    State = table.Column<int>(type: "integer", nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    SubmittedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ElapsedSeconds = table.Column<int>(type: "integer", nullable: false),
                    DraftVersion = table.Column<int>(type: "integer", nullable: false),
                    ParentAttemptId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ComparisonGroupId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    DeviceType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    LastClientSyncAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DraftContent = table.Column<string>(type: "text", nullable: false),
                    Scratchpad = table.Column<string>(type: "text", nullable: false),
                    ChecklistJson = table.Column<string>(type: "text", nullable: false),
                    AnswersJson = table.Column<string>(type: "text", nullable: false),
                    AudioUploadState = table.Column<int>(type: "integer", nullable: false),
                    AudioObjectKey = table.Column<string>(type: "text", nullable: true),
                    AudioMetadataJson = table.Column<string>(type: "text", nullable: false),
                    TranscriptJson = table.Column<string>(type: "text", nullable: false),
                    AnalysisJson = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Attempts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "BackgroundJobs",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Type = table.Column<int>(type: "integer", nullable: false),
                    State = table.Column<int>(type: "integer", nullable: false),
                    AttemptId = table.Column<string>(type: "text", nullable: true),
                    ResourceId = table.Column<string>(type: "text", nullable: true),
                    PayloadJson = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    AvailableAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastTransitionAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    StatusReasonCode = table.Column<string>(type: "text", nullable: false),
                    StatusMessage = table.Column<string>(type: "text", nullable: false),
                    Retryable = table.Column<bool>(type: "boolean", nullable: false),
                    RetryCount = table.Column<int>(type: "integer", nullable: false),
                    RetryAfterMs = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BackgroundJobs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ContentItems",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ContentType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    SubtestCode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ProfessionId = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Difficulty = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    EstimatedDurationMinutes = table.Column<int>(type: "integer", nullable: false),
                    CriteriaFocusJson = table.Column<string>(type: "text", nullable: false),
                    ScenarioType = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ModeSupportJson = table.Column<string>(type: "text", nullable: false),
                    PublishedRevisionId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    CaseNotes = table.Column<string>(type: "text", nullable: true),
                    DetailJson = table.Column<string>(type: "text", nullable: false),
                    ModelAnswerJson = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ContentItems", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Criteria",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    SubtestCode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Code = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Label = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Description = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Criteria", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DiagnosticSessions",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    UserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    State = table.Column<int>(type: "integer", nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DiagnosticSessions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DiagnosticSubtests",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    DiagnosticSessionId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SubtestCode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    State = table.Column<int>(type: "integer", nullable: false),
                    EstimatedDurationMinutes = table.Column<int>(type: "integer", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    AttemptId = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DiagnosticSubtests", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Evaluations",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    AttemptId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SubtestCode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    State = table.Column<int>(type: "integer", nullable: false),
                    ScoreRange = table.Column<string>(type: "text", nullable: false),
                    GradeRange = table.Column<string>(type: "text", nullable: true),
                    ConfidenceBand = table.Column<int>(type: "integer", nullable: false),
                    StrengthsJson = table.Column<string>(type: "text", nullable: false),
                    IssuesJson = table.Column<string>(type: "text", nullable: false),
                    CriterionScoresJson = table.Column<string>(type: "text", nullable: false),
                    FeedbackItemsJson = table.Column<string>(type: "text", nullable: false),
                    GeneratedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ModelExplanationSafe = table.Column<string>(type: "text", nullable: false),
                    LearnerDisclaimer = table.Column<string>(type: "text", nullable: false),
                    StatusReasonCode = table.Column<string>(type: "text", nullable: false),
                    StatusMessage = table.Column<string>(type: "text", nullable: false),
                    Retryable = table.Column<bool>(type: "boolean", nullable: false),
                    RetryAfterMs = table.Column<int>(type: "integer", nullable: true),
                    LastTransitionAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Evaluations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ExpertAvailabilities",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ReviewerId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Timezone = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    DaysJson = table.Column<string>(type: "text", nullable: false),
                    EffectiveFrom = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    EffectiveTo = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExpertAvailabilities", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ExpertCalibrationCases",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SubtestCode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ProfessionId = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    BenchmarkLabel = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    CaseArtifactsJson = table.Column<string>(type: "text", nullable: false),
                    ReferenceRubricJson = table.Column<string>(type: "text", nullable: false),
                    ReferenceNotesJson = table.Column<string>(type: "text", nullable: false),
                    Difficulty = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    BenchmarkScore = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExpertCalibrationCases", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ExpertCalibrationNotes",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Type = table.Column<int>(type: "integer", nullable: false),
                    Message = table.Column<string>(type: "text", nullable: false),
                    CaseId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ReviewerId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExpertCalibrationNotes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ExpertCalibrationResults",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CalibrationCaseId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ReviewerId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SubmittedRubricJson = table.Column<string>(type: "text", nullable: false),
                    ReviewerScore = table.Column<int>(type: "integer", nullable: false),
                    AlignmentScore = table.Column<double>(type: "double precision", nullable: false),
                    DisagreementSummary = table.Column<string>(type: "text", nullable: false),
                    Notes = table.Column<string>(type: "text", nullable: false),
                    SubmittedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExpertCalibrationResults", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ExpertMetricSnapshots",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ReviewerId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    WindowStart = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    WindowEnd = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CompletedReviews = table.Column<int>(type: "integer", nullable: false),
                    DraftReviews = table.Column<int>(type: "integer", nullable: false),
                    AvgTurnaroundHours = table.Column<double>(type: "double precision", nullable: false),
                    SlaHitRate = table.Column<double>(type: "double precision", nullable: false),
                    CalibrationScore = table.Column<double>(type: "double precision", nullable: false),
                    ReworkRate = table.Column<double>(type: "double precision", nullable: false),
                    CompletionDataJson = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExpertMetricSnapshots", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ExpertReviewAssignments",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ReviewRequestId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    AssignedReviewerId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    AssignedBy = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    AssignedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ClaimState = table.Column<int>(type: "integer", nullable: false),
                    ReleasedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ReassignedFrom = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ReasonCode = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExpertReviewAssignments", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ExpertReviewDrafts",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ReviewRequestId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ReviewerId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    State = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    RubricEntriesJson = table.Column<string>(type: "text", nullable: false),
                    CriterionCommentsJson = table.Column<string>(type: "text", nullable: false),
                    AnchoredCommentsJson = table.Column<string>(type: "text", nullable: false),
                    TimestampCommentsJson = table.Column<string>(type: "text", nullable: false),
                    FinalCommentDraft = table.Column<string>(type: "text", nullable: false),
                    DraftSavedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExpertReviewDrafts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ExpertUsers",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Role = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    SpecialtiesJson = table.Column<string>(type: "text", nullable: false),
                    Timezone = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExpertUsers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Goals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ProfessionId = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    TargetExamDate = table.Column<DateOnly>(type: "date", nullable: true),
                    OverallGoal = table.Column<string>(type: "text", nullable: true),
                    TargetWritingScore = table.Column<int>(type: "integer", nullable: true),
                    TargetSpeakingScore = table.Column<int>(type: "integer", nullable: true),
                    TargetReadingScore = table.Column<int>(type: "integer", nullable: true),
                    TargetListeningScore = table.Column<int>(type: "integer", nullable: true),
                    PreviousAttempts = table.Column<int>(type: "integer", nullable: false),
                    WeakSubtestsJson = table.Column<string>(type: "text", nullable: false),
                    StudyHoursPerWeek = table.Column<int>(type: "integer", nullable: false),
                    TargetCountry = table.Column<string>(type: "text", nullable: true),
                    TargetOrganization = table.Column<string>(type: "text", nullable: true),
                    DraftStateJson = table.Column<string>(type: "text", nullable: false),
                    SubmittedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Goals", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "IdempotencyRecords",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Scope = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Key = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ResponseJson = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IdempotencyRecords", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Invoices",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    UserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    IssuedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric", nullable: false),
                    Currency = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    Description = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Invoices", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MockAttempts",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    UserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ConfigJson = table.Column<string>(type: "text", nullable: false),
                    State = table.Column<int>(type: "integer", nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    SubmittedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ReportId = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MockAttempts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MockReports",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    MockAttemptId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    State = table.Column<int>(type: "integer", nullable: false),
                    PayloadJson = table.Column<string>(type: "text", nullable: false),
                    GeneratedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MockReports", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Professions",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Code = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Label = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Professions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ReadinessSnapshots",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    UserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ComputedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    PayloadJson = table.Column<string>(type: "text", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReadinessSnapshots", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ReviewRequests",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    AttemptId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SubtestCode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    State = table.Column<int>(type: "integer", nullable: false),
                    TurnaroundOption = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    FocusAreasJson = table.Column<string>(type: "text", nullable: false),
                    LearnerNotes = table.Column<string>(type: "text", nullable: false),
                    PaymentSource = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    PriceSnapshot = table.Column<decimal>(type: "numeric", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    EligibilitySnapshotJson = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReviewRequests", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Settings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ProfileJson = table.Column<string>(type: "text", nullable: false),
                    NotificationsJson = table.Column<string>(type: "text", nullable: false),
                    PrivacyJson = table.Column<string>(type: "text", nullable: false),
                    AccessibilityJson = table.Column<string>(type: "text", nullable: false),
                    AudioJson = table.Column<string>(type: "text", nullable: false),
                    StudyJson = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Settings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "StudyPlanItems",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    StudyPlanId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    SubtestCode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    DurationMinutes = table.Column<int>(type: "integer", nullable: false),
                    Rationale = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    DueDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Section = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ContentId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ItemType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StudyPlanItems", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "StudyPlans",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    UserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    GeneratedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    State = table.Column<int>(type: "integer", nullable: false),
                    Checkpoint = table.Column<string>(type: "text", nullable: false),
                    WeakSkillFocus = table.Column<string>(type: "text", nullable: false),
                    RetakeRescueMode = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StudyPlans", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Subscriptions",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    UserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    PlanId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    NextRenewalAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ChangedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    PriceAmount = table.Column<decimal>(type: "numeric", nullable: false),
                    Currency = table.Column<string>(type: "text", nullable: false),
                    Interval = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Subscriptions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Subtests",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Code = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Label = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SupportsProfessionSpecificContent = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Subtests", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "UploadSessions",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    AttemptId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    UploadUrl = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    StorageKey = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    State = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UploadSessions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Users",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Role = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Timezone = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Locale = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    CurrentPlanId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ActiveProfessionId = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    OnboardingCurrentStep = table.Column<int>(type: "integer", nullable: false),
                    OnboardingStepCount = table.Column<int>(type: "integer", nullable: false),
                    OnboardingCompleted = table.Column<bool>(type: "boolean", nullable: false),
                    OnboardingStartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    OnboardingCompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastActiveAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Users", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Wallets",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    UserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreditBalance = table.Column<int>(type: "integer", nullable: false),
                    LedgerSummaryJson = table.Column<string>(type: "text", nullable: false),
                    LastUpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Wallets", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AnalyticsEvents_UserId_EventName_OccurredAt",
                table: "AnalyticsEvents",
                columns: new[] { "UserId", "EventName", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Attempts_UserId_SubtestCode_State",
                table: "Attempts",
                columns: new[] { "UserId", "SubtestCode", "State" });

            migrationBuilder.CreateIndex(
                name: "IX_BackgroundJobs_State_AvailableAt",
                table: "BackgroundJobs",
                columns: new[] { "State", "AvailableAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ContentItems_SubtestCode_Status",
                table: "ContentItems",
                columns: new[] { "SubtestCode", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_Evaluations_AttemptId_State",
                table: "Evaluations",
                columns: new[] { "AttemptId", "State" });

            migrationBuilder.CreateIndex(
                name: "IX_ExpertAvailabilities_ReviewerId",
                table: "ExpertAvailabilities",
                column: "ReviewerId");

            migrationBuilder.CreateIndex(
                name: "IX_ExpertCalibrationResults_CalibrationCaseId_ReviewerId",
                table: "ExpertCalibrationResults",
                columns: new[] { "CalibrationCaseId", "ReviewerId" });

            migrationBuilder.CreateIndex(
                name: "IX_ExpertMetricSnapshots_ReviewerId_WindowStart",
                table: "ExpertMetricSnapshots",
                columns: new[] { "ReviewerId", "WindowStart" });

            migrationBuilder.CreateIndex(
                name: "IX_ExpertReviewAssignments_AssignedReviewerId",
                table: "ExpertReviewAssignments",
                column: "AssignedReviewerId");

            migrationBuilder.CreateIndex(
                name: "IX_ExpertReviewAssignments_ReviewRequestId_ClaimState",
                table: "ExpertReviewAssignments",
                columns: new[] { "ReviewRequestId", "ClaimState" });

            migrationBuilder.CreateIndex(
                name: "IX_ExpertReviewDrafts_ReviewRequestId_ReviewerId",
                table: "ExpertReviewDrafts",
                columns: new[] { "ReviewRequestId", "ReviewerId" });

            migrationBuilder.CreateIndex(
                name: "IX_IdempotencyRecords_Scope_Key",
                table: "IdempotencyRecords",
                columns: new[] { "Scope", "Key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Invoices_UserId_IssuedAt",
                table: "Invoices",
                columns: new[] { "UserId", "IssuedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ReviewRequests_AttemptId_State",
                table: "ReviewRequests",
                columns: new[] { "AttemptId", "State" });

            migrationBuilder.CreateIndex(
                name: "IX_StudyPlanItems_StudyPlanId_Section_Status",
                table: "StudyPlanItems",
                columns: new[] { "StudyPlanId", "Section", "Status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AnalyticsEvents");

            migrationBuilder.DropTable(
                name: "Attempts");

            migrationBuilder.DropTable(
                name: "BackgroundJobs");

            migrationBuilder.DropTable(
                name: "ContentItems");

            migrationBuilder.DropTable(
                name: "Criteria");

            migrationBuilder.DropTable(
                name: "DiagnosticSessions");

            migrationBuilder.DropTable(
                name: "DiagnosticSubtests");

            migrationBuilder.DropTable(
                name: "Evaluations");

            migrationBuilder.DropTable(
                name: "ExpertAvailabilities");

            migrationBuilder.DropTable(
                name: "ExpertCalibrationCases");

            migrationBuilder.DropTable(
                name: "ExpertCalibrationNotes");

            migrationBuilder.DropTable(
                name: "ExpertCalibrationResults");

            migrationBuilder.DropTable(
                name: "ExpertMetricSnapshots");

            migrationBuilder.DropTable(
                name: "ExpertReviewAssignments");

            migrationBuilder.DropTable(
                name: "ExpertReviewDrafts");

            migrationBuilder.DropTable(
                name: "ExpertUsers");

            migrationBuilder.DropTable(
                name: "Goals");

            migrationBuilder.DropTable(
                name: "IdempotencyRecords");

            migrationBuilder.DropTable(
                name: "Invoices");

            migrationBuilder.DropTable(
                name: "MockAttempts");

            migrationBuilder.DropTable(
                name: "MockReports");

            migrationBuilder.DropTable(
                name: "Professions");

            migrationBuilder.DropTable(
                name: "ReadinessSnapshots");

            migrationBuilder.DropTable(
                name: "ReviewRequests");

            migrationBuilder.DropTable(
                name: "Settings");

            migrationBuilder.DropTable(
                name: "StudyPlanItems");

            migrationBuilder.DropTable(
                name: "StudyPlans");

            migrationBuilder.DropTable(
                name: "Subscriptions");

            migrationBuilder.DropTable(
                name: "Subtests");

            migrationBuilder.DropTable(
                name: "UploadSessions");

            migrationBuilder.DropTable(
                name: "Users");

            migrationBuilder.DropTable(
                name: "Wallets");
        }
    }
}
