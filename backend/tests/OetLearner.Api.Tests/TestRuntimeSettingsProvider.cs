using OetLearner.Api.Configuration;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.AiAssistant;
using OetLearner.Api.Services.AiTools;
using OetLearner.Api.Services.Settings;
using OetLearner.Api.Services.Writing.Configuration;

namespace OetLearner.Api.Tests;

internal sealed class TestRuntimeSettingsProvider(EffectiveSettings settings, RuntimeSettingsRow? raw = null) : IRuntimeSettingsProvider
{
    public RuntimeSettingsSnapshot CurrentSnapshot { get; } =
        new(settings, raw ?? new RuntimeSettingsRow { Id = "default" });

    /// <summary>
    /// A fully-populated <see cref="EffectiveSettings"/> with every sub-record at
    /// its default. Tests override only what they care about via the record
    /// <c>with</c> expression, e.g. <c>Base() with { Zoom = ... }</c>. Adding a
    /// new sub-record to <see cref="EffectiveSettings"/> only requires a new line
    /// here — existing call-sites are unaffected.
    /// </summary>
    public static EffectiveSettings Base()
        => new(
            Email: new EmailSettings(
                null, null, null, null, null, null, null, null, null,
                // Wave 3 email-gap fields (defaults: flags off, SSL on).
                BrevoWelcomeTemplateId: null,
                BrevoPasswordChangedTemplateId: null,
                BrevoMfaEnabledTemplateId: null,
                BrevoAdminInviteTemplateId: null,
                BrevoSecurityAlertTemplateId: null,
                BrevoReviewCompletedTemplateId: null,
                BrevoWebhookSecret: null,
                BrevoEnabled: false,
                SmtpEnabled: false,
                SmtpEnableSsl: true),
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
            AiAssistant: DefaultAiAssistant(),
            AiGateway: DefaultAiGateway(),
            Writing: DefaultWriting(),
            Platform: DefaultPlatform(),
            Messaging: DefaultMessaging(),
            Fx: DefaultFx(),
            Storage: DefaultStorage(),
            PdfExtraction: DefaultPdfExtraction(),
            Pronunciation: DefaultPronunciation(),
            AuthTokens: DefaultAuthTokens(),
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
                options.PayPal.CancelUrl,
                PayPalAdvancedCardsEnabled: options.PayPal.AdvancedCardsEnabled,
                PublicAppBaseUrl: null,
                // ── Billing Core (non-gateway — Wave 4) ──
                CheckoutBaseUrl: options.CheckoutBaseUrl,
                WebhookMaxAgeSeconds: Math.Max(1, options.WebhookMaxAgeSeconds),
                WebhookMaxAttempts: options.WebhookMaxAttempts,
                DefaultCurrency: (string.IsNullOrWhiteSpace(options.DefaultCurrency) ? "GBP" : options.DefaultCurrency).ToUpperInvariant(),
                DefaultRegion: (string.IsNullOrWhiteSpace(options.DefaultRegion) ? "ROW" : options.DefaultRegion).ToUpperInvariant(),
                WalletCurrency: (string.IsNullOrWhiteSpace(options.Wallet.Currency) ? "AUD" : options.Wallet.Currency).ToUpperInvariant(),
                WalletTopUpTiers: options.Wallet.TopUpTiers,
                PayPalUseSandbox: options.PayPal.UseSandbox,
                PayPalApiBaseUrl: string.IsNullOrWhiteSpace(options.PayPal.ApiBaseUrl) ? "https://api-m.paypal.com" : options.PayPal.ApiBaseUrl),
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

    // ── Wave 2 (AiAssistant / AiGateway / Writing / Platform) ──────
    public static TestRuntimeSettingsProvider FromAiAssistantOptions(AiAssistantOptions o)
        => new(Base() with { AiAssistant = MapAiAssistant(o) });

    public static TestRuntimeSettingsProvider FromAiGatewayOptions(AiProviderOptions provider, AiToolOptions tool)
        => new(Base() with { AiGateway = MapAiGateway(provider, tool) });

    public static TestRuntimeSettingsProvider FromWritingOptions(WritingV2Options o)
        => new(Base() with { Writing = MapWriting(o) });

    public static TestRuntimeSettingsProvider FromPlatformOptions(PlatformOptions o)
        => new(Base() with { Platform = MapPlatform(o) });

    // ── Wave 3 (Messaging) ─────────────────────────────────────────
    public static TestRuntimeSettingsProvider FromMessagingOptions(TwilioOptions twilio, WhatsAppOptions whatsApp)
        => new(Base() with { Messaging = MapMessaging(twilio, whatsApp) });

    // ── Wave 4 (Fx / Storage / PdfExtraction / Pronunciation / AuthTokens) ──
    public static TestRuntimeSettingsProvider FromFxOptions(FxOptions o)
        => new(Base() with { Fx = MapFx(o) });

    public static TestRuntimeSettingsProvider FromStorageOptions(StorageOptions o)
        => new(Base() with { Storage = MapStorage(o) });

    public static TestRuntimeSettingsProvider FromPdfExtractionOptions(PdfExtractionOptions o)
        => new(Base() with { PdfExtraction = MapPdfExtraction(o) });

    public static TestRuntimeSettingsProvider FromPronunciationOptions(PronunciationOptions o)
        => new(Base() with { Pronunciation = MapPronunciation(o) });

    public static TestRuntimeSettingsProvider FromAuthTokenOptions(AuthTokenOptions o)
        => new(Base() with { AuthTokens = MapAuthTokens(o) });

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

    // ── Wave 2 default factories + Option mappers ──────────────────
    public static AiAssistantSettings DefaultAiAssistant()
        => MapAiAssistant(new AiAssistantOptions());

    public static AiGatewaySettings DefaultAiGateway()
        => MapAiGateway(new AiProviderOptions(), new AiToolOptions());

    public static WritingSettings DefaultWriting()
        => MapWriting(new WritingV2Options());

    public static PlatformSettings DefaultPlatform()
        => MapPlatform(new PlatformOptions());

    private static AiAssistantSettings MapAiAssistant(AiAssistantOptions o)
        => new(
            o.GlobalEnabled,
            o.RequireApprovalAlways,
            o.MaxIterations,
            o.MaxContextMessages,
            o.BackupRetentionDays,
            o.MaxWriteFileSizeBytes,
            o.CommandTimeoutSeconds,
            o.CircuitBreakerMaxFailures,
            o.CircuitBreakerFailureWindowSeconds,
            o.CircuitBreakerMaxWrites,
            o.CircuitBreakerWriteWindowSeconds,
            o.EmbeddingModel,
            o.MaxChunkTokens);

    private static AiGatewaySettings MapAiGateway(AiProviderOptions provider, AiToolOptions tool)
    {
        var hosts = (tool.AllowedExternalHosts is { Length: > 0 }
                ? tool.AllowedExternalHosts
                : ["api.dictionaryapi.dev"])
            .Select(static h => h.ToLowerInvariant())
            .Distinct()
            .ToArray();
        return new AiGatewaySettings(
            ProviderId: string.IsNullOrWhiteSpace(provider.ProviderId) ? "digitalocean-serverless" : provider.ProviderId,
            BaseUrl: string.IsNullOrWhiteSpace(provider.BaseUrl) ? "https://inference.do-ai.run/v1" : provider.BaseUrl,
            DefaultModel: string.IsNullOrWhiteSpace(provider.DefaultModel) ? "glm-5" : provider.DefaultModel,
            ReasoningEffort: provider.ReasoningEffort ?? string.Empty,
            DefaultMaxTokens: provider.DefaultMaxTokens > 0 ? provider.DefaultMaxTokens : 4096,
            DefaultTemperature: Math.Clamp(provider.DefaultTemperature, 0.0, 1.0),
            MaxToolCallsPerCompletion: tool.MaxToolCallsPerCompletion > 0 ? tool.MaxToolCallsPerCompletion : 4,
            FeatureGrantCacheSeconds: tool.FeatureGrantCacheSeconds > 0 ? tool.FeatureGrantCacheSeconds : 30,
            AllowedExternalHostsCsv: string.Join(",", hosts),
            AllowedExternalHosts: hosts,
            ExternalNetworkPerUserDailyCalls: tool.ExternalNetworkPerUserDailyCalls >= 0 ? tool.ExternalNetworkPerUserDailyCalls : 200,
            ExternalNetworkTimeoutMilliseconds: tool.ExternalNetworkTimeoutMilliseconds > 0 ? tool.ExternalNetworkTimeoutMilliseconds : 4000,
            ExternalNetworkMaxResponseBytes: tool.ExternalNetworkMaxResponseBytes > 0 ? tool.ExternalNetworkMaxResponseBytes : 65536);
    }

    private static WritingSettings MapWriting(WritingV2Options o)
        => new(
            o.CronsEnabled,
            o.CoachEnabled,
            o.CoachDailyCostCapPerLearnerUsd,
            o.CoachMaxHintsPerSession > 0 ? o.CoachMaxHintsPerSession : 80,
            o.CoachMinSecondsBetweenHints > 0 ? o.CoachMinSecondsBetweenHints : 30,
            o.GcvApiKey,
            o.OcrEnabled,
            o.AppealsEnabled,
            o.TutorReviewQueueMaxDepth > 0 ? o.TutorReviewQueueMaxDepth : 50,
            o.TutorReviewMaxWaitHours > 0 ? o.TutorReviewMaxWaitHours : 36,
            o.MaxDailyPlanRegenerationsPerDay > 0 ? o.MaxDailyPlanRegenerationsPerDay : 1,
            o.GradeIdempotencyTtlHours > 0 ? o.GradeIdempotencyTtlHours : 24);

    private static PlatformSettings MapPlatform(PlatformOptions o)
        => new(
            o.PublicApiBaseUrl,
            o.PublicWebBaseUrl,
            string.IsNullOrWhiteSpace(o.FallbackEmailDomain) ? "example.invalid" : o.FallbackEmailDomain);

    // ── Wave 3 default factory + Option mapper (Messaging) ──────────
    public static MessagingSettings DefaultMessaging()
        => MapMessaging(new TwilioOptions(), new WhatsAppOptions());

    private static MessagingSettings MapMessaging(TwilioOptions twilio, WhatsAppOptions whatsApp)
        => new(
            TwilioEnabled: twilio.Enabled,
            TwilioApiBaseUrl: string.IsNullOrWhiteSpace(twilio.ApiBaseUrl) ? "https://api.twilio.com" : twilio.ApiBaseUrl,
            TwilioAccountSid: twilio.AccountSid,
            TwilioAuthToken: twilio.AuthToken,
            TwilioFromNumber: twilio.FromNumber,
            TwilioMessagingServiceSid: twilio.MessagingServiceSid,
            WhatsAppEnabled: whatsApp.Enabled,
            WhatsAppApiBaseUrl: string.IsNullOrWhiteSpace(whatsApp.ApiBaseUrl) ? "https://graph.facebook.com/v20.0" : whatsApp.ApiBaseUrl,
            WhatsAppAccessToken: whatsApp.AccessToken,
            WhatsAppPhoneNumberId: whatsApp.PhoneNumberId,
            WhatsAppFallbackTemplateName: whatsApp.FallbackTemplateName,
            IsTwilioConfigured: twilio.Enabled
                && !string.IsNullOrWhiteSpace(twilio.AccountSid)
                && !string.IsNullOrWhiteSpace(twilio.AuthToken),
            IsWhatsAppConfigured: whatsApp.Enabled
                && !string.IsNullOrWhiteSpace(whatsApp.AccessToken)
                && !string.IsNullOrWhiteSpace(whatsApp.PhoneNumberId));

    // ── Wave 4 default factories + Option mappers ──────────────────
    public static FxSettings DefaultFx() => MapFx(new FxOptions());
    public static StorageSettings DefaultStorage() => MapStorage(new StorageOptions());
    public static PdfExtractionSettings DefaultPdfExtraction() => MapPdfExtraction(new PdfExtractionOptions());
    public static PronunciationSettings DefaultPronunciation() => MapPronunciation(new PronunciationOptions());
    public static AuthTokenSettings DefaultAuthTokens() => MapAuthTokens(new AuthTokenOptions());

    private static FxSettings MapFx(FxOptions o)
        => new(
            BaseCurrency: (string.IsNullOrWhiteSpace(o.BaseCurrency) ? "USD" : o.BaseCurrency).ToUpperInvariant(),
            ApiKey: string.IsNullOrWhiteSpace(o.ApiKey) ? null : o.ApiKey,
            ApiBaseUrl: string.IsNullOrWhiteSpace(o.ApiBaseUrl) ? null : o.ApiBaseUrl,
            DynamicPricingEnabled: o.DynamicPricingEnabled);

    private static StorageSettings MapStorage(StorageOptions o)
    {
        var cu = o.ContentUpload;
        return new StorageSettings(
            Provider: string.IsNullOrWhiteSpace(o.Provider) ? "local" : o.Provider,
            BucketName: string.IsNullOrWhiteSpace(o.BucketName) ? null : o.BucketName,
            EndpointUrl: string.IsNullOrWhiteSpace(o.EndpointUrl) ? null : o.EndpointUrl,
            AccessKeyId: string.IsNullOrWhiteSpace(o.AccessKeyId) ? null : o.AccessKeyId,
            SecretAccessKey: string.IsNullOrWhiteSpace(o.SecretAccessKey) ? null : o.SecretAccessKey,
            AwsRegion: string.IsNullOrWhiteSpace(o.AwsRegion) ? "us-east-1" : o.AwsRegion,
            SignedReadTtlSeconds: o.SignedReadTtlSeconds > 0 ? o.SignedReadTtlSeconds : 3600,
            MaxAudioBytes: cu.MaxAudioBytes > 0 ? cu.MaxAudioBytes : 150L * 1024 * 1024,
            MaxPdfBytes: cu.MaxPdfBytes > 0 ? cu.MaxPdfBytes : 25L * 1024 * 1024,
            MaxImageBytes: cu.MaxImageBytes > 0 ? cu.MaxImageBytes : 5L * 1024 * 1024,
            MaxZipBytes: cu.MaxZipBytes > 0 ? cu.MaxZipBytes : 500L * 1024 * 1024,
            MaxZipEntries: cu.MaxZipEntries > 0 ? cu.MaxZipEntries : 5000,
            MaxZipEntryBytes: cu.MaxZipEntryBytes > 0 ? cu.MaxZipEntryBytes : 150L * 1024 * 1024,
            MaxZipUncompressedBytes: cu.MaxZipUncompressedBytes > 0 ? cu.MaxZipUncompressedBytes : 2L * 1024 * 1024 * 1024,
            MaxZipCompressionRatio: cu.MaxZipCompressionRatio > 0 ? cu.MaxZipCompressionRatio : 100.0,
            ChunkSizeBytes: cu.ChunkSizeBytes > 0 ? cu.ChunkSizeBytes : 8L * 1024 * 1024,
            StagingTtlHours: cu.StagingTtlHours > 0 ? cu.StagingTtlHours : 24);
    }

    private static PdfExtractionSettings MapPdfExtraction(PdfExtractionOptions o)
        => new(
            Provider: string.IsNullOrWhiteSpace(o.Provider) ? "auto" : o.Provider,
            AzureEndpoint: o.AzureEndpoint ?? string.Empty,
            AzureApiKey: string.IsNullOrWhiteSpace(o.AzureApiKey) ? null : o.AzureApiKey,
            MinTextLengthForSuccess: o.MinTextLengthForSuccess > 0 ? o.MinTextLengthForSuccess : 50);

    private static PronunciationSettings MapPronunciation(PronunciationOptions o)
        => new(
            Provider: string.IsNullOrWhiteSpace(o.Provider) ? "auto" : o.Provider,
            AzureSpeechRegion: o.AzureSpeechRegion ?? string.Empty,
            AzureLocale: string.IsNullOrWhiteSpace(o.AzureLocale) ? "en-GB" : o.AzureLocale,
            WhisperBaseUrl: o.WhisperBaseUrl ?? string.Empty,
            WhisperModel: string.IsNullOrWhiteSpace(o.WhisperModel) ? "whisper-1" : o.WhisperModel,
            GeminiBaseUrl: string.IsNullOrWhiteSpace(o.GeminiBaseUrl) ? "https://generativelanguage.googleapis.com/v1beta" : o.GeminiBaseUrl,
            GeminiModel: string.IsNullOrWhiteSpace(o.GeminiModel) ? "gemini-3.5-flash" : o.GeminiModel,
            MaxAudioBytes: o.MaxAudioBytes > 0 ? o.MaxAudioBytes : 15L * 1024 * 1024,
            AudioRetentionDays: o.AudioRetentionDays > 0 ? o.AudioRetentionDays : 45,
            FreeTierWeeklyAttemptLimit: o.FreeTierWeeklyAttemptLimit,
            FreeTierWindowDays: o.FreeTierWindowDays > 0 ? o.FreeTierWindowDays : 7);

    private static AuthTokenSettings MapAuthTokens(AuthTokenOptions o)
        => new(
            AccessTokenLifetime: o.AccessTokenLifetime > TimeSpan.Zero ? o.AccessTokenLifetime : TimeSpan.FromHours(1),
            RefreshTokenLifetime: o.RefreshTokenLifetime > TimeSpan.Zero ? o.RefreshTokenLifetime : TimeSpan.FromDays(30),
            OtpLifetime: o.OtpLifetime > TimeSpan.Zero ? o.OtpLifetime : TimeSpan.FromMinutes(5),
            AuthenticatorIssuer: o.AuthenticatorIssuer);

    public Task<EffectiveSettings> GetAsync(CancellationToken ct = default) => Task.FromResult(settings);

    public Task<RuntimeSettingsRow> GetRawAsync(CancellationToken ct = default) => Task.FromResult(raw ?? new RuntimeSettingsRow());

    public void Invalidate() { }

    public string Protect(string plain) => plain;

    public string? Unprotect(string? cipher) => cipher;
}
