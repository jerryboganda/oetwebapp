using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OetLearner.Api.Data.Migrations
{
    /// <summary>
    /// Product decision (2026-08-31): stop emailing learners the "nudge"
    /// notification categories — low-credit balance and study-plan/streak
    /// reminders. This upserts admin-level NotificationPolicyOverride rows
    /// (audience "learner") with EmailEnabled = false for each event key,
    /// which sits above a learner's own per-event preference in
    /// NotificationService's resolution precedence, so it takes effect for
    /// every student regardless of any individual opt-in they made earlier.
    /// In-app/push channels are left untouched (only EmailEnabled is set) —
    /// this is an email-only opt-out, not a full notification kill switch.
    ///
    /// None of these five event keys are in NotificationCatalog's protected
    /// list (that list is OTP/password-reset/invoice/payment/refund/booking
    /// confirmations, which cannot be disabled by any override), so this is
    /// a supported, reversible admin action — identical to using the
    /// PUT /admin/notifications/policies/learner/{eventKey} endpoint by hand,
    /// just applied via migration so it ships with the next deploy.
    ///
    /// ON CONFLICT DO UPDATE so this is idempotent against a re-run and
    /// against an admin having already toggled one of these via the UI.
    /// </summary>
    [DbContext(typeof(LearnerDbContext))]
    [Migration("20261212090000_DisableLearnerMarketingNudgeEmails")]
    public partial class DisableLearnerMarketingNudgeEmails : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            var ts = "TIMESTAMP '2026-08-31 00:00:00+00'";
            var admin = "'system-migration'";
            var adminName = "'Automated: disable marketing/nudge emails to learners (2026-08-31)'";

            migrationBuilder.Sql($@"
                INSERT INTO ""NotificationPolicyOverrides""
                    (""Id"", ""AudienceRole"", ""EventKey"", ""InAppEnabled"", ""EmailEnabled"", ""PushEnabled"", ""EmailMode"", ""UpdatedByAdminId"", ""UpdatedByAdminName"", ""UpdatedAt"")
                VALUES
                  ('a1e2c3d4-0001-4a1b-9c1d-000000000001', 'learner', 'LearnerCreditsLow',          NULL, FALSE, NULL, NULL, {admin}, {adminName}, {ts}),
                  ('a1e2c3d4-0001-4a1b-9c1d-000000000002', 'learner', 'LearnerInactiveNudge',        NULL, FALSE, NULL, NULL, {admin}, {adminName}, {ts}),
                  ('a1e2c3d4-0001-4a1b-9c1d-000000000003', 'learner', 'LearnerDailyStudyReminder',   NULL, FALSE, NULL, NULL, {admin}, {adminName}, {ts}),
                  ('a1e2c3d4-0001-4a1b-9c1d-000000000004', 'learner', 'LearnerStudyPlanDueReminder', NULL, FALSE, NULL, NULL, {admin}, {adminName}, {ts}),
                  ('a1e2c3d4-0001-4a1b-9c1d-000000000005', 'learner', 'LearnerWeakSkillReminder',    NULL, FALSE, NULL, NULL, {admin}, {adminName}, {ts})
                ON CONFLICT (""AudienceRole"", ""EventKey"") DO UPDATE SET
                    ""EmailEnabled"" = FALSE,
                    ""UpdatedByAdminId"" = {admin},
                    ""UpdatedByAdminName"" = {adminName},
                    ""UpdatedAt"" = {ts};
            ");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                DELETE FROM ""NotificationPolicyOverrides""
                WHERE ""AudienceRole"" = 'learner'
                  AND ""EventKey"" IN (
                    'LearnerCreditsLow',
                    'LearnerInactiveNudge',
                    'LearnerDailyStudyReminder',
                    'LearnerStudyPlanDueReminder',
                    'LearnerWeakSkillReminder'
                  );
            ");
        }
    }
}
