using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Infrastructure;
using OetWithDrHesham.Api.Data;

#nullable disable

namespace OetWithDrHesham.Api.Data.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(LearnerDbContext))]
    [Migration("20260609120000_AddWritingPathwaySchema")]
    public partial class AddWritingPathwaySchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "LearnerWritingProfiles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Profession = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    TargetBand = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    ExamDate = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DaysPerWeek = table.Column<int>(type: "integer", nullable: false),
                    MinutesPerDay = table.Column<int>(type: "integer", nullable: false),
                    TargetCountry = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    LetterTypeFocusJson = table.Column<string>(type: "text", nullable: false),
                    CurrentStage = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CurrentReadinessScore = table.Column<int>(type: "integer", nullable: true),
                    PredictedScore = table.Column<int>(type: "integer", nullable: true),
                    LastDiagnosticEvaluationId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    OnboardingCompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    PathwayGeneratedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table => table.PrimaryKey("PK_LearnerWritingProfiles", x => x.Id));

            migrationBuilder.CreateTable(
                name: "LearnerWritingPathways",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    TotalWeeks = table.Column<int>(type: "integer", nullable: false),
                    GeneratedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    WeeksJson = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table => table.PrimaryKey("PK_LearnerWritingPathways", x => x.Id));

            migrationBuilder.CreateTable(
                name: "WritingDailyPlanItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    PlanDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Ordinal = table.Column<int>(type: "integer", nullable: false),
                    ItemType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    FocusSkill = table.Column<string>(type: "character varying(4)", maxLength: 4, nullable: true),
                    FocusCriterion = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    EstimatedMinutes = table.Column<int>(type: "integer", nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    ActionHref = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    ContentId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    PayloadJson = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    SkippedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table => table.PrimaryKey("PK_WritingDailyPlanItems", x => x.Id));

            migrationBuilder.CreateTable(
                name: "WritingLessons",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Slug = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Title = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    SkillCode = table.Column<string>(type: "character varying(4)", maxLength: 4, nullable: false),
                    OrderIndex = table.Column<int>(type: "integer", nullable: false),
                    EstimatedMinutes = table.Column<int>(type: "integer", nullable: false),
                    BodyMarkdownEn = table.Column<string>(type: "text", nullable: false),
                    DrillPrompt = table.Column<string>(type: "text", nullable: false),
                    QuizJson = table.Column<string>(type: "text", nullable: false),
                    PrerequisiteLessonId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsPublished = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table => table.PrimaryKey("PK_WritingLessons", x => x.Id));

            migrationBuilder.CreateTable(
                name: "LearnerWritingLessonProgresses",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    LessonId = table.Column<Guid>(type: "uuid", nullable: false),
                    BodyRead = table.Column<bool>(type: "boolean", nullable: false),
                    DrillCompleted = table.Column<bool>(type: "boolean", nullable: false),
                    QuizScore = table.Column<int>(type: "integer", nullable: true),
                    QuizAttempts = table.Column<int>(type: "integer", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table => table.PrimaryKey("PK_LearnerWritingLessonProgresses", x => x.Id));

            migrationBuilder.CreateIndex(name: "IX_LearnerWritingProfiles_UserId", table: "LearnerWritingProfiles", column: "UserId", unique: true);
            migrationBuilder.CreateIndex(name: "IX_LearnerWritingPathways_UserId", table: "LearnerWritingPathways", column: "UserId", unique: true);
            migrationBuilder.CreateIndex(name: "IX_WritingDailyPlanItems_UserId_PlanDate", table: "WritingDailyPlanItems", columns: new[] { "UserId", "PlanDate" });
            migrationBuilder.CreateIndex(name: "IX_WritingDailyPlanItems_UserId_Status", table: "WritingDailyPlanItems", columns: new[] { "UserId", "Status" });
            migrationBuilder.CreateIndex(name: "IX_WritingLessons_Slug", table: "WritingLessons", column: "Slug", unique: true);
            migrationBuilder.CreateIndex(name: "IX_WritingLessons_SkillCode_OrderIndex", table: "WritingLessons", columns: new[] { "SkillCode", "OrderIndex" });
            migrationBuilder.CreateIndex(name: "IX_LearnerWritingLessonProgresses_UserId_LessonId", table: "LearnerWritingLessonProgresses", columns: new[] { "UserId", "LessonId" }, unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "LearnerWritingLessonProgresses");
            migrationBuilder.DropTable(name: "WritingLessons");
            migrationBuilder.DropTable(name: "WritingDailyPlanItems");
            migrationBuilder.DropTable(name: "LearnerWritingPathways");
            migrationBuilder.DropTable(name: "LearnerWritingProfiles");
        }
    }
}