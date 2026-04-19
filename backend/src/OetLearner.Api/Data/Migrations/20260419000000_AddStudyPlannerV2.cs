using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OetLearner.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddStudyPlannerV2 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ── Extend StudyPlan ──
            migrationBuilder.AddColumn<string>(
                name: "TemplateId", table: "StudyPlans",
                type: "character varying(64)", maxLength: 64, nullable: true);
            migrationBuilder.AddColumn<string>(
                name: "AssignmentRuleIdsCsv", table: "StudyPlans",
                type: "character varying(1024)", maxLength: 1024, nullable: false, defaultValue: "");
            migrationBuilder.AddColumn<string>(
                name: "GoalSnapshotJson", table: "StudyPlans",
                type: "text", nullable: false, defaultValue: "{}");

            // ── Extend StudyPlanItem ──
            migrationBuilder.AddColumn<string>(
                name: "TaskTemplateId", table: "StudyPlanItems",
                type: "character varying(64)", maxLength: 64, nullable: true);
            migrationBuilder.AddColumn<string>(
                name: "ContentPaperId", table: "StudyPlanItems",
                type: "character varying(64)", maxLength: 64, nullable: true);
            migrationBuilder.AddColumn<int>(
                name: "Priority", table: "StudyPlanItems",
                type: "integer", nullable: false, defaultValue: 50);
            migrationBuilder.AddColumn<string>(
                name: "PrerequisiteItemId", table: "StudyPlanItems",
                type: "character varying(64)", maxLength: 64, nullable: true);
            migrationBuilder.AddColumn<string>(
                name: "AiRationaleAddendum", table: "StudyPlanItems",
                type: "character varying(2000)", maxLength: 2000, nullable: true);
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "StartedAt", table: "StudyPlanItems",
                type: "timestamp with time zone", nullable: true);
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "UpdatedAt", table: "StudyPlanItems",
                type: "timestamp with time zone", nullable: true);
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "SnoozedUntil", table: "StudyPlanItems",
                type: "timestamp with time zone", nullable: true);

            // ── StudyPlanTaskTemplates ──
            migrationBuilder.CreateTable(
                name: "StudyPlanTaskTemplates",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Slug = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    SubtestCode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ItemType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    DurationMinutes = table.Column<int>(type: "integer", nullable: false),
                    RationaleMarkdown = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    ProfessionScopeJson = table.Column<string>(type: "text", nullable: false),
                    ExamFamilyCode = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    TargetCountriesJson = table.Column<string>(type: "text", nullable: false),
                    DifficultyMin = table.Column<int>(type: "integer", nullable: false),
                    DifficultyMax = table.Column<int>(type: "integer", nullable: false),
                    DefaultSection = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    DefaultContentPaperId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    TagsCsv = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    IsArchived = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedByAdminId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table => table.PrimaryKey("PK_StudyPlanTaskTemplates", x => x.Id));
            migrationBuilder.CreateIndex(
                name: "IX_StudyPlanTaskTemplates_Slug",
                table: "StudyPlanTaskTemplates",
                column: "Slug", unique: true);
            migrationBuilder.CreateIndex(
                name: "IX_StudyPlanTaskTemplates_SubtestCode_IsArchived",
                table: "StudyPlanTaskTemplates",
                columns: new[] { "SubtestCode", "IsArchived" });

            // ── StudyPlanTemplates ──
            migrationBuilder.CreateTable(
                name: "StudyPlanTemplates",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Slug = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    DurationWeeks = table.Column<int>(type: "integer", nullable: false),
                    DefaultHoursPerWeek = table.Column<int>(type: "integer", nullable: false),
                    ExamFamilyCode = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    IsArchived = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedByAdminId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table => table.PrimaryKey("PK_StudyPlanTemplates", x => x.Id));
            migrationBuilder.CreateIndex(
                name: "IX_StudyPlanTemplates_Slug",
                table: "StudyPlanTemplates",
                column: "Slug", unique: true);

            // ── StudyPlanTemplateItems ──
            migrationBuilder.CreateTable(
                name: "StudyPlanTemplateItems",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    PlanTemplateId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    TaskTemplateId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    WeekOffset = table.Column<int>(type: "integer", nullable: false),
                    DayOffsetWithinWeek = table.Column<int>(type: "integer", nullable: false),
                    Section = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Priority = table.Column<int>(type: "integer", nullable: false),
                    IsMandatory = table.Column<bool>(type: "boolean", nullable: false),
                    PrerequisiteItemTemplateId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Ordering = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table => table.PrimaryKey("PK_StudyPlanTemplateItems", x => x.Id));
            migrationBuilder.CreateIndex(
                name: "IX_StudyPlanTemplateItems_PlanTemplateId_Ordering",
                table: "StudyPlanTemplateItems",
                columns: new[] { "PlanTemplateId", "Ordering" });

            // ── StudyPlanAssignmentRules ──
            migrationBuilder.CreateTable(
                name: "StudyPlanAssignmentRules",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ExamFamilyCode = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Priority = table.Column<int>(type: "integer", nullable: false),
                    Weight = table.Column<int>(type: "integer", nullable: false),
                    ConditionJson = table.Column<string>(type: "text", nullable: false),
                    TargetTemplateId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedByAdminId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table => table.PrimaryKey("PK_StudyPlanAssignmentRules", x => x.Id));
            migrationBuilder.CreateIndex(
                name: "IX_StudyPlanAssignmentRules_ExamFamilyCode_IsActive_Priority",
                table: "StudyPlanAssignmentRules",
                columns: new[] { "ExamFamilyCode", "IsActive", "Priority" });

            // ── StudyPlanDriftPolicies ──
            migrationBuilder.CreateTable(
                name: "StudyPlanDriftPolicies",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ExamFamilyCode = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    MildDays = table.Column<int>(type: "integer", nullable: false),
                    ModerateDays = table.Column<int>(type: "integer", nullable: false),
                    SevereDays = table.Column<int>(type: "integer", nullable: false),
                    MildCopy = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    ModerateCopy = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    SevereCopy = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    OnTrackCopy = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    AutoRegenerateOnModerate = table.Column<bool>(type: "boolean", nullable: false),
                    AutoRegenerateOnSevere = table.Column<bool>(type: "boolean", nullable: false),
                    UpdatedByAdminId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table => table.PrimaryKey("PK_StudyPlanDriftPolicies", x => x.Id));
            migrationBuilder.CreateIndex(
                name: "IX_StudyPlanDriftPolicies_ExamFamilyCode",
                table: "StudyPlanDriftPolicies",
                column: "ExamFamilyCode", unique: true);

            // ── StudyPlanGenerationLogs ──
            migrationBuilder.CreateTable(
                name: "StudyPlanGenerationLogs",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    UserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    PlanId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    TriggeredBy = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    RuleIdsMatchedCsv = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    TemplateId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    AiUsed = table.Column<bool>(type: "boolean", nullable: false),
                    AiFeatureCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    AiUsageRecordId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ItemCount = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table => table.PrimaryKey("PK_StudyPlanGenerationLogs", x => x.Id));
            migrationBuilder.CreateIndex(
                name: "IX_StudyPlanGenerationLogs_UserId_CreatedAt",
                table: "StudyPlanGenerationLogs",
                columns: new[] { "UserId", "CreatedAt" });
            migrationBuilder.CreateIndex(
                name: "IX_StudyPlanGenerationLogs_PlanId",
                table: "StudyPlanGenerationLogs",
                column: "PlanId");

            // ── StudyPlanAdminOverrides ──
            migrationBuilder.CreateTable(
                name: "StudyPlanAdminOverrides",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    UserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ItemId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    AdminId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Action = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Reason = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table => table.PrimaryKey("PK_StudyPlanAdminOverrides", x => x.Id));
            migrationBuilder.CreateIndex(
                name: "IX_StudyPlanAdminOverrides_UserId_CreatedAt",
                table: "StudyPlanAdminOverrides",
                columns: new[] { "UserId", "CreatedAt" });

            // ── LearnerCalendarLinks (Phase 9: Google Calendar integration) ──
            migrationBuilder.CreateTable(
                name: "LearnerCalendarLinks",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    UserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Provider = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CalendarId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    RefreshTokenEncrypted = table.Column<string>(type: "text", nullable: false),
                    TokenHint = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    AccessTokenExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    LastSyncedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastError = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table => table.PrimaryKey("PK_LearnerCalendarLinks", x => x.Id));
            migrationBuilder.CreateIndex(
                name: "IX_LearnerCalendarLinks_UserId_Provider",
                table: "LearnerCalendarLinks",
                columns: new[] { "UserId", "Provider" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "LearnerCalendarLinks");
            migrationBuilder.DropTable(name: "StudyPlanAdminOverrides");
            migrationBuilder.DropTable(name: "StudyPlanGenerationLogs");
            migrationBuilder.DropTable(name: "StudyPlanDriftPolicies");
            migrationBuilder.DropTable(name: "StudyPlanAssignmentRules");
            migrationBuilder.DropTable(name: "StudyPlanTemplateItems");
            migrationBuilder.DropTable(name: "StudyPlanTemplates");
            migrationBuilder.DropTable(name: "StudyPlanTaskTemplates");

            migrationBuilder.DropColumn(name: "TemplateId", table: "StudyPlans");
            migrationBuilder.DropColumn(name: "AssignmentRuleIdsCsv", table: "StudyPlans");
            migrationBuilder.DropColumn(name: "GoalSnapshotJson", table: "StudyPlans");

            migrationBuilder.DropColumn(name: "TaskTemplateId", table: "StudyPlanItems");
            migrationBuilder.DropColumn(name: "ContentPaperId", table: "StudyPlanItems");
            migrationBuilder.DropColumn(name: "Priority", table: "StudyPlanItems");
            migrationBuilder.DropColumn(name: "PrerequisiteItemId", table: "StudyPlanItems");
            migrationBuilder.DropColumn(name: "AiRationaleAddendum", table: "StudyPlanItems");
            migrationBuilder.DropColumn(name: "StartedAt", table: "StudyPlanItems");
            migrationBuilder.DropColumn(name: "UpdatedAt", table: "StudyPlanItems");
            migrationBuilder.DropColumn(name: "SnoozedUntil", table: "StudyPlanItems");
        }
    }
}
