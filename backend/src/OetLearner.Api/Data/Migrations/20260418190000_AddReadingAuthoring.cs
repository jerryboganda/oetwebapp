using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OetLearner.Api.Data.Migrations
{
    /// <inheritdoc />
    /// <remarks>
    /// Reading Authoring subsystem, Slice R1. Adds 7 tables for the
    /// structured Reading paper model (ReadingPart, ReadingText,
    /// ReadingQuestion) plus attempt lifecycle (ReadingAttempt,
    /// ReadingAnswer) and admin-configurable policy (ReadingPolicy,
    /// ReadingUserPolicyOverride).
    ///
    /// See <c>docs/READING-AUTHORING-PLAN.md</c> and
    /// <c>docs/READING-AUTHORING-POLICY.md</c>.
    /// </remarks>
    public partial class AddReadingAuthoring : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ── ReadingParts ──────────────────────────────────────────
            migrationBuilder.CreateTable(
                name: "ReadingParts",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    PaperId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    PartCode = table.Column<int>(type: "integer", nullable: false),
                    TimeLimitMinutes = table.Column<int>(type: "integer", nullable: false),
                    MaxRawScore = table.Column<int>(type: "integer", nullable: false),
                    Instructions = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                },
                constraints: table => { table.PrimaryKey("PK_ReadingParts", x => x.Id); });

            migrationBuilder.CreateIndex(
                name: "UX_ReadingPart_Paper_PartCode",
                table: "ReadingParts",
                columns: new[] { "PaperId", "PartCode" },
                unique: true);

            // ── ReadingTexts ──────────────────────────────────────────
            migrationBuilder.CreateTable(
                name: "ReadingTexts",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ReadingPartId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    DisplayOrder = table.Column<int>(type: "integer", nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Source = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    BodyHtml = table.Column<string>(type: "text", nullable: false),
                    WordCount = table.Column<int>(type: "integer", nullable: false),
                    TopicTag = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReadingTexts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReadingTexts_ReadingParts_ReadingPartId",
                        column: x => x.ReadingPartId,
                        principalTable: "ReadingParts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ReadingTexts_ReadingPartId_DisplayOrder",
                table: "ReadingTexts",
                columns: new[] { "ReadingPartId", "DisplayOrder" });

            // ── ReadingQuestions ──────────────────────────────────────
            migrationBuilder.CreateTable(
                name: "ReadingQuestions",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ReadingPartId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ReadingTextId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    DisplayOrder = table.Column<int>(type: "integer", nullable: false),
                    Points = table.Column<int>(type: "integer", nullable: false),
                    QuestionType = table.Column<int>(type: "integer", nullable: false),
                    Stem = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    OptionsJson = table.Column<string>(type: "text", nullable: false),
                    CorrectAnswerJson = table.Column<string>(type: "text", nullable: false),
                    AcceptedSynonymsJson = table.Column<string>(type: "text", nullable: true),
                    CaseSensitive = table.Column<bool>(type: "boolean", nullable: false),
                    ExplanationMarkdown = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: true),
                    SkillTag = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReadingQuestions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReadingQuestions_ReadingParts_ReadingPartId",
                        column: x => x.ReadingPartId,
                        principalTable: "ReadingParts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ReadingQuestions_ReadingTexts_ReadingTextId",
                        column: x => x.ReadingTextId,
                        principalTable: "ReadingTexts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ReadingQuestions_ReadingPartId_DisplayOrder",
                table: "ReadingQuestions",
                columns: new[] { "ReadingPartId", "DisplayOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_ReadingQuestions_ReadingTextId",
                table: "ReadingQuestions",
                column: "ReadingTextId");

            // ── ReadingAttempts ──────────────────────────────────────
            migrationBuilder.CreateTable(
                name: "ReadingAttempts",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    UserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    PaperId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    DeadlineAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    SubmittedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastActivityAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    RawScore = table.Column<int>(type: "integer", nullable: true),
                    ScaledScore = table.Column<int>(type: "integer", nullable: true),
                    MaxRawScore = table.Column<int>(type: "integer", nullable: false),
                    PolicySnapshotJson = table.Column<string>(type: "text", nullable: false),
                    PaperRevisionId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                },
                constraints: table => { table.PrimaryKey("PK_ReadingAttempts", x => x.Id); });

            migrationBuilder.CreateIndex(
                name: "IX_ReadingAttempts_UserId_Status",
                table: "ReadingAttempts",
                columns: new[] { "UserId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_ReadingAttempts_PaperId_StartedAt",
                table: "ReadingAttempts",
                columns: new[] { "PaperId", "StartedAt" });

            // ── ReadingAnswers ──────────────────────────────────────
            migrationBuilder.CreateTable(
                name: "ReadingAnswers",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ReadingAttemptId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ReadingQuestionId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    UserAnswerJson = table.Column<string>(type: "text", nullable: false),
                    IsCorrect = table.Column<bool>(type: "boolean", nullable: true),
                    PointsEarned = table.Column<int>(type: "integer", nullable: false),
                    AnsweredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReadingAnswers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReadingAnswers_ReadingAttempts_ReadingAttemptId",
                        column: x => x.ReadingAttemptId,
                        principalTable: "ReadingAttempts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ReadingAnswers_ReadingQuestions_ReadingQuestionId",
                        column: x => x.ReadingQuestionId,
                        principalTable: "ReadingQuestions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "UX_ReadingAnswer_Attempt_Question",
                table: "ReadingAnswers",
                columns: new[] { "ReadingAttemptId", "ReadingQuestionId" },
                unique: true);

            // ── ReadingPolicies (singleton) ─────────────────────────
            migrationBuilder.CreateTable(
                name: "ReadingPolicies",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    AttemptsPerPaperPerUser = table.Column<int>(type: "integer", nullable: false),
                    AttemptCooldownMinutes = table.Column<int>(type: "integer", nullable: false),
                    BestScoreDisplay = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    ShowPastAttempts = table.Column<bool>(type: "boolean", nullable: false),
                    AllowAttemptOnArchivedPaper = table.Column<bool>(type: "boolean", nullable: false),
                    PartATimerStrictness = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    PartATimerMinutes = table.Column<int>(type: "integer", nullable: false),
                    PartBCTimerMinutes = table.Column<int>(type: "integer", nullable: false),
                    GracePeriodSeconds = table.Column<int>(type: "integer", nullable: false),
                    OnExpirySubmitPolicy = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CountdownWarningsJson = table.Column<string>(type: "text", nullable: false),
                    EnabledQuestionTypesJson = table.Column<string>(type: "text", nullable: false),
                    ShortAnswerNormalisation = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ShortAnswerAcceptSynonyms = table.Column<bool>(type: "boolean", nullable: false),
                    MatchingAllowPartialCredit = table.Column<bool>(type: "boolean", nullable: false),
                    SentenceCompletionStrictness = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    UnknownTypeFallbackPolicy = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ShowExplanationsAfterSubmit = table.Column<bool>(type: "boolean", nullable: false),
                    ShowExplanationsOnlyIfWrong = table.Column<bool>(type: "boolean", nullable: false),
                    ShowCorrectAnswerOnReview = table.Column<bool>(type: "boolean", nullable: false),
                    AllowResultDownload = table.Column<bool>(type: "boolean", nullable: false),
                    AllowResultSharing = table.Column<bool>(type: "boolean", nullable: false),
                    AiExtractionEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    AiExtractionRequireHumanApproval = table.Column<bool>(type: "boolean", nullable: false),
                    AiExtractionMaxRetriesPerPaper = table.Column<int>(type: "integer", nullable: false),
                    AiExtractionModelOverride = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    AiExtractionStrictSchemaMode = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    QuestionBankEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    AssemblyStrategy = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    AllowLearnerRandomisation = table.Column<bool>(type: "boolean", nullable: false),
                    FontScaleUserControl = table.Column<bool>(type: "boolean", nullable: false),
                    HighContrastMode = table.Column<bool>(type: "boolean", nullable: false),
                    ScreenReaderOptimised = table.Column<bool>(type: "boolean", nullable: false),
                    AllowPaperReadingMode = table.Column<bool>(type: "boolean", nullable: false),
                    ExtraTimeApprovalWorkflow = table.Column<bool>(type: "boolean", nullable: false),
                    RequireFreshAuthForSubmit = table.Column<bool>(type: "boolean", nullable: false),
                    AllowMultipleConcurrentAttempts = table.Column<bool>(type: "boolean", nullable: false),
                    AttemptIpPinning = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    SubmitRateLimitPerMinute = table.Column<int>(type: "integer", nullable: false),
                    AutosaveRateLimitPerMinute = table.Column<int>(type: "integer", nullable: false),
                    PreventMultipleTabs = table.Column<bool>(type: "boolean", nullable: false),
                    RetainAnswerRowsDays = table.Column<int>(type: "integer", nullable: false),
                    RetainAttemptHeadersDays = table.Column<int>(type: "integer", nullable: false),
                    AnonymiseOnAccountDelete = table.Column<bool>(type: "boolean", nullable: false),
                    ShareAnonymousAnalytics = table.Column<bool>(type: "boolean", nullable: false),
                    AllowPausingAttempt = table.Column<bool>(type: "boolean", nullable: false),
                    AutoExpireWorkerEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    AutoExpireAfterMinutes = table.Column<int>(type: "integer", nullable: false),
                    AllowResumeAfterExpiry = table.Column<bool>(type: "boolean", nullable: false),
                    RowVersion = table.Column<int>(type: "integer", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedByAdminId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                },
                constraints: table => { table.PrimaryKey("PK_ReadingPolicies", x => x.Id); });

            // ── ReadingUserPolicyOverrides ──────────────────────────
            migrationBuilder.CreateTable(
                name: "ReadingUserPolicyOverrides",
                columns: table => new
                {
                    UserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ExtraTimeEntitlementPct = table.Column<int>(type: "integer", nullable: false),
                    BlockAttempts = table.Column<bool>(type: "boolean", nullable: false),
                    Reason = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    GrantedByAdminId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                },
                constraints: table => { table.PrimaryKey("PK_ReadingUserPolicyOverrides", x => x.UserId); });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "ReadingUserPolicyOverrides");
            migrationBuilder.DropTable(name: "ReadingPolicies");
            migrationBuilder.DropTable(name: "ReadingAnswers");
            migrationBuilder.DropTable(name: "ReadingAttempts");
            migrationBuilder.DropTable(name: "ReadingQuestions");
            migrationBuilder.DropTable(name: "ReadingTexts");
            migrationBuilder.DropTable(name: "ReadingParts");
        }
    }
}
