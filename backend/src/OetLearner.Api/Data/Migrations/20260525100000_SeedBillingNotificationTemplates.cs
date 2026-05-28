using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OetLearner.Api.Data.Migrations
{
    /// <summary>
    /// Seeds default English billing notification templates for the events
    /// fired by Phases 5-9 (trial / payment / refund / dunning / coupon /
    /// scholarship / manual-payment). Admin can re-author these via the
    /// /admin/billing/notification-templates UI; this migration only
    /// provides a working baseline so dispatcher has rows to render.
    ///
    /// 2026-05-28 — restored the missing [DbContext]/[Migration] attributes so
    /// EF's migration scanner recognises and applies this migration. The seed
    /// is also made idempotent below (ON CONFLICT DO NOTHING) so re-runs and
    /// already-seeded environments do not collide on the template primary keys.
    /// </summary>
    [DbContext(typeof(LearnerDbContext))]
    [Migration("20260525100000_SeedBillingNotificationTemplates")]
    public partial class SeedBillingNotificationTemplates : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            var ts = "TIMESTAMP '2026-05-25 00:00:00+00'";
            migrationBuilder.Sql($@"
                INSERT INTO ""BillingNotificationTemplates"" (""Id"", ""Code"", ""Channel"", ""LocaleTag"", ""Subject"", ""BodyTemplate"", ""VariablesJson"", ""Version"", ""IsActive"", ""CreatedAt"", ""UpdatedAt"")
                VALUES
                  ('tpl_trial_started_email',      'trial_started',              'email', 'en', 'Welcome to your OET trial', 'Your trial is active until {{trialEndsAt}}. Make the most of it!', '[""trialEndsAt""]', 1, TRUE, {ts}, {ts}),
                  ('tpl_trial_ending_email',       'trial_ending_3d',            'email', 'en', 'Your trial ends in 3 days', 'Trial ends on {{trialEndsAt}}. Upgrade now to keep access.', '[""trialEndsAt""]', 1, TRUE, {ts}, {ts}),
                  ('tpl_trial_expired_email',      'trial_expired',              'email', 'en', 'Your trial has ended', 'Your trial expired. Upgrade to a paid plan to continue.', '[]', 1, TRUE, {ts}, {ts}),
                  ('tpl_payment_succeeded_email',  'payment_succeeded',          'email', 'en', 'Payment received', 'We received {{amount}} {{currency}} for your subscription. Thank you!', '[""amount"",""currency""]', 1, TRUE, {ts}, {ts}),
                  ('tpl_payment_failed_email',     'payment_failed',             'email', 'en', 'Payment failed', 'Your payment failed: {{failureReason}}. Update your payment method at {{updateCardUrl}}.', '[""failureReason"",""updateCardUrl""]', 1, TRUE, {ts}, {ts}),
                  ('tpl_renewal_succeeded_email',  'renewal_succeeded',          'email', 'en', 'Subscription renewed', 'Your subscription was renewed for {{amount}} {{currency}}. Next renewal: {{nextRenewalAt}}.', '[""amount"",""currency"",""nextRenewalAt""]', 1, TRUE, {ts}, {ts}),
                  ('tpl_subscription_paused',      'subscription_paused',        'email', 'en', 'Subscription paused', 'Your subscription is paused until {{pausedUntil}}. Resume anytime from your billing page.', '[""pausedUntil""]', 1, TRUE, {ts}, {ts}),
                  ('tpl_subscription_resumed',     'subscription_resumed',       'email', 'en', 'Subscription resumed', 'Welcome back! Your subscription is active again. Next renewal: {{nextRenewalAt}}.', '[""nextRenewalAt""]', 1, TRUE, {ts}, {ts}),
                  ('tpl_subscription_cancelled',   'subscription_cancelled',     'email', 'en', 'Subscription cancelled', 'Your subscription is cancelled and access continues until {{accessUntil}}.', '[""accessUntil""]', 1, TRUE, {ts}, {ts}),
                  ('tpl_coupon_redeemed_email',    'coupon_redeemed',            'email', 'en', 'Coupon applied', 'Your coupon {{couponCode}} saved {{discountAmount}} {{currency}}.', '[""couponCode"",""discountAmount"",""currency""]', 1, TRUE, {ts}, {ts}),
                  ('tpl_scholarship_granted',     'scholarship_granted',        'email', 'en', 'Scholarship granted', 'Congratulations — you have been granted {{accessTier}} access until {{expiresAt}}.', '[""accessTier"",""expiresAt""]', 1, TRUE, {ts}, {ts}),
                  ('tpl_refund_initiated_email',   'refund_initiated',           'email', 'en', 'Refund initiated', 'Your refund of {{amount}} {{currency}} is being processed.', '[""amount"",""currency""]', 1, TRUE, {ts}, {ts}),
                  ('tpl_refund_completed_email',   'refund_completed',           'email', 'en', 'Refund completed', 'Your refund of {{amount}} {{currency}} is complete.', '[""amount"",""currency""]', 1, TRUE, {ts}, {ts}),
                  ('tpl_credits_low_email',        'credits_low',                'email', 'en', 'Credits running low', 'You have only {{remaining}} credits left. Top up to keep submitting.', '[""remaining""]', 1, TRUE, {ts}, {ts}),
                  ('tpl_credits_topped_up_email',  'credits_topped_up',          'email', 'en', 'Credits added', '{{credits}} credits added to your wallet.', '[""credits""]', 1, TRUE, {ts}, {ts}),
                  ('tpl_manual_received_email',    'manual_payment_received',    'email', 'en', 'Payment proof received', 'We received your payment proof. We will review within 24 hours.', '[]', 1, TRUE, {ts}, {ts}),
                  ('tpl_manual_approved_email',    'manual_payment_approved',    'email', 'en', 'Payment approved', 'Your manual payment was approved and your access is now active.', '[]', 1, TRUE, {ts}, {ts}),
                  ('tpl_manual_rejected_email',    'manual_payment_rejected',    'email', 'en', 'Payment proof rejected', 'Your manual payment was rejected. Reason: {{reason}}.', '[""reason""]', 1, TRUE, {ts}, {ts}),
                  ('tpl_dunning_day0_email',       'dunning_day0_email',         'email', 'en', 'Your payment failed', 'We could not process your renewal. We will retry tomorrow.', '[]', 1, TRUE, {ts}, {ts}),
                  ('tpl_dunning_day3_email',       'dunning_day3_retry_email',   'email', 'en', 'Retry failed — please update your card', 'We retried and failed again. Update your card at {{updateCardUrl}} before {{deadline}}.', '[""updateCardUrl"",""deadline""]', 1, TRUE, {ts}, {ts}),
                  ('tpl_dunning_day7_email',       'dunning_day7_retry_whatsapp','email', 'en', 'Final reminder', 'Your subscription will be paused on day 10 if payment is not received.', '[]', 1, TRUE, {ts}, {ts}),
                  ('tpl_dunning_day14_email',      'dunning_day14_winback_coupon','email','en', 'Come back — 25% off', 'We miss you. Use {{couponCode}} for 25% off when you reactivate.', '[""couponCode""]', 1, TRUE, {ts}, {ts})
                ON CONFLICT (""Id"") DO NOTHING;
            ");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"DELETE FROM ""BillingNotificationTemplates"" WHERE ""Id"" LIKE 'tpl_%';");
        }
    }
}
