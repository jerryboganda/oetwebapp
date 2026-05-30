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
            migrationBuilder.AddColumn<string>(
                name: "AcceptedAiPreAssessmentJson",
                table: "WritingTutorReviews",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ContentChecklistVerdictJson",
                table: "WritingTutorReviews",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<bool>(
                name: "IsContentChecklistMarked",
                table: "WritingTutorReviews",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "MarkerSequence",
                table: "WritingTutorReviews",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "");

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
                name: "IX_WritingScenarios_InternalCode",
                table: "WritingScenarios",
                column: "InternalCode");

            migrationBuilder.CreateIndex(
                name: "IX_WritingScenarios_SourceContentPaperId",
                table: "WritingScenarios",
                column: "SourceContentPaperId");

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

            migrationBuilder.DropIndex(
                name: "IX_WritingScenarios_InternalCode",
                table: "WritingScenarios");

            migrationBuilder.DropIndex(
                name: "IX_WritingScenarios_SourceContentPaperId",
                table: "WritingScenarios");

            migrationBuilder.DropColumn(
                name: "AcceptedAiPreAssessmentJson",
                table: "WritingTutorReviews");

            migrationBuilder.DropColumn(
                name: "ContentChecklistVerdictJson",
                table: "WritingTutorReviews");

            migrationBuilder.DropColumn(
                name: "IsContentChecklistMarked",
                table: "WritingTutorReviews");

            migrationBuilder.DropColumn(
                name: "MarkerSequence",
                table: "WritingTutorReviews");

            migrationBuilder.DropColumn(
                name: "CaseNoteSectionsJson",
                table: "WritingScenarios");

            migrationBuilder.DropColumn(
                name: "ContentOwnerId",
                table: "WritingScenarios");

            migrationBuilder.DropColumn(
                name: "ExpectedAction",
                table: "WritingScenarios");

            migrationBuilder.DropColumn(
                name: "ExpectedPurpose",
                table: "WritingScenarios");

            migrationBuilder.DropColumn(
                name: "FixedInstructionsJson",
                table: "WritingScenarios");

            migrationBuilder.DropColumn(
                name: "IntegrityAcknowledgedAt",
                table: "WritingScenarios");

            migrationBuilder.DropColumn(
                name: "IntegrityAcknowledgedById",
                table: "WritingScenarios");

            migrationBuilder.DropColumn(
                name: "InternalCode",
                table: "WritingScenarios");

            migrationBuilder.DropColumn(
                name: "MarkingMode",
                table: "WritingScenarios");

            migrationBuilder.DropColumn(
                name: "ModelAnswerExemplarId",
                table: "WritingScenarios");

            migrationBuilder.DropColumn(
                name: "ReadingTimeSeconds",
                table: "WritingScenarios");

            migrationBuilder.DropColumn(
                name: "RecipientJson",
                table: "WritingScenarios");

            migrationBuilder.DropColumn(
                name: "RetakePolicyJson",
                table: "WritingScenarios");

            migrationBuilder.DropColumn(
                name: "SimulationModes",
                table: "WritingScenarios");

            migrationBuilder.DropColumn(
                name: "SourceContentPaperId",
                table: "WritingScenarios");

            migrationBuilder.DropColumn(
                name: "SourceProvenance",
                table: "WritingScenarios");

            migrationBuilder.DropColumn(
                name: "TaskPromptMarkdown",
                table: "WritingScenarios");

            migrationBuilder.DropColumn(
                name: "TodayDate",
                table: "WritingScenarios");

            migrationBuilder.DropColumn(
                name: "UpdatedAt",
                table: "WritingScenarios");

            migrationBuilder.DropColumn(
                name: "WordGuideMax",
                table: "WritingScenarios");

            migrationBuilder.DropColumn(
                name: "WordGuideMin",
                table: "WritingScenarios");

            migrationBuilder.DropColumn(
                name: "WriterRole",
                table: "WritingScenarios");

            migrationBuilder.DropColumn(
                name: "WritingTimeSeconds",
                table: "WritingScenarios");
        }
    }
}
