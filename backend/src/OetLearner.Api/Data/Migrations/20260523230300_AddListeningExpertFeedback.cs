using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OetLearner.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddListeningExpertFeedback : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "AiCreditsRemaining",
                table: "Subscriptions",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "BasicEnglishUnlocked",
                table: "Subscriptions",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ExpiresAt",
                table: "Subscriptions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SpeakingSessionsRemaining",
                table: "Subscriptions",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "TutorBookUnlocked",
                table: "Subscriptions",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "WritingAssessmentsRemaining",
                table: "Subscriptions",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "BillingAddOnId",
                table: "ContentPackages",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "AccessDurationDays",
                table: "BillingPlanVersions",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "BundledAiCredits",
                table: "BillingPlanVersions",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "BundledBasicEnglish",
                table: "BillingPlanVersions",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "BundledSpeakingSessions",
                table: "BillingPlanVersions",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "BundledTutorBook",
                table: "BillingPlanVersions",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "BundledWritingAssessments",
                table: "BillingPlanVersions",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "DashboardModulesJson",
                table: "BillingPlanVersions",
                type: "character varying(2048)",
                maxLength: 2048,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<bool>(
                name: "ExtensionAllowed",
                table: "BillingPlanVersions",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsDraft",
                table: "BillingPlanVersions",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<decimal>(
                name: "OriginalPriceGbp",
                table: "BillingPlanVersions",
                type: "numeric",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProductCategory",
                table: "BillingPlanVersions",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Profession",
                table: "BillingPlanVersions",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<bool>(
                name: "RecallUpdatesEnabled",
                table: "BillingPlanVersions",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "SpeakingAddonsEnabled",
                table: "BillingPlanVersions",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "TutorBookDiscountEnabled",
                table: "BillingPlanVersions",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "WritingAddonsEnabled",
                table: "BillingPlanVersions",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "AccessDurationDays",
                table: "BillingPlans",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "BundledAiCredits",
                table: "BillingPlans",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "BundledBasicEnglish",
                table: "BillingPlans",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "BundledSpeakingSessions",
                table: "BillingPlans",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "BundledTutorBook",
                table: "BillingPlans",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "BundledWritingAssessments",
                table: "BillingPlans",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "DashboardModulesJson",
                table: "BillingPlans",
                type: "character varying(2048)",
                maxLength: 2048,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<bool>(
                name: "ExtensionAllowed",
                table: "BillingPlans",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsDraft",
                table: "BillingPlans",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<decimal>(
                name: "OriginalPriceGbp",
                table: "BillingPlans",
                type: "numeric",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProductCategory",
                table: "BillingPlans",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Profession",
                table: "BillingPlans",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<bool>(
                name: "RecallUpdatesEnabled",
                table: "BillingPlans",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "SpeakingAddonsEnabled",
                table: "BillingPlans",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "TutorBookDiscountEnabled",
                table: "BillingPlans",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "WritingAddonsEnabled",
                table: "BillingPlans",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "AddonKind",
                table: "BillingAddOnVersions",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "EligibilityFlag",
                table: "BillingAddOnVersions",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "LettersGranted",
                table: "BillingAddOnVersions",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<decimal>(
                name: "OriginalPriceGbp",
                table: "BillingAddOnVersions",
                type: "numeric",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "RequiresEligibleParent",
                table: "BillingAddOnVersions",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "SessionsGranted",
                table: "BillingAddOnVersions",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "AddonKind",
                table: "BillingAddOns",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "EligibilityFlag",
                table: "BillingAddOns",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "LettersGranted",
                table: "BillingAddOns",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<decimal>(
                name: "OriginalPriceGbp",
                table: "BillingAddOns",
                type: "numeric",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "RequiresEligibleParent",
                table: "BillingAddOns",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "SessionsGranted",
                table: "BillingAddOns",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "ListeningExpertFeedbacks",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    AttemptId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ExpertId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    OverallFeedbackMarkdown = table.Column<string>(type: "text", nullable: false),
                    PerQuestionFeedbackJson = table.Column<string>(type: "text", nullable: true),
                    RecommendedAreasJson = table.Column<string>(type: "text", nullable: true),
                    RawScoreOverride = table.Column<int>(type: "integer", nullable: true),
                    ScoreOverrideReason = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    SubmittedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ListeningExpertFeedbacks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ListeningExpertFeedbacks_ListeningAttempts_AttemptId",
                        column: x => x.AttemptId,
                        principalTable: "ListeningAttempts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TutorBookAudioScripts",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Chapter = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    AudioUrl = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    TranscriptUrl = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    DisplayOrder = table.Column<int>(type: "integer", nullable: false),
                    IsPublished = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedByAdminId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    CreatedByAdminName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TutorBookAudioScripts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TutorBookUpdates",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    BodyMarkdown = table.Column<string>(type: "text", nullable: false),
                    Audience = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    PublishedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    IsPublished = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedByAdminId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    CreatedByAdminName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TutorBookUpdates", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ListeningExpertFeedbacks_AttemptId",
                table: "ListeningExpertFeedbacks",
                column: "AttemptId");

            migrationBuilder.CreateIndex(
                name: "IX_TutorBookUpdates_Audience",
                table: "TutorBookUpdates",
                column: "Audience");

            migrationBuilder.CreateIndex(
                name: "IX_TutorBookUpdates_PublishedAt",
                table: "TutorBookUpdates",
                column: "PublishedAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ListeningExpertFeedbacks");

            migrationBuilder.DropTable(
                name: "TutorBookAudioScripts");

            migrationBuilder.DropTable(
                name: "TutorBookUpdates");

            migrationBuilder.DropColumn(
                name: "AiCreditsRemaining",
                table: "Subscriptions");

            migrationBuilder.DropColumn(
                name: "BasicEnglishUnlocked",
                table: "Subscriptions");

            migrationBuilder.DropColumn(
                name: "ExpiresAt",
                table: "Subscriptions");

            migrationBuilder.DropColumn(
                name: "SpeakingSessionsRemaining",
                table: "Subscriptions");

            migrationBuilder.DropColumn(
                name: "TutorBookUnlocked",
                table: "Subscriptions");

            migrationBuilder.DropColumn(
                name: "WritingAssessmentsRemaining",
                table: "Subscriptions");

            migrationBuilder.DropColumn(
                name: "BillingAddOnId",
                table: "ContentPackages");

            migrationBuilder.DropColumn(
                name: "AccessDurationDays",
                table: "BillingPlanVersions");

            migrationBuilder.DropColumn(
                name: "BundledAiCredits",
                table: "BillingPlanVersions");

            migrationBuilder.DropColumn(
                name: "BundledBasicEnglish",
                table: "BillingPlanVersions");

            migrationBuilder.DropColumn(
                name: "BundledSpeakingSessions",
                table: "BillingPlanVersions");

            migrationBuilder.DropColumn(
                name: "BundledTutorBook",
                table: "BillingPlanVersions");

            migrationBuilder.DropColumn(
                name: "BundledWritingAssessments",
                table: "BillingPlanVersions");

            migrationBuilder.DropColumn(
                name: "DashboardModulesJson",
                table: "BillingPlanVersions");

            migrationBuilder.DropColumn(
                name: "ExtensionAllowed",
                table: "BillingPlanVersions");

            migrationBuilder.DropColumn(
                name: "IsDraft",
                table: "BillingPlanVersions");

            migrationBuilder.DropColumn(
                name: "OriginalPriceGbp",
                table: "BillingPlanVersions");

            migrationBuilder.DropColumn(
                name: "ProductCategory",
                table: "BillingPlanVersions");

            migrationBuilder.DropColumn(
                name: "Profession",
                table: "BillingPlanVersions");

            migrationBuilder.DropColumn(
                name: "RecallUpdatesEnabled",
                table: "BillingPlanVersions");

            migrationBuilder.DropColumn(
                name: "SpeakingAddonsEnabled",
                table: "BillingPlanVersions");

            migrationBuilder.DropColumn(
                name: "TutorBookDiscountEnabled",
                table: "BillingPlanVersions");

            migrationBuilder.DropColumn(
                name: "WritingAddonsEnabled",
                table: "BillingPlanVersions");

            migrationBuilder.DropColumn(
                name: "AccessDurationDays",
                table: "BillingPlans");

            migrationBuilder.DropColumn(
                name: "BundledAiCredits",
                table: "BillingPlans");

            migrationBuilder.DropColumn(
                name: "BundledBasicEnglish",
                table: "BillingPlans");

            migrationBuilder.DropColumn(
                name: "BundledSpeakingSessions",
                table: "BillingPlans");

            migrationBuilder.DropColumn(
                name: "BundledTutorBook",
                table: "BillingPlans");

            migrationBuilder.DropColumn(
                name: "BundledWritingAssessments",
                table: "BillingPlans");

            migrationBuilder.DropColumn(
                name: "DashboardModulesJson",
                table: "BillingPlans");

            migrationBuilder.DropColumn(
                name: "ExtensionAllowed",
                table: "BillingPlans");

            migrationBuilder.DropColumn(
                name: "IsDraft",
                table: "BillingPlans");

            migrationBuilder.DropColumn(
                name: "OriginalPriceGbp",
                table: "BillingPlans");

            migrationBuilder.DropColumn(
                name: "ProductCategory",
                table: "BillingPlans");

            migrationBuilder.DropColumn(
                name: "Profession",
                table: "BillingPlans");

            migrationBuilder.DropColumn(
                name: "RecallUpdatesEnabled",
                table: "BillingPlans");

            migrationBuilder.DropColumn(
                name: "SpeakingAddonsEnabled",
                table: "BillingPlans");

            migrationBuilder.DropColumn(
                name: "TutorBookDiscountEnabled",
                table: "BillingPlans");

            migrationBuilder.DropColumn(
                name: "WritingAddonsEnabled",
                table: "BillingPlans");

            migrationBuilder.DropColumn(
                name: "AddonKind",
                table: "BillingAddOnVersions");

            migrationBuilder.DropColumn(
                name: "EligibilityFlag",
                table: "BillingAddOnVersions");

            migrationBuilder.DropColumn(
                name: "LettersGranted",
                table: "BillingAddOnVersions");

            migrationBuilder.DropColumn(
                name: "OriginalPriceGbp",
                table: "BillingAddOnVersions");

            migrationBuilder.DropColumn(
                name: "RequiresEligibleParent",
                table: "BillingAddOnVersions");

            migrationBuilder.DropColumn(
                name: "SessionsGranted",
                table: "BillingAddOnVersions");

            migrationBuilder.DropColumn(
                name: "AddonKind",
                table: "BillingAddOns");

            migrationBuilder.DropColumn(
                name: "EligibilityFlag",
                table: "BillingAddOns");

            migrationBuilder.DropColumn(
                name: "LettersGranted",
                table: "BillingAddOns");

            migrationBuilder.DropColumn(
                name: "OriginalPriceGbp",
                table: "BillingAddOns");

            migrationBuilder.DropColumn(
                name: "RequiresEligibleParent",
                table: "BillingAddOns");

            migrationBuilder.DropColumn(
                name: "SessionsGranted",
                table: "BillingAddOns");
        }
    }
}
