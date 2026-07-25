using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using OetLearner.Api.Data;

#nullable disable

namespace OetLearner.Api.Data.Migrations;

/// <summary>
/// Keeps both Tutor Book products payable through normal checkout while ensuring
/// fulfilment remains manual through WhatsApp and grants no platform entitlement.
/// </summary>
[DbContext(typeof(LearnerDbContext))]
[Migration("20260809110000_MakeTutorBookCheckoutManualDelivery")]
public partial class MakeTutorBookCheckoutManualDelivery : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        if (!ActiveProvider.Contains("Npgsql", StringComparison.OrdinalIgnoreCase)) return;

        migrationBuilder.Sql("""
            UPDATE "BillingPlans"
            SET "IncludedSubtestsJson" = '["none"]',
                "DashboardModulesJson" = '[]',
                "BundledTutorBook" = false,
                "RecallUpdatesEnabled" = false,
                "DeliveryMethod" = 'manual_material',
                "TelegramInviteUrl" = NULL,
                "DeliveryInstructions" = 'Payment is completed normally online. Contact us on WhatsApp at +44 7961 725989 after payment for manual delivery. This purchase grants no platform course, subtest, Recalls, or Tutor Book access.',
                "UpdatedAt" = NOW()
            WHERE "Code" = 'tutor-book';

            UPDATE "BillingPlanVersions"
            SET "IncludedSubtestsJson" = '["none"]',
                "DashboardModulesJson" = '[]',
                "BundledTutorBook" = false,
                "RecallUpdatesEnabled" = false,
                "DeliveryMethod" = 'manual_material',
                "TelegramInviteUrl" = NULL,
                "DeliveryInstructions" = 'Payment is completed normally online. Contact us on WhatsApp at +44 7961 725989 after payment for manual delivery. This purchase grants no platform course, subtest, Recalls, or Tutor Book access.'
            WHERE "Code" = 'tutor-book';

            UPDATE "BillingAddOns"
            SET "Description" = 'Discounted Tutor Book for registered candidates with an active eligible course from the guidance price list. Pay normally, then contact us on WhatsApp for manual delivery; no Tutor Book or course access is unlocked on the platform.',
                "GrantEntitlementsJson" = '{}',
                "UpdatedAt" = NOW()
            WHERE "Code" = 'tutor-book-addon';

            UPDATE "BillingAddOnVersions"
            SET "Description" = 'Discounted Tutor Book for registered candidates with an active eligible course from the guidance price list. Pay normally, then contact us on WhatsApp for manual delivery; no Tutor Book or course access is unlocked on the platform.',
                "GrantEntitlementsJson" = '{}'
            WHERE "Code" = 'tutor-book-addon';

            UPDATE "BillingPlans"
            SET "TutorBookDiscountEnabled" = false,
                "UpdatedAt" = NOW()
            WHERE "Code" IN ('full-physiotherapy', 'full-allied-health');

            UPDATE "BillingPlanVersions"
            SET "TutorBookDiscountEnabled" = false
            WHERE "Code" IN ('full-physiotherapy', 'full-allied-health');

            UPDATE "Subscriptions" AS subscription
            SET "TutorBookUnlocked" = false,
                "ChangedAt" = NOW()
            WHERE subscription."TutorBookUnlocked" = true
              AND EXISTS (
                  SELECT 1
                  FROM "SubscriptionItems" AS item
                  WHERE item."SubscriptionId" = subscription."Id"
                    AND item."ItemCode" = 'tutor-book-addon'
              );
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Deliberately non-restorative: rollback must not grant platform access.
    }
}
