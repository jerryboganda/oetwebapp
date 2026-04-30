using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OetLearner.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddListeningModuleEntities : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ListeningAttempts",
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
                    Mode = table.Column<int>(type: "integer", nullable: false),
                    RawScore = table.Column<int>(type: "integer", nullable: true),
                    ScaledScore = table.Column<int>(type: "integer", nullable: true),
                    MaxRawScore = table.Column<int>(type: "integer", nullable: false),
                    PolicySnapshotJson = table.Column<string>(type: "text", nullable: false),
                    PaperRevisionId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ScopeJson = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ListeningAttempts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ListeningParts",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    PaperId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    PartCode = table.Column<int>(type: "integer", nullable: false),
                    MaxRawScore = table.Column<int>(type: "integer", nullable: false),
                    Instructions = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ListeningParts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ListeningPolicies",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    AttemptsPerPaperPerUser = table.Column<int>(type: "integer", nullable: false),
                    AttemptCooldownMinutes = table.Column<int>(type: "integer", nullable: false),
                    BestScoreDisplay = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    ShowPastAttempts = table.Column<bool>(type: "boolean", nullable: false),
                    FullPaperTimerMinutes = table.Column<int>(type: "integer", nullable: false),
                    GracePeriodSeconds = table.Column<int>(type: "integer", nullable: false),
                    OnExpirySubmitPolicy = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CountdownWarningsJson = table.Column<string>(type: "text", nullable: false),
                    ExamReplayAllowed = table.Column<bool>(type: "boolean", nullable: false),
                    LearningReplayAllowed = table.Column<bool>(type: "boolean", nullable: false),
                    LearningEvidenceLoopEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    ShortAnswerNormalisation = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ShortAnswerAcceptSynonyms = table.Column<bool>(type: "boolean", nullable: false),
                    AiExtractionEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    AiExtractionRequireHumanApproval = table.Column<bool>(type: "boolean", nullable: false),
                    AiExtractionMaxRetriesPerPaper = table.Column<int>(type: "integer", nullable: false),
                    ShowExplanationsAfterSubmit = table.Column<bool>(type: "boolean", nullable: false),
                    ShowExplanationsOnlyIfWrong = table.Column<bool>(type: "boolean", nullable: false),
                    ShowCorrectAnswerOnReview = table.Column<bool>(type: "boolean", nullable: false),
                    DefaultExtraTimePct = table.Column<int>(type: "integer", nullable: false),
                    ScreenReaderOptimised = table.Column<bool>(type: "boolean", nullable: false),
                    AutoExpireWorkerEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    AutoExpireAfterMinutes = table.Column<int>(type: "integer", nullable: false),
                    AllowResumeAfterExpiry = table.Column<bool>(type: "boolean", nullable: false),
                    RetainAnswerRowsDays = table.Column<int>(type: "integer", nullable: false),
                    RetainAttemptHeadersDays = table.Column<int>(type: "integer", nullable: false),
                    AnonymiseOnAccountDelete = table.Column<bool>(type: "boolean", nullable: false),
                    RowVersion = table.Column<int>(type: "integer", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedByAdminId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ListeningPolicies", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ListeningUserPolicyOverrides",
                columns: table => new
                {
                    UserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ExtraTimeEntitlementPct = table.Column<int>(type: "integer", nullable: false),
                    BlockAttempts = table.Column<bool>(type: "boolean", nullable: false),
                    Reason = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    GrantedByAdminId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ListeningUserPolicyOverrides", x => x.UserId);
                });

            migrationBuilder.CreateTable(
                name: "ListeningExtracts",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ListeningPartId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    DisplayOrder = table.Column<int>(type: "integer", nullable: false),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    AccentCode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    SpeakersJson = table.Column<string>(type: "text", nullable: false),
                    AudioStartMs = table.Column<int>(type: "integer", nullable: true),
                    AudioEndMs = table.Column<int>(type: "integer", nullable: true),
                    ReplayInLearningOnly = table.Column<bool>(type: "boolean", nullable: false),
                    TranscriptSegmentsJson = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ListeningExtracts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ListeningExtracts_ListeningParts_ListeningPartId",
                        column: x => x.ListeningPartId,
                        principalTable: "ListeningParts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ListeningQuestions",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    PaperId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ListeningPartId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ListeningExtractId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    QuestionNumber = table.Column<int>(type: "integer", nullable: false),
                    DisplayOrder = table.Column<int>(type: "integer", nullable: false),
                    Points = table.Column<int>(type: "integer", nullable: false),
                    QuestionType = table.Column<int>(type: "integer", nullable: false),
                    Stem = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    CorrectAnswerJson = table.Column<string>(type: "text", nullable: false),
                    AcceptedSynonymsJson = table.Column<string>(type: "text", nullable: true),
                    CaseSensitive = table.Column<bool>(type: "boolean", nullable: false),
                    ExplanationMarkdown = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: true),
                    SkillTag = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    TranscriptEvidenceText = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    TranscriptEvidenceStartMs = table.Column<int>(type: "integer", nullable: true),
                    TranscriptEvidenceEndMs = table.Column<int>(type: "integer", nullable: true),
                    SpeakerAttitude = table.Column<int>(type: "integer", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ListeningQuestions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ListeningQuestions_ListeningExtracts_ListeningExtractId",
                        column: x => x.ListeningExtractId,
                        principalTable: "ListeningExtracts",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ListeningQuestions_ListeningParts_ListeningPartId",
                        column: x => x.ListeningPartId,
                        principalTable: "ListeningParts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ListeningAnswers",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ListeningAttemptId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ListeningQuestionId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    UserAnswerJson = table.Column<string>(type: "text", nullable: false),
                    IsCorrect = table.Column<bool>(type: "boolean", nullable: true),
                    PointsEarned = table.Column<int>(type: "integer", nullable: false),
                    SelectedDistractorCategory = table.Column<int>(type: "integer", nullable: true),
                    AnsweredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ListeningAnswers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ListeningAnswers_ListeningAttempts_ListeningAttemptId",
                        column: x => x.ListeningAttemptId,
                        principalTable: "ListeningAttempts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ListeningAnswers_ListeningQuestions_ListeningQuestionId",
                        column: x => x.ListeningQuestionId,
                        principalTable: "ListeningQuestions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ListeningQuestionOptions",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ListeningQuestionId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    OptionKey = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: false),
                    DisplayOrder = table.Column<int>(type: "integer", nullable: false),
                    Text = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    IsCorrect = table.Column<bool>(type: "boolean", nullable: false),
                    DistractorCategory = table.Column<int>(type: "integer", nullable: true),
                    WhyWrongMarkdown = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ListeningQuestionOptions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ListeningQuestionOptions_ListeningQuestions_ListeningQuesti~",
                        column: x => x.ListeningQuestionId,
                        principalTable: "ListeningQuestions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ListeningAnswers_ListeningQuestionId",
                table: "ListeningAnswers",
                column: "ListeningQuestionId");

            migrationBuilder.CreateIndex(
                name: "UX_ListeningAnswer_Attempt_Question",
                table: "ListeningAnswers",
                columns: new[] { "ListeningAttemptId", "ListeningQuestionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ListeningAttempts_PaperId_StartedAt",
                table: "ListeningAttempts",
                columns: new[] { "PaperId", "StartedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ListeningAttempts_UserId_Status",
                table: "ListeningAttempts",
                columns: new[] { "UserId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_ListeningExtracts_ListeningPartId_DisplayOrder",
                table: "ListeningExtracts",
                columns: new[] { "ListeningPartId", "DisplayOrder" });

            migrationBuilder.CreateIndex(
                name: "UX_ListeningPart_Paper_PartCode",
                table: "ListeningParts",
                columns: new[] { "PaperId", "PartCode" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_ListeningQuestionOption_Question_Key",
                table: "ListeningQuestionOptions",
                columns: new[] { "ListeningQuestionId", "OptionKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ListeningQuestions_ListeningExtractId",
                table: "ListeningQuestions",
                column: "ListeningExtractId");

            migrationBuilder.CreateIndex(
                name: "IX_ListeningQuestions_ListeningPartId_DisplayOrder",
                table: "ListeningQuestions",
                columns: new[] { "ListeningPartId", "DisplayOrder" });

            migrationBuilder.CreateIndex(
                name: "UX_ListeningQuestion_Paper_Number",
                table: "ListeningQuestions",
                columns: new[] { "PaperId", "QuestionNumber" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ListeningAnswers");

            migrationBuilder.DropTable(
                name: "ListeningPolicies");

            migrationBuilder.DropTable(
                name: "ListeningQuestionOptions");

            migrationBuilder.DropTable(
                name: "ListeningUserPolicyOverrides");

            migrationBuilder.DropTable(
                name: "ListeningAttempts");

            migrationBuilder.DropTable(
                name: "ListeningQuestions");

            migrationBuilder.DropTable(
                name: "ListeningExtracts");

            migrationBuilder.DropTable(
                name: "ListeningParts");
        }
    }
}
