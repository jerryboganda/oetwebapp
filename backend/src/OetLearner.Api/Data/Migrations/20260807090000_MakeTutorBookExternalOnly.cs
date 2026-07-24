using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using OetLearner.Api.Data;

#nullable disable

namespace OetLearner.Api.Data.Migrations;

/// <summary>
/// Aligns the standalone Tutor Book with the owner-directed WhatsApp-only delivery model.
/// Existing purchases lose only the unintended platform unlock; purchase/audit rows remain intact.
/// </summary>
[DbContext(typeof(LearnerDbContext))]
[Migration("20260807090000_MakeTutorBookExternalOnly")]
public partial class MakeTutorBookExternalOnly : Migration
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
                "DeliveryInstructions" = 'Contact support on WhatsApp after purchase. The personalised Tutor Book will be delivered manually; this purchase grants no platform course or subtest access.',
                "UpdatedAt" = NOW()
            WHERE "Code" = 'tutor-book';

            UPDATE "BillingPlanVersions"
            SET "IncludedSubtestsJson" = '["none"]',
                "DashboardModulesJson" = '[]',
                "BundledTutorBook" = false,
                "RecallUpdatesEnabled" = false,
                "DeliveryMethod" = 'manual_material',
                "TelegramInviteUrl" = NULL,
                "DeliveryInstructions" = 'Contact support on WhatsApp after purchase. The personalised Tutor Book will be delivered manually; this purchase grants no platform course or subtest access.'
            WHERE "Code" = 'tutor-book';

            UPDATE "Subscriptions"
            SET "TutorBookUnlocked" = false,
                "Status" = 1,
                "FulfilmentStatus" = 'pending_manual',
                "ChangedAt" = NOW()
            WHERE "PlanId" = 'tutor-book';
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Deliberately non-restorative: re-granting learner access on rollback would be unsafe.
    }
}
