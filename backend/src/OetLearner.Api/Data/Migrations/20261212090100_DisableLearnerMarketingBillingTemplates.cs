using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OetLearner.Api.Data.Migrations
{
    /// <summary>
    /// Product decision (2026-08-31), part 2: alongside disabling the
    /// learner nudge notification emails (see
    /// 20261212090000_DisableLearnerMarketingNudgeEmails), also stop the
    /// billing-side marketing/retention emails dispatched by
    /// BillingNotificationDispatcher — abandoned-cart recovery and the
    /// churn-risk win-back / trial-extension / check-in offers fired by
    /// RetentionActionDispatcher (Services/Billing/RetentionActionDispatcher.cs
    /// and AbandonedCartRecoveryService.cs).
    ///
    /// Deliberately excludes the "dunning_*" payment-retry sequence
    /// (Services/Billing/BillingDunningNotifications.cs /
    /// DunningCampaignService.cs) — those tell a learner their card was
    /// declined and their subscription is about to lapse, which was kept on
    /// by explicit product decision (silent involuntary churn is worse than
    /// the email).
    ///
    /// BillingNotificationDispatcher.DispatchAsync only sends a channel for
    /// rows matching Code == eventCode && IsActive == true (see
    /// Services/Billing/BillingNotificationDispatcher.cs); a missing row is
    /// already a no-op send. This UPDATE covers both cases correctly: if an
    /// admin has since authored live templates for these codes via
    /// POST /admin/billing/notification-templates (not visible in any
    /// migration), it deactivates them; if no such row exists, it matches
    /// zero rows harmlessly.
    /// </summary>
    [DbContext(typeof(LearnerDbContext))]
    [Migration("20261212090100_DisableLearnerMarketingBillingTemplates")]
    public partial class DisableLearnerMarketingBillingTemplates : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                UPDATE ""BillingNotificationTemplates""
                SET ""IsActive"" = FALSE,
                    ""UpdatedAt"" = TIMESTAMP '2026-08-31 00:00:00+00'
                WHERE ""Code"" IN (
                    'cart_abandoned_24h',
                    'retention_winback_coupon',
                    'retention_trial_extension',
                    'retention_dunning_winback',
                    'retention_check_in',
                    'retention_generic'
                )
                AND ""IsActive"" = TRUE;
            ");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                UPDATE ""BillingNotificationTemplates""
                SET ""IsActive"" = TRUE,
                    ""UpdatedAt"" = TIMESTAMP '2026-08-31 00:00:00+00'
                WHERE ""Code"" IN (
                    'cart_abandoned_24h',
                    'retention_winback_coupon',
                    'retention_trial_extension',
                    'retention_dunning_winback',
                    'retention_check_in',
                    'retention_generic'
                );
            ");
        }
    }
}
