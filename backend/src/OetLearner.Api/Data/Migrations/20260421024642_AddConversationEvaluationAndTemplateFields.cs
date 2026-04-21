using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OetLearner.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddConversationEvaluationAndTemplateFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ── ConversationSession: new columns ───────────────────────────────
            migrationBuilder.AddColumn<string>(
                name: "LastErrorCode",
                table: "ConversationSessions",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Profession",
                table: "ConversationSessions",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "medicine");

            migrationBuilder.AddColumn<string>(
                name: "TemplateId",
                table: "ConversationSessions",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ConversationSessions_TemplateId",
                table: "ConversationSessions",
                column: "TemplateId");

            migrationBuilder.CreateIndex(
                name: "IX_ConversationSessions_UserId_CreatedAt",
                table: "ConversationSessions",
                columns: new[] { "UserId", "CreatedAt" });

            // ── ConversationTurn: new columns + widen AudioUrl ────────────────
            migrationBuilder.AddColumn<string>(
                name: "AiFeatureCode",
                table: "ConversationTurns",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AiUsageId",
                table: "ConversationTurns",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "CreatedAt",
                table: "ConversationTurns",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc), TimeSpan.Zero));

            migrationBuilder.AlterColumn<string>(
                name: "AudioUrl",
                table: "ConversationTurns",
                type: "character varying(512)",
                maxLength: 512,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(256)",
                oldMaxLength: 256,
                oldNullable: true);

            // ── ConversationTemplates (new table) ─────────────────────────────
            migrationBuilder.CreateTable(
                name: "ConversationTemplates",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ProfessionId = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    TaskTypeCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Scenario = table.Column<string>(type: "text", nullable: false),
                    RoleDescription = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    PatientContext = table.Column<string>(type: "text", nullable: true),
                    ExpectedOutcomes = table.Column<string>(type: "text", nullable: true),
                    ObjectivesJson = table.Column<string>(type: "text", nullable: false),
                    ExpectedRedFlagsJson = table.Column<string>(type: "text", nullable: false),
                    KeyVocabularyJson = table.Column<string>(type: "text", nullable: false),
                    PatientVoiceJson = table.Column<string>(type: "text", nullable: false),
                    Difficulty = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    EstimatedDurationSeconds = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    PublishedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    UpdatedByUserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConversationTemplates", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ConversationTemplates_Status_Difficulty",
                table: "ConversationTemplates",
                columns: new[] { "Status", "Difficulty" });

            migrationBuilder.CreateIndex(
                name: "IX_ConversationTemplates_Status_TaskTypeCode_ProfessionId",
                table: "ConversationTemplates",
                columns: new[] { "Status", "TaskTypeCode", "ProfessionId" });

            // ── ConversationEvaluations (new table) ───────────────────────────
            migrationBuilder.CreateTable(
                name: "ConversationEvaluations",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SessionId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    UserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    OverallScaled = table.Column<int>(type: "integer", nullable: false),
                    OverallGrade = table.Column<string>(type: "character varying(4)", maxLength: 4, nullable: false),
                    Passed = table.Column<bool>(type: "boolean", nullable: false),
                    CountryVariant = table.Column<string>(type: "character varying(4)", maxLength: 4, nullable: true),
                    CriteriaJson = table.Column<string>(type: "text", nullable: false),
                    StrengthsJson = table.Column<string>(type: "text", nullable: false),
                    ImprovementsJson = table.Column<string>(type: "text", nullable: false),
                    SuggestedPracticeJson = table.Column<string>(type: "text", nullable: false),
                    AppliedRuleIdsJson = table.Column<string>(type: "text", nullable: false),
                    RulebookVersion = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Advisory = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    AiUsageId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConversationEvaluations", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ConversationEvaluations_SessionId",
                table: "ConversationEvaluations",
                column: "SessionId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ConversationEvaluations_UserId_CreatedAt",
                table: "ConversationEvaluations",
                columns: new[] { "UserId", "CreatedAt" });

            // ── ConversationTurnAnnotations (new table) ───────────────────────
            migrationBuilder.CreateTable(
                name: "ConversationTurnAnnotations",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SessionId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    EvaluationId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    TurnNumber = table.Column<int>(type: "integer", nullable: false),
                    Type = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Category = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    RuleId = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    Evidence = table.Column<string>(type: "text", nullable: false),
                    Suggestion = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConversationTurnAnnotations", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ConversationTurnAnnotations_EvaluationId",
                table: "ConversationTurnAnnotations",
                column: "EvaluationId");

            migrationBuilder.CreateIndex(
                name: "IX_ConversationTurnAnnotations_SessionId_TurnNumber",
                table: "ConversationTurnAnnotations",
                columns: new[] { "SessionId", "TurnNumber" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "ConversationTurnAnnotations");
            migrationBuilder.DropTable(name: "ConversationEvaluations");
            migrationBuilder.DropTable(name: "ConversationTemplates");

            migrationBuilder.DropIndex(
                name: "IX_ConversationSessions_TemplateId",
                table: "ConversationSessions");

            migrationBuilder.DropIndex(
                name: "IX_ConversationSessions_UserId_CreatedAt",
                table: "ConversationSessions");

            migrationBuilder.DropColumn(name: "LastErrorCode", table: "ConversationSessions");
            migrationBuilder.DropColumn(name: "Profession", table: "ConversationSessions");
            migrationBuilder.DropColumn(name: "TemplateId", table: "ConversationSessions");

            migrationBuilder.DropColumn(name: "AiFeatureCode", table: "ConversationTurns");
            migrationBuilder.DropColumn(name: "AiUsageId", table: "ConversationTurns");
            migrationBuilder.DropColumn(name: "CreatedAt", table: "ConversationTurns");

            migrationBuilder.AlterColumn<string>(
                name: "AudioUrl",
                table: "ConversationTurns",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(512)",
                oldMaxLength: 512,
                oldNullable: true);
        }
    }
}
