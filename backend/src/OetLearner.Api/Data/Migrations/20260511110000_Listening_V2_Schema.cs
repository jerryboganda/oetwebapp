using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using OetLearner.Api.Data;

#nullable disable

namespace OetLearner.Api.Data.Migrations
{
    /// <summary>
    /// Listening V2 — additive schema migration. Hand-written (auto-generation
    /// is contaminated by the snapshot drift documented in
    /// /memories/repo/migration-drift-note.md). NEVER drops or renames any
    /// existing column. All new columns are nullable or carry server defaults.
    ///
    /// PRD-LISTENING-V2.md §5.2 mapping:
    ///   • Version pinning: ListeningQuestion.Version, ListeningQuestionOption.Version,
    ///     ListeningAnswer.QuestionVersionSnapshot/OptionVersionSnapshot,
    ///     ListeningAttempt.LastQuestionVersionMapJson.
    ///   • FSM state: ListeningAttempt.NavigationStateJson +
    ///     WindowStartedAt + WindowDurationMs + AudioCueTimelineJson.
    ///   • R08 / R10 / R07.3: ListeningAttempt.AnnotationsJson +
    ///     TechReadinessJson + HumanScoreOverridesJson.
    ///   • Topic + difficulty: ListeningExtract.TopicCsv/DifficultyRating,
    ///     ListeningQuestion.DifficultyLevel.
    ///   • Accessibility: ListeningUserPolicyOverride.AccessibilityModeEnabled.
    ///   • Per-window timing + R06/R08/R10 flags: 30+ columns on ListeningPolicy.
    ///   • New tables: ListeningPathwayProgress, TeacherClass,
    ///     TeacherClassMember, ListeningAttemptNote.
    /// </summary>
    [DbContext(typeof(LearnerDbContext))]
    [Migration("20260511110000_Listening_V2_Schema")]
    public partial class Listening_V2_Schema : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ── ListeningQuestion ──
            migrationBuilder.AddColumn<int>(
                name: "Version",
                table: "ListeningQuestions",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "DifficultyLevel",
                table: "ListeningQuestions",
                type: "integer",
                nullable: true);

            // ── ListeningQuestionOption ──
            migrationBuilder.AddColumn<int>(
                name: "Version",
                table: "ListeningQuestionOptions",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            // ── ListeningAnswer ──
            migrationBuilder.AddColumn<int>(
                name: "QuestionVersionSnapshot",
                table: "ListeningAnswers",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "OptionVersionSnapshot",
                table: "ListeningAnswers",
                type: "integer",
                nullable: true);

            // ── ListeningExtract ──
            migrationBuilder.AddColumn<string>(
                name: "TopicCsv",
                table: "ListeningExtracts",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DifficultyRating",
                table: "ListeningExtracts",
                type: "integer",
                nullable: true);

            // ── ListeningAttempt — FSM + R08 + R10 jsonb columns ──
            // jsonb on Postgres; SQLite test harness keeps these as TEXT.
            migrationBuilder.AddColumn<string>(
                name: "NavigationStateJson",
                table: "ListeningAttempts",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<System.DateTimeOffset>(
                name: "WindowStartedAt",
                table: "ListeningAttempts",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "WindowDurationMs",
                table: "ListeningAttempts",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AudioCueTimelineJson",
                table: "ListeningAttempts",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TechReadinessJson",
                table: "ListeningAttempts",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AnnotationsJson",
                table: "ListeningAttempts",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "HumanScoreOverridesJson",
                table: "ListeningAttempts",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LastQuestionVersionMapJson",
                table: "ListeningAttempts",
                type: "jsonb",
                nullable: true);

            // ── ListeningUserPolicyOverride ──
            migrationBuilder.AddColumn<bool>(
                name: "AccessibilityModeEnabled",
                table: "ListeningUserPolicyOverrides",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            // ── ListeningPolicy — R05 / R06 / R07 / R08 / R10 columns ──
            // All nullable; runtime falls back to ListeningPolicyDefaults.
            foreach (var col in new[]
            {
                "PreviewWindowMsA1", "PreviewWindowMsA2",
                "PreviewWindowMsC1", "PreviewWindowMsC2",
                "ReviewWindowMsA1", "ReviewWindowMsA2",
                "ReviewWindowMsC1", "ReviewWindowMsC2FinalCbt",
                "ReviewWindowMsC2FinalPaper",
                "BetweenSectionTransitionMs", "PartBQuestionWindowMs",
                "ConfirmTokenTtlMs", "TechReadinessTtlMs",
                "FinalReviewAllPartsMsPaper",
            })
            {
                migrationBuilder.AddColumn<int>(
                    name: col,
                    table: "ListeningPolicies",
                    type: "integer",
                    nullable: true);
            }

            foreach (var col in new[]
            {
                "OneWayLocksEnabled", "ConfirmDialogRequired",
                "UnansweredWarningRequired",
                "HighlightingEnabledPartA", "HighlightingEnabledPartBC",
                "OptionStrikethroughEnabled", "InAppZoomEnabled",
                "CtrlZoomBlocked", "AnnotationsPersistOnAdvance",
                "TechReadinessRequired",
            })
            {
                migrationBuilder.AddColumn<bool>(
                    name: col,
                    table: "ListeningPolicies",
                    type: "boolean",
                    nullable: true);
            }

            // ── New table: ListeningPathwayProgress ──
            migrationBuilder.CreateTable(
                name: "ListeningPathwayProgress",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    UserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    StageCode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    StartedAt = table.Column<System.DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CompletedAt = table.Column<System.DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ScaledScore = table.Column<int>(type: "integer", nullable: true),
                    AttemptId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    UnlockOverrideBy = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    CreatedAt = table.Column<System.DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<System.DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                },
                constraints: table => table.PrimaryKey("PK_ListeningPathwayProgress", x => x.Id));

            migrationBuilder.CreateIndex(
                name: "UX_ListeningPathwayProgress_User_Stage",
                table: "ListeningPathwayProgress",
                columns: new[] { "UserId", "StageCode" },
                unique: true);

            // ── New table: TeacherClass (cross-skill) ──
            migrationBuilder.CreateTable(
                name: "TeacherClasses",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    OwnerUserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    CreatedAt = table.Column<System.DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<System.DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                },
                constraints: table => table.PrimaryKey("PK_TeacherClasses", x => x.Id));

            migrationBuilder.CreateIndex(
                name: "IX_TeacherClasses_OwnerUserId",
                table: "TeacherClasses",
                column: "OwnerUserId");

            // ── New table: TeacherClassMember ──
            migrationBuilder.CreateTable(
                name: "TeacherClassMembers",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    TeacherClassId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    UserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    AddedAt = table.Column<System.DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TeacherClassMembers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TeacherClassMembers_TeacherClasses_TeacherClassId",
                        column: x => x.TeacherClassId,
                        principalTable: "TeacherClasses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "UX_TeacherClassMember_Class_User",
                table: "TeacherClassMembers",
                columns: new[] { "TeacherClassId", "UserId" },
                unique: true);

            // ── New table: ListeningAttemptNote ──
            migrationBuilder.CreateTable(
                name: "ListeningAttemptNotes",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ListeningAttemptId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ListeningExtractId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    TranscriptMs = table.Column<int>(type: "integer", nullable: true),
                    Text = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: false),
                    CreatedAt = table.Column<System.DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<System.DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ListeningAttemptNotes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ListeningAttemptNotes_ListeningAttempts_ListeningAttemptId",
                        column: x => x.ListeningAttemptId,
                        principalTable: "ListeningAttempts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ListeningAttemptNotes_ListeningAttemptId",
                table: "ListeningAttemptNotes",
                column: "ListeningAttemptId");

            // ── Backfill: seed Question/Option Version=1 idempotently. ──
            // Default value already does this for new rows; this catches any
            // pre-existing rows that may have been inserted with raw SQL.
            migrationBuilder.Sql(
                "UPDATE \"ListeningQuestions\" SET \"Version\" = 1 WHERE \"Version\" IS NULL OR \"Version\" = 0;");
            migrationBuilder.Sql(
                "UPDATE \"ListeningQuestionOptions\" SET \"Version\" = 1 WHERE \"Version\" IS NULL OR \"Version\" = 0;");

            // Backfill: for each existing in-flight ListeningAnswer, snapshot
            // the current Question.Version. Idempotent — only updates rows
            // where the snapshot is still null.
            migrationBuilder.Sql(@"
                UPDATE ""ListeningAnswers"" a
                SET ""QuestionVersionSnapshot"" = q.""Version""
                FROM ""ListeningQuestions"" q
                WHERE a.""ListeningQuestionId"" = q.""Id""
                  AND a.""QuestionVersionSnapshot"" IS NULL;");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "ListeningAttemptNotes");
            migrationBuilder.DropTable(name: "TeacherClassMembers");
            migrationBuilder.DropTable(name: "TeacherClasses");
            migrationBuilder.DropTable(name: "ListeningPathwayProgress");

            migrationBuilder.DropColumn(name: "AccessibilityModeEnabled", table: "ListeningUserPolicyOverrides");

            foreach (var col in new[]
            {
                "PreviewWindowMsA1", "PreviewWindowMsA2",
                "PreviewWindowMsC1", "PreviewWindowMsC2",
                "ReviewWindowMsA1", "ReviewWindowMsA2",
                "ReviewWindowMsC1", "ReviewWindowMsC2FinalCbt",
                "ReviewWindowMsC2FinalPaper",
                "BetweenSectionTransitionMs", "PartBQuestionWindowMs",
                "ConfirmTokenTtlMs", "TechReadinessTtlMs",
                "FinalReviewAllPartsMsPaper",
                "OneWayLocksEnabled", "ConfirmDialogRequired",
                "UnansweredWarningRequired",
                "HighlightingEnabledPartA", "HighlightingEnabledPartBC",
                "OptionStrikethroughEnabled", "InAppZoomEnabled",
                "CtrlZoomBlocked", "AnnotationsPersistOnAdvance",
                "TechReadinessRequired",
            })
            {
                migrationBuilder.DropColumn(name: col, table: "ListeningPolicies");
            }

            migrationBuilder.DropColumn(name: "NavigationStateJson", table: "ListeningAttempts");
            migrationBuilder.DropColumn(name: "WindowStartedAt", table: "ListeningAttempts");
            migrationBuilder.DropColumn(name: "WindowDurationMs", table: "ListeningAttempts");
            migrationBuilder.DropColumn(name: "AudioCueTimelineJson", table: "ListeningAttempts");
            migrationBuilder.DropColumn(name: "TechReadinessJson", table: "ListeningAttempts");
            migrationBuilder.DropColumn(name: "AnnotationsJson", table: "ListeningAttempts");
            migrationBuilder.DropColumn(name: "HumanScoreOverridesJson", table: "ListeningAttempts");
            migrationBuilder.DropColumn(name: "LastQuestionVersionMapJson", table: "ListeningAttempts");

            migrationBuilder.DropColumn(name: "TopicCsv", table: "ListeningExtracts");
            migrationBuilder.DropColumn(name: "DifficultyRating", table: "ListeningExtracts");

            migrationBuilder.DropColumn(name: "QuestionVersionSnapshot", table: "ListeningAnswers");
            migrationBuilder.DropColumn(name: "OptionVersionSnapshot", table: "ListeningAnswers");

            migrationBuilder.DropColumn(name: "Version", table: "ListeningQuestionOptions");
            migrationBuilder.DropColumn(name: "Version", table: "ListeningQuestions");
            migrationBuilder.DropColumn(name: "DifficultyLevel", table: "ListeningQuestions");
        }
    }
}
