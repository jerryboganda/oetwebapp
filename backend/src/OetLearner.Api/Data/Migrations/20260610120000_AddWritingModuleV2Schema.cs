using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OetLearner.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddWritingModuleV2Schema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ============================================================
            // Additive columns on existing LearnerWriting* tables.
            // All nullable / defaulted — safe for existing rows.
            // ============================================================
            migrationBuilder.AddColumn<string>(
                name: "SubDiscipline",
                table: "LearnerWritingProfiles",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "YearsExperience",
                table: "LearnerWritingProfiles",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "OptInCommunity",
                table: "LearnerWritingProfiles",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "OptInLeaderboard",
                table: "LearnerWritingProfiles",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "OptInDataForTraining",
                table: "LearnerWritingProfiles",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "AccommodationProfileJson",
                table: "LearnerWritingProfiles",
                type: "jsonb",
                nullable: false,
                defaultValue: "{}");

            migrationBuilder.AddColumn<string>(
                name: "CanonVersionPinned",
                table: "LearnerWritingProfiles",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "WeaknessVectorJson",
                table: "LearnerWritingPathways",
                type: "jsonb",
                nullable: false,
                defaultValue: "{}");

            migrationBuilder.AddColumn<string>(
                name: "SubSkillMasteryJson",
                table: "LearnerWritingPathways",
                type: "jsonb",
                nullable: false,
                defaultValue: "{}");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LastRecalculatedAt",
                table: "LearnerWritingPathways",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "DiagnosticSubmissionId",
                table: "LearnerWritingPathways",
                type: "uuid",
                nullable: true);

            // ============================================================
            // WritingScenarios + structured sentences + embeddings
            // ============================================================
            migrationBuilder.CreateTable(
                name: "WritingScenarios",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    LetterType = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    Profession = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SubDiscipline = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    TopicsJson = table.Column<string>(type: "jsonb", nullable: false, defaultValue: "[]"),
                    Difficulty = table.Column<int>(type: "integer", nullable: false),
                    CaseNotesMarkdown = table.Column<string>(type: "text", nullable: false),
                    CaseNotesStructuredJson = table.Column<string>(type: "jsonb", nullable: true),
                    EstimatedReadingMinutes = table.Column<int>(type: "integer", nullable: false),
                    IsDiagnostic = table.Column<bool>(type: "boolean", nullable: false),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    PreviousVersionId = table.Column<Guid>(type: "uuid", nullable: true),
                    AuthorId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ApprovedById = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    PublishedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table => table.PrimaryKey("PK_WritingScenarios", x => x.Id));

            migrationBuilder.CreateTable(
                name: "WritingScenarioStructuredSentences",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ScenarioId = table.Column<Guid>(type: "uuid", nullable: false),
                    Ordinal = table.Column<int>(type: "integer", nullable: false),
                    SentenceText = table.Column<string>(type: "text", nullable: false),
                    RelevanceLabel = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Notes = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table => table.PrimaryKey("PK_WritingScenarioStructuredSentences", x => x.Id));

            migrationBuilder.CreateTable(
                name: "WritingScenarioEmbeddings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ScenarioId = table.Column<Guid>(type: "uuid", nullable: false),
                    ModelId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Dimensions = table.Column<int>(type: "integer", nullable: false),
                    EmbeddingJson = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table => table.PrimaryKey("PK_WritingScenarioEmbeddings", x => x.Id));

            // ============================================================
            // WritingExemplars + annotations + embeddings
            // ============================================================
            migrationBuilder.CreateTable(
                name: "WritingExemplars",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ScenarioId = table.Column<Guid>(type: "uuid", nullable: true),
                    LetterType = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    Profession = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    LetterContent = table.Column<string>(type: "text", nullable: false),
                    AnnotationsJson = table.Column<string>(type: "jsonb", nullable: false, defaultValue: "[]"),
                    TargetBand = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    AuthorId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    PublishedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table => table.PrimaryKey("PK_WritingExemplars", x => x.Id));

            migrationBuilder.CreateTable(
                name: "WritingExemplarAnnotations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ExemplarId = table.Column<Guid>(type: "uuid", nullable: false),
                    Ordinal = table.Column<int>(type: "integer", nullable: false),
                    CharStart = table.Column<int>(type: "integer", nullable: true),
                    CharEnd = table.Column<int>(type: "integer", nullable: true),
                    AnnotationType = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    RuleId = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    Note = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table => table.PrimaryKey("PK_WritingExemplarAnnotations", x => x.Id));

            migrationBuilder.CreateTable(
                name: "WritingExemplarEmbeddings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ExemplarId = table.Column<Guid>(type: "uuid", nullable: false),
                    ModelId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Dimensions = table.Column<int>(type: "integer", nullable: false),
                    EmbeddingJson = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table => table.PrimaryKey("PK_WritingExemplarEmbeddings", x => x.Id));

            // ============================================================
            // WritingSubmissions + Grades + ScoreAppeals
            // ============================================================
            migrationBuilder.CreateTable(
                name: "WritingSubmissions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ScenarioId = table.Column<Guid>(type: "uuid", nullable: false),
                    Mode = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    LetterContent = table.Column<string>(type: "text", nullable: false),
                    LetterContentHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    WordCount = table.Column<int>(type: "integer", nullable: false),
                    TimeSpentSeconds = table.Column<int>(type: "integer", nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    SubmittedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    IsRevision = table.Column<bool>(type: "boolean", nullable: false),
                    OriginalSubmissionId = table.Column<Guid>(type: "uuid", nullable: true),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    GradingTier = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    InputSource = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table => table.PrimaryKey("PK_WritingSubmissions", x => x.Id));

            migrationBuilder.CreateTable(
                name: "WritingGrades",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SubmissionId = table.Column<Guid>(type: "uuid", nullable: false),
                    C1Purpose = table.Column<short>(type: "smallint", nullable: false),
                    C2Content = table.Column<short>(type: "smallint", nullable: false),
                    C3Conciseness = table.Column<short>(type: "smallint", nullable: false),
                    C4Genre = table.Column<short>(type: "smallint", nullable: false),
                    C5Organisation = table.Column<short>(type: "smallint", nullable: false),
                    C6Language = table.Column<short>(type: "smallint", nullable: false),
                    RawTotal = table.Column<short>(type: "smallint", nullable: false),
                    EstimatedBand = table.Column<int>(type: "integer", nullable: false),
                    BandLabel = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    PerCriterionFeedbackJson = table.Column<string>(type: "jsonb", nullable: false, defaultValue: "{}"),
                    TopThreePrioritiesJson = table.Column<string>(type: "jsonb", nullable: false, defaultValue: "[]"),
                    ConfidenceFlag = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    ModelUsed = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CanonVersion = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    AppealedByGradeId = table.Column<Guid>(type: "uuid", nullable: true),
                    TutorReviewId = table.Column<Guid>(type: "uuid", nullable: true),
                    GradedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table => table.PrimaryKey("PK_WritingGrades", x => x.Id));

            migrationBuilder.CreateTable(
                name: "WritingScoreAppeals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SubmissionId = table.Column<Guid>(type: "uuid", nullable: false),
                    OriginalGradeId = table.Column<Guid>(type: "uuid", nullable: false),
                    NewGradeId = table.Column<Guid>(type: "uuid", nullable: true),
                    UserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Resolution = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    ResolutionNote = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    DeltaRawPoints = table.Column<int>(type: "integer", nullable: true),
                    RequestedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ResolvedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table => table.PrimaryKey("PK_WritingScoreAppeals", x => x.Id));

            // ============================================================
            // WritingCanonRules + Violations
            // ============================================================
            migrationBuilder.CreateTable(
                name: "WritingCanonRules",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Category = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    AppliesToLetterTypesJson = table.Column<string>(type: "jsonb", nullable: false, defaultValue: "[]"),
                    AppliesToProfessionsJson = table.Column<string>(type: "jsonb", nullable: false, defaultValue: "[]"),
                    Severity = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    RuleText = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    CorrectExamplesJson = table.Column<string>(type: "jsonb", nullable: false, defaultValue: "[]"),
                    IncorrectExamplesJson = table.Column<string>(type: "jsonb", nullable: false, defaultValue: "[]"),
                    DetectionType = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    DetectionConfigJson = table.Column<string>(type: "jsonb", nullable: false, defaultValue: "{}"),
                    LessonId = table.Column<Guid>(type: "uuid", nullable: true),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    Active = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table => table.PrimaryKey("PK_WritingCanonRules", x => x.Id));

            migrationBuilder.CreateTable(
                name: "WritingCanonViolations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SubmissionId = table.Column<Guid>(type: "uuid", nullable: false),
                    RuleId = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Severity = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    Snippet = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    LineNumber = table.Column<int>(type: "integer", nullable: true),
                    CharStart = table.Column<int>(type: "integer", nullable: true),
                    CharEnd = table.Column<int>(type: "integer", nullable: true),
                    SuggestedFix = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Disputed = table.Column<bool>(type: "boolean", nullable: false),
                    DisputeResolution = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    DetectedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table => table.PrimaryKey("PK_WritingCanonViolations", x => x.Id));

            // ============================================================
            // WritingDrills + Attempts (+ Case-note variant)
            // ============================================================
            migrationBuilder.CreateTable(
                name: "WritingDrills",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DrillType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    TargetSubSkill = table.Column<string>(type: "character varying(4)", maxLength: 4, nullable: false),
                    TargetCanonRuleId = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    AppliesToProfessionsJson = table.Column<string>(type: "jsonb", nullable: false, defaultValue: "[]"),
                    AppliesToLetterTypesJson = table.Column<string>(type: "jsonb", nullable: false, defaultValue: "[]"),
                    Difficulty = table.Column<int>(type: "integer", nullable: false),
                    PromptMarkdown = table.Column<string>(type: "text", nullable: false),
                    ExpectedAnswer = table.Column<string>(type: "text", nullable: true),
                    AlternativesJson = table.Column<string>(type: "jsonb", nullable: false, defaultValue: "[]"),
                    GradingMethod = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    GradingConfigJson = table.Column<string>(type: "jsonb", nullable: false, defaultValue: "{}"),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table => table.PrimaryKey("PK_WritingDrills", x => x.Id));

            migrationBuilder.CreateTable(
                name: "WritingDrillAttempts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    DrillId = table.Column<Guid>(type: "uuid", nullable: false),
                    ResponseText = table.Column<string>(type: "text", nullable: true),
                    IsCorrect = table.Column<bool>(type: "boolean", nullable: false),
                    FeedbackText = table.Column<string>(type: "text", nullable: true),
                    TimeSpentSeconds = table.Column<int>(type: "integer", nullable: true),
                    EaseFactor = table.Column<double>(type: "double precision", nullable: false),
                    IntervalDays = table.Column<int>(type: "integer", nullable: false),
                    Repetitions = table.Column<int>(type: "integer", nullable: false),
                    NextDueAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    AttemptedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table => table.PrimaryKey("PK_WritingDrillAttempts", x => x.Id));

            migrationBuilder.CreateTable(
                name: "WritingCaseNoteDrills",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Profession = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    LetterType = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    Format = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CaseNotesMarkdown = table.Column<string>(type: "text", nullable: false),
                    Difficulty = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table => table.PrimaryKey("PK_WritingCaseNoteDrills", x => x.Id));

            migrationBuilder.CreateTable(
                name: "WritingCaseNoteDrillSentences",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DrillId = table.Column<Guid>(type: "uuid", nullable: false),
                    Ordinal = table.Column<int>(type: "integer", nullable: false),
                    SentenceText = table.Column<string>(type: "text", nullable: false),
                    RelevanceLabel = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Rationale = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table => table.PrimaryKey("PK_WritingCaseNoteDrillSentences", x => x.Id));

            migrationBuilder.CreateTable(
                name: "WritingCaseNoteDrillAttempts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    DrillId = table.Column<Guid>(type: "uuid", nullable: false),
                    ResponsesJson = table.Column<string>(type: "jsonb", nullable: false, defaultValue: "[]"),
                    CorrectCount = table.Column<int>(type: "integer", nullable: false),
                    TotalCount = table.Column<int>(type: "integer", nullable: false),
                    ScorePercent = table.Column<double>(type: "double precision", nullable: false),
                    TimeSpentSeconds = table.Column<int>(type: "integer", nullable: true),
                    AttemptedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table => table.PrimaryKey("PK_WritingCaseNoteDrillAttempts", x => x.Id));

            // ============================================================
            // WritingLessonsV2 + completions (V2 to avoid collision with the
            // existing slug-based WritingLesson table from the pathway slice).
            // ============================================================
            migrationBuilder.CreateTable(
                name: "WritingLessonsV2",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SubSkill = table.Column<string>(type: "character varying(4)", maxLength: 4, nullable: false),
                    OrderInCourse = table.Column<int>(type: "integer", nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    BodyMarkdown = table.Column<string>(type: "text", nullable: false),
                    VideoUrl = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    EstimatedMinutes = table.Column<int>(type: "integer", nullable: false),
                    QuizQuestionsJson = table.Column<string>(type: "jsonb", nullable: false, defaultValue: "[]"),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table => table.PrimaryKey("PK_WritingLessonsV2", x => x.Id));

            migrationBuilder.CreateTable(
                name: "WritingLessonCompletionsV2",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    LessonId = table.Column<Guid>(type: "uuid", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    QuizScore = table.Column<int>(type: "integer", nullable: true),
                    QuizAttempts = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table => table.PrimaryKey("PK_WritingLessonCompletionsV2", x => x.Id));

            // ============================================================
            // WritingMocks + sessions
            // ============================================================
            migrationBuilder.CreateTable(
                name: "WritingMocks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ScenarioId = table.Column<Guid>(type: "uuid", nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Difficulty = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table => table.PrimaryKey("PK_WritingMocks", x => x.Id));

            migrationBuilder.CreateTable(
                name: "WritingMockSessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    MockId = table.Column<Guid>(type: "uuid", nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ReadingPhaseEndedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    SubmittedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    SubmissionId = table.Column<Guid>(type: "uuid", nullable: true),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table => table.PrimaryKey("PK_WritingMockSessions", x => x.Id));

            // ============================================================
            // WritingReadinessScores + DraftV2 + PathwayItem
            // ============================================================
            migrationBuilder.CreateTable(
                name: "WritingReadinessScores",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    Score = table.Column<int>(type: "integer", nullable: false),
                    MockAverageBand = table.Column<decimal>(type: "numeric(4,2)", nullable: true),
                    TrajectorySlope = table.Column<decimal>(type: "numeric(4,2)", nullable: true),
                    CanonCleanRate = table.Column<decimal>(type: "numeric(4,2)", nullable: true),
                    TimeMgmtScore = table.Column<int>(type: "integer", nullable: true),
                    TypeConsistency = table.Column<int>(type: "integer", nullable: true),
                    PredictedBandLabel = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: true),
                    ComputedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table => table.PrimaryKey("PK_WritingReadinessScores", x => x.Id));

            migrationBuilder.CreateTable(
                name: "WritingDraftsV2",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ScenarioId = table.Column<Guid>(type: "uuid", nullable: false),
                    Mode = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Content = table.Column<string>(type: "text", nullable: false),
                    WordCount = table.Column<int>(type: "integer", nullable: false),
                    TimeSpentSeconds = table.Column<int>(type: "integer", nullable: false),
                    LastSavedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table => table.PrimaryKey("PK_WritingDraftsV2", x => x.Id));

            migrationBuilder.CreateTable(
                name: "WritingPathwayItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PathwayId = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderIndex = table.Column<int>(type: "integer", nullable: false),
                    Stage = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Phase = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    FocusSkill = table.Column<string>(type: "character varying(4)", maxLength: 4, nullable: true),
                    FocusCriterion = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    ItemKind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ContentRefId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    WeekNumber = table.Column<int>(type: "integer", nullable: true),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table => table.PrimaryKey("PK_WritingPathwayItems", x => x.Id));

            // ============================================================
            // WritingCommonMistakes + LearnerMistakeStats
            // ============================================================
            migrationBuilder.CreateTable(
                name: "WritingCommonMistakes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Category = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Summary = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    ExampleWrong = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    ExampleRight = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    CanonRuleId = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    RelatedSubSkill = table.Column<string>(type: "character varying(4)", maxLength: 4, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table => table.PrimaryKey("PK_WritingCommonMistakes", x => x.Id));

            migrationBuilder.CreateTable(
                name: "WritingLearnerMistakeStats",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    MistakeId = table.Column<Guid>(type: "uuid", nullable: false),
                    OccurrenceCount = table.Column<int>(type: "integer", nullable: false),
                    LastOccurredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    FirstOccurredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table => table.PrimaryKey("PK_WritingLearnerMistakeStats", x => x.Id));

            // ============================================================
            // WritingTutorReview + Assignment + Calibration
            // ============================================================
            migrationBuilder.CreateTable(
                name: "WritingTutorReviews",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SubmissionId = table.Column<Guid>(type: "uuid", nullable: false),
                    TutorId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    FreeTextFeedback = table.Column<string>(type: "text", nullable: false),
                    PerCriterionCommentsJson = table.Column<string>(type: "jsonb", nullable: false, defaultValue: "{}"),
                    ScoreOverrideJson = table.Column<string>(type: "jsonb", nullable: true),
                    SubmittedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    // Columns relocated here from 20260530185804_AddWritingExamModuleClosure:
                    // that earlier migration added them to WritingTutorReviews, but this
                    // migration (which actually CREATES the table) runs later, so a fresh-DB
                    // migrate crashed ("relation WritingTutorReviews does not exist"). Prod is
                    // unaffected (both migrations already applied; AutoMigrate skips them).
                    AcceptedAiPreAssessmentJson = table.Column<string>(type: "text", nullable: true),
                    ContentChecklistVerdictJson = table.Column<string>(type: "text", nullable: false, defaultValue: ""),
                    IsContentChecklistMarked = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    MarkerSequence = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false, defaultValue: "")
                },
                constraints: table => table.PrimaryKey("PK_WritingTutorReviews", x => x.Id));

            migrationBuilder.CreateTable(
                name: "WritingTutorReviewAssignments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SubmissionId = table.Column<Guid>(type: "uuid", nullable: false),
                    TutorId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ClaimedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    DueAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    ReleasedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table => table.PrimaryKey("PK_WritingTutorReviewAssignments", x => x.Id));

            migrationBuilder.CreateTable(
                name: "WritingTutorCalibrations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TutorId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    AgreementCoefficient = table.Column<decimal>(type: "numeric(5,4)", nullable: false),
                    SamplesReviewed = table.Column<int>(type: "integer", nullable: false),
                    LastCalibratedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false)
                },
                constraints: table => table.PrimaryKey("PK_WritingTutorCalibrations", x => x.Id));

            // ============================================================
            // WritingOcrJobs
            // ============================================================
            migrationBuilder.CreateTable(
                name: "WritingOcrJobs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SubmissionId = table.Column<Guid>(type: "uuid", nullable: true),
                    UserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Provider = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    ConfidenceScore = table.Column<double>(type: "double precision", nullable: true),
                    ExtractedText = table.Column<string>(type: "text", nullable: true),
                    ImageUrlsJson = table.Column<string>(type: "jsonb", nullable: false, defaultValue: "[]"),
                    ErrorMessage = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table => table.PrimaryKey("PK_WritingOcrJobs", x => x.Id));

            // ============================================================
            // WritingShowcasePosts
            // ============================================================
            migrationBuilder.CreateTable(
                name: "WritingShowcasePosts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SubmissionId = table.Column<Guid>(type: "uuid", nullable: false),
                    AuthorUserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    AnonymizedLetterContent = table.Column<string>(type: "text", nullable: false),
                    Profession = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    LetterType = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    ApprovedById = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    PublishedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table => table.PrimaryKey("PK_WritingShowcasePosts", x => x.Id));

            // ============================================================
            // Indexes — per spec §25.3 + service query patterns
            // ============================================================

            // Scenarios
            migrationBuilder.CreateIndex(name: "IX_WritingScenarios_Profession_LetterType", table: "WritingScenarios", columns: new[] { "Profession", "LetterType" });
            migrationBuilder.CreateIndex(name: "IX_WritingScenarios_Status", table: "WritingScenarios", column: "Status");
            migrationBuilder.CreateIndex(name: "IX_WritingScenarios_IsDiagnostic", table: "WritingScenarios", column: "IsDiagnostic");
            migrationBuilder.CreateIndex(name: "IX_WritingScenarioStructuredSentences_ScenarioId_Ordinal", table: "WritingScenarioStructuredSentences", columns: new[] { "ScenarioId", "Ordinal" }, unique: true);
            migrationBuilder.CreateIndex(name: "IX_WritingScenarioEmbeddings_ScenarioId", table: "WritingScenarioEmbeddings", column: "ScenarioId", unique: true);

            // Exemplars
            migrationBuilder.CreateIndex(name: "IX_WritingExemplars_Profession_LetterType", table: "WritingExemplars", columns: new[] { "Profession", "LetterType" });
            migrationBuilder.CreateIndex(name: "IX_WritingExemplars_Status", table: "WritingExemplars", column: "Status");
            migrationBuilder.CreateIndex(name: "IX_WritingExemplars_ScenarioId", table: "WritingExemplars", column: "ScenarioId");
            migrationBuilder.CreateIndex(name: "IX_WritingExemplarAnnotations_ExemplarId_Ordinal", table: "WritingExemplarAnnotations", columns: new[] { "ExemplarId", "Ordinal" }, unique: true);
            migrationBuilder.CreateIndex(name: "IX_WritingExemplarEmbeddings_ExemplarId", table: "WritingExemplarEmbeddings", column: "ExemplarId", unique: true);

            // Submissions + grades + appeals
            migrationBuilder.Sql("CREATE INDEX \"IX_WritingSubmissions_User_CreatedAt\" ON \"WritingSubmissions\" (\"UserId\", \"CreatedAt\" DESC);");
            migrationBuilder.Sql("CREATE INDEX \"IX_WritingSubmissions_Status_Pending\" ON \"WritingSubmissions\" (\"Status\") WHERE \"Status\" IN ('queued','grading');");
            migrationBuilder.CreateIndex(name: "IX_WritingSubmissions_LetterContentHash", table: "WritingSubmissions", column: "LetterContentHash");
            migrationBuilder.CreateIndex(name: "IX_WritingSubmissions_UserId_ScenarioId", table: "WritingSubmissions", columns: new[] { "UserId", "ScenarioId" });
            migrationBuilder.CreateIndex(name: "IX_WritingSubmissions_OriginalSubmissionId", table: "WritingSubmissions", column: "OriginalSubmissionId");

            migrationBuilder.CreateIndex(name: "IX_WritingGrades_SubmissionId", table: "WritingGrades", column: "SubmissionId", unique: true);
            migrationBuilder.CreateIndex(name: "IX_WritingGrades_AppealedByGradeId", table: "WritingGrades", column: "AppealedByGradeId");
            migrationBuilder.CreateIndex(name: "IX_WritingGrades_TutorReviewId", table: "WritingGrades", column: "TutorReviewId");

            migrationBuilder.CreateIndex(name: "IX_WritingScoreAppeals_SubmissionId", table: "WritingScoreAppeals", column: "SubmissionId");
            migrationBuilder.CreateIndex(name: "IX_WritingScoreAppeals_UserId_Status", table: "WritingScoreAppeals", columns: new[] { "UserId", "Status" });
            migrationBuilder.CreateIndex(name: "IX_WritingScoreAppeals_OriginalGradeId", table: "WritingScoreAppeals", column: "OriginalGradeId");

            // Canon rules + violations
            migrationBuilder.CreateIndex(name: "IX_WritingCanonRules_Category_Active", table: "WritingCanonRules", columns: new[] { "Category", "Active" });
            migrationBuilder.CreateIndex(name: "IX_WritingCanonRules_DetectionType", table: "WritingCanonRules", column: "DetectionType");
            migrationBuilder.CreateIndex(name: "IX_WritingCanonViolations_SubmissionId", table: "WritingCanonViolations", column: "SubmissionId");
            migrationBuilder.CreateIndex(name: "IX_WritingCanonViolations_RuleId", table: "WritingCanonViolations", column: "RuleId");
            migrationBuilder.CreateIndex(name: "IX_WritingCanonViolations_SubmissionId_RuleId", table: "WritingCanonViolations", columns: new[] { "SubmissionId", "RuleId" });

            // Drills
            migrationBuilder.CreateIndex(name: "IX_WritingDrills_DrillType_Status", table: "WritingDrills", columns: new[] { "DrillType", "Status" });
            migrationBuilder.CreateIndex(name: "IX_WritingDrills_TargetSubSkill", table: "WritingDrills", column: "TargetSubSkill");
            migrationBuilder.CreateIndex(name: "IX_WritingDrills_TargetCanonRuleId", table: "WritingDrills", column: "TargetCanonRuleId");
            migrationBuilder.Sql("CREATE INDEX \"IX_WritingDrillAttempts_User_Drill_Time\" ON \"WritingDrillAttempts\" (\"UserId\", \"DrillId\", \"AttemptedAt\" DESC);");
            migrationBuilder.CreateIndex(name: "IX_WritingDrillAttempts_UserId_NextDueAt", table: "WritingDrillAttempts", columns: new[] { "UserId", "NextDueAt" });

            // Case-note drills
            migrationBuilder.CreateIndex(name: "IX_WritingCaseNoteDrills_Profession_LetterType_Status", table: "WritingCaseNoteDrills", columns: new[] { "Profession", "LetterType", "Status" });
            migrationBuilder.CreateIndex(name: "IX_WritingCaseNoteDrillSentences_DrillId_Ordinal", table: "WritingCaseNoteDrillSentences", columns: new[] { "DrillId", "Ordinal" }, unique: true);
            migrationBuilder.Sql("CREATE INDEX \"IX_WritingCaseNoteDrillAttempts_User_Drill_Time\" ON \"WritingCaseNoteDrillAttempts\" (\"UserId\", \"DrillId\", \"AttemptedAt\" DESC);");

            // Lessons V2
            migrationBuilder.CreateIndex(name: "IX_WritingLessonsV2_SubSkill_OrderInCourse", table: "WritingLessonsV2", columns: new[] { "SubSkill", "OrderInCourse" });
            migrationBuilder.CreateIndex(name: "IX_WritingLessonsV2_Status", table: "WritingLessonsV2", column: "Status");
            migrationBuilder.CreateIndex(name: "IX_WritingLessonCompletionsV2_UserId_LessonId", table: "WritingLessonCompletionsV2", columns: new[] { "UserId", "LessonId" }, unique: true);

            // Mocks
            migrationBuilder.CreateIndex(name: "IX_WritingMocks_Status", table: "WritingMocks", column: "Status");
            migrationBuilder.CreateIndex(name: "IX_WritingMocks_ScenarioId", table: "WritingMocks", column: "ScenarioId");
            migrationBuilder.CreateIndex(name: "IX_WritingMockSessions_UserId_StartedAt", table: "WritingMockSessions", columns: new[] { "UserId", "StartedAt" });
            migrationBuilder.CreateIndex(name: "IX_WritingMockSessions_MockId", table: "WritingMockSessions", column: "MockId");
            migrationBuilder.CreateIndex(name: "IX_WritingMockSessions_Status", table: "WritingMockSessions", column: "Status");
            migrationBuilder.CreateIndex(name: "IX_WritingMockSessions_SubmissionId", table: "WritingMockSessions", column: "SubmissionId");

            // Readiness / drafts / pathway items
            migrationBuilder.CreateIndex(name: "IX_WritingReadinessScores_UserId_Date", table: "WritingReadinessScores", columns: new[] { "UserId", "Date" }, unique: true);
            migrationBuilder.CreateIndex(name: "IX_WritingReadinessScores_ComputedAt", table: "WritingReadinessScores", column: "ComputedAt");
            migrationBuilder.CreateIndex(name: "IX_WritingDraftsV2_UserId_ScenarioId_Mode", table: "WritingDraftsV2", columns: new[] { "UserId", "ScenarioId", "Mode" }, unique: true);
            migrationBuilder.Sql("CREATE INDEX \"IX_WritingDraftsV2_UserId_LastSavedAt\" ON \"WritingDraftsV2\" (\"UserId\", \"LastSavedAt\" DESC);");
            migrationBuilder.CreateIndex(name: "IX_WritingPathwayItems_PathwayId_OrderIndex", table: "WritingPathwayItems", columns: new[] { "PathwayId", "OrderIndex" });
            migrationBuilder.CreateIndex(name: "IX_WritingPathwayItems_PathwayId_Status", table: "WritingPathwayItems", columns: new[] { "PathwayId", "Status" });

            // Mistakes
            migrationBuilder.CreateIndex(name: "IX_WritingCommonMistakes_Category", table: "WritingCommonMistakes", column: "Category");
            migrationBuilder.CreateIndex(name: "IX_WritingCommonMistakes_CanonRuleId", table: "WritingCommonMistakes", column: "CanonRuleId");
            migrationBuilder.CreateIndex(name: "IX_WritingCommonMistakes_RelatedSubSkill", table: "WritingCommonMistakes", column: "RelatedSubSkill");
            migrationBuilder.CreateIndex(name: "IX_WritingLearnerMistakeStats_UserId_MistakeId", table: "WritingLearnerMistakeStats", columns: new[] { "UserId", "MistakeId" }, unique: true);
            migrationBuilder.Sql("CREATE INDEX \"IX_WritingLearnerMistakeStats_UserId_LastOccurredAt\" ON \"WritingLearnerMistakeStats\" (\"UserId\", \"LastOccurredAt\" DESC);");

            // Tutor
            migrationBuilder.CreateIndex(name: "IX_WritingTutorReviews_SubmissionId", table: "WritingTutorReviews", column: "SubmissionId");
            migrationBuilder.CreateIndex(name: "IX_WritingTutorReviews_TutorId_Status", table: "WritingTutorReviews", columns: new[] { "TutorId", "Status" });
            migrationBuilder.CreateIndex(name: "IX_WritingTutorReviewAssignments_TutorId_Status", table: "WritingTutorReviewAssignments", columns: new[] { "TutorId", "Status" });
            migrationBuilder.CreateIndex(name: "IX_WritingTutorReviewAssignments_SubmissionId", table: "WritingTutorReviewAssignments", column: "SubmissionId");
            migrationBuilder.CreateIndex(name: "IX_WritingTutorReviewAssignments_DueAt", table: "WritingTutorReviewAssignments", column: "DueAt");
            migrationBuilder.CreateIndex(name: "IX_WritingTutorCalibrations_TutorId", table: "WritingTutorCalibrations", column: "TutorId", unique: true);

            // OCR
            migrationBuilder.Sql("CREATE INDEX \"IX_WritingOcrJobs_UserId_CreatedAt\" ON \"WritingOcrJobs\" (\"UserId\", \"CreatedAt\" DESC);");
            migrationBuilder.CreateIndex(name: "IX_WritingOcrJobs_Status", table: "WritingOcrJobs", column: "Status");
            migrationBuilder.CreateIndex(name: "IX_WritingOcrJobs_SubmissionId", table: "WritingOcrJobs", column: "SubmissionId");

            // Showcase
            migrationBuilder.Sql("CREATE INDEX \"IX_WritingShowcasePosts_Status_PublishedAt\" ON \"WritingShowcasePosts\" (\"Status\", \"PublishedAt\" DESC);");
            migrationBuilder.CreateIndex(name: "IX_WritingShowcasePosts_Profession_LetterType_Status", table: "WritingShowcasePosts", columns: new[] { "Profession", "LetterType", "Status" });
            migrationBuilder.CreateIndex(name: "IX_WritingShowcasePosts_SubmissionId", table: "WritingShowcasePosts", column: "SubmissionId", unique: true);

            // ============================================================
            // WritingScenarios additive columns + indexes, relocated from the
            // EARLIER migration 20260530185804_AddWritingExamModuleClosure. That
            // migration ran before this one but referenced WritingScenarios, which
            // is created above in this migration's Up(), so a fresh-DB migrate
            // crashed ("relation WritingScenarios does not exist"). Definitions are
            // verbatim from the original migration. Prod is unaffected (both already
            // applied; AutoMigrate skips them).
            // ============================================================
            migrationBuilder.AddColumn<string>(
                name: "CaseNoteSectionsJson",
                table: "WritingScenarios",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ContentOwnerId",
                table: "WritingScenarios",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExpectedAction",
                table: "WritingScenarios",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExpectedPurpose",
                table: "WritingScenarios",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FixedInstructionsJson",
                table: "WritingScenarios",
                type: "jsonb",
                nullable: false,
                defaultValue: "[]");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "IntegrityAcknowledgedAt",
                table: "WritingScenarios",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "IntegrityAcknowledgedById",
                table: "WritingScenarios",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "InternalCode",
                table: "WritingScenarios",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MarkingMode",
                table: "WritingScenarios",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<Guid>(
                name: "ModelAnswerExemplarId",
                table: "WritingScenarios",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ReadingTimeSeconds",
                table: "WritingScenarios",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "RecipientJson",
                table: "WritingScenarios",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RetakePolicyJson",
                table: "WritingScenarios",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SimulationModes",
                table: "WritingScenarios",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "SourceContentPaperId",
                table: "WritingScenarios",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SourceProvenance",
                table: "WritingScenarios",
                type: "character varying(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TaskPromptMarkdown",
                table: "WritingScenarios",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TodayDate",
                table: "WritingScenarios",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "UpdatedAt",
                table: "WritingScenarios",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            migrationBuilder.AddColumn<int>(
                name: "WordGuideMax",
                table: "WritingScenarios",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "WordGuideMin",
                table: "WritingScenarios",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "WriterRole",
                table: "WritingScenarios",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "WritingTimeSeconds",
                table: "WritingScenarios",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_WritingScenarios_InternalCode",
                table: "WritingScenarios",
                column: "InternalCode");

            migrationBuilder.CreateIndex(
                name: "IX_WritingScenarios_SourceContentPaperId",
                table: "WritingScenarios",
                column: "SourceContentPaperId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "WritingShowcasePosts");
            migrationBuilder.DropTable(name: "WritingOcrJobs");
            migrationBuilder.DropTable(name: "WritingTutorCalibrations");
            migrationBuilder.DropTable(name: "WritingTutorReviewAssignments");
            migrationBuilder.DropTable(name: "WritingTutorReviews");
            migrationBuilder.DropTable(name: "WritingLearnerMistakeStats");
            migrationBuilder.DropTable(name: "WritingCommonMistakes");
            migrationBuilder.DropTable(name: "WritingPathwayItems");
            migrationBuilder.DropTable(name: "WritingDraftsV2");
            migrationBuilder.DropTable(name: "WritingReadinessScores");
            migrationBuilder.DropTable(name: "WritingMockSessions");
            migrationBuilder.DropTable(name: "WritingMocks");
            migrationBuilder.DropTable(name: "WritingLessonCompletionsV2");
            migrationBuilder.DropTable(name: "WritingLessonsV2");
            migrationBuilder.DropTable(name: "WritingCaseNoteDrillAttempts");
            migrationBuilder.DropTable(name: "WritingCaseNoteDrillSentences");
            migrationBuilder.DropTable(name: "WritingCaseNoteDrills");
            migrationBuilder.DropTable(name: "WritingDrillAttempts");
            migrationBuilder.DropTable(name: "WritingDrills");
            migrationBuilder.DropTable(name: "WritingCanonViolations");
            migrationBuilder.DropTable(name: "WritingCanonRules");
            migrationBuilder.DropTable(name: "WritingScoreAppeals");
            migrationBuilder.DropTable(name: "WritingGrades");
            migrationBuilder.DropTable(name: "WritingSubmissions");
            migrationBuilder.DropTable(name: "WritingExemplarEmbeddings");
            migrationBuilder.DropTable(name: "WritingExemplarAnnotations");
            migrationBuilder.DropTable(name: "WritingExemplars");
            migrationBuilder.DropTable(name: "WritingScenarioEmbeddings");
            migrationBuilder.DropTable(name: "WritingScenarioStructuredSentences");
            migrationBuilder.DropTable(name: "WritingScenarios");

            migrationBuilder.DropColumn(name: "DiagnosticSubmissionId", table: "LearnerWritingPathways");
            migrationBuilder.DropColumn(name: "LastRecalculatedAt", table: "LearnerWritingPathways");
            migrationBuilder.DropColumn(name: "SubSkillMasteryJson", table: "LearnerWritingPathways");
            migrationBuilder.DropColumn(name: "WeaknessVectorJson", table: "LearnerWritingPathways");

            migrationBuilder.DropColumn(name: "CanonVersionPinned", table: "LearnerWritingProfiles");
            migrationBuilder.DropColumn(name: "AccommodationProfileJson", table: "LearnerWritingProfiles");
            migrationBuilder.DropColumn(name: "OptInDataForTraining", table: "LearnerWritingProfiles");
            migrationBuilder.DropColumn(name: "OptInLeaderboard", table: "LearnerWritingProfiles");
            migrationBuilder.DropColumn(name: "OptInCommunity", table: "LearnerWritingProfiles");
            migrationBuilder.DropColumn(name: "YearsExperience", table: "LearnerWritingProfiles");
            migrationBuilder.DropColumn(name: "SubDiscipline", table: "LearnerWritingProfiles");
        }
    }
}
