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
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            key = default;
            return false;
        }

        var normalizedValue = value.Trim();
        foreach (var candidate in Entries.Keys)
        {
            if (!string.Equals(GetKey(candidate), normalizedValue, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            key = candidate;
            return true;
        }

        key = default;
        return false;
    }

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
                false,
                false,
                NormalizeEmailMode(entry.DefaultChannels.EmailMode),
                IsPolicyProtected(entry)))
            .ToArray();

    public static bool IsPolicyProtected(NotificationEventKey key)
        => IsPolicyProtected(Get(key));

    public static bool IsPolicyProtected(NotificationCatalogEntry entry)
        => ProtectedEventKeys.Contains(entry.Key)
            || entry.DefaultSeverity == NotificationSeverity.Critical
            || string.Equals(entry.Category, "security", StringComparison.OrdinalIgnoreCase);

    private static readonly IReadOnlySet<NotificationEventKey> ProtectedEventKeys = new HashSet<NotificationEventKey>
    {
        NotificationEventKey.LearnerEmailVerificationRequested,
        NotificationEventKey.LearnerPasswordResetRequested,
        NotificationEventKey.LearnerInvoiceGenerated,
        NotificationEventKey.LearnerPaymentSucceeded,
        NotificationEventKey.LearnerRefundProcessed
    };

    public static string BuildTitle(NotificationEventKey key, IReadOnlyDictionary<string, string?> tokens)
        => key switch
        {
            NotificationEventKey.LearnerEvaluationCompleted => $"Your {ReadToken(tokens, "subtest", "practice")} evaluation is ready",
            NotificationEventKey.LearnerEvaluationFailed => $"We could not finish your {ReadToken(tokens, "subtest", "practice")} evaluation",
            NotificationEventKey.LearnerMockReportReady => "Your mock report is ready",
            NotificationEventKey.LearnerReviewRequested => "Your tutor review request has been received",
            NotificationEventKey.LearnerReviewCompleted => $"Your {ReadToken(tokens, "subtest", "review")} tutor review is ready",
            NotificationEventKey.LearnerReviewReworkRequested => "Your tutor reviewer requested follow-up work",
            NotificationEventKey.LearnerStudyPlanRegenerated => "Your study plan has been refreshed",
            NotificationEventKey.LearnerStudyPlanDueReminder => $"Study plan reminder: {ReadToken(tokens, "itemTitle", "next task")}",
            NotificationEventKey.LearnerReadinessUpdated => "Your readiness snapshot has been updated",
            NotificationEventKey.LearnerInvoiceFailed => "Payment issue: your latest invoice failed",
            NotificationEventKey.LearnerSubscriptionChanged => "Your subscription status changed",
            NotificationEventKey.LearnerAccountStatusChanged => "Your account status changed",
            NotificationEventKey.LearnerFreezeRequested => "Your freeze request has been received",
            NotificationEventKey.LearnerFreezeApproved => "Your freeze request has been approved",
            NotificationEventKey.LearnerFreezeRejected => "Your freeze request was rejected",
            NotificationEventKey.LearnerFreezeStarted => "Your freeze is now active",
            NotificationEventKey.LearnerFreezeEnded => "Your freeze has ended",
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
            NotificationEventKey.AdminFreezePolicyChanged => "Freeze policy updated",
            NotificationEventKey.AdminFreezeLifecycleAction => "Freeze lifecycle action completed",
            NotificationEventKey.LearnerPrivateSpeakingBooked => $"Private speaking session booked with {ReadToken(tokens, "tutorName", "your tutor")}",
            NotificationEventKey.LearnerPrivateSpeakingReminder => $"Upcoming session with {ReadToken(tokens, "tutorName", "your tutor")} in {ReadToken(tokens, "hoursUntil", "a few")} hours",
            NotificationEventKey.LearnerPrivateSpeakingCancelled => "Your private speaking session has been cancelled",
            NotificationEventKey.ExpertPrivateSpeakingAssigned => $"New private speaking session booked for {ReadToken(tokens, "sessionTime", "upcoming")}",
            NotificationEventKey.ExpertPrivateSpeakingReminder => $"Upcoming session in {ReadToken(tokens, "hoursUntil", "a few")} hours",
            NotificationEventKey.ExpertPrivateSpeakingCancelled => "A private speaking session has been cancelled",
            NotificationEventKey.AdminPrivateSpeakingBooked => $"Private speaking session booked: {ReadToken(tokens, "tutorName", "tutor")} at {ReadToken(tokens, "sessionTime", "time to be confirmed")}",
            _ when Entries.TryGetValue(key, out var entry) => entry.Label,
            _ => "Notification update"
        };

    public static string BuildBody(NotificationEventKey key, IReadOnlyDictionary<string, string?> tokens)
        => key switch
        {
            NotificationEventKey.LearnerEvaluationCompleted => $"Open the {ReadToken(tokens, "subtest", "practice")} result to review score guidance and next steps.",
            NotificationEventKey.LearnerEvaluationFailed => ReadToken(tokens, "message", "Try again shortly or contact support if the problem continues."),
            NotificationEventKey.LearnerMockReportReady => "Your latest OET mock has finished processing and the full report is available.",
            NotificationEventKey.LearnerReviewRequested => "We have queued your tutor review request and will notify you when status changes.",
            NotificationEventKey.LearnerReviewCompleted => "Your tutor feedback bundle has been published in the learner workspace.",
            NotificationEventKey.LearnerReviewReworkRequested => ReadToken(tokens, "message", "A reviewer requested another pass before final sign-off."),
            NotificationEventKey.LearnerStudyPlanRegenerated => ReadToken(tokens, "message", "Your latest evaluated work changed the recommended next tasks."),
            NotificationEventKey.LearnerStudyPlanDueReminder => $"The task \"{ReadToken(tokens, "itemTitle", "next task")}\" is due {ReadToken(tokens, "dueLabel", "soon")}.",
            NotificationEventKey.LearnerReadinessUpdated => ReadToken(tokens, "message", "Readiness indicators were recalculated from your latest scored evidence."),
            NotificationEventKey.LearnerInvoiceFailed => ReadToken(tokens, "message", "Update billing details to avoid subscription interruption."),
            NotificationEventKey.LearnerSubscriptionChanged => ReadToken(tokens, "message", "A subscription or plan change was recorded on your account."),
            NotificationEventKey.LearnerAccountStatusChanged => ReadToken(tokens, "message", "Your learner account access or status was updated."),
            NotificationEventKey.LearnerFreezeRequested => ReadToken(tokens, "message", "We received your freeze request and will process it shortly."),
            NotificationEventKey.LearnerFreezeApproved => ReadToken(tokens, "message", "Your freeze request was approved and will take effect according to the schedule."),
            NotificationEventKey.LearnerFreezeRejected => ReadToken(tokens, "message", "Your freeze request was rejected by an admin."),
            NotificationEventKey.LearnerFreezeStarted => ReadToken(tokens, "message", "Your freeze is now active and the learner workspace is read-only."),
            NotificationEventKey.LearnerFreezeEnded => ReadToken(tokens, "message", "Your freeze period has finished and study access is restored."),
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
            NotificationEventKey.AdminFreezePolicyChanged => ReadToken(tokens, "message", "Freeze policy settings were updated."),
            NotificationEventKey.AdminFreezeLifecycleAction => ReadToken(tokens, "message", "A freeze lifecycle action was processed."),
            NotificationEventKey.LearnerPrivateSpeakingBooked => $"Your session with {ReadToken(tokens, "tutorName", "your tutor")} is confirmed for {ReadToken(tokens, "sessionTime", "the scheduled time")}. A Zoom link will be sent shortly.",
            NotificationEventKey.LearnerPrivateSpeakingReminder => $"Your private speaking session starts in {ReadToken(tokens, "hoursUntil", "a few")} hours. Check your booking details for the Zoom link.",
            NotificationEventKey.LearnerPrivateSpeakingCancelled => ReadToken(tokens, "message", "Your private speaking session has been cancelled. If you paid, a refund will be processed."),
            NotificationEventKey.ExpertPrivateSpeakingAssigned => $"A learner has booked a private session for {ReadToken(tokens, "sessionTime", "an upcoming time")}. Check your schedule for details.",
            NotificationEventKey.ExpertPrivateSpeakingReminder => $"Your private speaking session starts in {ReadToken(tokens, "hoursUntil", "a few")} hours. Use the Zoom start link in your session details.",
            NotificationEventKey.ExpertPrivateSpeakingCancelled => ReadToken(tokens, "message", "A private speaking session has been cancelled by the learner or admin."),
            NotificationEventKey.AdminPrivateSpeakingBooked => $"Private speaking session booked with {ReadToken(tokens, "tutorName", "a tutor")} at {ReadToken(tokens, "sessionTime", "time to be confirmed")}. Booking ID: {ReadToken(tokens, "bookingId", "unknown")}.",
            _ when Entries.TryGetValue(key, out var entry) => entry.Description,
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
            NotificationEventKey.LearnerFreezeRequested => "/freeze",
            NotificationEventKey.LearnerFreezeApproved => "/freeze",
            NotificationEventKey.LearnerFreezeRejected => "/freeze",
            NotificationEventKey.LearnerFreezeStarted => "/freeze",
            NotificationEventKey.LearnerFreezeEnded => "/freeze",
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
            NotificationEventKey.AdminFreezePolicyChanged => "/admin/freeze",
            NotificationEventKey.AdminFreezeLifecycleAction => "/admin/freeze",
            NotificationEventKey.LearnerPrivateSpeakingBooked => $"/private-speaking/bookings/{ReadToken(tokens, "bookingId", string.Empty)}",
            NotificationEventKey.LearnerPrivateSpeakingReminder => $"/private-speaking/bookings/{ReadToken(tokens, "bookingId", string.Empty)}",
            NotificationEventKey.LearnerPrivateSpeakingCancelled => "/private-speaking",
            NotificationEventKey.ExpertPrivateSpeakingAssigned => $"/expert/private-speaking/{ReadToken(tokens, "bookingId", string.Empty)}",
            NotificationEventKey.ExpertPrivateSpeakingReminder => $"/expert/private-speaking/{ReadToken(tokens, "bookingId", string.Empty)}",
            NotificationEventKey.ExpertPrivateSpeakingCancelled => "/expert/private-speaking",
            NotificationEventKey.AdminPrivateSpeakingBooked => "/admin/private-speaking",
            _ => BuildFallbackActionUrl(key)
        };

    private static IReadOnlyCollection<NotificationCatalogEntry> BuildEntries()
    {
        return
        [
            new(NotificationEventKey.LearnerEvaluationCompleted, ApplicationUserRoles.Learner, "results", "Evaluation Completed", "Notify learners when an AI evaluation completes successfully.", NotificationSeverity.Success, new(true, true, true, NotificationEmailMode.Immediate)),
            new(NotificationEventKey.LearnerEvaluationFailed, ApplicationUserRoles.Learner, "results", "Evaluation Failed", "Notify learners when an AI evaluation cannot complete.", NotificationSeverity.Warning, new(true, true, true, NotificationEmailMode.Immediate)),
            new(NotificationEventKey.LearnerMockReportReady, ApplicationUserRoles.Learner, "results", "Mock Report Ready", "Notify learners when a mock report is generated.", NotificationSeverity.Success, new(true, true, true, NotificationEmailMode.Immediate)),
            new(NotificationEventKey.LearnerReviewRequested, ApplicationUserRoles.Learner, "reviews", "Review Requested", "Acknowledge tutor review submission requests.", NotificationSeverity.Info, new(true, true, false, NotificationEmailMode.Immediate)),
            new(NotificationEventKey.LearnerReviewCompleted, ApplicationUserRoles.Learner, "reviews", "Review Completed", "Notify learners when tutor feedback is published.", NotificationSeverity.Success, new(true, true, true, NotificationEmailMode.Immediate)),
            new(NotificationEventKey.LearnerReviewReworkRequested, ApplicationUserRoles.Learner, "reviews", "Review Rework Requested", "Notify learners when a review requires follow-up work.", NotificationSeverity.Warning, new(true, true, true, NotificationEmailMode.Immediate)),
            new(NotificationEventKey.LearnerStudyPlanRegenerated, ApplicationUserRoles.Learner, "study_plan", "Study Plan Regenerated", "Notify learners when the study plan is refreshed.", NotificationSeverity.Info, new(true, false, true, NotificationEmailMode.DailyDigest)),
            new(NotificationEventKey.LearnerStudyPlanDueReminder, ApplicationUserRoles.Learner, "reminders", "Study Plan Reminder", "Remind learners about due study plan items.", NotificationSeverity.Info, new(true, true, true, NotificationEmailMode.DailyDigest)),
            new(NotificationEventKey.LearnerReadinessUpdated, ApplicationUserRoles.Learner, "readiness", "Readiness Updated", "Notify learners when readiness metrics change.", NotificationSeverity.Info, new(true, true, true, NotificationEmailMode.Immediate)),
            new(NotificationEventKey.LearnerInvoiceFailed, ApplicationUserRoles.Learner, "billing", "Invoice Failed", "Notify learners when a billing attempt fails.", NotificationSeverity.Critical, new(true, true, true, NotificationEmailMode.Immediate)),
            new(NotificationEventKey.LearnerSubscriptionChanged, ApplicationUserRoles.Learner, "billing", "Subscription Changed", "Notify learners when a subscription changes.", NotificationSeverity.Info, new(true, true, true, NotificationEmailMode.Immediate)),
            new(NotificationEventKey.LearnerAccountStatusChanged, ApplicationUserRoles.Learner, "account", "Account Status Changed", "Notify learners when their account status changes.", NotificationSeverity.Warning, new(true, true, true, NotificationEmailMode.Immediate)),
            new(NotificationEventKey.LearnerFreezeRequested, ApplicationUserRoles.Learner, "freeze", "Freeze Requested", "Notify learners when a freeze request is submitted.", NotificationSeverity.Info, new(true, true, true, NotificationEmailMode.Immediate)),
            new(NotificationEventKey.LearnerFreezeApproved, ApplicationUserRoles.Learner, "freeze", "Freeze Approved", "Notify learners when a freeze request is approved.", NotificationSeverity.Success, new(true, true, true, NotificationEmailMode.Immediate)),
            new(NotificationEventKey.LearnerFreezeRejected, ApplicationUserRoles.Learner, "freeze", "Freeze Rejected", "Notify learners when a freeze request is rejected.", NotificationSeverity.Warning, new(true, true, true, NotificationEmailMode.Immediate)),
            new(NotificationEventKey.LearnerFreezeStarted, ApplicationUserRoles.Learner, "freeze", "Freeze Started", "Notify learners when an approved freeze becomes active.", NotificationSeverity.Info, new(true, true, true, NotificationEmailMode.Immediate)),
            new(NotificationEventKey.LearnerFreezeEnded, ApplicationUserRoles.Learner, "freeze", "Freeze Ended", "Notify learners when a freeze finishes or is ended early.", NotificationSeverity.Info, new(true, true, true, NotificationEmailMode.Immediate)),
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
            new(NotificationEventKey.AdminNotificationDeliveryFailureAlert, ApplicationUserRoles.Admin, "operations", "Notification Delivery Failure Alert", "Notify admins when notification delivery failures cross alert thresholds.", NotificationSeverity.Critical, new(true, true, false, NotificationEmailMode.Immediate)),
            new(NotificationEventKey.AdminFreezePolicyChanged, ApplicationUserRoles.Admin, "operations", "Freeze Policy Changed", "Notify admins when freeze policy settings are updated.", NotificationSeverity.Info, new(true, false, false, NotificationEmailMode.Off)),
            new(NotificationEventKey.AdminFreezeLifecycleAction, ApplicationUserRoles.Admin, "operations", "Freeze Lifecycle Action", "Notify admins when freeze requests are approved, rejected, started, ended, or force-ended.", NotificationSeverity.Info, new(true, false, false, NotificationEmailMode.Off)),

            // Private Speaking Session notifications
            new(NotificationEventKey.LearnerPrivateSpeakingBooked, ApplicationUserRoles.Learner, "private_speaking", "Session Booked", "Notify learners when a private speaking session is booked.", NotificationSeverity.Success, new(true, true, true, NotificationEmailMode.Immediate)),
            new(NotificationEventKey.LearnerPrivateSpeakingReminder, ApplicationUserRoles.Learner, "private_speaking", "Session Reminder", "Remind learners about upcoming private speaking sessions.", NotificationSeverity.Info, new(true, true, true, NotificationEmailMode.Immediate)),
            new(NotificationEventKey.LearnerPrivateSpeakingCancelled, ApplicationUserRoles.Learner, "private_speaking", "Session Cancelled", "Notify learners when a private speaking session is cancelled.", NotificationSeverity.Warning, new(true, true, true, NotificationEmailMode.Immediate)),
            new(NotificationEventKey.ExpertPrivateSpeakingAssigned, ApplicationUserRoles.Expert, "private_speaking", "Session Assigned", "Notify experts when a learner books a private speaking session.", NotificationSeverity.Info, new(true, true, true, NotificationEmailMode.Immediate)),
            new(NotificationEventKey.ExpertPrivateSpeakingReminder, ApplicationUserRoles.Expert, "private_speaking", "Session Reminder", "Remind experts about upcoming private speaking sessions.", NotificationSeverity.Info, new(true, true, true, NotificationEmailMode.Immediate)),
            new(NotificationEventKey.ExpertPrivateSpeakingCancelled, ApplicationUserRoles.Expert, "private_speaking", "Session Cancelled", "Notify experts when a private speaking session is cancelled.", NotificationSeverity.Warning, new(true, true, true, NotificationEmailMode.Immediate)),
            new(NotificationEventKey.AdminPrivateSpeakingBooked, ApplicationUserRoles.Admin, "operations", "Private Speaking Booked", "Notify admins when a private speaking session is booked.", NotificationSeverity.Info, new(true, false, false, NotificationEmailMode.Off)),

            // Expanded OET lifecycle catalog
            new(NotificationEventKey.LearnerAccountCreated, ApplicationUserRoles.Learner, "account", "Account Created", "Confirm account creation and point learners to onboarding.", NotificationSeverity.Success, new(true, true, false, NotificationEmailMode.Immediate)),
            new(NotificationEventKey.LearnerEmailVerificationRequested, ApplicationUserRoles.Learner, "account", "Email Verification", "Ask learners to verify their email address.", NotificationSeverity.Warning, new(true, true, false, NotificationEmailMode.Immediate)),
            new(NotificationEventKey.LearnerPasswordResetRequested, ApplicationUserRoles.Learner, "security", "Password Reset Requested", "Notify learners when a password reset is requested.", NotificationSeverity.Warning, new(true, true, false, NotificationEmailMode.Immediate)),
            new(NotificationEventKey.LearnerClassBookingConfirmed, ApplicationUserRoles.Learner, "lessons", "Class Booking Confirmed", "Confirm class or lesson bookings for learners.", NotificationSeverity.Success, new(true, true, true, NotificationEmailMode.Immediate)),
            new(NotificationEventKey.LearnerMockSubmitted, ApplicationUserRoles.Learner, "mocks", "Mock Submitted", "Confirm mock submission and processing status.", NotificationSeverity.Info, new(true, true, true, NotificationEmailMode.Immediate)),
            new(NotificationEventKey.LearnerInvoiceGenerated, ApplicationUserRoles.Learner, "billing", "Invoice Generated", "Notify learners when a new invoice is generated.", NotificationSeverity.Info, new(true, true, false, NotificationEmailMode.Immediate)),
            new(NotificationEventKey.LearnerDailyStudyReminder, ApplicationUserRoles.Learner, "reminders", "Daily Study Reminder", "Prompt learners to return to their daily OET study tasks.", NotificationSeverity.Info, new(true, true, true, NotificationEmailMode.DailyDigest)),
            new(NotificationEventKey.LearnerMissedLesson, ApplicationUserRoles.Learner, "lessons", "Missed Lesson", "Notify learners after a missed lesson and route them to recovery steps.", NotificationSeverity.Warning, new(true, true, true, NotificationEmailMode.Immediate)),
            new(NotificationEventKey.LearnerWeakSkillReminder, ApplicationUserRoles.Learner, "learning", "Weak Skill Reminder", "Nudge learners to practice weak OET skill areas.", NotificationSeverity.Info, new(true, true, true, NotificationEmailMode.DailyDigest)),
            new(NotificationEventKey.LearnerHomeworkDue, ApplicationUserRoles.Learner, "lessons", "Homework Due", "Remind learners about homework due dates.", NotificationSeverity.Info, new(true, true, true, NotificationEmailMode.DailyDigest)),
            new(NotificationEventKey.LearnerLessonUnlocked, ApplicationUserRoles.Learner, "lessons", "Lesson Unlocked", "Notify learners when a new lesson is available.", NotificationSeverity.Success, new(true, true, true, NotificationEmailMode.DailyDigest)),
            new(NotificationEventKey.LearnerStreakReminder, ApplicationUserRoles.Learner, "engagement", "Streak Reminder", "Encourage learners to keep their study streak active.", NotificationSeverity.Info, new(true, false, true, NotificationEmailMode.DailyDigest)),
            new(NotificationEventKey.LearnerMockScheduled, ApplicationUserRoles.Learner, "mocks", "Mock Scheduled", "Confirm scheduled mock exams.", NotificationSeverity.Success, new(true, true, true, NotificationEmailMode.Immediate)),
            new(NotificationEventKey.LearnerMockReminder24h, ApplicationUserRoles.Learner, "mocks", "Mock Reminder 24h", "Remind learners twenty-four hours before a mock exam.", NotificationSeverity.Info, new(true, true, true, NotificationEmailMode.Immediate)),
            new(NotificationEventKey.LearnerMockReminder2h, ApplicationUserRoles.Learner, "mocks", "Mock Reminder 2h", "Remind learners two hours before a mock exam.", NotificationSeverity.Info, new(true, true, true, NotificationEmailMode.Immediate)),
            new(NotificationEventKey.LearnerMockReminder30m, ApplicationUserRoles.Learner, "mocks", "Mock Reminder 30m", "Remind learners thirty minutes before a mock exam.", NotificationSeverity.Warning, new(true, true, true, NotificationEmailMode.Immediate)),
            new(NotificationEventKey.ExpertMockReminder24h, ApplicationUserRoles.Expert, "mocks", "Mock Reminder 24h", "Remind assigned experts twenty-four hours before a mock exam.", NotificationSeverity.Info, new(true, true, true, NotificationEmailMode.Immediate)),
            new(NotificationEventKey.ExpertMockReminder2h, ApplicationUserRoles.Expert, "mocks", "Mock Reminder 2h", "Remind assigned experts two hours before a mock exam.", NotificationSeverity.Info, new(true, true, true, NotificationEmailMode.Immediate)),
            new(NotificationEventKey.ExpertMockReminder30m, ApplicationUserRoles.Expert, "mocks", "Mock Reminder 30m", "Remind assigned experts thirty minutes before a mock exam.", NotificationSeverity.Warning, new(true, true, true, NotificationEmailMode.Immediate)),
            new(NotificationEventKey.LearnerMockTechnicalCheck, ApplicationUserRoles.Learner, "mocks", "Mock Technical Check", "Prompt learners to complete technical checks before a mock exam.", NotificationSeverity.Warning, new(true, true, true, NotificationEmailMode.Immediate)),
            new(NotificationEventKey.LearnerMockStarted, ApplicationUserRoles.Learner, "mocks", "Mock Started", "Notify learners when a mock attempt starts or becomes available.", NotificationSeverity.Info, new(true, false, true, NotificationEmailMode.Off)),
            new(NotificationEventKey.LearnerMockIncomplete, ApplicationUserRoles.Learner, "mocks", "Mock Incomplete", "Notify learners when a mock attempt remains incomplete.", NotificationSeverity.Warning, new(true, true, true, NotificationEmailMode.Immediate)),
            new(NotificationEventKey.LearnerMockAutoScoreReady, ApplicationUserRoles.Learner, "results", "Mock Auto Score Ready", "Notify learners when automated mock scores are ready.", NotificationSeverity.Success, new(true, true, true, NotificationEmailMode.Immediate)),
            new(NotificationEventKey.LearnerMockTeacherFeedbackReady, ApplicationUserRoles.Learner, "results", "Mock Teacher Feedback Ready", "Notify learners when teacher feedback for a mock is ready.", NotificationSeverity.Success, new(true, true, true, NotificationEmailMode.Immediate)),
            new(NotificationEventKey.LearnerMockRetakeRecommended, ApplicationUserRoles.Learner, "mocks", "Mock Retake Recommended", "Recommend a mock retake when the platform detects a useful practice opportunity.", NotificationSeverity.Info, new(true, true, true, NotificationEmailMode.DailyDigest)),
            new(NotificationEventKey.LearnerSpeakingBooked, ApplicationUserRoles.Learner, "speaking", "Speaking Booked", "Confirm speaking session bookings.", NotificationSeverity.Success, new(true, true, true, NotificationEmailMode.Immediate)),
            new(NotificationEventKey.LearnerSpeakingReminder24h, ApplicationUserRoles.Learner, "speaking", "Speaking Reminder 24h", "Remind learners twenty-four hours before speaking sessions.", NotificationSeverity.Info, new(true, true, true, NotificationEmailMode.Immediate)),
            new(NotificationEventKey.LearnerSpeakingReminder30m, ApplicationUserRoles.Learner, "speaking", "Speaking Reminder 30m", "Remind learners thirty minutes before speaking sessions.", NotificationSeverity.Warning, new(true, true, true, NotificationEmailMode.Immediate)),
            new(NotificationEventKey.LearnerSpeakingTutorJoined, ApplicationUserRoles.Learner, "speaking", "Tutor Joined", "Notify learners when the speaking tutor joins.", NotificationSeverity.Info, new(true, false, true, NotificationEmailMode.Off)),
            new(NotificationEventKey.LearnerSpeakingStudentLate, ApplicationUserRoles.Learner, "speaking", "Speaking Session Late", "Nudge learners who are late for speaking sessions.", NotificationSeverity.Warning, new(true, true, true, NotificationEmailMode.Immediate)),
            new(NotificationEventKey.LearnerSpeakingRecordingReady, ApplicationUserRoles.Learner, "speaking", "Speaking Recording Ready", "Notify learners when a speaking recording is ready.", NotificationSeverity.Success, new(true, true, true, NotificationEmailMode.Immediate)),
            new(NotificationEventKey.LearnerSpeakingFeedbackReady, ApplicationUserRoles.Learner, "speaking", "Speaking Feedback Ready", "Notify learners when speaking feedback is ready.", NotificationSeverity.Success, new(true, true, true, NotificationEmailMode.Immediate)),
            new(NotificationEventKey.LearnerSpeakingRescheduleNeeded, ApplicationUserRoles.Learner, "speaking", "Speaking Reschedule Needed", "Notify learners when a speaking session must be rescheduled.", NotificationSeverity.Warning, new(true, true, true, NotificationEmailMode.Immediate)),
            new(NotificationEventKey.ExpertSpeakingSessionAssigned, ApplicationUserRoles.Expert, "speaking", "Speaking Session Assigned", "Notify experts when a speaking session is assigned.", NotificationSeverity.Info, new(true, true, true, NotificationEmailMode.Immediate)),
            new(NotificationEventKey.ExpertSpeakingStudentNoShow, ApplicationUserRoles.Expert, "speaking", "Student No-Show", "Notify experts when a speaking learner is marked no-show.", NotificationSeverity.Warning, new(true, true, true, NotificationEmailMode.Immediate)),
            new(NotificationEventKey.ExpertSpeakingFeedbackOverdue, ApplicationUserRoles.Expert, "speaking", "Speaking Feedback Overdue", "Notify experts when speaking feedback is overdue.", NotificationSeverity.Warning, new(true, true, true, NotificationEmailMode.DailyDigest)),
            new(NotificationEventKey.AdminSpeakingNoShowAlert, ApplicationUserRoles.Admin, "operations", "Speaking No-Show Alert", "Alert admins about speaking no-shows that may need intervention.", NotificationSeverity.Warning, new(true, true, false, NotificationEmailMode.Immediate)),
            new(NotificationEventKey.AdminSpeakingOverdueFeedbackAlert, ApplicationUserRoles.Admin, "operations", "Speaking Feedback Overdue", "Alert admins when speaking feedback is overdue.", NotificationSeverity.Critical, new(true, true, false, NotificationEmailMode.Immediate)),
            new(NotificationEventKey.LearnerWritingAssigned, ApplicationUserRoles.Learner, "writing", "Writing Assigned", "Notify learners when a writing task is assigned.", NotificationSeverity.Info, new(true, true, true, NotificationEmailMode.Immediate)),
            new(NotificationEventKey.LearnerWritingDeadlineReminder, ApplicationUserRoles.Learner, "writing", "Writing Deadline Reminder", "Remind learners about writing task deadlines.", NotificationSeverity.Warning, new(true, true, true, NotificationEmailMode.Immediate)),
            new(NotificationEventKey.LearnerWritingSubmitted, ApplicationUserRoles.Learner, "writing", "Writing Submitted", "Confirm learner writing submissions.", NotificationSeverity.Success, new(true, true, false, NotificationEmailMode.Immediate)),
            new(NotificationEventKey.LearnerWritingTeacherAssigned, ApplicationUserRoles.Learner, "writing", "Writing Teacher Assigned", "Notify learners when a writing review teacher is assigned.", NotificationSeverity.Info, new(true, true, true, NotificationEmailMode.Immediate)),
            new(NotificationEventKey.LearnerWritingFeedbackReady, ApplicationUserRoles.Learner, "writing", "Writing Feedback Ready", "Notify learners when writing feedback is ready.", NotificationSeverity.Success, new(true, true, true, NotificationEmailMode.Immediate)),
            new(NotificationEventKey.LearnerWritingRewriteRequested, ApplicationUserRoles.Learner, "writing", "Writing Rewrite Requested", "Notify learners when a writing rewrite is requested.", NotificationSeverity.Warning, new(true, true, true, NotificationEmailMode.Immediate)),
            new(NotificationEventKey.LearnerWritingRewriteOverdue, ApplicationUserRoles.Learner, "writing", "Writing Rewrite Overdue", "Remind learners when a requested writing rewrite is overdue.", NotificationSeverity.Warning, new(true, true, true, NotificationEmailMode.Immediate)),
            new(NotificationEventKey.ExpertWritingAssigned, ApplicationUserRoles.Expert, "writing", "Writing Assigned", "Notify experts when writing feedback work is assigned.", NotificationSeverity.Info, new(true, true, true, NotificationEmailMode.Immediate)),
            new(NotificationEventKey.ExpertWritingFeedbackOverdue, ApplicationUserRoles.Expert, "writing", "Writing Feedback Overdue", "Notify experts when writing feedback is overdue.", NotificationSeverity.Warning, new(true, true, true, NotificationEmailMode.DailyDigest)),
            new(NotificationEventKey.AdminWritingOverdueEscalation, ApplicationUserRoles.Admin, "operations", "Writing Overdue Escalation", "Alert admins when writing feedback or rewrites are overdue.", NotificationSeverity.Critical, new(true, true, false, NotificationEmailMode.Immediate)),
            new(NotificationEventKey.LearnerTrialStarted, ApplicationUserRoles.Learner, "billing", "Trial Started", "Notify learners when a trial starts.", NotificationSeverity.Success, new(true, true, true, NotificationEmailMode.Immediate)),
            new(NotificationEventKey.LearnerTrialEnding, ApplicationUserRoles.Learner, "billing", "Trial Ending", "Remind learners when a trial is ending soon.", NotificationSeverity.Warning, new(true, true, true, NotificationEmailMode.Immediate)),
            new(NotificationEventKey.LearnerTrialExpired, ApplicationUserRoles.Learner, "billing", "Trial Expired", "Notify learners when a trial expires.", NotificationSeverity.Warning, new(true, true, true, NotificationEmailMode.Immediate)),
            new(NotificationEventKey.LearnerPaymentSucceeded, ApplicationUserRoles.Learner, "billing", "Payment Succeeded", "Confirm successful learner payments.", NotificationSeverity.Success, new(true, true, false, NotificationEmailMode.Immediate)),
            new(NotificationEventKey.LearnerPaymentFailed, ApplicationUserRoles.Learner, "billing", "Payment Failed", "Notify learners when payment fails and action is needed.", NotificationSeverity.Critical, new(true, true, true, NotificationEmailMode.Immediate)),
            new(NotificationEventKey.LearnerRenewalComing, ApplicationUserRoles.Learner, "billing", "Renewal Coming", "Remind learners before subscription renewal.", NotificationSeverity.Info, new(true, true, false, NotificationEmailMode.Immediate)),
            new(NotificationEventKey.LearnerSubscriptionCancelled, ApplicationUserRoles.Learner, "billing", "Subscription Cancelled", "Notify learners when a subscription is cancelled.", NotificationSeverity.Warning, new(true, true, false, NotificationEmailMode.Immediate)),
            new(NotificationEventKey.LearnerCouponApplied, ApplicationUserRoles.Learner, "billing", "Coupon Applied", "Confirm coupon application.", NotificationSeverity.Success, new(true, true, false, NotificationEmailMode.Immediate)),
            new(NotificationEventKey.LearnerCreditsLow, ApplicationUserRoles.Learner, "billing", "Credits Low", "Notify learners when credits are running low.", NotificationSeverity.Warning, new(true, true, true, NotificationEmailMode.Immediate)),
            new(NotificationEventKey.LearnerCreditsExpiring, ApplicationUserRoles.Learner, "billing", "Credits Expiring", "Notify learners when credits will expire soon.", NotificationSeverity.Warning, new(true, true, true, NotificationEmailMode.Immediate)),
            new(NotificationEventKey.LearnerRefundProcessed, ApplicationUserRoles.Learner, "billing", "Refund Processed", "Notify learners when a refund is processed.", NotificationSeverity.Info, new(true, true, false, NotificationEmailMode.Immediate)),
            new(NotificationEventKey.LearnerTrialConversionNudge, ApplicationUserRoles.Learner, "marketing", "Trial Conversion Nudge", "Encourage trial learners to choose a plan when consent allows marketing contact.", NotificationSeverity.Info, new(true, true, true, NotificationEmailMode.DailyDigest)),
            new(NotificationEventKey.LearnerAbandonedCheckout, ApplicationUserRoles.Learner, "marketing", "Abandoned Checkout", "Recover learners who leave checkout before payment.", NotificationSeverity.Info, new(true, true, true, NotificationEmailMode.DailyDigest)),
            new(NotificationEventKey.LearnerInactiveNudge, ApplicationUserRoles.Learner, "engagement", "Inactive Learner Nudge", "Encourage inactive learners to return to study.", NotificationSeverity.Info, new(true, true, true, NotificationEmailMode.DailyDigest)),
            new(NotificationEventKey.LearnerExamUrgencyReminder, ApplicationUserRoles.Learner, "engagement", "Exam Urgency Reminder", "Remind learners about upcoming exam urgency and study focus.", NotificationSeverity.Warning, new(true, true, true, NotificationEmailMode.DailyDigest)),
            new(NotificationEventKey.LearnerWinBack, ApplicationUserRoles.Learner, "marketing", "Win-Back", "Invite inactive or churned learners back when consent allows marketing contact.", NotificationSeverity.Info, new(true, true, true, NotificationEmailMode.DailyDigest)),
            new(NotificationEventKey.LearnerCourseLaunch, ApplicationUserRoles.Learner, "marketing", "Course Launch", "Notify opted-in learners about new course launches.", NotificationSeverity.Info, new(true, true, true, NotificationEmailMode.DailyDigest)),
            new(NotificationEventKey.AdminSuspiciousActivityAlert, ApplicationUserRoles.Admin, "security", "Suspicious Activity Alert", "Alert admins about suspicious account activity.", NotificationSeverity.Critical, new(true, true, false, NotificationEmailMode.Immediate)),
            new(NotificationEventKey.AdminProviderOutageAlert, ApplicationUserRoles.Admin, "operations", "Provider Outage Alert", "Alert admins when an external provider appears unavailable.", NotificationSeverity.Critical, new(true, true, false, NotificationEmailMode.Immediate)),
            new(NotificationEventKey.AdminOverdueFeedbackSpikeAlert, ApplicationUserRoles.Admin, "operations", "Overdue Feedback Spike", "Alert admins when overdue feedback crosses configured thresholds.", NotificationSeverity.Critical, new(true, true, false, NotificationEmailMode.Immediate)),
            new(NotificationEventKey.AdminFailedPaymentSpikeAlert, ApplicationUserRoles.Admin, "operations", "Failed Payment Spike", "Alert admins when failed payments spike.", NotificationSeverity.Critical, new(true, true, false, NotificationEmailMode.Immediate)),
            new(NotificationEventKey.AdminCouponAbuseAlert, ApplicationUserRoles.Admin, "security", "Coupon Abuse Alert", "Alert admins about suspicious coupon activity.", NotificationSeverity.Warning, new(true, true, false, NotificationEmailMode.Immediate)),
            new(NotificationEventKey.AdminContentFlaggedAlert, ApplicationUserRoles.Admin, "operations", "Content Flagged", "Alert admins when content is flagged for review.", NotificationSeverity.Warning, new(true, true, false, NotificationEmailMode.Immediate))
        ];
    }

    private static string? BuildFallbackActionUrl(NotificationEventKey key)
    {
        if (!Entries.TryGetValue(key, out var entry))
        {
            return null;
        }

        return (entry.AudienceRole, entry.Category) switch
        {
            (ApplicationUserRoles.Learner, "account") => "/settings",
            (ApplicationUserRoles.Learner, "security") => "/settings/security",
            (ApplicationUserRoles.Learner, "billing") => "/billing",
            (ApplicationUserRoles.Learner, "lessons") => "/lessons",
            (ApplicationUserRoles.Learner, "learning") => "/learning-paths",
            (ApplicationUserRoles.Learner, "engagement") => "/dashboard",
            (ApplicationUserRoles.Learner, "marketing") => "/dashboard",
            (ApplicationUserRoles.Learner, "mocks") => "/mocks",
            (ApplicationUserRoles.Learner, "results") => "/history",
            (ApplicationUserRoles.Learner, "reminders") => "/goals",
            (ApplicationUserRoles.Learner, "speaking") => "/conversation",
            (ApplicationUserRoles.Learner, "writing") => "/lessons/writing",
            (ApplicationUserRoles.Expert, "speaking") => "/expert/private-speaking",
            (ApplicationUserRoles.Expert, "writing") => "/expert",
            (ApplicationUserRoles.Admin, "security") => "/admin/security",
            (ApplicationUserRoles.Admin, "operations") => "/admin",
            _ => entry.AudienceRole switch
            {
                ApplicationUserRoles.Expert => "/expert",
                ApplicationUserRoles.Admin => "/admin",
                _ => "/dashboard"
            }
        };
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
