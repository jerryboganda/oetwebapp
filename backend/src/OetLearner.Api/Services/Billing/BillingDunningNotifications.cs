namespace OetLearner.Api.Services.Billing;

/// <summary>
/// Wave A5 — typed wrappers for the dunning + renewal + cart notification
/// events fired through <see cref="IBillingNotificationDispatcher"/>. The
/// underlying dispatcher remains event-code/template driven (templates seeded
/// in <c>20260525100000_SeedBillingNotificationTemplates.cs</c>); these helpers
/// keep the call-site readable and pin the contract on a single point so the
/// templates can't drift.
/// </summary>
public static class BillingDunningNotifications
{
    public const string DunningAttempt1EventCode = "dunning_smart_retry_attempt_1";
    public const string DunningAttempt2EventCode = "dunning_smart_retry_attempt_2";
    public const string DunningAttempt3EventCode = "dunning_smart_retry_attempt_3";
    public const string SubscriptionLostEventCode = "subscription_lost";
    public const string RenewalReminderEventCode = "renewal_reminder_3d";
    public const string AbandonedCartRecoveryEventCode = "cart_abandoned_24h";

    public static Task SendDunningAttemptAsync(
        this IBillingNotificationDispatcher dispatcher,
        int attemptNumber,
        string userId,
        string invoiceId,
        string failureReason,
        string updateCardUrl,
        CancellationToken ct)
    {
        var code = attemptNumber switch
        {
            1 => DunningAttempt1EventCode,
            2 => DunningAttempt2EventCode,
            3 => DunningAttempt3EventCode,
            _ => DunningAttempt3EventCode,
        };

        return dispatcher.DispatchAsync(new BillingNotificationEvent(
            EventCode: code,
            EventId: $"{invoiceId}:attempt:{attemptNumber}",
            UserId: userId,
            Variables: new Dictionary<string, string>
            {
                ["attemptNumber"] = attemptNumber.ToString(),
                ["invoiceId"] = invoiceId,
                ["failureReason"] = failureReason,
                ["updateCardUrl"] = updateCardUrl,
            }), ct);
    }

    public static Task SendSubscriptionLostAsync(
        this IBillingNotificationDispatcher dispatcher,
        string userId,
        string stripeSubscriptionId,
        CancellationToken ct)
        => dispatcher.DispatchAsync(new BillingNotificationEvent(
            EventCode: SubscriptionLostEventCode,
            EventId: $"{stripeSubscriptionId}:lost",
            UserId: userId,
            Variables: new Dictionary<string, string>
            {
                ["subscriptionId"] = stripeSubscriptionId,
            }), ct);

    public static Task SendRenewalReminderAsync(
        this IBillingNotificationDispatcher dispatcher,
        string userId,
        string stripeSubscriptionId,
        DateTimeOffset renewsAt,
        string amount,
        string currency,
        CancellationToken ct)
        => dispatcher.DispatchAsync(new BillingNotificationEvent(
            EventCode: RenewalReminderEventCode,
            EventId: $"{stripeSubscriptionId}:renewal:{renewsAt:yyyyMMdd}",
            UserId: userId,
            Variables: new Dictionary<string, string>
            {
                ["renewsAt"] = renewsAt.UtcDateTime.ToString("yyyy-MM-dd"),
                ["amount"] = amount,
                ["currency"] = currency,
            }), ct);

    public static Task SendAbandonedCartRecoveryAsync(
        this IBillingNotificationDispatcher dispatcher,
        string userId,
        string cartId,
        string resumeUrl,
        CancellationToken ct)
        => dispatcher.DispatchAsync(new BillingNotificationEvent(
            EventCode: AbandonedCartRecoveryEventCode,
            EventId: $"{cartId}:recovery:24h",
            UserId: userId,
            Variables: new Dictionary<string, string>
            {
                ["cartId"] = cartId,
                ["resumeUrl"] = resumeUrl,
            }), ct);
}
