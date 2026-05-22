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
            migrationBuilder.Sql("""
                ALTER TABLE "StudyPlans" ADD COLUMN IF NOT EXISTS "DiagnosticAttemptId" character varying(64);
                ALTER TABLE "StudyPlans" ADD COLUMN IF NOT EXISTS "EntitlementTierAtGeneration" character varying(32) NOT NULL DEFAULT '';
                ALTER TABLE "StudyPlans" ADD COLUMN IF NOT EXISTS "GenerationInputsHash" character varying(128);
                ALTER TABLE "StudyPlans" ADD COLUMN IF NOT EXISTS "IsActive" boolean NOT NULL DEFAULT false;
                ALTER TABLE "StudyPlans" ADD COLUMN IF NOT EXISTS "IsPremiumPersonalized" boolean NOT NULL DEFAULT false;
                ALTER TABLE "StudyPlans" ADD COLUMN IF NOT EXISTS "MinutesPerDayBudget" integer NOT NULL DEFAULT 0;
                ALTER TABLE "StudyPlans" ADD COLUMN IF NOT EXISTS "PlanWindowEnd" date;
                ALTER TABLE "StudyPlans" ADD COLUMN IF NOT EXISTS "PlanWindowStart" date;
                ALTER TABLE "StudyPlans" ADD COLUMN IF NOT EXISTS "SubtestWeightsJson" jsonb NOT NULL DEFAULT '[]'::jsonb;
                ALTER TABLE "StudyPlans" ADD COLUMN IF NOT EXISTS "TemplateId" character varying(64);
                ALTER TABLE "StudyPlans" ADD COLUMN IF NOT EXISTS "TotalWeeks" integer NOT NULL DEFAULT 0;
                ALTER TABLE "StudyPlans" ADD COLUMN IF NOT EXISTS "WeekNumber" integer NOT NULL DEFAULT 0;

                ALTER TABLE "StudyPlanItems" ADD COLUMN IF NOT EXISTS "ActualMinutesSpent" integer;
                ALTER TABLE "StudyPlanItems" ADD COLUMN IF NOT EXISTS "CompletedAt" timestamp with time zone;
                ALTER TABLE "StudyPlanItems" ADD COLUMN IF NOT EXISTS "ContentRoute" character varying(512);
                ALTER TABLE "StudyPlanItems" ADD COLUMN IF NOT EXISTS "FeedbackRating" integer;
                ALTER TABLE "StudyPlanItems" ADD COLUMN IF NOT EXISTS "LinkedReviewItemId" character varying(64);
                ALTER TABLE "StudyPlanItems" ADD COLUMN IF NOT EXISTS "PriorityScore" integer NOT NULL DEFAULT 0;
                ALTER TABLE "StudyPlanItems" ADD COLUMN IF NOT EXISTS "ReplacedById" character varying(64);
                ALTER TABLE "StudyPlanItems" ADD COLUMN IF NOT EXISTS "SlotKind" character varying(32);
                ALTER TABLE "StudyPlanItems" ADD COLUMN IF NOT EXISTS "SourceContentId" character varying(64);
                ALTER TABLE "StudyPlanItems" ADD COLUMN IF NOT EXISTS "TagsJson" text;
                ALTER TABLE "StudyPlanItems" ADD COLUMN IF NOT EXISTS "WeekIndex" integer NOT NULL DEFAULT 0;

                ALTER TABLE "SpeakingSessions" ADD COLUMN IF NOT EXISTS "RecommendedDrillIdsJson" character varying(1024);
                ALTER TABLE "SpeakingSessions" ADD COLUMN IF NOT EXISTS "WarmupEndedAt" timestamp with time zone;
                ALTER TABLE "SpeakingSessions" ADD COLUMN IF NOT EXISTS "WarmupStartedAt" timestamp with time zone;
                ALTER TABLE "SpeakingRecordings" ADD COLUMN IF NOT EXISTS "IsWarmup" boolean NOT NULL DEFAULT false;
                ALTER TABLE "SpeakingMockSessions" ADD COLUMN IF NOT EXISTS "BridgeStartedAt" timestamp with time zone;
                ALTER TABLE "SpeakingMockSessions" ADD COLUMN IF NOT EXISTS "OrchestratorState" character varying(16) NOT NULL DEFAULT '';
                ALTER TABLE "ReadingAnswers" ADD COLUMN IF NOT EXISTS "ElapsedMs" integer;
                ALTER TABLE "ReadingAnswers" ADD COLUMN IF NOT EXISTS "TotalElapsedMs" integer;
                ALTER TABLE "ListeningAnswers" ADD COLUMN IF NOT EXISTS "MissReason" integer;
                ALTER TABLE "ApplicationUserAccounts" ADD COLUMN IF NOT EXISTS "Country" character varying(2);
                ALTER TABLE "ApplicationUserAccounts" ADD COLUMN IF NOT EXISTS "PreferredCurrency" character varying(3);
                ALTER TABLE "ApplicationUserAccounts" ADD COLUMN IF NOT EXISTS "PreferredRegion" character varying(16);

                CREATE TABLE IF NOT EXISTS "GatewayRoutingConfigs" (
                    "Id" character varying(64) NOT NULL,
                    "Region" character varying(16) NOT NULL,
                    "Currency" character varying(8) NOT NULL,
                    "ProductType" character varying(32) NOT NULL,
                    "GatewayName" character varying(32) NOT NULL,
                    "Priority" integer NOT NULL,
                    "IsEnabled" boolean NOT NULL,
                    "CreatedAt" timestamp with time zone NOT NULL,
                    "UpdatedAt" timestamp with time zone NOT NULL,
                    "UpdatedByAdminId" character varying(64),
                    CONSTRAINT "PK_GatewayRoutingConfigs" PRIMARY KEY ("Id")
                );

                CREATE TABLE IF NOT EXISTS "RegionPricings" (
                    "Id" character varying(64) NOT NULL,
                    "TargetType" character varying(32) NOT NULL,
                    "TargetId" character varying(64) NOT NULL,
                    "Region" character varying(16) NOT NULL,
                    "Currency" character varying(8) NOT NULL,
                    "PriceAmount" numeric(12,2) NOT NULL,
                    "IsActive" boolean NOT NULL,
                    "CreatedAt" timestamp with time zone NOT NULL,
                    "UpdatedAt" timestamp with time zone NOT NULL,
                    "CreatedByAdminId" character varying(64),
                    "UpdatedByAdminId" character varying(64),
                    CONSTRAINT "PK_RegionPricings" PRIMARY KEY ("Id")
                );

                CREATE TABLE IF NOT EXISTS "SpeakingCardBatchRequests" (
                    "Id" character varying(64) NOT NULL,
                    "ProfessionId" character varying(32) NOT NULL,
                    "Count" integer NOT NULL,
                    "GeneratedCount" integer NOT NULL,
                    "TopicListJson" character varying(2000) NOT NULL,
                    "DifficultyDistributionJson" character varying(500) NOT NULL,
                    "Status" integer NOT NULL,
                    "RequestedByAdminId" character varying(64) NOT NULL,
                    "RequestedByAdminName" character varying(160),
                    "IdempotencyKey" character varying(96),
                    "Error" character varying(1024),
                    "CreatedAt" timestamp with time zone NOT NULL,
                    "StartedAt" timestamp with time zone,
                    "CompletedAt" timestamp with time zone,
                    CONSTRAINT "PK_SpeakingCardBatchRequests" PRIMARY KEY ("Id")
                );

                CREATE TABLE IF NOT EXISTS "StudyPlanTemplates" (
                    "Id" character varying(64) NOT NULL,
                    "Name" character varying(128) NOT NULL,
                    "Slug" character varying(64) NOT NULL,
                    "Description" character varying(1024),
                    "ExamTypeCode" character varying(16) NOT NULL,
                    "ExamFamilyCode" character varying(16) NOT NULL,
                    "MinWeeks" integer NOT NULL,
                    "MaxWeeks" integer NOT NULL,
                    "TargetBand" character varying(8),
                    "ProfessionId" character varying(32),
                    "FocusTagsJson" jsonb NOT NULL,
                    "DefaultMinutesPerDay" integer NOT NULL,
                    "TemplateBodyJson" jsonb NOT NULL,
                    "IsActive" boolean NOT NULL,
                    "Version" integer NOT NULL,
                    "CreatedBy" character varying(64),
                    "CreatedAt" timestamp with time zone NOT NULL,
                    "UpdatedAt" timestamp with time zone NOT NULL,
                    CONSTRAINT "PK_StudyPlanTemplates" PRIMARY KEY ("Id")
                );

                CREATE TABLE IF NOT EXISTS "StudyPlanTemplateTiers" (
                    "Id" character varying(64) NOT NULL,
                    "TemplateId" character varying(64) NOT NULL,
                    "TierCode" character varying(32) NOT NULL,
                    "CreatedAt" timestamp with time zone NOT NULL,
                    CONSTRAINT "PK_StudyPlanTemplateTiers" PRIMARY KEY ("Id")
                );

                ALTER TABLE "GatewayRoutingConfigs" ADD COLUMN IF NOT EXISTS "Id" character varying(64) NOT NULL DEFAULT '';
                ALTER TABLE "GatewayRoutingConfigs" ADD COLUMN IF NOT EXISTS "Region" character varying(16) NOT NULL DEFAULT '';
                ALTER TABLE "GatewayRoutingConfigs" ADD COLUMN IF NOT EXISTS "Currency" character varying(8) NOT NULL DEFAULT '';
                ALTER TABLE "GatewayRoutingConfigs" ADD COLUMN IF NOT EXISTS "ProductType" character varying(32) NOT NULL DEFAULT '';
                ALTER TABLE "GatewayRoutingConfigs" ADD COLUMN IF NOT EXISTS "GatewayName" character varying(32) NOT NULL DEFAULT '';
                ALTER TABLE "GatewayRoutingConfigs" ADD COLUMN IF NOT EXISTS "Priority" integer NOT NULL DEFAULT 0;
                ALTER TABLE "GatewayRoutingConfigs" ADD COLUMN IF NOT EXISTS "IsEnabled" boolean NOT NULL DEFAULT false;
                ALTER TABLE "GatewayRoutingConfigs" ADD COLUMN IF NOT EXISTS "CreatedAt" timestamp with time zone NOT NULL DEFAULT NOW();
                ALTER TABLE "GatewayRoutingConfigs" ADD COLUMN IF NOT EXISTS "UpdatedAt" timestamp with time zone NOT NULL DEFAULT NOW();
                ALTER TABLE "GatewayRoutingConfigs" ADD COLUMN IF NOT EXISTS "UpdatedByAdminId" character varying(64);

                ALTER TABLE "RegionPricings" ADD COLUMN IF NOT EXISTS "Id" character varying(64) NOT NULL DEFAULT '';
                ALTER TABLE "RegionPricings" ADD COLUMN IF NOT EXISTS "TargetType" character varying(32) NOT NULL DEFAULT '';
                ALTER TABLE "RegionPricings" ADD COLUMN IF NOT EXISTS "TargetId" character varying(64) NOT NULL DEFAULT '';
                ALTER TABLE "RegionPricings" ADD COLUMN IF NOT EXISTS "Region" character varying(16) NOT NULL DEFAULT '';
                ALTER TABLE "RegionPricings" ADD COLUMN IF NOT EXISTS "Currency" character varying(8) NOT NULL DEFAULT '';
                ALTER TABLE "RegionPricings" ADD COLUMN IF NOT EXISTS "PriceAmount" numeric(12,2) NOT NULL DEFAULT 0;
                ALTER TABLE "RegionPricings" ADD COLUMN IF NOT EXISTS "IsActive" boolean NOT NULL DEFAULT false;
                ALTER TABLE "RegionPricings" ADD COLUMN IF NOT EXISTS "CreatedAt" timestamp with time zone NOT NULL DEFAULT NOW();
                ALTER TABLE "RegionPricings" ADD COLUMN IF NOT EXISTS "UpdatedAt" timestamp with time zone NOT NULL DEFAULT NOW();
                ALTER TABLE "RegionPricings" ADD COLUMN IF NOT EXISTS "CreatedByAdminId" character varying(64);
                ALTER TABLE "RegionPricings" ADD COLUMN IF NOT EXISTS "UpdatedByAdminId" character varying(64);

                ALTER TABLE "SpeakingCardBatchRequests" ADD COLUMN IF NOT EXISTS "Id" character varying(64) NOT NULL DEFAULT '';
                ALTER TABLE "SpeakingCardBatchRequests" ADD COLUMN IF NOT EXISTS "ProfessionId" character varying(32) NOT NULL DEFAULT '';
                ALTER TABLE "SpeakingCardBatchRequests" ADD COLUMN IF NOT EXISTS "Count" integer NOT NULL DEFAULT 0;
                ALTER TABLE "SpeakingCardBatchRequests" ADD COLUMN IF NOT EXISTS "GeneratedCount" integer NOT NULL DEFAULT 0;
                ALTER TABLE "SpeakingCardBatchRequests" ADD COLUMN IF NOT EXISTS "TopicListJson" character varying(2000) NOT NULL DEFAULT '[]';
                ALTER TABLE "SpeakingCardBatchRequests" ADD COLUMN IF NOT EXISTS "DifficultyDistributionJson" character varying(500) NOT NULL DEFAULT '{}';
                ALTER TABLE "SpeakingCardBatchRequests" ADD COLUMN IF NOT EXISTS "Status" integer NOT NULL DEFAULT 0;
                ALTER TABLE "SpeakingCardBatchRequests" ADD COLUMN IF NOT EXISTS "RequestedByAdminId" character varying(64) NOT NULL DEFAULT '';
                ALTER TABLE "SpeakingCardBatchRequests" ADD COLUMN IF NOT EXISTS "RequestedByAdminName" character varying(160);
                ALTER TABLE "SpeakingCardBatchRequests" ADD COLUMN IF NOT EXISTS "IdempotencyKey" character varying(96);
                ALTER TABLE "SpeakingCardBatchRequests" ADD COLUMN IF NOT EXISTS "Error" character varying(1024);
                ALTER TABLE "SpeakingCardBatchRequests" ADD COLUMN IF NOT EXISTS "CreatedAt" timestamp with time zone NOT NULL DEFAULT NOW();
                ALTER TABLE "SpeakingCardBatchRequests" ADD COLUMN IF NOT EXISTS "StartedAt" timestamp with time zone;
                ALTER TABLE "SpeakingCardBatchRequests" ADD COLUMN IF NOT EXISTS "CompletedAt" timestamp with time zone;

                ALTER TABLE "StudyPlanTemplates" ADD COLUMN IF NOT EXISTS "Id" character varying(64) NOT NULL DEFAULT '';
                ALTER TABLE "StudyPlanTemplates" ADD COLUMN IF NOT EXISTS "Name" character varying(128) NOT NULL DEFAULT '';
                ALTER TABLE "StudyPlanTemplates" ADD COLUMN IF NOT EXISTS "Slug" character varying(64) NOT NULL DEFAULT '';
                ALTER TABLE "StudyPlanTemplates" ADD COLUMN IF NOT EXISTS "Description" character varying(1024);
                ALTER TABLE "StudyPlanTemplates" ADD COLUMN IF NOT EXISTS "ExamTypeCode" character varying(16) NOT NULL DEFAULT 'OET';
                ALTER TABLE "StudyPlanTemplates" ADD COLUMN IF NOT EXISTS "ExamFamilyCode" character varying(16) NOT NULL DEFAULT 'oet';
                ALTER TABLE "StudyPlanTemplates" ADD COLUMN IF NOT EXISTS "MinWeeks" integer NOT NULL DEFAULT 1;
                ALTER TABLE "StudyPlanTemplates" ADD COLUMN IF NOT EXISTS "MaxWeeks" integer NOT NULL DEFAULT 52;
                ALTER TABLE "StudyPlanTemplates" ADD COLUMN IF NOT EXISTS "TargetBand" character varying(8);
                ALTER TABLE "StudyPlanTemplates" ADD COLUMN IF NOT EXISTS "ProfessionId" character varying(32);
                ALTER TABLE "StudyPlanTemplates" ADD COLUMN IF NOT EXISTS "FocusTagsJson" jsonb NOT NULL DEFAULT '[]'::jsonb;
                ALTER TABLE "StudyPlanTemplates" ADD COLUMN IF NOT EXISTS "DefaultMinutesPerDay" integer NOT NULL DEFAULT 60;
                ALTER TABLE "StudyPlanTemplates" ADD COLUMN IF NOT EXISTS "TemplateBodyJson" jsonb NOT NULL DEFAULT '{"weeks":[],"checkpoints":[]}'::jsonb;
                ALTER TABLE "StudyPlanTemplates" ADD COLUMN IF NOT EXISTS "IsActive" boolean NOT NULL DEFAULT true;
                ALTER TABLE "StudyPlanTemplates" ADD COLUMN IF NOT EXISTS "Version" integer NOT NULL DEFAULT 1;
                ALTER TABLE "StudyPlanTemplates" ADD COLUMN IF NOT EXISTS "CreatedBy" character varying(64);
                ALTER TABLE "StudyPlanTemplates" ADD COLUMN IF NOT EXISTS "CreatedAt" timestamp with time zone NOT NULL DEFAULT NOW();
                ALTER TABLE "StudyPlanTemplates" ADD COLUMN IF NOT EXISTS "UpdatedAt" timestamp with time zone NOT NULL DEFAULT NOW();

                UPDATE "StudyPlanTemplates"
                SET
                    "ExamTypeCode" = COALESCE(NULLIF("ExamTypeCode", ''), 'OET'),
                    "ExamFamilyCode" = COALESCE(NULLIF("ExamFamilyCode", ''), 'oet');

                DO $$
                BEGIN
                    IF EXISTS (
                        SELECT 1 FROM information_schema.columns
                        WHERE table_schema = 'public' AND table_name = 'StudyPlanTemplates' AND column_name = 'DurationWeeks'
                    ) THEN
                        UPDATE "StudyPlanTemplates"
                        SET
                            "MinWeeks" = CASE WHEN "MinWeeks" <= 0 AND "DurationWeeks" > 0 THEN "DurationWeeks" ELSE "MinWeeks" END,
                            "MaxWeeks" = CASE WHEN "MaxWeeks" <= 0 AND "DurationWeeks" > 0 THEN "DurationWeeks" ELSE "MaxWeeks" END;
                    END IF;

                    IF EXISTS (
                        SELECT 1 FROM information_schema.columns
                        WHERE table_schema = 'public' AND table_name = 'StudyPlanTemplates' AND column_name = 'DefaultHoursPerWeek'
                    ) THEN
                        UPDATE "StudyPlanTemplates"
                        SET "DefaultMinutesPerDay" = CASE
                            WHEN "DefaultMinutesPerDay" <= 0 AND "DefaultHoursPerWeek" > 0 THEN GREATEST(15, ("DefaultHoursPerWeek" * 60) / 7)
                            ELSE "DefaultMinutesPerDay"
                        END;
                    END IF;

                    IF EXISTS (
                        SELECT 1 FROM information_schema.columns
                        WHERE table_schema = 'public' AND table_name = 'StudyPlanTemplates' AND column_name = 'IsArchived'
                    ) THEN
                        UPDATE "StudyPlanTemplates" SET "IsActive" = NOT "IsArchived";
                    END IF;

                    IF EXISTS (
                        SELECT 1 FROM information_schema.columns
                        WHERE table_schema = 'public' AND table_name = 'StudyPlanTemplates' AND column_name = 'CreatedByAdminId'
                    ) THEN
                        UPDATE "StudyPlanTemplates" SET "CreatedBy" = COALESCE("CreatedBy", "CreatedByAdminId");
                    END IF;
                END $$;

                ALTER TABLE "StudyPlanTemplateTiers" ADD COLUMN IF NOT EXISTS "Id" character varying(64) NOT NULL DEFAULT '';
                ALTER TABLE "StudyPlanTemplateTiers" ADD COLUMN IF NOT EXISTS "TemplateId" character varying(64) NOT NULL DEFAULT '';
                ALTER TABLE "StudyPlanTemplateTiers" ADD COLUMN IF NOT EXISTS "TierCode" character varying(32) NOT NULL DEFAULT '';
                ALTER TABLE "StudyPlanTemplateTiers" ADD COLUMN IF NOT EXISTS "CreatedAt" timestamp with time zone NOT NULL DEFAULT NOW();

                CREATE INDEX IF NOT EXISTS "IX_StudyPlans_TemplateId" ON "StudyPlans" ("TemplateId");
                CREATE INDEX IF NOT EXISTS "IX_StudyPlans_UserId_IsActive" ON "StudyPlans" ("UserId", "IsActive");
                CREATE INDEX IF NOT EXISTS "IX_StudyPlanItems_LinkedReviewItemId" ON "StudyPlanItems" ("LinkedReviewItemId");
                CREATE INDEX IF NOT EXISTS "IX_StudyPlanItems_ReplacedById" ON "StudyPlanItems" ("ReplacedById");
                CREATE INDEX IF NOT EXISTS "IX_StudyPlanItems_StudyPlanId_WeekIndex" ON "StudyPlanItems" ("StudyPlanId", "WeekIndex");
                CREATE UNIQUE INDEX IF NOT EXISTS "IX_GatewayRoutingConfigs_Region_Currency_ProductType_GatewayNa~" ON "GatewayRoutingConfigs" ("Region", "Currency", "ProductType", "GatewayName");
                CREATE INDEX IF NOT EXISTS "IX_GatewayRoutingConfigs_Region_Currency_ProductType_Priority" ON "GatewayRoutingConfigs" ("Region", "Currency", "ProductType", "Priority");
                CREATE INDEX IF NOT EXISTS "IX_RegionPricings_Region_IsActive" ON "RegionPricings" ("Region", "IsActive");
                CREATE UNIQUE INDEX IF NOT EXISTS "IX_RegionPricings_TargetType_TargetId_Region" ON "RegionPricings" ("TargetType", "TargetId", "Region");
                CREATE UNIQUE INDEX IF NOT EXISTS "IX_SpeakingCardBatchRequests_IdempotencyKey" ON "SpeakingCardBatchRequests" ("IdempotencyKey");
                CREATE INDEX IF NOT EXISTS "IX_SpeakingCardBatchRequests_Status_CreatedAt" ON "SpeakingCardBatchRequests" ("Status", "CreatedAt");
                CREATE INDEX IF NOT EXISTS "IX_StudyPlanTemplates_IsActive_ExamTypeCode" ON "StudyPlanTemplates" ("IsActive", "ExamTypeCode");
                CREATE INDEX IF NOT EXISTS "IX_StudyPlanTemplates_IsActive_MinWeeks_MaxWeeks" ON "StudyPlanTemplates" ("IsActive", "MinWeeks", "MaxWeeks");
                CREATE INDEX IF NOT EXISTS "IX_StudyPlanTemplates_ProfessionId" ON "StudyPlanTemplates" ("ProfessionId");
                CREATE UNIQUE INDEX IF NOT EXISTS "IX_StudyPlanTemplates_Slug" ON "StudyPlanTemplates" ("Slug");
                CREATE UNIQUE INDEX IF NOT EXISTS "IX_StudyPlanTemplateTiers_TemplateId_TierCode" ON "StudyPlanTemplateTiers" ("TemplateId", "TierCode");
                """);

            return;

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
