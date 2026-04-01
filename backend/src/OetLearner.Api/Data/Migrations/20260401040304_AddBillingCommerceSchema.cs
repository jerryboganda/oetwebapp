using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OetLearner.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddBillingCommerceSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ArchivedAt",
                table: "BillingPlans",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Code",
                table: "BillingPlans",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.Sql("""
                UPDATE "BillingPlans"
                SET "Code" = 'basic-monthly'
                WHERE "Id" = 'plan-basic-monthly';

                UPDATE "BillingPlans"
                SET "Code" = 'premium-monthly'
                WHERE "Id" = 'plan-premium-monthly';

                UPDATE "BillingPlans"
                SET "Code" = 'premium-yearly'
                WHERE "Id" = 'plan-premium-yearly';

                UPDATE "BillingPlans"
                SET "Code" = 'intensive-monthly'
                WHERE "Id" = 'plan-intensive-monthly';

                UPDATE "BillingPlans"
                SET "Code" = 'legacy-trial'
                WHERE "Id" = 'plan-legacy-trial';
                """);

            migrationBuilder.AddColumn<string>(
                name: "Description",
                table: "BillingPlans",
                type: "character varying(1024)",
                maxLength: 1024,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "DisplayOrder",
                table: "BillingPlans",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "DurationMonths",
                table: "BillingPlans",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "EntitlementsJson",
                table: "BillingPlans",
                type: "character varying(2048)",
                maxLength: 2048,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "IncludedCredits",
                table: "BillingPlans",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "IncludedSubtestsJson",
                table: "BillingPlans",
                type: "character varying(2048)",
                maxLength: 2048,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<bool>(
                name: "IsRenewable",
                table: "BillingPlans",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsVisible",
                table: "BillingPlans",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "TrialDays",
                table: "BillingPlans",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "BillingAddOns",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
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
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BillingAddOns", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "BillingCouponRedemptions",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CouponCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    UserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    QuoteId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CheckoutSessionId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    SubscriptionId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    DiscountAmount = table.Column<decimal>(type: "numeric", nullable: false),
                    Currency = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    RedeemedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BillingCouponRedemptions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "BillingCoupons",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
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
                    RedemptionCount = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BillingCoupons", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "BillingEvents",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    UserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    SubscriptionId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    QuoteId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    EventType = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    EntityType = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    EntityId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    PayloadJson = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BillingEvents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "BillingQuotes",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    UserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SubscriptionId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    PlanCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    AddOnCodesJson = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    CouponCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Currency = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    SubtotalAmount = table.Column<decimal>(type: "numeric", nullable: false),
                    DiscountAmount = table.Column<decimal>(type: "numeric", nullable: false),
                    TotalAmount = table.Column<decimal>(type: "numeric", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CheckoutSessionId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    IdempotencyKey = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    SnapshotJson = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BillingQuotes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SubscriptionItems",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SubscriptionId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ItemCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ItemType = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Quantity = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    StartsAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    EndsAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    QuoteId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    CheckoutSessionId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SubscriptionItems", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BillingPlans_Code",
                table: "BillingPlans",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BillingPlans_Status_DisplayOrder",
                table: "BillingPlans",
                columns: new[] { "Status", "DisplayOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_BillingAddOns_Code",
                table: "BillingAddOns",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BillingAddOns_Status_DisplayOrder",
                table: "BillingAddOns",
                columns: new[] { "Status", "DisplayOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_BillingCouponRedemptions_CouponCode_UserId_RedeemedAt",
                table: "BillingCouponRedemptions",
                columns: new[] { "CouponCode", "UserId", "RedeemedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_BillingCoupons_Code",
                table: "BillingCoupons",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BillingCoupons_Status_EndsAt",
                table: "BillingCoupons",
                columns: new[] { "Status", "EndsAt" });

            migrationBuilder.CreateIndex(
                name: "IX_BillingEvents_EntityType_EntityId_OccurredAt",
                table: "BillingEvents",
                columns: new[] { "EntityType", "EntityId", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_BillingEvents_UserId_OccurredAt",
                table: "BillingEvents",
                columns: new[] { "UserId", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_BillingQuotes_Status_ExpiresAt",
                table: "BillingQuotes",
                columns: new[] { "Status", "ExpiresAt" });

            migrationBuilder.CreateIndex(
                name: "IX_BillingQuotes_UserId_CreatedAt",
                table: "BillingQuotes",
                columns: new[] { "UserId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionItems_ItemCode_SubscriptionId",
                table: "SubscriptionItems",
                columns: new[] { "ItemCode", "SubscriptionId" });

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionItems_SubscriptionId_Status",
                table: "SubscriptionItems",
                columns: new[] { "SubscriptionId", "Status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BillingAddOns");

            migrationBuilder.DropTable(
                name: "BillingCouponRedemptions");

            migrationBuilder.DropTable(
                name: "BillingCoupons");

            migrationBuilder.DropTable(
                name: "BillingEvents");

            migrationBuilder.DropTable(
                name: "BillingQuotes");

            migrationBuilder.DropTable(
                name: "SubscriptionItems");

            migrationBuilder.DropIndex(
                name: "IX_BillingPlans_Code",
                table: "BillingPlans");

            migrationBuilder.DropIndex(
                name: "IX_BillingPlans_Status_DisplayOrder",
                table: "BillingPlans");

            migrationBuilder.DropColumn(
                name: "ArchivedAt",
                table: "BillingPlans");

            migrationBuilder.DropColumn(
                name: "Code",
                table: "BillingPlans");

            migrationBuilder.DropColumn(
                name: "Description",
                table: "BillingPlans");

            migrationBuilder.DropColumn(
                name: "DisplayOrder",
                table: "BillingPlans");

            migrationBuilder.DropColumn(
                name: "DurationMonths",
                table: "BillingPlans");

            migrationBuilder.DropColumn(
                name: "EntitlementsJson",
                table: "BillingPlans");

            migrationBuilder.DropColumn(
                name: "IncludedCredits",
                table: "BillingPlans");

            migrationBuilder.DropColumn(
                name: "IncludedSubtestsJson",
                table: "BillingPlans");

            migrationBuilder.DropColumn(
                name: "IsRenewable",
                table: "BillingPlans");

            migrationBuilder.DropColumn(
                name: "IsVisible",
                table: "BillingPlans");

            migrationBuilder.DropColumn(
                name: "TrialDays",
                table: "BillingPlans");
        }
    }
}
