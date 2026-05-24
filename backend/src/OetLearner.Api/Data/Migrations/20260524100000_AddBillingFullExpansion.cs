using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using OetLearner.Api.Data;

#nullable disable

namespace OetLearner.Api.Data.Migrations
{
    /// <summary>
    /// Phases 2-10 of the international billing expansion roadmap. Adds all
    /// remaining schema: tax rules, manual-payment workflow, dunning, pause +
    /// cancellation deflection, scholarship, affiliate program, notification
    /// templates + dispatch log, billing metrics rollup, plus coupon scope
    /// fields and Subscription.PausedUntil / GracePeriodUntil columns.
    ///
    /// Hand-authored to avoid absorbing unrelated EF snapshot drift; matches
    /// the pattern used by <c>20260522100000_AddSpeakingDriftColumns</c> and
    /// <c>20260523200000_AddBillingRegionFoundation</c>.
    /// </summary>
    [DbContext(typeof(LearnerDbContext))]
    [Migration("20260524100000_AddBillingFullExpansion")]
    public partial class AddBillingFullExpansion : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ── Subscription column additions (Phase 5 + 6) ─────────────────
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "PausedUntil", table: "Subscriptions",
                type: "timestamp with time zone", nullable: true);
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "GracePeriodUntil", table: "Subscriptions",
                type: "timestamp with time zone", nullable: true);

            // ── BillingCoupon scope extensions (Phase 7) ────────────────────
            migrationBuilder.AddColumn<string>(
                name: "CouponVariant", table: "BillingCoupons",
                type: "character varying(32)", maxLength: 32, nullable: false, defaultValue: "percent_off");
            migrationBuilder.AddColumn<string>(
                name: "VariantMetadataJson", table: "BillingCoupons",
                type: "character varying(2048)", maxLength: 2048, nullable: false, defaultValue: "{}");
            migrationBuilder.AddColumn<string>(
                name: "EligibleCountriesJson", table: "BillingCoupons",
                type: "character varying(1024)", maxLength: 1024, nullable: false, defaultValue: "[]");
            migrationBuilder.AddColumn<bool>(
                name: "NewUsersOnly", table: "BillingCoupons",
                type: "boolean", nullable: false, defaultValue: false);
            migrationBuilder.AddColumn<bool>(
                name: "ExistingUsersOnly", table: "BillingCoupons",
                type: "boolean", nullable: false, defaultValue: false);
            migrationBuilder.AddColumn<bool>(
                name: "StackableWithReferral", table: "BillingCoupons",
                type: "boolean", nullable: false, defaultValue: false);
            migrationBuilder.AddColumn<bool>(
                name: "StackableWithAffiliate", table: "BillingCoupons",
                type: "boolean", nullable: false, defaultValue: false);

            // ── Phase 3: TaxRules ───────────────────────────────────────────
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
                    ZeroRateForB2BReverseCharge = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    IsTaxInclusiveDisplay = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                },
                constraints: table => table.PrimaryKey("PK_TaxRules", x => x.Id));
            migrationBuilder.CreateIndex(name: "IX_TaxRules_Country_EffectiveFrom", table: "TaxRules", columns: new[] { "Country", "EffectiveFrom" });
            migrationBuilder.CreateIndex(name: "IX_TaxRules_Region_IsActive", table: "TaxRules", columns: new[] { "Region", "IsActive" });

            // ── Phase 4: ManualPaymentRequests + BankAccountConfigs ─────────
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
                    ProofUrl = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false, defaultValue: ""),
                    ProofHashHex = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false, defaultValue: ""),
                    Reference = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false, defaultValue: ""),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false, defaultValue: "pending"),
                    SubmittedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ReviewedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ReviewedByAdminId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    AdminNotes = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    AccessGrantedSubscriptionId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                },
                constraints: table => table.PrimaryKey("PK_ManualPaymentRequests", x => x.Id));
            migrationBuilder.CreateIndex(name: "IX_ManualPaymentRequests_UserId_Status", table: "ManualPaymentRequests", columns: new[] { "UserId", "Status" });
            migrationBuilder.CreateIndex(name: "IX_ManualPaymentRequests_ProofHashHex", table: "ManualPaymentRequests", column: "ProofHashHex");
            migrationBuilder.CreateIndex(name: "IX_ManualPaymentRequests_Status_SubmittedAt", table: "ManualPaymentRequests", columns: new[] { "Status", "SubmittedAt" });

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
                    IsActive = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                },
                constraints: table => table.PrimaryKey("PK_BankAccountConfigs", x => x.Id));
            migrationBuilder.CreateIndex(name: "IX_BankAccountConfigs_Region_Currency_IsActive", table: "BankAccountConfigs", columns: new[] { "Region", "Currency", "IsActive" });

            // ── Phase 5: DunningCampaigns + PaymentMethodUpdateLinks ────────
            migrationBuilder.CreateTable(
                name: "DunningCampaigns",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SubscriptionId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    UserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false, defaultValue: "active"),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    NextAttemptAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    AttemptCount = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    LastFailureCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    LastFailureReason = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    StepsCompletedCsv = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false, defaultValue: ""),
                    RecoveredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CancelledAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                },
                constraints: table => table.PrimaryKey("PK_DunningCampaigns", x => x.Id));
            migrationBuilder.CreateIndex(name: "IX_DunningCampaigns_SubscriptionId_Status", table: "DunningCampaigns", columns: new[] { "SubscriptionId", "Status" });
            migrationBuilder.CreateIndex(name: "IX_DunningCampaigns_Status_NextAttemptAt", table: "DunningCampaigns", columns: new[] { "Status", "NextAttemptAt" });

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
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                },
                constraints: table => table.PrimaryKey("PK_PaymentMethodUpdateLinks", x => x.Id));
            migrationBuilder.CreateIndex(name: "IX_PaymentMethodUpdateLinks_UserId_ExpiresAt", table: "PaymentMethodUpdateLinks", columns: new[] { "UserId", "ExpiresAt" });

            // ── Phase 6: CancellationIntents + DeflectionRules ──────────────
            migrationBuilder.CreateTable(
                name: "CancellationIntents",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SubscriptionId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    UserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Reason = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ReasonDetail = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false, defaultValue: "started"),
                    OfferedCouponCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ResolvedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                },
                constraints: table => table.PrimaryKey("PK_CancellationIntents", x => x.Id));
            migrationBuilder.CreateIndex(name: "IX_CancellationIntents_SubscriptionId_Status", table: "CancellationIntents", columns: new[] { "SubscriptionId", "Status" });

            migrationBuilder.CreateTable(
                name: "DeflectionRules",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    TriggerReason = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    OfferedCouponCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    MinTenureDays = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    MaxOffersPerUser = table.Column<int>(type: "integer", nullable: false, defaultValue: 1),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                },
                constraints: table => table.PrimaryKey("PK_DeflectionRules", x => x.Id));
            migrationBuilder.CreateIndex(name: "IX_DeflectionRules_TriggerReason_IsActive", table: "DeflectionRules", columns: new[] { "TriggerReason", "IsActive" });

            // ── Phase 7: Scholarships ───────────────────────────────────────
            migrationBuilder.CreateTable(
                name: "Scholarships",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    UserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    GrantedByAdminId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Reason = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    AccessTier = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    EntitlementsJson = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false, defaultValue: "{}"),
                    GrantedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RevokedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RevokedByAdminId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    AdminNotes = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false, defaultValue: "active"),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                },
                constraints: table => table.PrimaryKey("PK_Scholarships", x => x.Id));
            migrationBuilder.CreateIndex(name: "IX_Scholarships_UserId_Status", table: "Scholarships", columns: new[] { "UserId", "Status" });
            migrationBuilder.CreateIndex(name: "IX_Scholarships_Status_ExpiresAt", table: "Scholarships", columns: new[] { "Status", "ExpiresAt" });

            // ── Phase 8: Affiliates + Attribution + Commission ──────────────
            migrationBuilder.CreateTable(
                name: "Affiliates",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    OwnerName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ContactEmail = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    CommissionPercent = table.Column<decimal>(type: "numeric(6,3)", nullable: false),
                    CookieDays = table.Column<int>(type: "integer", nullable: false, defaultValue: 30),
                    PayoutThresholdAmount = table.Column<decimal>(type: "numeric(12,2)", nullable: false),
                    PayoutCurrency = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false, defaultValue: "USD"),
                    PayoutMethod = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false, defaultValue: "bank_transfer"),
                    PayoutDetailsEncrypted = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: false, defaultValue: ""),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false, defaultValue: "active"),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                },
                constraints: table => table.PrimaryKey("PK_Affiliates", x => x.Id));
            migrationBuilder.CreateIndex(name: "IX_Affiliates_Code", table: "Affiliates", column: "Code", unique: true);
            migrationBuilder.CreateIndex(name: "IX_Affiliates_Status", table: "Affiliates", column: "Status");

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
                    FirstPaymentTransactionId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                },
                constraints: table => table.PrimaryKey("PK_AffiliateAttributions", x => x.Id));
            migrationBuilder.CreateIndex(name: "IX_AffiliateAttributions_UserId_AffiliateId", table: "AffiliateAttributions", columns: new[] { "UserId", "AffiliateId" }, unique: true);
            migrationBuilder.CreateIndex(name: "IX_AffiliateAttributions_AffiliateId_ConvertedAt", table: "AffiliateAttributions", columns: new[] { "AffiliateId", "ConvertedAt" });

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
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false, defaultValue: "accrued"),
                    AccruedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    PaidAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ReversedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    PayoutBatchId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                },
                constraints: table => table.PrimaryKey("PK_AffiliateCommissions", x => x.Id));
            migrationBuilder.CreateIndex(name: "IX_AffiliateCommissions_AffiliateId_Status", table: "AffiliateCommissions", columns: new[] { "AffiliateId", "Status" });
            migrationBuilder.CreateIndex(name: "IX_AffiliateCommissions_PaymentTransactionId", table: "AffiliateCommissions", column: "PaymentTransactionId", unique: true);

            // ── Phase 9: BillingNotificationTemplates + DispatchLogs ────────
            migrationBuilder.CreateTable(
                name: "BillingNotificationTemplates",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Channel = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    LocaleTag = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false, defaultValue: "en"),
                    Subject = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    BodyTemplate = table.Column<string>(type: "character varying(8192)", maxLength: 8192, nullable: false, defaultValue: ""),
                    VariablesJson = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false, defaultValue: "[]"),
                    Version = table.Column<int>(type: "integer", nullable: false, defaultValue: 1),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                },
                constraints: table => table.PrimaryKey("PK_BillingNotificationTemplates", x => x.Id));
            migrationBuilder.CreateIndex(
                name: "IX_BillingNotificationTemplates_Code_Channel_LocaleTag",
                table: "BillingNotificationTemplates",
                columns: new[] { "Code", "Channel", "LocaleTag" },
                unique: true);

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
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false, defaultValue: "queued"),
                    FailureReason = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    SentAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                },
                constraints: table => table.PrimaryKey("PK_BillingNotificationDispatchLogs", x => x.Id));
            migrationBuilder.CreateIndex(
                name: "IX_BillingNotificationDispatchLogs_Idempotency",
                table: "BillingNotificationDispatchLogs",
                columns: new[] { "UserId", "EventCode", "EventId", "TemplateCode" },
                unique: true);
            migrationBuilder.CreateIndex(
                name: "IX_BillingNotificationDispatchLogs_UserId_SentAt",
                table: "BillingNotificationDispatchLogs",
                columns: new[] { "UserId", "SentAt" });

            // ── Phase 10: BillingMetricDailies ──────────────────────────────
            migrationBuilder.CreateTable(
                name: "BillingMetricDailies",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    MetricDate = table.Column<DateOnly>(type: "date", nullable: false),
                    MetricCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Region = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false, defaultValue: "GLOBAL"),
                    Currency = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false, defaultValue: "AUD"),
                    Value = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    DetailsJson = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    ComputedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                },
                constraints: table => table.PrimaryKey("PK_BillingMetricDailies", x => x.Id));
            migrationBuilder.CreateIndex(
                name: "IX_BillingMetricDailies_Date_Code_Region",
                table: "BillingMetricDailies",
                columns: new[] { "MetricDate", "MetricCode", "Region" },
                unique: true);
            migrationBuilder.CreateIndex(
                name: "IX_BillingMetricDailies_MetricCode_MetricDate",
                table: "BillingMetricDailies",
                columns: new[] { "MetricCode", "MetricDate" });

            // ── Phase 2-3: seed gateway routes + tax rules ──────────────────
            var ts = "TIMESTAMP '2026-05-24 00:00:00+00'";

            // PayTabs (Gulf + Egypt) seed routes.
            migrationBuilder.Sql($@"
                INSERT INTO ""GatewayRoutingConfigs"" (""Id"", ""Region"", ""Currency"", ""ProductType"", ""GatewayName"", ""Priority"", ""IsEnabled"", ""CreatedAt"", ""UpdatedAt"")
                VALUES
                  ('seed_gulf_aed_paytabs',  'GULF', 'AED', '*', 'paytabs',     10, TRUE, {ts}, {ts}),
                  ('seed_gulf_sar_paytabs',  'GULF', 'SAR', '*', 'paytabs',     10, TRUE, {ts}, {ts}),
                  ('seed_gulf_omr_paytabs',  'GULF', 'OMR', '*', 'paytabs',     10, TRUE, {ts}, {ts}),
                  ('seed_gulf_qar_paytabs',  'GULF', 'QAR', '*', 'paytabs',     10, TRUE, {ts}, {ts}),
                  ('seed_gulf_kwd_paytabs',  'GULF', 'KWD', '*', 'paytabs',     10, TRUE, {ts}, {ts}),
                  ('seed_gulf_bhd_paytabs',  'GULF', 'BHD', '*', 'paytabs',     10, TRUE, {ts}, {ts}),
                  ('seed_gulf_any_cko',      'GULF', '*',   '*', 'checkoutcom', 20, TRUE, {ts}, {ts}),
                  ('seed_egypt_egp_paymob',  'EGYPT', 'EGP', '*', 'paymob',     10, TRUE, {ts}, {ts}),
                  ('seed_egypt_egp_paytabs', 'EGYPT', 'EGP', '*', 'paytabs',    20, TRUE, {ts}, {ts}),
                  ('seed_egypt_any_cko',     'EGYPT', '*',   '*', 'checkoutcom', 30, TRUE, {ts}, {ts}),
                  ('seed_uk_gbp_stripe',     'UK',    'GBP', '*', 'stripe',     5,  TRUE, {ts}, {ts});
            ");

            // Seed tax rates for the launch regions. Production tax authority APIs
            // remain authoritative; these rows back the local TaxResolver when
            // Stripe Tax / external services are unavailable.
            migrationBuilder.Sql($@"
                INSERT INTO ""TaxRules"" (""Id"", ""Country"", ""Region"", ""TaxType"", ""DisplayName"", ""RatePercent"", ""EffectiveFrom"", ""EffectiveTo"", ""ZeroRateForB2BReverseCharge"", ""IsTaxInclusiveDisplay"", ""IsActive"", ""CreatedAt"", ""UpdatedAt"")
                VALUES
                  ('tax_gb_vat', 'GB', 'UK',    'vat', 'UK VAT',    20.000, {ts}, NULL, TRUE,  TRUE,  TRUE, {ts}, {ts}),
                  ('tax_ae_vat', 'AE', 'GULF',  'vat', 'UAE VAT',    5.000, {ts}, NULL, TRUE,  FALSE, TRUE, {ts}, {ts}),
                  ('tax_sa_vat', 'SA', 'GULF',  'vat', 'Saudi VAT', 15.000, {ts}, NULL, TRUE,  FALSE, TRUE, {ts}, {ts}),
                  ('tax_om_vat', 'OM', 'GULF',  'vat', 'Oman VAT',   5.000, {ts}, NULL, TRUE,  FALSE, TRUE, {ts}, {ts}),
                  ('tax_bh_vat', 'BH', 'GULF',  'vat', 'Bahrain VAT',10.000,{ts}, NULL, TRUE,  FALSE, TRUE, {ts}, {ts}),
                  ('tax_eg_vat', 'EG', 'EGYPT', 'vat', 'Egypt VAT', 14.000, {ts}, NULL, TRUE,  FALSE, TRUE, {ts}, {ts});
            ");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "BillingMetricDailies");
            migrationBuilder.DropTable(name: "BillingNotificationDispatchLogs");
            migrationBuilder.DropTable(name: "BillingNotificationTemplates");
            migrationBuilder.DropTable(name: "AffiliateCommissions");
            migrationBuilder.DropTable(name: "AffiliateAttributions");
            migrationBuilder.DropTable(name: "Affiliates");
            migrationBuilder.DropTable(name: "Scholarships");
            migrationBuilder.DropTable(name: "DeflectionRules");
            migrationBuilder.DropTable(name: "CancellationIntents");
            migrationBuilder.DropTable(name: "PaymentMethodUpdateLinks");
            migrationBuilder.DropTable(name: "DunningCampaigns");
            migrationBuilder.DropTable(name: "BankAccountConfigs");
            migrationBuilder.DropTable(name: "ManualPaymentRequests");
            migrationBuilder.DropTable(name: "TaxRules");

            migrationBuilder.DropColumn(name: "StackableWithAffiliate", table: "BillingCoupons");
            migrationBuilder.DropColumn(name: "StackableWithReferral", table: "BillingCoupons");
            migrationBuilder.DropColumn(name: "ExistingUsersOnly", table: "BillingCoupons");
            migrationBuilder.DropColumn(name: "NewUsersOnly", table: "BillingCoupons");
            migrationBuilder.DropColumn(name: "EligibleCountriesJson", table: "BillingCoupons");
            migrationBuilder.DropColumn(name: "VariantMetadataJson", table: "BillingCoupons");
            migrationBuilder.DropColumn(name: "CouponVariant", table: "BillingCoupons");

            migrationBuilder.DropColumn(name: "GracePeriodUntil", table: "Subscriptions");
            migrationBuilder.DropColumn(name: "PausedUntil", table: "Subscriptions");
        }
    }
}
