using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OetLearner.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddStudyPlannerEngineFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DiagnosticAttemptId",
                table: "StudyPlans",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EntitlementTierAtGeneration",
                table: "StudyPlans",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "GenerationInputsHash",
                table: "StudyPlans",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsActive",
                table: "StudyPlans",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsPremiumPersonalized",
                table: "StudyPlans",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "MinutesPerDayBudget",
                table: "StudyPlans",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateOnly>(
                name: "PlanWindowEnd",
                table: "StudyPlans",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "PlanWindowStart",
                table: "StudyPlans",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SubtestWeightsJson",
                table: "StudyPlans",
                type: "jsonb",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "TemplateId",
                table: "StudyPlans",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TotalWeeks",
                table: "StudyPlans",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "WeekNumber",
                table: "StudyPlans",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "ActualMinutesSpent",
                table: "StudyPlanItems",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "CompletedAt",
                table: "StudyPlanItems",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ContentRoute",
                table: "StudyPlanItems",
                type: "character varying(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "FeedbackRating",
                table: "StudyPlanItems",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LinkedReviewItemId",
                table: "StudyPlanItems",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PriorityScore",
                table: "StudyPlanItems",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "ReplacedById",
                table: "StudyPlanItems",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SlotKind",
                table: "StudyPlanItems",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SourceContentId",
                table: "StudyPlanItems",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TagsJson",
                table: "StudyPlanItems",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "WeekIndex",
                table: "StudyPlanItems",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "RecommendedDrillIdsJson",
                table: "SpeakingSessions",
                type: "character varying(1024)",
                maxLength: 1024,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "WarmupEndedAt",
                table: "SpeakingSessions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "WarmupStartedAt",
                table: "SpeakingSessions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsWarmup",
                table: "SpeakingRecordings",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "BridgeStartedAt",
                table: "SpeakingMockSessions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OrchestratorState",
                table: "SpeakingMockSessions",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "ElapsedMs",
                table: "ReadingAnswers",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TotalElapsedMs",
                table: "ReadingAnswers",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "MissReason",
                table: "ListeningAnswers",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Country",
                table: "ApplicationUserAccounts",
                type: "character varying(2)",
                maxLength: 2,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PreferredCurrency",
                table: "ApplicationUserAccounts",
                type: "character varying(3)",
                maxLength: 3,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PreferredRegion",
                table: "ApplicationUserAccounts",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "GatewayRoutingConfigs",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Region = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Currency = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    ProductType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    GatewayName = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Priority = table.Column<int>(type: "integer", nullable: false),
                    IsEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedByAdminId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GatewayRoutingConfigs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RegionPricings",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    TargetType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    TargetId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Region = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Currency = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    PriceAmount = table.Column<decimal>(type: "numeric(12,2)", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedByAdminId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    UpdatedByAdminId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RegionPricings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SpeakingCardBatchRequests",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ProfessionId = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Count = table.Column<int>(type: "integer", nullable: false),
                    GeneratedCount = table.Column<int>(type: "integer", nullable: false),
                    TopicListJson = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    DifficultyDistributionJson = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    RequestedByAdminId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    RequestedByAdminName = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: true),
                    IdempotencyKey = table.Column<string>(type: "character varying(96)", maxLength: 96, nullable: true),
                    Error = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SpeakingCardBatchRequests", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "StudyPlanTemplates",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Slug = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Description = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    ExamTypeCode = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    ExamFamilyCode = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    MinWeeks = table.Column<int>(type: "integer", nullable: false),
                    MaxWeeks = table.Column<int>(type: "integer", nullable: false),
                    TargetBand = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: true),
                    ProfessionId = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    FocusTagsJson = table.Column<string>(type: "jsonb", nullable: false),
                    DefaultMinutesPerDay = table.Column<int>(type: "integer", nullable: false),
                    TemplateBodyJson = table.Column<string>(type: "jsonb", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StudyPlanTemplates", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "StudyPlanTemplateTiers",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    TemplateId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    TierCode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StudyPlanTemplateTiers", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_StudyPlans_TemplateId",
                table: "StudyPlans",
                column: "TemplateId");

            migrationBuilder.CreateIndex(
                name: "IX_StudyPlans_UserId_IsActive",
                table: "StudyPlans",
                columns: new[] { "UserId", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_StudyPlanItems_LinkedReviewItemId",
                table: "StudyPlanItems",
                column: "LinkedReviewItemId");

            migrationBuilder.CreateIndex(
                name: "IX_StudyPlanItems_ReplacedById",
                table: "StudyPlanItems",
                column: "ReplacedById");

            migrationBuilder.CreateIndex(
                name: "IX_StudyPlanItems_StudyPlanId_WeekIndex",
                table: "StudyPlanItems",
                columns: new[] { "StudyPlanId", "WeekIndex" });

            migrationBuilder.CreateIndex(
                name: "IX_GatewayRoutingConfigs_Region_Currency_ProductType_GatewayNa~",
                table: "GatewayRoutingConfigs",
                columns: new[] { "Region", "Currency", "ProductType", "GatewayName" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GatewayRoutingConfigs_Region_Currency_ProductType_Priority",
                table: "GatewayRoutingConfigs",
                columns: new[] { "Region", "Currency", "ProductType", "Priority" });

            migrationBuilder.CreateIndex(
                name: "IX_RegionPricings_Region_IsActive",
                table: "RegionPricings",
                columns: new[] { "Region", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_RegionPricings_TargetType_TargetId_Region",
                table: "RegionPricings",
                columns: new[] { "TargetType", "TargetId", "Region" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SpeakingCardBatchRequests_IdempotencyKey",
                table: "SpeakingCardBatchRequests",
                column: "IdempotencyKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SpeakingCardBatchRequests_Status_CreatedAt",
                table: "SpeakingCardBatchRequests",
                columns: new[] { "Status", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_StudyPlanTemplates_IsActive_ExamTypeCode",
                table: "StudyPlanTemplates",
                columns: new[] { "IsActive", "ExamTypeCode" });

            migrationBuilder.CreateIndex(
                name: "IX_StudyPlanTemplates_IsActive_MinWeeks_MaxWeeks",
                table: "StudyPlanTemplates",
                columns: new[] { "IsActive", "MinWeeks", "MaxWeeks" });

            migrationBuilder.CreateIndex(
                name: "IX_StudyPlanTemplates_ProfessionId",
                table: "StudyPlanTemplates",
                column: "ProfessionId");

            migrationBuilder.CreateIndex(
                name: "IX_StudyPlanTemplates_Slug",
                table: "StudyPlanTemplates",
                column: "Slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StudyPlanTemplateTiers_TemplateId_TierCode",
                table: "StudyPlanTemplateTiers",
                columns: new[] { "TemplateId", "TierCode" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "GatewayRoutingConfigs");

            migrationBuilder.DropTable(
                name: "RegionPricings");

            migrationBuilder.DropTable(
                name: "SpeakingCardBatchRequests");

            migrationBuilder.DropTable(
                name: "StudyPlanTemplates");

            migrationBuilder.DropTable(
                name: "StudyPlanTemplateTiers");

            migrationBuilder.DropIndex(
                name: "IX_StudyPlans_TemplateId",
                table: "StudyPlans");

            migrationBuilder.DropIndex(
                name: "IX_StudyPlans_UserId_IsActive",
                table: "StudyPlans");

            migrationBuilder.DropIndex(
                name: "IX_StudyPlanItems_LinkedReviewItemId",
                table: "StudyPlanItems");

            migrationBuilder.DropIndex(
                name: "IX_StudyPlanItems_ReplacedById",
                table: "StudyPlanItems");

            migrationBuilder.DropIndex(
                name: "IX_StudyPlanItems_StudyPlanId_WeekIndex",
                table: "StudyPlanItems");

            migrationBuilder.DropColumn(
                name: "DiagnosticAttemptId",
                table: "StudyPlans");

            migrationBuilder.DropColumn(
                name: "EntitlementTierAtGeneration",
                table: "StudyPlans");

            migrationBuilder.DropColumn(
                name: "GenerationInputsHash",
                table: "StudyPlans");

            migrationBuilder.DropColumn(
                name: "IsActive",
                table: "StudyPlans");

            migrationBuilder.DropColumn(
                name: "IsPremiumPersonalized",
                table: "StudyPlans");

            migrationBuilder.DropColumn(
                name: "MinutesPerDayBudget",
                table: "StudyPlans");

            migrationBuilder.DropColumn(
                name: "PlanWindowEnd",
                table: "StudyPlans");

            migrationBuilder.DropColumn(
                name: "PlanWindowStart",
                table: "StudyPlans");

            migrationBuilder.DropColumn(
                name: "SubtestWeightsJson",
                table: "StudyPlans");

            migrationBuilder.DropColumn(
                name: "TemplateId",
                table: "StudyPlans");

            migrationBuilder.DropColumn(
                name: "TotalWeeks",
                table: "StudyPlans");

            migrationBuilder.DropColumn(
                name: "WeekNumber",
                table: "StudyPlans");

            migrationBuilder.DropColumn(
                name: "ActualMinutesSpent",
                table: "StudyPlanItems");

            migrationBuilder.DropColumn(
                name: "CompletedAt",
                table: "StudyPlanItems");

            migrationBuilder.DropColumn(
                name: "ContentRoute",
                table: "StudyPlanItems");

            migrationBuilder.DropColumn(
                name: "FeedbackRating",
                table: "StudyPlanItems");

            migrationBuilder.DropColumn(
                name: "LinkedReviewItemId",
                table: "StudyPlanItems");

            migrationBuilder.DropColumn(
                name: "PriorityScore",
                table: "StudyPlanItems");

            migrationBuilder.DropColumn(
                name: "ReplacedById",
                table: "StudyPlanItems");

            migrationBuilder.DropColumn(
                name: "SlotKind",
                table: "StudyPlanItems");

            migrationBuilder.DropColumn(
                name: "SourceContentId",
                table: "StudyPlanItems");

            migrationBuilder.DropColumn(
                name: "TagsJson",
                table: "StudyPlanItems");

            migrationBuilder.DropColumn(
                name: "WeekIndex",
                table: "StudyPlanItems");

            migrationBuilder.DropColumn(
                name: "RecommendedDrillIdsJson",
                table: "SpeakingSessions");

            migrationBuilder.DropColumn(
                name: "WarmupEndedAt",
                table: "SpeakingSessions");

            migrationBuilder.DropColumn(
                name: "WarmupStartedAt",
                table: "SpeakingSessions");

            migrationBuilder.DropColumn(
                name: "IsWarmup",
                table: "SpeakingRecordings");

            migrationBuilder.DropColumn(
                name: "BridgeStartedAt",
                table: "SpeakingMockSessions");

            migrationBuilder.DropColumn(
                name: "OrchestratorState",
                table: "SpeakingMockSessions");

            migrationBuilder.DropColumn(
                name: "ElapsedMs",
                table: "ReadingAnswers");

            migrationBuilder.DropColumn(
                name: "TotalElapsedMs",
                table: "ReadingAnswers");

            migrationBuilder.DropColumn(
                name: "MissReason",
                table: "ListeningAnswers");

            migrationBuilder.DropColumn(
                name: "Country",
                table: "ApplicationUserAccounts");

            migrationBuilder.DropColumn(
                name: "PreferredCurrency",
                table: "ApplicationUserAccounts");

            migrationBuilder.DropColumn(
                name: "PreferredRegion",
                table: "ApplicationUserAccounts");
        }
    }
}
