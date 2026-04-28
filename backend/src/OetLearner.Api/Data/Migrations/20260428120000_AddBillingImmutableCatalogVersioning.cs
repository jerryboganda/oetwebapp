using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OetLearner.Api.Data.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(LearnerDbContext))]
    [Migration("20260428120000_AddBillingImmutableCatalogVersioning")]
    public partial class AddBillingImmutableCatalogVersioning : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ActiveVersionId",
                table: "BillingPlans",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LatestVersionId",
                table: "BillingPlans",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ActiveVersionId",
                table: "BillingAddOns",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LatestVersionId",
                table: "BillingAddOns",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ActiveVersionId",
                table: "BillingCoupons",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LatestVersionId",
                table: "BillingCoupons",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PlanVersionId",
                table: "BillingQuotes",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AddOnVersionIdsJson",
                table: "BillingQuotes",
                type: "character varying(1024)",
                maxLength: 1024,
                nullable: false,
                defaultValue: "{}");

            migrationBuilder.AddColumn<string>(
                name: "CouponVersionId",
                table: "BillingQuotes",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CouponId",
                table: "BillingCouponRedemptions",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CouponVersionId",
                table: "BillingCouponRedemptions",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PlanVersionId",
                table: "Subscriptions",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AddOnVersionId",
                table: "SubscriptionItems",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PlanVersionId",
                table: "Invoices",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AddOnVersionIdsJson",
                table: "Invoices",
                type: "character varying(1024)",
                maxLength: 1024,
                nullable: false,
                defaultValue: "{}");

            migrationBuilder.AddColumn<string>(
                name: "CouponVersionId",
                table: "Invoices",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "QuoteId",
                table: "Invoices",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CheckoutSessionId",
                table: "Invoices",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "QuoteId",
                table: "PaymentTransactions",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PlanVersionId",
                table: "PaymentTransactions",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AddOnVersionIdsJson",
                table: "PaymentTransactions",
                type: "character varying(1024)",
                maxLength: 1024,
                nullable: false,
                defaultValue: "{}");

            migrationBuilder.AddColumn<string>(
                name: "CouponVersionId",
                table: "PaymentTransactions",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "BillingPlanVersions",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    PlanId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    VersionNumber = table.Column<int>(type: "integer", nullable: false),
                    Code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Description = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    Price = table.Column<decimal>(type: "numeric", nullable: false),
                    Currency = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    Interval = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    DurationMonths = table.Column<int>(type: "integer", nullable: false),
                    IsVisible = table.Column<bool>(type: "boolean", nullable: false),
                    IsRenewable = table.Column<bool>(type: "boolean", nullable: false),
                    TrialDays = table.Column<int>(type: "integer", nullable: false),
                    DisplayOrder = table.Column<int>(type: "integer", nullable: false),
                    IncludedCredits = table.Column<int>(type: "integer", nullable: false),
                    IncludedSubtestsJson = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    EntitlementsJson = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    ArchivedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedByAdminId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    CreatedByAdminName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BillingPlanVersions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "BillingAddOnVersions",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    AddOnId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    VersionNumber = table.Column<int>(type: "integer", nullable: false),
                    Code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Description = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    Price = table.Column<decimal>(type: "numeric", nullable: false),
                    Currency = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    Interval = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    IsRecurring = table.Column<bool>(type: "boolean", nullable: false),
                    DurationDays = table.Column<int>(type: "integer", nullable: false),
                    GrantCredits = table.Column<int>(type: "integer", nullable: false),
                    GrantEntitlementsJson = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    CompatiblePlanCodesJson = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    AppliesToAllPlans = table.Column<bool>(type: "boolean", nullable: false),
                    IsStackable = table.Column<bool>(type: "boolean", nullable: false),
                    QuantityStep = table.Column<int>(type: "integer", nullable: false),
                    MaxQuantity = table.Column<int>(type: "integer", nullable: true),
                    DisplayOrder = table.Column<int>(type: "integer", nullable: false),
                    CreatedByAdminId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    CreatedByAdminName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BillingAddOnVersions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "BillingCouponVersions",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CouponId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    VersionNumber = table.Column<int>(type: "integer", nullable: false),
                    Code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Description = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    DiscountType = table.Column<int>(type: "integer", nullable: false),
                    DiscountValue = table.Column<decimal>(type: "numeric", nullable: false),
                    Currency = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    StartsAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    EndsAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    UsageLimitTotal = table.Column<int>(type: "integer", nullable: true),
                    UsageLimitPerUser = table.Column<int>(type: "integer", nullable: true),
                    MinimumSubtotal = table.Column<decimal>(type: "numeric", nullable: true),
                    ApplicablePlanCodesJson = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    ApplicableAddOnCodesJson = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    IsStackable = table.Column<bool>(type: "boolean", nullable: false),
                    Notes = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    CreatedByAdminId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    CreatedByAdminName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BillingCouponVersions", x => x.Id);
                });

            migrationBuilder.Sql("""
                INSERT INTO "BillingPlanVersions" (
                    "Id", "PlanId", "VersionNumber", "Code", "Name", "Description", "Price", "Currency", "Interval",
                    "DurationMonths", "IsVisible", "IsRenewable", "TrialDays", "DisplayOrder", "IncludedCredits",
                    "IncludedSubtestsJson", "EntitlementsJson", "Status", "ArchivedAt", "CreatedAt")
                SELECT
                    'plan-version-' || substr(md5(p."Id"), 1, 32), p."Id", 1, p."Code", p."Name", p."Description", p."Price", p."Currency", p."Interval",
                    p."DurationMonths", p."IsVisible", p."IsRenewable", p."TrialDays", p."DisplayOrder", p."IncludedCredits",
                    p."IncludedSubtestsJson", p."EntitlementsJson", p."Status", p."ArchivedAt", p."CreatedAt"
                FROM "BillingPlans" p
                WHERE NOT EXISTS (SELECT 1 FROM "BillingPlanVersions" v WHERE v."PlanId" = p."Id");

                UPDATE "BillingPlans" p
                SET "ActiveVersionId" = v."Id", "LatestVersionId" = v."Id"
                FROM "BillingPlanVersions" v
                WHERE v."PlanId" = p."Id" AND v."VersionNumber" = 1
                  AND p."ActiveVersionId" IS NULL AND p."LatestVersionId" IS NULL;

                INSERT INTO "BillingAddOnVersions" (
                    "Id", "AddOnId", "VersionNumber", "Code", "Name", "Description", "Price", "Currency", "Interval", "Status",
                    "IsRecurring", "DurationDays", "GrantCredits", "GrantEntitlementsJson", "CompatiblePlanCodesJson",
                    "AppliesToAllPlans", "IsStackable", "QuantityStep", "MaxQuantity", "DisplayOrder", "CreatedAt")
                SELECT
                    'addon-version-' || substr(md5(a."Id"), 1, 32), a."Id", 1, a."Code", a."Name", a."Description", a."Price", a."Currency", a."Interval", a."Status",
                    a."IsRecurring", a."DurationDays", a."GrantCredits", a."GrantEntitlementsJson", a."CompatiblePlanCodesJson",
                    a."AppliesToAllPlans", a."IsStackable", a."QuantityStep", a."MaxQuantity", a."DisplayOrder", a."CreatedAt"
                FROM "BillingAddOns" a
                WHERE NOT EXISTS (SELECT 1 FROM "BillingAddOnVersions" v WHERE v."AddOnId" = a."Id");

                UPDATE "BillingAddOns" a
                SET "ActiveVersionId" = v."Id", "LatestVersionId" = v."Id"
                FROM "BillingAddOnVersions" v
                WHERE v."AddOnId" = a."Id" AND v."VersionNumber" = 1
                  AND a."ActiveVersionId" IS NULL AND a."LatestVersionId" IS NULL;

                INSERT INTO "BillingCouponVersions" (
                    "Id", "CouponId", "VersionNumber", "Code", "Name", "Description", "DiscountType", "DiscountValue", "Currency", "Status",
                    "StartsAt", "EndsAt", "UsageLimitTotal", "UsageLimitPerUser", "MinimumSubtotal", "ApplicablePlanCodesJson",
                    "ApplicableAddOnCodesJson", "IsStackable", "Notes", "CreatedAt")
                SELECT
                    'coupon-version-' || substr(md5(c."Id"), 1, 32), c."Id", 1, c."Code", c."Name", c."Description", c."DiscountType", c."DiscountValue", c."Currency", c."Status",
                    c."StartsAt", c."EndsAt", c."UsageLimitTotal", c."UsageLimitPerUser", c."MinimumSubtotal", c."ApplicablePlanCodesJson",
                    c."ApplicableAddOnCodesJson", c."IsStackable", c."Notes", c."CreatedAt"
                FROM "BillingCoupons" c
                WHERE NOT EXISTS (SELECT 1 FROM "BillingCouponVersions" v WHERE v."CouponId" = c."Id");

                UPDATE "BillingCoupons" c
                SET "ActiveVersionId" = v."Id", "LatestVersionId" = v."Id"
                FROM "BillingCouponVersions" v
                WHERE v."CouponId" = c."Id" AND v."VersionNumber" = 1
                  AND c."ActiveVersionId" IS NULL AND c."LatestVersionId" IS NULL;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_BillingPlanVersions_Code",
                table: "BillingPlanVersions",
                column: "Code");

            migrationBuilder.CreateIndex(
                name: "IX_BillingPlanVersions_PlanId_VersionNumber",
                table: "BillingPlanVersions",
                columns: new[] { "PlanId", "VersionNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BillingAddOnVersions_Code",
                table: "BillingAddOnVersions",
                column: "Code");

            migrationBuilder.CreateIndex(
                name: "IX_BillingAddOnVersions_AddOnId_VersionNumber",
                table: "BillingAddOnVersions",
                columns: new[] { "AddOnId", "VersionNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BillingCouponVersions_Code",
                table: "BillingCouponVersions",
                column: "Code");

            migrationBuilder.CreateIndex(
                name: "IX_BillingCouponVersions_CouponId_VersionNumber",
                table: "BillingCouponVersions",
                columns: new[] { "CouponId", "VersionNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionItems_AddOnVersionId",
                table: "SubscriptionItems",
                column: "AddOnVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_BillingCouponRedemptions_CouponId_UserId_RedeemedAt",
                table: "BillingCouponRedemptions",
                columns: new[] { "CouponId", "UserId", "RedeemedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_BillingCouponRedemptions_CouponVersionId",
                table: "BillingCouponRedemptions",
                column: "CouponVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_BillingQuotes_PlanVersionId",
                table: "BillingQuotes",
                column: "PlanVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_BillingQuotes_CouponVersionId",
                table: "BillingQuotes",
                column: "CouponVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_Subscriptions_PlanVersionId",
                table: "Subscriptions",
                column: "PlanVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_Invoices_PlanVersionId",
                table: "Invoices",
                column: "PlanVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_Invoices_QuoteId",
                table: "Invoices",
                column: "QuoteId");

            migrationBuilder.CreateIndex(
                name: "IX_Invoices_CheckoutSessionId",
                table: "Invoices",
                column: "CheckoutSessionId");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentTransactions_QuoteId",
                table: "PaymentTransactions",
                column: "QuoteId");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentTransactions_PlanVersionId",
                table: "PaymentTransactions",
                column: "PlanVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentTransactions_CouponVersionId",
                table: "PaymentTransactions",
                column: "CouponVersionId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(name: "IX_PaymentTransactions_CouponVersionId", table: "PaymentTransactions");
            migrationBuilder.DropIndex(name: "IX_PaymentTransactions_PlanVersionId", table: "PaymentTransactions");
            migrationBuilder.DropIndex(name: "IX_PaymentTransactions_QuoteId", table: "PaymentTransactions");
            migrationBuilder.DropIndex(name: "IX_Invoices_CheckoutSessionId", table: "Invoices");
            migrationBuilder.DropIndex(name: "IX_Invoices_QuoteId", table: "Invoices");
            migrationBuilder.DropIndex(name: "IX_Invoices_PlanVersionId", table: "Invoices");
            migrationBuilder.DropIndex(name: "IX_Subscriptions_PlanVersionId", table: "Subscriptions");
            migrationBuilder.DropIndex(name: "IX_BillingQuotes_CouponVersionId", table: "BillingQuotes");
            migrationBuilder.DropIndex(name: "IX_BillingQuotes_PlanVersionId", table: "BillingQuotes");
            migrationBuilder.DropIndex(name: "IX_BillingCouponRedemptions_CouponVersionId", table: "BillingCouponRedemptions");
            migrationBuilder.DropIndex(name: "IX_BillingCouponRedemptions_CouponId_UserId_RedeemedAt", table: "BillingCouponRedemptions");
            migrationBuilder.DropIndex(name: "IX_SubscriptionItems_AddOnVersionId", table: "SubscriptionItems");

            migrationBuilder.DropTable(name: "BillingCouponVersions");
            migrationBuilder.DropTable(name: "BillingAddOnVersions");
            migrationBuilder.DropTable(name: "BillingPlanVersions");

            migrationBuilder.DropColumn(name: "CouponVersionId", table: "PaymentTransactions");
            migrationBuilder.DropColumn(name: "AddOnVersionIdsJson", table: "PaymentTransactions");
            migrationBuilder.DropColumn(name: "PlanVersionId", table: "PaymentTransactions");
            migrationBuilder.DropColumn(name: "QuoteId", table: "PaymentTransactions");
            migrationBuilder.DropColumn(name: "CheckoutSessionId", table: "Invoices");
            migrationBuilder.DropColumn(name: "QuoteId", table: "Invoices");
            migrationBuilder.DropColumn(name: "CouponVersionId", table: "Invoices");
            migrationBuilder.DropColumn(name: "AddOnVersionIdsJson", table: "Invoices");
            migrationBuilder.DropColumn(name: "PlanVersionId", table: "Invoices");
            migrationBuilder.DropColumn(name: "AddOnVersionId", table: "SubscriptionItems");
            migrationBuilder.DropColumn(name: "PlanVersionId", table: "Subscriptions");
            migrationBuilder.DropColumn(name: "CouponVersionId", table: "BillingCouponRedemptions");
            migrationBuilder.DropColumn(name: "CouponId", table: "BillingCouponRedemptions");
            migrationBuilder.DropColumn(name: "CouponVersionId", table: "BillingQuotes");
            migrationBuilder.DropColumn(name: "AddOnVersionIdsJson", table: "BillingQuotes");
            migrationBuilder.DropColumn(name: "PlanVersionId", table: "BillingQuotes");
            migrationBuilder.DropColumn(name: "LatestVersionId", table: "BillingCoupons");
            migrationBuilder.DropColumn(name: "ActiveVersionId", table: "BillingCoupons");
            migrationBuilder.DropColumn(name: "LatestVersionId", table: "BillingAddOns");
            migrationBuilder.DropColumn(name: "ActiveVersionId", table: "BillingAddOns");
            migrationBuilder.DropColumn(name: "LatestVersionId", table: "BillingPlans");
            migrationBuilder.DropColumn(name: "ActiveVersionId", table: "BillingPlans");
        }
    }
}