using OetLearner.Api.Configuration;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Settings;

namespace OetLearner.Api.Tests;

internal sealed class TestRuntimeSettingsProvider(EffectiveSettings settings, RuntimeSettingsRow? raw = null) : IRuntimeSettingsProvider
{
    /// <summary>
    /// A fully-populated <see cref="EffectiveSettings"/> with every sub-record at
    /// its default. Tests override only what they care about via the record
    /// <c>with</c> expression, e.g. <c>Base() with { Zoom = ... }</c>. Adding a
    /// new sub-record to <see cref="EffectiveSettings"/> only requires a new line
    /// here — existing call-sites are unaffected.
    /// </summary>
    public static EffectiveSettings Base()
        => new(
            Email: new EmailSettings(null, null, null, null, null, null, null, null, null),
            Billing: new BillingSettings(null, null, null, null, null, null, null, null, null, null),
            Sentry: new SentrySettings(null, null, null),
            Backup: new BackupSettings(null, null, null, null, null),
            OAuth: new OAuthSettings(null, null, null, null, null, null, null, null),
            Push: new PushSettings(null, null, null, null, null, null),
            UploadScanner: new UploadScannerSettings("noop", "127.0.0.1", 3310, 30, true),
            Zoom: DefaultZoomSettings(),
            Stripe: DefaultStripeSettings(),
            LiveClasses: DefaultLiveClassSettings(),
            SpeakingWhisper: SpeakingSettingsTestDefaults.Whisper(),
            SpeakingLiveKit: SpeakingSettingsTestDefaults.LiveKit(),
            SpeakingAi: SpeakingSettingsTestDefaults.Ai(),
            SpeakingStorage: SpeakingSettingsTestDefaults.Storage(),
            SpeakingCompliance: SpeakingSettingsTestDefaults.Compliance(),
            SpeakingFeatures: SpeakingSettingsTestDefaults.Features(),
            CheckoutCom: DefaultCheckoutCom(),
            Paymob: DefaultPaymob(),
            PayTabs: DefaultPayTabs(),
            Soketi: DefaultSoketi(),
            DataRetention: DefaultDataRetention(),
            ExpertAutoAssignment: DefaultExpertAutoAssignment(),
            PasswordPolicy: DefaultPasswordPolicy(),
            UpdatedByUserId: null,
            UpdatedByUserName: null,
            UpdatedAt: null);

    public static TestRuntimeSettingsProvider FromBillingSettings(BillingSettings billing)
        => new(Base() with { Billing = billing });

    public static TestRuntimeSettingsProvider FromBillingOptions(BillingOptions options)
        => new(Base() with
        {
            Billing = new BillingSettings(
                options.Stripe.SecretKey,
                options.Stripe.PublishableKey,
                options.Stripe.WebhookSecret,
                options.Stripe.SuccessUrl,
                options.Stripe.CancelUrl,
                options.PayPal.ClientId,
                options.PayPal.ClientSecret,
                options.PayPal.WebhookId,
                options.PayPal.SuccessUrl,
                options.PayPal.CancelUrl),
        });

    public static TestRuntimeSettingsProvider FromZoomOptions(ZoomOptions options)
        => new(Base() with
        {
            Zoom = new ZoomSettings(
                options.Enabled,
                options.AccountId,
                options.ClientId,
                options.ClientSecret,
                string.IsNullOrWhiteSpace(options.ApiBaseUrl) ? "https://api.zoom.us/v2" : options.ApiBaseUrl,
                string.IsNullOrWhiteSpace(options.TokenUrl) ? "https://zoom.us/oauth/token" : options.TokenUrl,
                options.HostUserId,
                options.MeetingSdkKey,
                options.MeetingSdkSecret,
                options.WebhookSecretToken,
                Math.Clamp(options.WebhookRetryToleranceSeconds <= 0 ? 300 : options.WebhookRetryToleranceSeconds, 60, 3600),
                options.AllowSandboxFallback),
        });

    /// <summary>Convenience builder for Wave A2 tests that need the AI
    /// recording-processing flag in a specific state.</summary>
    public static TestRuntimeSettingsProvider WithLiveClassAi(bool enabled)
        => new(Base() with { LiveClasses = new LiveClassSettings(AiRecordingProcessingEnabled: enabled) });

    // ── Wave 1 (DataRetention / ExpertAutoAssignment / PasswordPolicy) ──
    // Translate the env Options the existing logic tests already build into the
    // merged settings shape, so those tests keep their call sites unchanged.
    public static TestRuntimeSettingsProvider FromDataRetentionOptions(DataRetentionOptions o)
        => new(Base() with { DataRetention = MapDataRetention(o) });

    public static TestRuntimeSettingsProvider FromExpertAutoAssignmentOptions(ExpertAutoAssignmentOptions o)
        => new(Base() with { ExpertAutoAssignment = MapExpertAutoAssignment(o) });

    public static TestRuntimeSettingsProvider FromPasswordPolicyOptions(PasswordPolicyOptions o)
        => new(Base() with { PasswordPolicy = MapPasswordPolicy(o) });

    private static ZoomSettings DefaultZoomSettings()
        => new(
            Enabled: false,
            AccountId: null,
            ClientId: null,
            ClientSecret: null,
            ApiBaseUrl: "https://api.zoom.us/v2",
            TokenUrl: "https://zoom.us/oauth/token",
            HostUserId: null,
            MeetingSdkKey: null,
            MeetingSdkSecret: null,
            WebhookSecretToken: null,
            WebhookRetryToleranceSeconds: 300,
            AllowSandboxFallback: false);

    private static StripeSettings DefaultStripeSettings()
        => new(
            SecretKey: null,
            PublishableKey: null,
            WebhookSecret: null,
            TaxAutomaticEnabled: true,
            TaxRegistrations: Array.Empty<string>(),
            CustomerPortalConfigurationId: null,
            RadarHighRiskCountryAllowReview: false,
            RadarBlockEmailDomainsCsv: null);

    private static LiveClassSettings DefaultLiveClassSettings()
        => new(AiRecordingProcessingEnabled: false);

    public static CheckoutComSettings DefaultCheckoutCom()
        => new("https://api.checkout.com", null, null, null, null, null, null);

    public static PaymobSettings DefaultPaymob()
        => new("https://accept.paymob.com", null, null, null, new Dictionary<string, int>(), 0, null, null);

    public static PayTabsSettings DefaultPayTabs()
        => new("https://secure.paytabs.com", null, null, null, null, null);

    public static SoketiSettings DefaultSoketi()
        => new("localhost", 6001, "oet-app", "oet-key", null, false, true);

    public static DataRetentionSettings DefaultDataRetention()
        => MapDataRetention(new DataRetentionOptions());

    public static ExpertAutoAssignmentSettings DefaultExpertAutoAssignment()
        => MapExpertAutoAssignment(new ExpertAutoAssignmentOptions());

    public static PasswordPolicySettings DefaultPasswordPolicy()
        => MapPasswordPolicy(new PasswordPolicyOptions());

    private static DataRetentionSettings MapDataRetention(DataRetentionOptions o)
        => new(
            o.AnalyticsEvents,
            o.AuditEvents,
            o.PaymentWebhookEvents,
            o.PaymentWebhookPiiNullOutAge,
            o.NotificationDeliveryAttempts,
            o.SweepInterval,
            o.BatchSize);

    private static ExpertAutoAssignmentSettings MapExpertAutoAssignment(ExpertAutoAssignmentOptions o)
        => new(
            o.Enabled,
            o.PollingIntervalSeconds,
            o.SlaEscalationIntervalSeconds,
            o.SlaHoursStandard,
            o.SlaHoursExpress,
            o.MaxActiveAssignmentsPerExpert,
            o.LookbackHoursForLoad,
            o.BatchSize);

    private static PasswordPolicySettings MapPasswordPolicy(PasswordPolicyOptions o)
        => new(
            o.MinimumLength,
            o.RequireMixedCase,
            o.RequireDigit,
            o.RequireSymbol,
            o.BreachCheckEnabled,
            o.BreachApiBaseUrl,
            o.BreachApiTimeout);

    public Task<EffectiveSettings> GetAsync(CancellationToken ct = default) => Task.FromResult(settings);

    public Task<RuntimeSettingsRow> GetRawAsync(CancellationToken ct = default) => Task.FromResult(raw ?? new RuntimeSettingsRow());

    public void Invalidate() { }

    public string Protect(string plain) => plain;

    public string? Unprotect(string? cipher) => cipher;
}
