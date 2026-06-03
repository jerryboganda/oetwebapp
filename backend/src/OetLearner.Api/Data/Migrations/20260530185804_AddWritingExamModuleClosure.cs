using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OetLearner.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddWritingExamModuleClosure : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // NOTE: the four WritingTutorReviews columns (AcceptedAiPreAssessmentJson,
            // ContentChecklistVerdictJson, IsContentChecklistMarked, MarkerSequence) used to
            // be added here, but that table is created by the LATER migration
            // 20260610120000_AddWritingModuleV2Schema, so on a fresh database this crashed
            // ("relation WritingTutorReviews does not exist"). They now live in that
            // migration's CreateTable. Prod is unaffected (this migration is already applied).
            //
            // The ~23 WritingScenarios AddColumn calls and the two WritingScenarios
            // indexes (IX_WritingScenarios_InternalCode / _SourceContentPaperId) that
            // used to live here were relocated to 20260610120000_AddWritingModuleV2Schema
            // for the same reason: that later migration CREATES the WritingScenarios table.
            migrationBuilder.CreateTable(
                name: "WritingAttemptEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SubmissionId = table.Column<Guid>(type: "uuid", nullable: true),
                    SessionId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ScenarioId = table.Column<Guid>(type: "uuid", nullable: true),
                    Mode = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    EventType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Timestamp = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    PayloadJson = table.Column<string>(type: "jsonb", nullable: false, defaultValue: "{}"),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WritingAttemptEvents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "WritingContentChecklistItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ScenarioId = table.Column<Guid>(type: "uuid", nullable: false),
                    ItemText = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Category = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Importance = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    RequiredStatus = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    LinkedCaseNoteSection = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    ExpectedRepresentation = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    CommonError = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    Ordinal = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WritingContentChecklistItems", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "WritingFeedbackAnnotations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SubmissionId = table.Column<Guid>(type: "uuid", nullable: false),
                    ReviewId = table.Column<Guid>(type: "uuid", nullable: true),
                    TutorId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Criterion = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: true),
                    HighlightedText = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    StartOffset = table.Column<int>(type: "integer", nullable: false),
                    EndOffset = table.Column<int>(type: "integer", nullable: false),
                    Severity = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Suggestion = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    FeedbackText = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WritingFeedbackAnnotations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "WritingModerations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SubmissionId = table.Column<Guid>(type: "uuid", nullable: false),
                    FirstMarkerId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    SecondMarkerId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    SeniorMarkerId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    FirstScoreJson = table.Column<string>(type: "jsonb", nullable: true),
                    SecondScoreJson = table.Column<string>(type: "jsonb", nullable: true),
                    FinalScoreJson = table.Column<string>(type: "jsonb", nullable: true),
                    VariancePoints = table.Column<int>(type: "integer", nullable: true),
                    VarianceReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    FinalDecisionNote = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    Status = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WritingModerations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "WritingResultVisibilityConfigs",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ScenarioId = table.Column<Guid>(type: "uuid", nullable: true),
                    ShowSubmissionReceived = table.Column<bool>(type: "boolean", nullable: false),
                    ShowAiEstimate = table.Column<bool>(type: "boolean", nullable: false),
                    ShowTutorScore = table.Column<bool>(type: "boolean", nullable: false),
                    ShowFullCriteria = table.Column<bool>(type: "boolean", nullable: false),
                    ShowAnnotatedResponse = table.Column<bool>(type: "boolean", nullable: false),
                    ShowMissingContent = table.Column<bool>(type: "boolean", nullable: false),
                    ShowModelAnswer = table.Column<bool>(type: "boolean", nullable: false),
                    ShowContentChecklist = table.Column<bool>(type: "boolean", nullable: false),
                    AllowRewrite = table.Column<bool>(type: "boolean", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WritingResultVisibilityConfigs", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WritingAttemptEvents_SessionId_Timestamp",
                table: "WritingAttemptEvents",
                columns: new[] { "SessionId", "Timestamp" });

            migrationBuilder.CreateIndex(
                name: "IX_WritingAttemptEvents_SubmissionId",
                table: "WritingAttemptEvents",
                column: "SubmissionId");

            migrationBuilder.CreateIndex(
                name: "IX_WritingAttemptEvents_UserId_SessionId",
                table: "WritingAttemptEvents",
                columns: new[] { "UserId", "SessionId" });

            migrationBuilder.CreateIndex(
                name: "IX_WritingContentChecklistItems_ScenarioId_Ordinal",
                table: "WritingContentChecklistItems",
                columns: new[] { "ScenarioId", "Ordinal" });

            migrationBuilder.CreateIndex(
                name: "IX_WritingContentChecklistItems_ScenarioId_RequiredStatus",
                table: "WritingContentChecklistItems",
                columns: new[] { "ScenarioId", "RequiredStatus" });

            migrationBuilder.CreateIndex(
                name: "IX_WritingFeedbackAnnotations_ReviewId",
                table: "WritingFeedbackAnnotations",
                column: "ReviewId");

            migrationBuilder.CreateIndex(
                name: "IX_WritingFeedbackAnnotations_SubmissionId",
                table: "WritingFeedbackAnnotations",
                column: "SubmissionId");

            migrationBuilder.CreateIndex(
                name: "IX_WritingModerations_Status",
                table: "WritingModerations",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_WritingModerations_SubmissionId",
                table: "WritingModerations",
                column: "SubmissionId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WritingResultVisibilityConfigs_ScenarioId",
                table: "WritingResultVisibilityConfigs",
                column: "ScenarioId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "WritingAttemptEvents");

            migrationBuilder.DropTable(
                name: "WritingContentChecklistItems");

            migrationBuilder.DropTable(
                name: "WritingFeedbackAnnotations");

            migrationBuilder.DropTable(
                name: "WritingModerations");

            migrationBuilder.DropTable(
                name: "WritingResultVisibilityConfigs");

            // The WritingScenarios DropIndex/DropColumn calls were removed — those columns
            // and indexes are now owned by 20260610120000_AddWritingModuleV2Schema, which
            // CREATES the WritingScenarios table (see Up()). The four WritingTutorReviews
            // DropColumns were likewise relocated there.
        }
    }
}
