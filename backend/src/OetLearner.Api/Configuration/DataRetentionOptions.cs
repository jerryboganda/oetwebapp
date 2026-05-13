namespace OetLearner.Api.Configuration;

/// <summary>
/// Retention windows for high-volume, append-only event tables. Any value
/// <see cref="TimeSpan.Zero"/> or negative disables sweeping for that table.
/// Defaults are conservative (long retention) so operators opt in to
/// shorter windows per their compliance requirements.
/// </summary>
public sealed class DataRetentionOptions
{
    /// <summary>
    /// How long <c>AnalyticsEvents</c> rows are kept. Default: 365 days.
    /// These are high-volume product analytics; long retention is useful for
    /// year-over-year comparisons but not for regulatory reasons.
    /// </summary>
    public TimeSpan AnalyticsEvents { get; set; } = TimeSpan.FromDays(365);

    /// <summary>
    /// How long <c>AuditEvents</c> rows are kept. Default: 730 days (2 years).
    /// Retention is intentionally generous — audit trail is the primary
    /// forensic artefact for admin actions and billing disputes.
    /// </summary>
    public TimeSpan AuditEvents { get; set; } = TimeSpan.FromDays(730);

    /// <summary>
    /// How long <c>PaymentWebhookEvents</c> rows are kept after processing.
    /// Default: 180 days. Stripe / PayPal retain their own copies, so the
    /// local copy is only needed for a reasonable debugging window.
    /// </summary>
    public TimeSpan PaymentWebhookEvents { get; set; } = TimeSpan.FromDays(180);

    /// <summary>
    /// Billing-hardening I-9: tiered PII retention. After this age, the
    /// <c>PaymentWebhookEvent.PayloadJson</c> column is nulled to <c>"{}"</c>
    /// by <see cref="OetLearner.Api.Services.Billing.WebhookPiiRetentionWorker"/>
    /// while the row itself (id, status, timestamps, gateway transaction id)
    /// is preserved for forensic chain-of-custody. Set to
    /// <see cref="TimeSpan.Zero"/> to disable the null-out step.
    /// Default: 90 days — half the row-delete window above.
    /// </summary>
    public TimeSpan PaymentWebhookPiiNullOutAge { get; set; } = TimeSpan.FromDays(90);

    /// <summary>
    /// How long <c>NotificationDeliveryAttempts</c> rows are kept.
    /// Default: 90 days. Enough to debug delivery issues without letting
    /// the retry-attempts table grow unbounded.
    /// </summary>
    public TimeSpan NotificationDeliveryAttempts { get; set; } = TimeSpan.FromDays(90);

    /// <summary>
    /// How often the sweeper runs. Default: 24 hours.
    /// </summary>
    public TimeSpan SweepInterval { get; set; } = TimeSpan.FromHours(24);

    /// <summary>
    /// Maximum rows deleted per table per sweep. Default: 5000.
    /// Caps the duration of any single delete to avoid long locks during
    /// one-off cleanups of a large backlog; subsequent sweeps catch up.
    /// </summary>
    public int BatchSize { get; set; } = 5000;
}
