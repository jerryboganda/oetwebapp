using OetLearner.Api.Contracts;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services;

public sealed record NotificationChannelDefaults(
    bool InAppEnabled,
    bool EmailEnabled,
    bool PushEnabled,
    NotificationEmailMode EmailMode);

public sealed record NotificationCatalogEntry(
    NotificationEventKey Key,
    string AudienceRole,
    string Category,
    string Label,
    string Description,
    NotificationSeverity DefaultSeverity,
    NotificationChannelDefaults DefaultChannels);

public static class NotificationCatalog
{
    public const string GlobalPolicyEventKey = "__global__";

    private static readonly IReadOnlyDictionary<NotificationEventKey, NotificationCatalogEntry> Entries =
        BuildEntries().ToDictionary(entry => entry.Key);

    public static IReadOnlyCollection<NotificationCatalogEntry> All => Entries.Values.OrderBy(entry => entry.AudienceRole).ThenBy(entry => entry.Category).ThenBy(entry => entry.Label).ToArray();

    public static NotificationCatalogEntry Get(NotificationEventKey key)
        => Entries[key];

    public static bool TryParseKey(string value, out NotificationEventKey key)
        => Enum.TryParse(value, ignoreCase: true, out key);

    public static string GetKey(NotificationEventKey key)
        => Enum.GetName(key) ?? key.ToString();

    public static IReadOnlyList<AdminNotificationCatalogEntry> ToAdminEntries()
        => All
            .Select(entry => new AdminNotificationCatalogEntry(
                GetKey(entry.Key),
                entry.AudienceRole,
                entry.Category,
                entry.Label,
                entry.Description,
                NormalizeSeverity(entry.DefaultSeverity),
                entry.DefaultChannels.InAppEnabled,
                entry.DefaultChannels.EmailEnabled,
                entry.DefaultChannels.PushEnabled,
                NormalizeEmailMode(entry.DefaultChannels.EmailMode)))
            .ToArray();

    public static string BuildTitle(NotificationEventKey key, IReadOnlyDictionary<string, string?> tokens)
        => key switch
        {
            NotificationEventKey.LearnerEvaluationCompleted => $"Your {ReadToken(tokens, "subtest", "practice")} evaluation is ready",
            NotificationEventKey.LearnerEvaluationFailed => $"We could not finish your {ReadToken(tokens, "subtest", "practice")} evaluation",
            NotificationEventKey.LearnerMockReportReady => "Your mock report is ready",
            NotificationEventKey.LearnerReviewRequested => "Your expert review request has been received",
            NotificationEventKey.LearnerReviewCompleted => $"Your {ReadToken(tokens, "subtest", "review")} expert review is ready",
            NotificationEventKey.LearnerReviewReworkRequested => "Your expert reviewer requested follow-up work",
            NotificationEventKey.LearnerStudyPlanRegenerated => "Your study plan has been refreshed",
            NotificationEventKey.LearnerStudyPlanDueReminder => $"Study plan reminder: {ReadToken(tokens, "itemTitle", "next task")}",
            NotificationEventKey.LearnerReadinessUpdated => "Your readiness snapshot has been updated",
            NotificationEventKey.LearnerInvoiceFailed => "Payment issue: your latest invoice failed",
            NotificationEventKey.LearnerSubscriptionChanged => "Your subscription status changed",
            NotificationEventKey.LearnerAccountStatusChanged => "Your account status changed",
            NotificationEventKey.ExpertReviewAssigned => "A review has been assigned to you",
            NotificationEventKey.ExpertReviewClaimed => "Review claimed",
            NotificationEventKey.ExpertReviewReleased => "Review released back to the queue",
            NotificationEventKey.ExpertReviewReassigned => "A review has been reassigned",
            NotificationEventKey.ExpertReviewOverdue => "A review in your queue is overdue",
            NotificationEventKey.ExpertReviewReworkRequested => "Admin requested review rework",
            NotificationEventKey.ExpertCalibrationAvailable => "A calibration case is ready for you",
            NotificationEventKey.ExpertCalibrationResult => "Your calibration result is ready",
            NotificationEventKey.ExpertScheduleChanged => "Your review schedule changed",
            NotificationEventKey.AdminReviewOpsAction => "Review operations update",
            NotificationEventKey.AdminUserLifecycleAction => "User lifecycle action completed",
            NotificationEventKey.AdminBillingFailureAlert => "Billing failures need attention",
            NotificationEventKey.AdminFeatureFlagChanged => "Feature flag configuration changed",
            NotificationEventKey.AdminAiConfigChanged => "AI evaluation config changed",
            NotificationEventKey.AdminStuckJobAlert => "A background job is stuck",
            NotificationEventKey.AdminNotificationDeliveryFailureAlert => "Notification delivery failures need attention",
            _ => "Notification update"
        };

    public static string BuildBody(NotificationEventKey key, IReadOnlyDictionary<string, string?> tokens)
        => key switch
        {
            NotificationEventKey.LearnerEvaluationCompleted => $"Open the {ReadToken(tokens, "subtest", "practice")} result to review score guidance and next steps.",
            NotificationEventKey.LearnerEvaluationFailed => ReadToken(tokens, "message", "Try again shortly or contact support if the problem continues."),
            NotificationEventKey.LearnerMockReportReady => "Your latest OET mock has finished processing and the full report is available.",
            NotificationEventKey.LearnerReviewRequested => "We have queued your expert review request and will notify you when status changes.",
            NotificationEventKey.LearnerReviewCompleted => "Your expert feedback bundle has been published in the learner workspace.",
            NotificationEventKey.LearnerReviewReworkRequested => ReadToken(tokens, "message", "A reviewer requested another pass before final sign-off."),
            NotificationEventKey.LearnerStudyPlanRegenerated => ReadToken(tokens, "message", "Your latest evaluated work changed the recommended next tasks."),
            NotificationEventKey.LearnerStudyPlanDueReminder => $"The task \"{ReadToken(tokens, "itemTitle", "next task")}\" is due {ReadToken(tokens, "dueLabel", "soon")}.",
            NotificationEventKey.LearnerReadinessUpdated => ReadToken(tokens, "message", "Readiness indicators were recalculated from your latest scored evidence."),
            NotificationEventKey.LearnerInvoiceFailed => ReadToken(tokens, "message", "Update billing details to avoid subscription interruption."),
            NotificationEventKey.LearnerSubscriptionChanged => ReadToken(tokens, "message", "A subscription or plan change was recorded on your account."),
            NotificationEventKey.LearnerAccountStatusChanged => ReadToken(tokens, "message", "Your learner account access or status was updated."),
            NotificationEventKey.ExpertReviewAssigned => $"Review {ReadToken(tokens, "reviewRequestId", string.Empty)} is ready in your expert queue.",
            NotificationEventKey.ExpertReviewClaimed => $"You successfully claimed review {ReadToken(tokens, "reviewRequestId", string.Empty)}.",
            NotificationEventKey.ExpertReviewReleased => $"Review {ReadToken(tokens, "reviewRequestId", string.Empty)} was released back to the shared queue.",
            NotificationEventKey.ExpertReviewReassigned => $"Review {ReadToken(tokens, "reviewRequestId", string.Empty)} was reassigned by admin review ops.",
            NotificationEventKey.ExpertReviewOverdue => $"Review {ReadToken(tokens, "reviewRequestId", string.Empty)} is past its expected turnaround window.",
            NotificationEventKey.ExpertReviewReworkRequested => ReadToken(tokens, "message", "Admin requested a revised expert submission."),
            NotificationEventKey.ExpertCalibrationAvailable => "A new calibration case is available in the expert calibration workspace.",
            NotificationEventKey.ExpertCalibrationResult => ReadToken(tokens, "message", "Open calibration to compare your scoring against the benchmark."),
            NotificationEventKey.ExpertScheduleChanged => ReadToken(tokens, "message", "An availability or assignment window was updated."),
            NotificationEventKey.AdminReviewOpsAction => ReadToken(tokens, "message", "A review-ops action changed queue state or ownership."),
            NotificationEventKey.AdminUserLifecycleAction => ReadToken(tokens, "message", "A user lifecycle action was completed from admin."),
            NotificationEventKey.AdminBillingFailureAlert => ReadToken(tokens, "message", "Failed invoices crossed the configured alert threshold."),
            NotificationEventKey.AdminFeatureFlagChanged => ReadToken(tokens, "message", "A feature flag was created, activated, deactivated, or updated."),
            NotificationEventKey.AdminAiConfigChanged => ReadToken(tokens, "message", "An AI evaluation configuration version was activated or updated."),
            NotificationEventKey.AdminStuckJobAlert => ReadToken(tokens, "message", "Background processing has stalled past the acceptable window."),
            NotificationEventKey.AdminNotificationDeliveryFailureAlert => ReadToken(tokens, "message", "Notification delivery failures crossed the alert threshold."),
            _ => "A new notification is available."
        };

    public static string? BuildActionUrl(NotificationEventKey key, IReadOnlyDictionary<string, string?> tokens)
        => key switch
        {
            NotificationEventKey.LearnerEvaluationCompleted => $"/submissions/{ReadToken(tokens, "attemptId", string.Empty)}",
            NotificationEventKey.LearnerEvaluationFailed => $"/submissions/{ReadToken(tokens, "attemptId", string.Empty)}",
            NotificationEventKey.LearnerMockReportReady => $"/mocks/{ReadToken(tokens, "mockAttemptId", string.Empty)}",
            NotificationEventKey.LearnerReviewRequested => $"/submissions/{ReadToken(tokens, "attemptId", string.Empty)}",
            NotificationEventKey.LearnerReviewCompleted => $"/submissions/{ReadToken(tokens, "attemptId", string.Empty)}",
            NotificationEventKey.LearnerReviewReworkRequested => $"/submissions/{ReadToken(tokens, "attemptId", string.Empty)}",
            NotificationEventKey.LearnerStudyPlanRegenerated => "/study-plan",
            NotificationEventKey.LearnerStudyPlanDueReminder => "/study-plan",
            NotificationEventKey.LearnerReadinessUpdated => "/readiness",
            NotificationEventKey.LearnerInvoiceFailed => "/billing",
            NotificationEventKey.LearnerSubscriptionChanged => "/billing",
            NotificationEventKey.LearnerAccountStatusChanged => "/settings",
            NotificationEventKey.ExpertReviewAssigned => $"/expert/review/{ReadToken(tokens, "reviewRequestId", string.Empty)}",
            NotificationEventKey.ExpertReviewClaimed => $"/expert/review/{ReadToken(tokens, "reviewRequestId", string.Empty)}",
            NotificationEventKey.ExpertReviewReleased => "/expert/queue",
            NotificationEventKey.ExpertReviewReassigned => $"/expert/review/{ReadToken(tokens, "reviewRequestId", string.Empty)}",
            NotificationEventKey.ExpertReviewOverdue => $"/expert/review/{ReadToken(tokens, "reviewRequestId", string.Empty)}",
            NotificationEventKey.ExpertReviewReworkRequested => $"/expert/review/{ReadToken(tokens, "reviewRequestId", string.Empty)}",
            NotificationEventKey.ExpertCalibrationAvailable => "/expert/calibration",
            NotificationEventKey.ExpertCalibrationResult => "/expert/calibration",
            NotificationEventKey.ExpertScheduleChanged => "/expert/schedule",
            NotificationEventKey.AdminReviewOpsAction => "/admin/review-ops",
            NotificationEventKey.AdminUserLifecycleAction => "/admin/users",
            NotificationEventKey.AdminBillingFailureAlert => "/admin/billing",
            NotificationEventKey.AdminFeatureFlagChanged => "/admin/flags",
            NotificationEventKey.AdminAiConfigChanged => "/admin/ai-config",
            NotificationEventKey.AdminStuckJobAlert => "/admin/review-ops",
            NotificationEventKey.AdminNotificationDeliveryFailureAlert => "/admin/notifications",
            _ => null
        };

    private static IReadOnlyCollection<NotificationCatalogEntry> BuildEntries()
    {
        return
        [
            new(NotificationEventKey.LearnerEvaluationCompleted, ApplicationUserRoles.Learner, "results", "Evaluation Completed", "Notify learners when an AI evaluation completes successfully.", NotificationSeverity.Success, new(true, true, true, NotificationEmailMode.Immediate)),
            new(NotificationEventKey.LearnerEvaluationFailed, ApplicationUserRoles.Learner, "results", "Evaluation Failed", "Notify learners when an AI evaluation cannot complete.", NotificationSeverity.Warning, new(true, true, true, NotificationEmailMode.Immediate)),
            new(NotificationEventKey.LearnerMockReportReady, ApplicationUserRoles.Learner, "results", "Mock Report Ready", "Notify learners when a mock report is generated.", NotificationSeverity.Success, new(true, true, true, NotificationEmailMode.Immediate)),
            new(NotificationEventKey.LearnerReviewRequested, ApplicationUserRoles.Learner, "reviews", "Review Requested", "Acknowledge expert review submission requests.", NotificationSeverity.Info, new(true, true, false, NotificationEmailMode.Immediate)),
            new(NotificationEventKey.LearnerReviewCompleted, ApplicationUserRoles.Learner, "reviews", "Review Completed", "Notify learners when expert feedback is published.", NotificationSeverity.Success, new(true, true, true, NotificationEmailMode.Immediate)),
            new(NotificationEventKey.LearnerReviewReworkRequested, ApplicationUserRoles.Learner, "reviews", "Review Rework Requested", "Notify learners when a review requires follow-up work.", NotificationSeverity.Warning, new(true, true, true, NotificationEmailMode.Immediate)),
            new(NotificationEventKey.LearnerStudyPlanRegenerated, ApplicationUserRoles.Learner, "study_plan", "Study Plan Regenerated", "Notify learners when the study plan is refreshed.", NotificationSeverity.Info, new(true, false, true, NotificationEmailMode.DailyDigest)),
            new(NotificationEventKey.LearnerStudyPlanDueReminder, ApplicationUserRoles.Learner, "reminders", "Study Plan Reminder", "Remind learners about due study plan items.", NotificationSeverity.Info, new(true, true, true, NotificationEmailMode.DailyDigest)),
            new(NotificationEventKey.LearnerReadinessUpdated, ApplicationUserRoles.Learner, "readiness", "Readiness Updated", "Notify learners when readiness metrics change.", NotificationSeverity.Info, new(true, true, true, NotificationEmailMode.Immediate)),
            new(NotificationEventKey.LearnerInvoiceFailed, ApplicationUserRoles.Learner, "billing", "Invoice Failed", "Notify learners when a billing attempt fails.", NotificationSeverity.Critical, new(true, true, true, NotificationEmailMode.Immediate)),
            new(NotificationEventKey.LearnerSubscriptionChanged, ApplicationUserRoles.Learner, "billing", "Subscription Changed", "Notify learners when a subscription changes.", NotificationSeverity.Info, new(true, true, true, NotificationEmailMode.Immediate)),
            new(NotificationEventKey.LearnerAccountStatusChanged, ApplicationUserRoles.Learner, "account", "Account Status Changed", "Notify learners when their account status changes.", NotificationSeverity.Warning, new(true, true, true, NotificationEmailMode.Immediate)),
            new(NotificationEventKey.ExpertReviewAssigned, ApplicationUserRoles.Expert, "review_ops", "Review Assigned", "Notify experts when a review is assigned.", NotificationSeverity.Info, new(true, true, true, NotificationEmailMode.Immediate)),
            new(NotificationEventKey.ExpertReviewClaimed, ApplicationUserRoles.Expert, "review_ops", "Review Claimed", "Confirm claim actions in the expert queue.", NotificationSeverity.Info, new(true, false, false, NotificationEmailMode.Off)),
            new(NotificationEventKey.ExpertReviewReleased, ApplicationUserRoles.Expert, "review_ops", "Review Released", "Confirm release actions in the expert queue.", NotificationSeverity.Info, new(true, false, false, NotificationEmailMode.Off)),
            new(NotificationEventKey.ExpertReviewReassigned, ApplicationUserRoles.Expert, "review_ops", "Review Reassigned", "Notify experts when admin reassigns work.", NotificationSeverity.Warning, new(true, true, true, NotificationEmailMode.Immediate)),
            new(NotificationEventKey.ExpertReviewOverdue, ApplicationUserRoles.Expert, "review_ops", "Review Overdue", "Notify experts when queue items become overdue.", NotificationSeverity.Warning, new(true, true, true, NotificationEmailMode.DailyDigest)),
            new(NotificationEventKey.ExpertReviewReworkRequested, ApplicationUserRoles.Expert, "review_ops", "Expert Rework Requested", "Notify experts when an admin requests rework.", NotificationSeverity.Warning, new(true, true, true, NotificationEmailMode.Immediate)),
            new(NotificationEventKey.ExpertCalibrationAvailable, ApplicationUserRoles.Expert, "calibration", "Calibration Available", "Notify experts when new calibration work is available.", NotificationSeverity.Info, new(true, true, true, NotificationEmailMode.Immediate)),
            new(NotificationEventKey.ExpertCalibrationResult, ApplicationUserRoles.Expert, "calibration", "Calibration Result", "Notify experts when calibration scoring is published.", NotificationSeverity.Info, new(true, true, true, NotificationEmailMode.Immediate)),
            new(NotificationEventKey.ExpertScheduleChanged, ApplicationUserRoles.Expert, "schedule", "Schedule Changed", "Notify experts when availability or assignment windows shift.", NotificationSeverity.Info, new(true, true, true, NotificationEmailMode.Immediate)),
            new(NotificationEventKey.AdminReviewOpsAction, ApplicationUserRoles.Admin, "operations", "Review Ops Action", "Operational admin updates for review assignment and requeue activity.", NotificationSeverity.Info, new(true, false, false, NotificationEmailMode.Off)),
            new(NotificationEventKey.AdminUserLifecycleAction, ApplicationUserRoles.Admin, "operations", "User Lifecycle Action", "Operational admin updates for invites, suspension, restore, delete, and password reset.", NotificationSeverity.Info, new(true, false, false, NotificationEmailMode.Off)),
            new(NotificationEventKey.AdminBillingFailureAlert, ApplicationUserRoles.Admin, "operations", "Billing Failure Alert", "Notify admins when billing failures need intervention.", NotificationSeverity.Critical, new(true, true, false, NotificationEmailMode.Immediate)),
            new(NotificationEventKey.AdminFeatureFlagChanged, ApplicationUserRoles.Admin, "operations", "Feature Flag Changed", "Notify admins when feature flag configuration changes.", NotificationSeverity.Info, new(true, false, false, NotificationEmailMode.Off)),
            new(NotificationEventKey.AdminAiConfigChanged, ApplicationUserRoles.Admin, "operations", "AI Config Changed", "Notify admins when AI config changes.", NotificationSeverity.Info, new(true, false, false, NotificationEmailMode.Off)),
            new(NotificationEventKey.AdminStuckJobAlert, ApplicationUserRoles.Admin, "operations", "Stuck Job Alert", "Notify admins when background jobs are stuck.", NotificationSeverity.Critical, new(true, true, false, NotificationEmailMode.Immediate)),
            new(NotificationEventKey.AdminNotificationDeliveryFailureAlert, ApplicationUserRoles.Admin, "operations", "Notification Delivery Failure Alert", "Notify admins when notification delivery failures cross alert thresholds.", NotificationSeverity.Critical, new(true, true, false, NotificationEmailMode.Immediate))
        ];
    }

    private static string ReadToken(IReadOnlyDictionary<string, string?> tokens, string key, string fallback)
        => tokens.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : fallback;

    private static string NormalizeSeverity(NotificationSeverity severity)
        => severity switch
        {
            NotificationSeverity.Info => "info",
            NotificationSeverity.Success => "success",
            NotificationSeverity.Warning => "warning",
            NotificationSeverity.Critical => "critical",
            _ => throw new ArgumentOutOfRangeException(nameof(severity), severity, "Unsupported notification severity.")
        };

    private static string NormalizeEmailMode(NotificationEmailMode emailMode)
        => emailMode switch
        {
            NotificationEmailMode.Off => "off",
            NotificationEmailMode.Immediate => "immediate",
            NotificationEmailMode.DailyDigest => "daily_digest",
            _ => throw new ArgumentOutOfRangeException(nameof(emailMode), emailMode, "Unsupported notification email mode.")
        };
}
