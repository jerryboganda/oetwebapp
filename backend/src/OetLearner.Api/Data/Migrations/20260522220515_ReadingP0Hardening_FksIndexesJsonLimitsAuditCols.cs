using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OetLearner.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class ReadingP0Hardening_FksIndexesJsonLimitsAuditCols : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_BillingQuotes_ExperimentAssignmentId",
                table: "BillingQuotes");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "GracePeriodUntil",
                table: "Subscriptions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "PausedUntil",
                table: "Subscriptions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "BodyHtml",
                table: "ReadingTexts",
                type: "character varying(65536)",
                maxLength: 65536,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "OptionsJson",
                table: "ReadingQuestions",
                type: "character varying(4096)",
                maxLength: 4096,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "OptionDistractorsJson",
                table: "ReadingQuestions",
                type: "character varying(2048)",
                maxLength: 2048,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "CorrectAnswerJson",
                table: "ReadingQuestions",
                type: "character varying(512)",
                maxLength: 512,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "AcceptedSynonymsJson",
                table: "ReadingQuestions",
                type: "character varying(4096)",
                maxLength: 4096,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "ScopeJson",
                table: "ReadingAttempts",
                type: "character varying(8192)",
                maxLength: 8192,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "PolicySnapshotJson",
                table: "ReadingAttempts",
                type: "character varying(16384)",
                maxLength: 16384,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AddColumn<int>(
                name: "RowVersion",
                table: "ReadingAttempts",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AlterColumn<string>(
                name: "UserAnswerJson",
                table: "ReadingAnswers",
                type: "character varying(2048)",
                maxLength: 2048,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "CreatedAt",
                table: "ReadingAnswers",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "UpdatedAt",
                table: "ReadingAnswers",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            migrationBuilder.AddColumn<string>(
                name: "ConfidenceLevel",
                table: "ReadinessSnapshots",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "Low");

            migrationBuilder.AddColumn<int>(
                name: "DataPointCount",
                table: "ReadinessSnapshots",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ExpiresAt",
                table: "ReadinessSnapshots",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            migrationBuilder.AddColumn<decimal>(
                name: "ListeningReadiness",
                table: "ReadinessSnapshots",
                type: "numeric",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "OverallReadiness",
                table: "ReadinessSnapshots",
                type: "numeric",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "OverallRisk",
                table: "ReadinessSnapshots",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "Unknown");

            migrationBuilder.AddColumn<decimal>(
                name: "ReadingReadiness",
                table: "ReadinessSnapshots",
                type: "numeric",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<int>(
                name: "RecommendedStudyHoursPerWeek",
                table: "ReadinessSnapshots",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<decimal>(
                name: "SpeakingReadiness",
                table: "ReadinessSnapshots",
                type: "numeric",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "TargetDateProbability",
                table: "ReadinessSnapshots",
                type: "numeric",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "VocabularyReadiness",
                table: "ReadinessSnapshots",
                type: "numeric",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "WeakestSubtest",
                table: "ReadinessSnapshots",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "WritingReadiness",
                table: "ReadinessSnapshots",
                type: "numeric",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "AudioContentSha",
                table: "ListeningExtracts",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CouponVariant",
                table: "BillingCoupons",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "EligibleCountriesJson",
                table: "BillingCoupons",
                type: "character varying(1024)",
                maxLength: 1024,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<bool>(
                name: "ExistingUsersOnly",
                table: "BillingCoupons",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "NewUsersOnly",
                table: "BillingCoupons",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "StackableWithAffiliate",
                table: "BillingCoupons",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "StackableWithReferral",
                table: "BillingCoupons",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "VariantMetadataJson",
                table: "BillingCoupons",
                type: "character varying(2048)",
                maxLength: 2048,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "AffiliateAttributions",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    UserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    AffiliateId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ClickedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    AttributedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ConvertedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    FirstPaymentTransactionId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AffiliateAttributions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AffiliateCommissions",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    AffiliateId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    UserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    PaymentTransactionId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    AmountAmount = table.Column<decimal>(type: "numeric(12,2)", nullable: false),
                    Currency = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    AccruedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    PaidAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ReversedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    PayoutBatchId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AffiliateCommissions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Affiliates",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    OwnerName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ContactEmail = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    CommissionPercent = table.Column<decimal>(type: "numeric(6,3)", nullable: false),
                    CookieDays = table.Column<int>(type: "integer", nullable: false),
                    PayoutThresholdAmount = table.Column<decimal>(type: "numeric(12,2)", nullable: false),
                    PayoutCurrency = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    PayoutMethod = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    PayoutDetailsEncrypted = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: false),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Affiliates", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "BankAccountConfigs",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Region = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Currency = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    BankName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    AccountHolderName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Iban = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    SwiftBic = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    AccountNumber = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    RoutingOrSortCode = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    InstructionsMarkdown = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BankAccountConfigs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "BillingMetricDailies",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    MetricDate = table.Column<DateOnly>(type: "date", nullable: false),
                    MetricCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Region = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Currency = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    Value = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    DetailsJson = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    ComputedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BillingMetricDailies", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "BillingNotificationDispatchLogs",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    UserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    EventCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    EventId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    TemplateCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Channel = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    FailureReason = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    SentAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BillingNotificationDispatchLogs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "BillingNotificationTemplates",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Channel = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    LocaleTag = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Subject = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    BodyTemplate = table.Column<string>(type: "character varying(8192)", maxLength: 8192, nullable: false),
                    VariablesJson = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BillingNotificationTemplates", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CancellationIntents",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SubscriptionId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    UserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Reason = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ReasonDetail = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    OfferedCouponCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ResolvedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CancellationIntents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ChurnRiskSnapshots",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    UserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SnapshotDate = table.Column<DateOnly>(type: "date", nullable: false),
                    RiskScore = table.Column<decimal>(type: "numeric(6,4)", nullable: false),
                    RiskBand = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    FactorsJson = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    RecommendedAction = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ActionDispatched = table.Column<bool>(type: "boolean", nullable: false),
                    ComputedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChurnRiskSnapshots", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DeflectionRules",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    TriggerReason = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    OfferedCouponCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    MinTenureDays = table.Column<int>(type: "integer", nullable: false),
                    MaxOffersPerUser = table.Column<int>(type: "integer", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeflectionRules", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DunningCampaigns",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SubscriptionId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    UserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    NextAttemptAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    AttemptCount = table.Column<int>(type: "integer", nullable: false),
                    LastFailureCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    LastFailureReason = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    StepsCompletedCsv = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    RecoveredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CancelledAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DunningCampaigns", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ExchangeRates",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    FromCurrency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    ToCurrency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    Rate = table.Column<decimal>(type: "numeric(18,8)", nullable: false),
                    EffectiveFrom = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Source = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExchangeRates", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ListeningTtsJobs",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ExtractId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    RequestedBy = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    RetryCount = table.Column<int>(type: "integer", nullable: false),
                    ErrorMessage = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    RetryAfter = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ListeningTtsJobs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ManualPaymentRequests",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    UserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    QuoteId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    AmountAmount = table.Column<decimal>(type: "numeric(12,2)", nullable: false),
                    Currency = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    Method = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ProofUrl = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    ProofHashHex = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Reference = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    SubmittedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ReviewedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ReviewedByAdminId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    AdminNotes = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    AccessGrantedSubscriptionId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ManualPaymentRequests", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PaymentMethodUpdateLinks",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    UserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SubscriptionId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Token = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UsedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PaymentMethodUpdateLinks", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PricingExperimentAssignments",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ExperimentId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    UserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    VariantCode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Converted = table.Column<bool>(type: "boolean", nullable: false),
                    ConvertedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ConvertedAmount = table.Column<decimal>(type: "numeric(12,2)", nullable: true),
                    AssignedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PricingExperimentAssignments", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PricingExperiments",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    TargetType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    TargetId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Region = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    RolloutPercent = table.Column<int>(type: "integer", nullable: false),
                    VariantsJson = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    EndedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedByAdminId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PricingExperiments", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ReadinessHistories",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    UserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    WeekStartDate = table.Column<DateOnly>(type: "date", nullable: false),
                    RecordedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Overall = table.Column<decimal>(type: "numeric", nullable: false),
                    Writing = table.Column<decimal>(type: "numeric", nullable: false),
                    Speaking = table.Column<decimal>(type: "numeric", nullable: false),
                    Reading = table.Column<decimal>(type: "numeric", nullable: false),
                    Listening = table.Column<decimal>(type: "numeric", nullable: false),
                    Vocabulary = table.Column<decimal>(type: "numeric", nullable: false),
                    Risk = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false, defaultValue: "Unknown"),
                    TargetDateProbability = table.Column<decimal>(type: "numeric", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReadinessHistories", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Scholarships",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    UserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    GrantedByAdminId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Reason = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    AccessTier = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    EntitlementsJson = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    GrantedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RevokedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RevokedByAdminId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    AdminNotes = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Scholarships", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TaxRules",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Country = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: false),
                    Region = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    TaxType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    RatePercent = table.Column<decimal>(type: "numeric(6,3)", nullable: false),
                    EffectiveFrom = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    EffectiveTo = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ZeroRateForB2BReverseCharge = table.Column<bool>(type: "boolean", nullable: false),
                    IsTaxInclusiveDisplay = table.Column<bool>(type: "boolean", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TaxRules", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "UsageForecastSnapshots",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    UserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SnapshotDate = table.Column<DateOnly>(type: "date", nullable: false),
                    FeatureCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    WindowDays = table.Column<int>(type: "integer", nullable: false),
                    ForecastCalls = table.Column<int>(type: "integer", nullable: false),
                    ForecastCredits = table.Column<int>(type: "integer", nullable: false),
                    ForecastCostUsd = table.Column<decimal>(type: "numeric(12,4)", nullable: false),
                    Ema30DailyCalls = table.Column<decimal>(type: "numeric(12,3)", nullable: false),
                    PerFeatureJson = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: true),
                    SuggestedTopUpCredits = table.Column<int>(type: "integer", nullable: false),
                    ComputedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UsageForecastSnapshots", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ReadingAttempt_User_Paper_Mode_Status",
                table: "ReadingAttempts",
                columns: new[] { "UserId", "PaperId", "Mode", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_ReadingAttempts_UserId_PaperId",
                table: "ReadingAttempts",
                columns: new[] { "UserId", "PaperId" });

            migrationBuilder.CreateIndex(
                name: "IX_ReadingAnswer_AttemptId",
                table: "ReadingAnswers",
                column: "ReadingAttemptId");

            migrationBuilder.CreateIndex(
                name: "IX_ReadinessSnapshots_ExpiresAt",
                table: "ReadinessSnapshots",
                column: "ExpiresAt");

            migrationBuilder.CreateIndex(
                name: "IX_ReadinessSnapshots_UserId_ComputedAt",
                table: "ReadinessSnapshots",
                columns: new[] { "UserId", "ComputedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_AffiliateAttributions_AffiliateId_ConvertedAt",
                table: "AffiliateAttributions",
                columns: new[] { "AffiliateId", "ConvertedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_AffiliateAttributions_UserId_AffiliateId",
                table: "AffiliateAttributions",
                columns: new[] { "UserId", "AffiliateId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AffiliateCommissions_AffiliateId_Status",
                table: "AffiliateCommissions",
                columns: new[] { "AffiliateId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_AffiliateCommissions_PaymentTransactionId",
                table: "AffiliateCommissions",
                column: "PaymentTransactionId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Affiliates_Code",
                table: "Affiliates",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Affiliates_Status",
                table: "Affiliates",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_BankAccountConfigs_Region_Currency_IsActive",
                table: "BankAccountConfigs",
                columns: new[] { "Region", "Currency", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_BillingMetricDailies_MetricCode_MetricDate",
                table: "BillingMetricDailies",
                columns: new[] { "MetricCode", "MetricDate" });

            migrationBuilder.CreateIndex(
                name: "IX_BillingMetricDailies_MetricDate_MetricCode_Region",
                table: "BillingMetricDailies",
                columns: new[] { "MetricDate", "MetricCode", "Region" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BillingNotificationDispatchLogs_UserId_EventCode_EventId_Te~",
                table: "BillingNotificationDispatchLogs",
                columns: new[] { "UserId", "EventCode", "EventId", "TemplateCode" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BillingNotificationDispatchLogs_UserId_SentAt",
                table: "BillingNotificationDispatchLogs",
                columns: new[] { "UserId", "SentAt" });

            migrationBuilder.CreateIndex(
                name: "IX_BillingNotificationTemplates_Code_Channel_LocaleTag",
                table: "BillingNotificationTemplates",
                columns: new[] { "Code", "Channel", "LocaleTag" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CancellationIntents_SubscriptionId_Status",
                table: "CancellationIntents",
                columns: new[] { "SubscriptionId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_ChurnRiskSnapshots_RiskBand_RiskScore",
                table: "ChurnRiskSnapshots",
                columns: new[] { "RiskBand", "RiskScore" });

            migrationBuilder.CreateIndex(
                name: "IX_ChurnRiskSnapshots_SnapshotDate_RiskBand",
                table: "ChurnRiskSnapshots",
                columns: new[] { "SnapshotDate", "RiskBand" });

            migrationBuilder.CreateIndex(
                name: "IX_ChurnRiskSnapshots_UserId_SnapshotDate",
                table: "ChurnRiskSnapshots",
                columns: new[] { "UserId", "SnapshotDate" });

            migrationBuilder.CreateIndex(
                name: "IX_DeflectionRules_TriggerReason_IsActive",
                table: "DeflectionRules",
                columns: new[] { "TriggerReason", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_DunningCampaigns_Status_NextAttemptAt",
                table: "DunningCampaigns",
                columns: new[] { "Status", "NextAttemptAt" });

            migrationBuilder.CreateIndex(
                name: "IX_DunningCampaigns_SubscriptionId_Status",
                table: "DunningCampaigns",
                columns: new[] { "SubscriptionId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_ExchangeRates_EffectiveFrom",
                table: "ExchangeRates",
                column: "EffectiveFrom");

            migrationBuilder.CreateIndex(
                name: "IX_ExchangeRates_FromCurrency_ToCurrency_EffectiveFrom",
                table: "ExchangeRates",
                columns: new[] { "FromCurrency", "ToCurrency", "EffectiveFrom" });

            migrationBuilder.CreateIndex(
                name: "IX_ListeningTtsJobs_ExtractId",
                table: "ListeningTtsJobs",
                column: "ExtractId");

            migrationBuilder.CreateIndex(
                name: "IX_ListeningTtsJobs_Status_CreatedAt",
                table: "ListeningTtsJobs",
                columns: new[] { "Status", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ManualPaymentRequests_ProofHashHex",
                table: "ManualPaymentRequests",
                column: "ProofHashHex");

            migrationBuilder.CreateIndex(
                name: "IX_ManualPaymentRequests_Status_SubmittedAt",
                table: "ManualPaymentRequests",
                columns: new[] { "Status", "SubmittedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ManualPaymentRequests_UserId_Status",
                table: "ManualPaymentRequests",
                columns: new[] { "UserId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_PaymentMethodUpdateLinks_UserId_ExpiresAt",
                table: "PaymentMethodUpdateLinks",
                columns: new[] { "UserId", "ExpiresAt" });

            migrationBuilder.CreateIndex(
                name: "IX_PricingExperimentAssignments_ExperimentId_UserId",
                table: "PricingExperimentAssignments",
                columns: new[] { "ExperimentId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PricingExperimentAssignments_ExperimentId_VariantCode",
                table: "PricingExperimentAssignments",
                columns: new[] { "ExperimentId", "VariantCode" });

            migrationBuilder.CreateIndex(
                name: "IX_PricingExperiments_Status",
                table: "PricingExperiments",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_PricingExperiments_TargetType_TargetId",
                table: "PricingExperiments",
                columns: new[] { "TargetType", "TargetId" });

            migrationBuilder.CreateIndex(
                name: "IX_ReadinessHistories_RecordedAt",
                table: "ReadinessHistories",
                column: "RecordedAt");

            migrationBuilder.CreateIndex(
                name: "IX_ReadinessHistories_UserId_WeekStartDate",
                table: "ReadinessHistories",
                columns: new[] { "UserId", "WeekStartDate" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Scholarships_Status_ExpiresAt",
                table: "Scholarships",
                columns: new[] { "Status", "ExpiresAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Scholarships_UserId_Status",
                table: "Scholarships",
                columns: new[] { "UserId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_TaxRules_Country_EffectiveFrom",
                table: "TaxRules",
                columns: new[] { "Country", "EffectiveFrom" });

            migrationBuilder.CreateIndex(
                name: "IX_TaxRules_Region_IsActive",
                table: "TaxRules",
                columns: new[] { "Region", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_UsageForecastSnapshots_FeatureCode_SnapshotDate",
                table: "UsageForecastSnapshots",
                columns: new[] { "FeatureCode", "SnapshotDate" });

            migrationBuilder.CreateIndex(
                name: "IX_UsageForecastSnapshots_UserId_SnapshotDate",
                table: "UsageForecastSnapshots",
                columns: new[] { "UserId", "SnapshotDate" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AffiliateAttributions");

            migrationBuilder.DropTable(
                name: "AffiliateCommissions");

            migrationBuilder.DropTable(
                name: "Affiliates");

            migrationBuilder.DropTable(
                name: "BankAccountConfigs");

            migrationBuilder.DropTable(
                name: "BillingMetricDailies");

            migrationBuilder.DropTable(
                name: "BillingNotificationDispatchLogs");

            migrationBuilder.DropTable(
                name: "BillingNotificationTemplates");

            migrationBuilder.DropTable(
                name: "CancellationIntents");

            migrationBuilder.DropTable(
                name: "ChurnRiskSnapshots");

            migrationBuilder.DropTable(
                name: "DeflectionRules");

            migrationBuilder.DropTable(
                name: "DunningCampaigns");

            migrationBuilder.DropTable(
                name: "ExchangeRates");

            migrationBuilder.DropTable(
                name: "ListeningTtsJobs");

            migrationBuilder.DropTable(
                name: "ManualPaymentRequests");

            migrationBuilder.DropTable(
                name: "PaymentMethodUpdateLinks");

            migrationBuilder.DropTable(
                name: "PricingExperimentAssignments");

            migrationBuilder.DropTable(
                name: "PricingExperiments");

            migrationBuilder.DropTable(
                name: "ReadinessHistories");

            migrationBuilder.DropTable(
                name: "Scholarships");

            migrationBuilder.DropTable(
                name: "TaxRules");

            migrationBuilder.DropTable(
                name: "UsageForecastSnapshots");

            migrationBuilder.DropIndex(
                name: "IX_ReadingAttempt_User_Paper_Mode_Status",
                table: "ReadingAttempts");

            migrationBuilder.DropIndex(
                name: "IX_ReadingAttempts_UserId_PaperId",
                table: "ReadingAttempts");

            migrationBuilder.DropIndex(
                name: "IX_ReadingAnswer_AttemptId",
                table: "ReadingAnswers");

            migrationBuilder.DropIndex(
                name: "IX_ReadinessSnapshots_ExpiresAt",
                table: "ReadinessSnapshots");

            migrationBuilder.DropIndex(
                name: "IX_ReadinessSnapshots_UserId_ComputedAt",
                table: "ReadinessSnapshots");

            migrationBuilder.DropColumn(
                name: "GracePeriodUntil",
                table: "Subscriptions");

            migrationBuilder.DropColumn(
                name: "PausedUntil",
                table: "Subscriptions");

            migrationBuilder.DropColumn(
                name: "RowVersion",
                table: "ReadingAttempts");

            migrationBuilder.DropColumn(
                name: "CreatedAt",
                table: "ReadingAnswers");

            migrationBuilder.DropColumn(
                name: "UpdatedAt",
                table: "ReadingAnswers");

            migrationBuilder.DropColumn(
                name: "ConfidenceLevel",
                table: "ReadinessSnapshots");

            migrationBuilder.DropColumn(
                name: "DataPointCount",
                table: "ReadinessSnapshots");

            migrationBuilder.DropColumn(
                name: "ExpiresAt",
                table: "ReadinessSnapshots");

            migrationBuilder.DropColumn(
                name: "ListeningReadiness",
                table: "ReadinessSnapshots");

            migrationBuilder.DropColumn(
                name: "OverallReadiness",
                table: "ReadinessSnapshots");

            migrationBuilder.DropColumn(
                name: "OverallRisk",
                table: "ReadinessSnapshots");

            migrationBuilder.DropColumn(
                name: "ReadingReadiness",
                table: "ReadinessSnapshots");

            migrationBuilder.DropColumn(
                name: "RecommendedStudyHoursPerWeek",
                table: "ReadinessSnapshots");

            migrationBuilder.DropColumn(
                name: "SpeakingReadiness",
                table: "ReadinessSnapshots");

            migrationBuilder.DropColumn(
                name: "TargetDateProbability",
                table: "ReadinessSnapshots");

            migrationBuilder.DropColumn(
                name: "VocabularyReadiness",
                table: "ReadinessSnapshots");

            migrationBuilder.DropColumn(
                name: "WeakestSubtest",
                table: "ReadinessSnapshots");

            migrationBuilder.DropColumn(
                name: "WritingReadiness",
                table: "ReadinessSnapshots");

            migrationBuilder.DropColumn(
                name: "AudioContentSha",
                table: "ListeningExtracts");

            migrationBuilder.DropColumn(
                name: "CouponVariant",
                table: "BillingCoupons");

            migrationBuilder.DropColumn(
                name: "EligibleCountriesJson",
                table: "BillingCoupons");

            migrationBuilder.DropColumn(
                name: "ExistingUsersOnly",
                table: "BillingCoupons");

            migrationBuilder.DropColumn(
                name: "NewUsersOnly",
                table: "BillingCoupons");

            migrationBuilder.DropColumn(
                name: "StackableWithAffiliate",
                table: "BillingCoupons");

            migrationBuilder.DropColumn(
                name: "StackableWithReferral",
                table: "BillingCoupons");

            migrationBuilder.DropColumn(
                name: "VariantMetadataJson",
                table: "BillingCoupons");

            migrationBuilder.AlterColumn<string>(
                name: "BodyHtml",
                table: "ReadingTexts",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(65536)",
                oldMaxLength: 65536);

            migrationBuilder.AlterColumn<string>(
                name: "OptionsJson",
                table: "ReadingQuestions",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(4096)",
                oldMaxLength: 4096);

            migrationBuilder.AlterColumn<string>(
                name: "OptionDistractorsJson",
                table: "ReadingQuestions",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(2048)",
                oldMaxLength: 2048,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "CorrectAnswerJson",
                table: "ReadingQuestions",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(512)",
                oldMaxLength: 512);

            migrationBuilder.AlterColumn<string>(
                name: "AcceptedSynonymsJson",
                table: "ReadingQuestions",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(4096)",
                oldMaxLength: 4096,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "ScopeJson",
                table: "ReadingAttempts",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(8192)",
                oldMaxLength: 8192,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "PolicySnapshotJson",
                table: "ReadingAttempts",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(16384)",
                oldMaxLength: 16384);

            migrationBuilder.AlterColumn<string>(
                name: "UserAnswerJson",
                table: "ReadingAnswers",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(2048)",
                oldMaxLength: 2048);

            migrationBuilder.CreateIndex(
                name: "IX_BillingQuotes_ExperimentAssignmentId",
                table: "BillingQuotes",
                column: "ExperimentAssignmentId");
        }
    }
}
