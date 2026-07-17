using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OetWithDrHesham.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class Mocks_V2_W8_AllPhases : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PayloadSchemaVersion",
                table: "MockReports",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Stage",
                table: "MockContentReviews",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ModelAnswerReleasePolicy",
                table: "MockBundleSections",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "InterlocutorTrainingModules",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    OrderIndex = table.Column<int>(type: "integer", nullable: false),
                    ContentMarkdown = table.Column<string>(type: "text", nullable: false),
                    MediaAssetIdsJson = table.Column<string>(type: "text", nullable: false),
                    RequiredForCalibration = table.Column<bool>(type: "boolean", nullable: false),
                    Stage = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    PublishedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InterlocutorTrainingModules", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MockEntitlementLedgers",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    UserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    AddOnId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    MockType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ConsumedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    MockAttemptId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MockEntitlementLedgers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RolePlayCards",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ContentItemId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ProfessionId = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ScenarioTitle = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Setting = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    CandidateRole = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    InterlocutorRole = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    PatientName = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    PatientAge = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    Background = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    Task1 = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Task2 = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Task3 = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Task4 = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Task5 = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    AllowedNotes = table.Column<bool>(type: "boolean", nullable: false),
                    PrepTimeSeconds = table.Column<int>(type: "integer", nullable: false),
                    RolePlayTimeSeconds = table.Column<int>(type: "integer", nullable: false),
                    PatientEmotion = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CommunicationGoal = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ClinicalTopic = table.Column<string>(type: "character varying(96)", maxLength: 96, nullable: false),
                    Difficulty = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    CriteriaFocusJson = table.Column<string>(type: "text", nullable: false),
                    Disclaimer = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    IsLiveTutorEligible = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedByUserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    PublishedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ArchivedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RolePlayCards", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RolePlayCards_ContentItems_ContentItemId",
                        column: x => x.ContentItemId,
                        principalTable: "ContentItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SpeakingComplianceConsents",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    UserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ConsentType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ConsentVersion = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    AcceptedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    AcceptedFromIp = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    UserAgent = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    RevokedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SpeakingComplianceConsents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SpeakingDrillItems",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ContentItemId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    DrillKind = table.Column<int>(type: "integer", nullable: false),
                    TargetCriteriaJson = table.Column<string>(type: "text", nullable: false),
                    RecommendedAfterSessionScoreBelow = table.Column<int>(type: "integer", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SpeakingDrillItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SpeakingDrillItems_ContentItems_ContentItemId",
                        column: x => x.ContentItemId,
                        principalTable: "ContentItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SpeakingReviewVoiceNotes",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ReviewRequestId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ExpertUserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    MediaAssetId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    DurationSeconds = table.Column<int>(type: "integer", nullable: false),
                    TranscriptText = table.Column<string>(type: "text", nullable: true),
                    WrittenNotes = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    RubricJson = table.Column<string>(type: "character varying(8000)", maxLength: 8000, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SpeakingReviewVoiceNotes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "InterlocutorTrainingProgress",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    TutorId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ModuleId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    QuizScore = table.Column<int>(type: "integer", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InterlocutorTrainingProgress", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InterlocutorTrainingProgress_InterlocutorTrainingModules_Mo~",
                        column: x => x.ModuleId,
                        principalTable: "InterlocutorTrainingModules",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "InterlocutorScripts",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    RolePlayCardId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    OpeningResponse = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Prompt1 = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Prompt2 = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Prompt3 = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    HiddenInformation = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    ResistanceLevel = table.Column<int>(type: "integer", nullable: false),
                    ClosingCue = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    EmotionalState = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ProfessionRoleNotes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    LayLanguageTriggersJson = table.Column<string>(type: "text", nullable: false),
                    CreatedByUserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InterlocutorScripts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InterlocutorScripts_RolePlayCards_RolePlayCardId",
                        column: x => x.RolePlayCardId,
                        principalTable: "RolePlayCards",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SpeakingSessions",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    UserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    RolePlayCardId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    MockSetId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    MockSessionId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Mode = table.Column<int>(type: "integer", nullable: false),
                    State = table.Column<int>(type: "integer", nullable: false),
                    InterlocutorActorId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    LiveRoomId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    AttemptId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    PrepStartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RolePlayStartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    EndedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ElapsedSeconds = table.Column<int>(type: "integer", nullable: false),
                    ConsentVersion = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ConsentAcceptedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    PaperDestroyedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SpeakingSessions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SpeakingSessions_RolePlayCards_RolePlayCardId",
                        column: x => x.RolePlayCardId,
                        principalTable: "RolePlayCards",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SpeakingDrillAttempts",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    UserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    DrillItemId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Score = table.Column<int>(type: "integer", nullable: true),
                    AudioRecordingId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    TranscriptId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    AiFeedbackJson = table.Column<string>(type: "text", nullable: false),
                    Source = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SpeakingDrillAttempts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SpeakingDrillAttempts_SpeakingDrillItems_DrillItemId",
                        column: x => x.DrillItemId,
                        principalTable: "SpeakingDrillItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SpeakingAiAssessments",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SpeakingSessionId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    TranscriptId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Provider = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ModelId = table.Column<string>(type: "character varying(96)", maxLength: 96, nullable: false),
                    PromptTemplateId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Intelligibility = table.Column<int>(type: "integer", nullable: false),
                    Fluency = table.Column<int>(type: "integer", nullable: false),
                    Appropriateness = table.Column<int>(type: "integer", nullable: false),
                    GrammarExpression = table.Column<int>(type: "integer", nullable: false),
                    RelationshipBuilding = table.Column<int>(type: "integer", nullable: false),
                    PatientPerspective = table.Column<int>(type: "integer", nullable: false),
                    Structure = table.Column<int>(type: "integer", nullable: false),
                    InformationGathering = table.Column<int>(type: "integer", nullable: false),
                    InformationGiving = table.Column<int>(type: "integer", nullable: false),
                    EstimatedScaledScore = table.Column<int>(type: "integer", nullable: false),
                    ReadinessBand = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    PerCriterionRationalesJson = table.Column<string>(type: "text", nullable: false),
                    OverallSummary = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    ConfidenceBand = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    GeneratedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RulebookFindingsJson = table.Column<string>(type: "text", nullable: false),
                    IsAdvisory = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SpeakingAiAssessments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SpeakingAiAssessments_SpeakingSessions_SpeakingSessionId",
                        column: x => x.SpeakingSessionId,
                        principalTable: "SpeakingSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SpeakingLiveRooms",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SpeakingSessionId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Provider = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    RoomName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    LearnerIdentity = table.Column<string>(type: "character varying(96)", maxLength: 96, nullable: false),
                    TutorIdentity = table.Column<string>(type: "character varying(96)", maxLength: 96, nullable: false),
                    LiveKitRoomSid = table.Column<string>(type: "character varying(96)", maxLength: 96, nullable: true),
                    ScheduledStartUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ActualStartUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ActualEndUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    State = table.Column<int>(type: "integer", nullable: false),
                    EgressId = table.Column<string>(type: "character varying(96)", maxLength: 96, nullable: true),
                    EgressOutputUrl = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    MaxDurationSeconds = table.Column<int>(type: "integer", nullable: false),
                    RecordingEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    RecordingConsentVersion = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    WebhookEventsJson = table.Column<string>(type: "text", nullable: false),
                    BookingId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SpeakingLiveRooms", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SpeakingLiveRooms_SpeakingSessions_SpeakingSessionId",
                        column: x => x.SpeakingSessionId,
                        principalTable: "SpeakingSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SpeakingRecordings",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SpeakingSessionId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    MediaAssetId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    Source = table.Column<int>(type: "integer", nullable: false),
                    DurationSeconds = table.Column<int>(type: "integer", nullable: false),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    Sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    MimeType = table.Column<string>(type: "character varying(96)", maxLength: 96, nullable: false),
                    ConsentVersion = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    IsArchived = table.Column<bool>(type: "boolean", nullable: false),
                    RetentionExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    EgressTrackId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SpeakingRecordings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SpeakingRecordings_MediaAssets_MediaAssetId",
                        column: x => x.MediaAssetId,
                        principalTable: "MediaAssets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SpeakingRecordings_SpeakingSessions_SpeakingSessionId",
                        column: x => x.SpeakingSessionId,
                        principalTable: "SpeakingSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SpeakingTimestampedComments",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SpeakingSessionId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    AuthorId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    AuthorRole = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    TranscriptSegmentIndex = table.Column<int>(type: "integer", nullable: false),
                    StartMs = table.Column<int>(type: "integer", nullable: false),
                    EndMs = table.Column<int>(type: "integer", nullable: false),
                    CriterionCode = table.Column<string>(type: "character varying(48)", maxLength: 48, nullable: false),
                    Severity = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    BodyMarkdown = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    LinkedRulebookEntryCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    LinkedDrillId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SpeakingTimestampedComments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SpeakingTimestampedComments_SpeakingSessions_SpeakingSessio~",
                        column: x => x.SpeakingSessionId,
                        principalTable: "SpeakingSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SpeakingTranscripts",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SpeakingSessionId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Provider = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Language = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    SegmentsJson = table.Column<string>(type: "text", nullable: false),
                    IsLatest = table.Column<bool>(type: "boolean", nullable: false),
                    WordCount = table.Column<int>(type: "integer", nullable: false),
                    MeanConfidence = table.Column<double>(type: "double precision", nullable: false),
                    GeneratedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SpeakingTranscripts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SpeakingTranscripts_SpeakingSessions_SpeakingSessionId",
                        column: x => x.SpeakingSessionId,
                        principalTable: "SpeakingSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SpeakingTutorAssessments",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SpeakingSessionId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    TutorId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Intelligibility = table.Column<int>(type: "integer", nullable: false),
                    Fluency = table.Column<int>(type: "integer", nullable: false),
                    Appropriateness = table.Column<int>(type: "integer", nullable: false),
                    GrammarExpression = table.Column<int>(type: "integer", nullable: false),
                    RelationshipBuilding = table.Column<int>(type: "integer", nullable: false),
                    PatientPerspective = table.Column<int>(type: "integer", nullable: false),
                    Structure = table.Column<int>(type: "integer", nullable: false),
                    InformationGathering = table.Column<int>(type: "integer", nullable: false),
                    InformationGiving = table.Column<int>(type: "integer", nullable: false),
                    EstimatedScaledScore = table.Column<int>(type: "integer", nullable: false),
                    ReadinessBand = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    OverallFeedbackMarkdown = table.Column<string>(type: "text", nullable: false),
                    StrengthsJson = table.Column<string>(type: "text", nullable: false),
                    ImprovementsJson = table.Column<string>(type: "text", nullable: false),
                    RecommendedDrillsJson = table.Column<string>(type: "text", nullable: false),
                    RecommendedRulebookEntries = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    IsFinal = table.Column<bool>(type: "boolean", nullable: false),
                    SubmittedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    MarkingDurationSeconds = table.Column<int>(type: "integer", nullable: false),
                    CalibrationDeltaJson = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SpeakingTutorAssessments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SpeakingTutorAssessments_SpeakingSessions_SpeakingSessionId",
                        column: x => x.SpeakingSessionId,
                        principalTable: "SpeakingSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SpeakingLiveRoomTokens",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    LiveRoomId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Identity = table.Column<string>(type: "character varying(96)", maxLength: 96, nullable: false),
                    IssuedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Role = table.Column<int>(type: "integer", nullable: false),
                    RevokedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Capabilities = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SpeakingLiveRoomTokens", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SpeakingLiveRoomTokens_SpeakingLiveRooms_LiveRoomId",
                        column: x => x.LiveRoomId,
                        principalTable: "SpeakingLiveRooms",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_InterlocutorScripts_RolePlayCardId",
                table: "InterlocutorScripts",
                column: "RolePlayCardId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_InterlocutorTrainingModules_Stage_Status",
                table: "InterlocutorTrainingModules",
                columns: new[] { "Stage", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_InterlocutorTrainingProgress_ModuleId",
                table: "InterlocutorTrainingProgress",
                column: "ModuleId");

            migrationBuilder.CreateIndex(
                name: "IX_InterlocutorTrainingProgress_TutorId_ModuleId",
                table: "InterlocutorTrainingProgress",
                columns: new[] { "TutorId", "ModuleId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MockEntitlementLedgers_AddOnId",
                table: "MockEntitlementLedgers",
                column: "AddOnId");

            migrationBuilder.CreateIndex(
                name: "IX_MockEntitlementLedgers_UserId_ConsumedAt",
                table: "MockEntitlementLedgers",
                columns: new[] { "UserId", "ConsumedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_MockEntitlementLedgers_UserId_MockType",
                table: "MockEntitlementLedgers",
                columns: new[] { "UserId", "MockType" });

            migrationBuilder.CreateIndex(
                name: "IX_RolePlayCards_ContentItemId",
                table: "RolePlayCards",
                column: "ContentItemId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RolePlayCards_ProfessionId_Status",
                table: "RolePlayCards",
                columns: new[] { "ProfessionId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_SpeakingAiAssessments_SpeakingSessionId",
                table: "SpeakingAiAssessments",
                column: "SpeakingSessionId");

            migrationBuilder.CreateIndex(
                name: "IX_SpeakingComplianceConsents_ConsentType_ConsentVersion",
                table: "SpeakingComplianceConsents",
                columns: new[] { "ConsentType", "ConsentVersion" });

            migrationBuilder.CreateIndex(
                name: "IX_SpeakingComplianceConsents_UserId_ConsentType",
                table: "SpeakingComplianceConsents",
                columns: new[] { "UserId", "ConsentType" });

            migrationBuilder.CreateIndex(
                name: "IX_SpeakingDrillAttempts_DrillItemId",
                table: "SpeakingDrillAttempts",
                column: "DrillItemId");

            migrationBuilder.CreateIndex(
                name: "IX_SpeakingDrillAttempts_UserId_DrillItemId",
                table: "SpeakingDrillAttempts",
                columns: new[] { "UserId", "DrillItemId" });

            migrationBuilder.CreateIndex(
                name: "IX_SpeakingDrillItems_ContentItemId",
                table: "SpeakingDrillItems",
                column: "ContentItemId");

            migrationBuilder.CreateIndex(
                name: "IX_SpeakingDrillItems_DrillKind",
                table: "SpeakingDrillItems",
                column: "DrillKind");

            migrationBuilder.CreateIndex(
                name: "IX_SpeakingLiveRooms_RoomName",
                table: "SpeakingLiveRooms",
                column: "RoomName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SpeakingLiveRooms_SpeakingSessionId",
                table: "SpeakingLiveRooms",
                column: "SpeakingSessionId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SpeakingLiveRoomTokens_LiveRoomId",
                table: "SpeakingLiveRoomTokens",
                column: "LiveRoomId");

            migrationBuilder.CreateIndex(
                name: "IX_SpeakingRecordings_MediaAssetId",
                table: "SpeakingRecordings",
                column: "MediaAssetId");

            migrationBuilder.CreateIndex(
                name: "IX_SpeakingRecordings_Sha256",
                table: "SpeakingRecordings",
                column: "Sha256");

            migrationBuilder.CreateIndex(
                name: "IX_SpeakingRecordings_SpeakingSessionId",
                table: "SpeakingRecordings",
                column: "SpeakingSessionId");

            migrationBuilder.CreateIndex(
                name: "IX_SpeakingReviewVoiceNotes_ReviewRequestId_CreatedAt",
                table: "SpeakingReviewVoiceNotes",
                columns: new[] { "ReviewRequestId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_SpeakingSessions_MockSessionId",
                table: "SpeakingSessions",
                column: "MockSessionId");

            migrationBuilder.CreateIndex(
                name: "IX_SpeakingSessions_RolePlayCardId_State",
                table: "SpeakingSessions",
                columns: new[] { "RolePlayCardId", "State" });

            migrationBuilder.CreateIndex(
                name: "IX_SpeakingSessions_UserId_State",
                table: "SpeakingSessions",
                columns: new[] { "UserId", "State" });

            migrationBuilder.CreateIndex(
                name: "IX_SpeakingTimestampedComments_SpeakingSessionId_StartMs",
                table: "SpeakingTimestampedComments",
                columns: new[] { "SpeakingSessionId", "StartMs" });

            migrationBuilder.CreateIndex(
                name: "IX_SpeakingTranscripts_SpeakingSessionId_IsLatest",
                table: "SpeakingTranscripts",
                columns: new[] { "SpeakingSessionId", "IsLatest" });

            migrationBuilder.CreateIndex(
                name: "IX_SpeakingTutorAssessments_SpeakingSessionId_IsFinal",
                table: "SpeakingTutorAssessments",
                columns: new[] { "SpeakingSessionId", "IsFinal" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "InterlocutorScripts");

            migrationBuilder.DropTable(
                name: "InterlocutorTrainingProgress");

            migrationBuilder.DropTable(
                name: "MockEntitlementLedgers");

            migrationBuilder.DropTable(
                name: "SpeakingAiAssessments");

            migrationBuilder.DropTable(
                name: "SpeakingComplianceConsents");

            migrationBuilder.DropTable(
                name: "SpeakingDrillAttempts");

            migrationBuilder.DropTable(
                name: "SpeakingLiveRoomTokens");

            migrationBuilder.DropTable(
                name: "SpeakingRecordings");

            migrationBuilder.DropTable(
                name: "SpeakingReviewVoiceNotes");

            migrationBuilder.DropTable(
                name: "SpeakingTimestampedComments");

            migrationBuilder.DropTable(
                name: "SpeakingTranscripts");

            migrationBuilder.DropTable(
                name: "SpeakingTutorAssessments");

            migrationBuilder.DropTable(
                name: "InterlocutorTrainingModules");

            migrationBuilder.DropTable(
                name: "SpeakingDrillItems");

            migrationBuilder.DropTable(
                name: "SpeakingLiveRooms");

            migrationBuilder.DropTable(
                name: "SpeakingSessions");

            migrationBuilder.DropTable(
                name: "RolePlayCards");

            migrationBuilder.DropColumn(
                name: "PayloadSchemaVersion",
                table: "MockReports");

            migrationBuilder.DropColumn(
                name: "Stage",
                table: "MockContentReviews");

            migrationBuilder.DropColumn(
                name: "ModelAnswerReleasePolicy",
                table: "MockBundleSections");
        }
    }
}
