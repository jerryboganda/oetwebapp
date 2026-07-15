using OetLearner.Api.Configuration;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services.Settings;

/// <summary>
/// Resolves the effective runtime infrastructure settings by merging
/// env-var / appsettings defaults with DB overrides from the
/// <see cref="RuntimeSettingsRow"/> singleton (Id = "default").
///
/// Secrets stored in the DB row are encrypted via ASP.NET Data Protection
/// under the purpose <c>RuntimeSettings.Secret.v1</c>. Decryption happens
/// inside this provider only — callers in the API layer never see ciphertext.
/// Results are cached in <see cref="Microsoft.Extensions.Caching.Memory.IMemoryCache"/>
/// for 30 seconds; the admin PUT endpoint invokes <see cref="Invalidate"/>
/// after a successful save so subsequent requests pick up the new values
/// without an app restart.
/// </summary>
public interface IRuntimeSettingsProvider
{
    /// <summary>
    /// Last successfully resolved DB-over-env snapshot. Synchronous consumers
    /// may inspect this value, but must never trigger I/O while doing so.
    /// Implementations that do not maintain a snapshot may return <c>null</c>.
    /// </summary>
    RuntimeSettingsSnapshot? CurrentSnapshot => null;

    /// <summary>
    /// Return the merged settings and the raw row from the same load. The
    /// production provider overrides this with its cached single-flight load.
    /// The default only returns an already-published atomic snapshot.
    /// </summary>
    Task<RuntimeSettingsSnapshot> GetSnapshotAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return CurrentSnapshot is { } snapshot
            ? Task.FromResult(snapshot)
            : Task.FromException<RuntimeSettingsSnapshot>(
                new InvalidOperationException("This runtime settings provider does not expose an atomic snapshot."));
    }

    /// <summary>Return the merged (env + DB-override) effective settings.</summary>
    Task<EffectiveSettings> GetAsync(CancellationToken ct = default);

    /// <summary>
    /// Return the raw DB row (un-decrypted ciphertext stays as ciphertext) for
    /// audit/diff use. Returns a fresh blank row with Id="default" when nothing
    /// has been stored yet.
    /// </summary>
    Task<RuntimeSettingsRow> GetRawAsync(CancellationToken ct = default);

    /// <summary>Drop the cached effective view; next read re-fetches from the DB.</summary>
    void Invalidate();

    /// <summary>Encrypt a plain secret with the runtime-settings protector.</summary>
    string Protect(string plain);

    /// <summary>Decrypt a previously-protected secret. Returns null on null/empty or failure.</summary>
    string? Unprotect(string? cipher);
}

/// <summary>
/// Atomically published runtime-settings view. <see cref="Effective"/> and
/// <see cref="Raw"/> always originate from the same database load.
/// </summary>
public sealed record RuntimeSettingsSnapshot(
    EffectiveSettings Effective,
    RuntimeSettingsRow Raw);

/// <summary>
/// The merged effective view of all runtime infrastructure settings.
/// Secrets are decrypted plain strings; non-secret strings stay plain.
/// All inner records are immutable snapshots — to mutate, write to the
/// DB row and call <see cref="IRuntimeSettingsProvider.Invalidate"/>.
/// </summary>
public sealed record EffectiveSettings(
    EmailSettings Email,
    BillingSettings Billing,
    SentrySettings Sentry,
    BackupSettings Backup,
    OAuthSettings OAuth,
    PushSettings Push,
    UploadScannerSettings UploadScanner,
    ZoomSettings Zoom,
    StripeSettings Stripe,
    LiveClassSettings LiveClasses,
    SpeakingWhisperSettings SpeakingWhisper,
    SpeakingLiveKitSettings SpeakingLiveKit,
    SpeakingAiSettings SpeakingAi,
    SpeakingStorageSettings SpeakingStorage,
    SpeakingComplianceSettings SpeakingCompliance,
    SpeakingFeatureSettings SpeakingFeatures,
    CheckoutComSettings CheckoutCom,
    PaymobSettings Paymob,
    PayTabsSettings PayTabs,
    SoketiSettings Soketi,
    DataRetentionSettings DataRetention,
    ExpertAutoAssignmentSettings ExpertAutoAssignment,
    PasswordPolicySettings PasswordPolicy,
    AiAssistantSettings AiAssistant,
    AiGatewaySettings AiGateway,
    WritingSettings Writing,
    PlatformSettings Platform,
    MessagingSettings Messaging,
    FxSettings Fx,
    StorageSettings Storage,
    PdfExtractionSettings PdfExtraction,
    PronunciationSettings Pronunciation,
    AuthTokenSettings AuthTokens,
    string? UpdatedByUserId,
    string? UpdatedByUserName,
    DateTimeOffset? UpdatedAt)
{
    // ── Video Library (Bunny Stream + playback attestation) ────────
    // Declared as init-only properties (not positional params) so existing
    // EffectiveSettings construction sites keep compiling unchanged; the
    // provider sets them via object initializer in Merge.
    public BunnyStreamSettings BunnyStream { get; init; } = BunnyStreamSettings.Unconfigured;
    public VideoAttestationSettings VideoAttestation { get; init; } = VideoAttestationSettings.Unconfigured;

    // EasyKash (Egypt hosted Direct-Pay). Init-only property (like BunnyStream)
    // so the existing EffectiveSettings construction sites keep compiling; the
    // provider sets it via object initializer in Merge.
    public EasyKashSettings EasyKash { get; init; } = EasyKashSettings.Unconfigured;

    // Support (public WhatsApp proof channel). Init-only property (like
    // BunnyStream) so the existing EffectiveSettings construction sites keep
    // compiling; the provider sets it via object initializer in Merge.
    public SupportSettings Support { get; init; } = SupportSettings.Unconfigured;
}

/// <summary>
/// Public support channel settings (DB-over-env merged). <see cref="WhatsAppNumber"/>
/// is the wa.me-dialable number printed next to every package ("send your proof
/// on WhatsApp") — it is public, NOT a secret, and is therefore stored plain.
///
/// <para>
/// This is a different concept from <see cref="MessagingSettings.WhatsAppPhoneNumberId"/>,
/// which is the Meta Cloud API sender id used to POST outbound template messages
/// and is not dialable by a learner. Do not conflate the two.
/// </para>
/// </summary>
public sealed record SupportSettings(
    string? WhatsAppNumber,
    string? WhatsAppProofTemplate)
{
    public bool IsWhatsAppConfigured => !string.IsNullOrWhiteSpace(WhatsAppNumber);

    public static SupportSettings Unconfigured { get; } = new(null, null);
}

/// <summary>
/// Bunny Stream (Video Library CDN) settings (DB-over-env merged). ApiKey,
/// TokenAuthKey and WebhookSecret are secrets decrypted by the provider.
/// <see cref="IsConfigured"/> gates every live Bunny call — the feature is
/// dormant (503 bunny_not_configured) until an admin supplies credentials
/// AND flips <see cref="Enabled"/> on.
/// </summary>
public sealed record BunnyStreamSettings(
    bool Enabled,
    string? LibraryId,
    string? ApiKey,
    string? CdnHostname,
    string? TokenAuthKey,
    string? WebhookSecret,
    string? CollectionId,
    int PlaybackTokenTtlSeconds)
{
    public bool IsConfigured => Enabled
        && !string.IsNullOrWhiteSpace(LibraryId)
        && !string.IsNullOrWhiteSpace(ApiKey)
        && !string.IsNullOrWhiteSpace(CdnHostname)
        && !string.IsNullOrWhiteSpace(TokenAuthKey);

    public static BunnyStreamSettings Unconfigured { get; } =
        new(false, null, null, null, null, null, null, 14400);
}

/// <summary>
/// Video Library playback attestation keys (DB-over-env merged). Map of
/// "{platform}:{keyId}" → lowercase-hex HMAC-SHA256 secret. Playback sessions
/// are refused with 403 attestation_unavailable while unconfigured.
/// </summary>
public sealed record VideoAttestationSettings(
    IReadOnlyDictionary<string, string> Keys)
{
    public bool IsConfigured => Keys.Count > 0;

    public static VideoAttestationSettings Unconfigured { get; } =
        new(new Dictionary<string, string>(StringComparer.Ordinal));
}

public sealed record EmailSettings(
    string? BrevoApiKey,
    int? BrevoEmailVerificationTemplateId,
    int? BrevoPasswordResetTemplateId,
    string? SmtpHost,
    int? SmtpPort,
    string? SmtpUsername,
    string? SmtpPassword,
    string? SmtpFromAddress,
    string? SmtpFromName,
    // ── Email partial-coverage gap (Wave 3) ────────────────────────
    // Optional positional params with defaults so existing 9-arg call-sites
    // keep compiling. Brevo:WebhookSecret is decrypted by the provider.
    int? BrevoWelcomeTemplateId = null,
    int? BrevoPasswordChangedTemplateId = null,
    int? BrevoMfaEnabledTemplateId = null,
    int? BrevoAdminInviteTemplateId = null,
    int? BrevoSecurityAlertTemplateId = null,
    int? BrevoReviewCompletedTemplateId = null,
    string? BrevoWebhookSecret = null,
    bool BrevoEnabled = false,
    bool SmtpEnabled = false,
    bool SmtpEnableSsl = true);

public sealed record BillingSettings(
    string? StripeSecretKey,
    string? StripePublishableKey,
    string? StripeWebhookSecret,
    string? StripeSuccessUrl,
    string? StripeCancelUrl,
    string? PayPalClientId,
    string? PayPalClientSecret,
    string? PayPalWebhookId,
    string? PayPalSuccessUrl,
    string? PayPalCancelUrl,
    bool PayPalAdvancedCardsEnabled = true,
    string? PublicAppBaseUrl = null,
    // ── Billing Core (non-gateway — Wave 4) ────────────────────────
    // Optional positional params with defaults so existing call-sites keep
    // compiling. These are the core (non-gateway) billing knobs.
    string? CheckoutBaseUrl = null,
    int WebhookMaxAgeSeconds = 300,
    int WebhookMaxAttempts = 5,
    string DefaultCurrency = "GBP",
    string DefaultRegion = "ROW",
    string WalletCurrency = "AUD",
    IReadOnlyList<WalletTopUpTierOption>? WalletTopUpTiers = null,
    bool PayPalUseSandbox = true,
    string PayPalApiBaseUrl = "https://api-m.paypal.com");

public sealed record SentrySettings(
    string? Dsn,
    string? Environment,
    double? SampleRate);

public sealed record BackupSettings(
    string? S3Url,
    string? AwsAccessKeyId,
    string? AwsSecretAccessKey,
    string? GpgPassphrase,
    string? AlertWebhook);

public sealed record OAuthSettings(
    string? GoogleClientId,
    string? GoogleClientSecret,
    string? AppleClientId,
    string? AppleTeamId,
    string? AppleKeyId,
    string? ApplePrivateKey,
    string? FacebookAppId,
    string? FacebookAppSecret,
    // ── Auth external providers (Wave 4) ───────────────────────────
    // LinkedIn is the genuine OAuth gap (ClientId + ClientSecret are secrets,
    // decrypted by the provider). Per-provider Enabled toggles default to env.
    // Optional positional params with defaults so existing call-sites compile.
    string? LinkedInClientId = null,
    string? LinkedInClientSecret = null,
    bool LinkedInEnabled = false,
    bool GoogleAuthEnabled = false,
    bool FacebookAuthEnabled = false);

public sealed record PushSettings(
    string? ApnsKeyId,
    string? ApnsTeamId,
    string? ApnsBundleId,
    string? ApnsAuthKey,
    string? FcmServerKey,
    string? FcmProjectId,
    string? VapidSubject = null,
    string? VapidPublicKey = null,
    string? VapidPrivateKey = null,
    // ── Web push enablement (Wave 4) ───────────────────────────────
    // WebPush:Enabled master toggle. VAPID keys already live on this record.
    bool WebPushEnabled = false);

public sealed record UploadScannerSettings(
    string Provider,
    string Host,
    int Port,
    int TimeoutSeconds,
    bool FailClosedOnError);

public sealed record ZoomSettings(
    bool Enabled,
    string? AccountId,
    string? ClientId,
    string? ClientSecret,
    string ApiBaseUrl,
    string TokenUrl,
    string? HostUserId,
    string? MeetingSdkKey,
    string? MeetingSdkSecret,
    string? WebhookSecretToken,
    int WebhookRetryToleranceSeconds,
    bool AllowSandboxFallback);

/// <summary>
/// Wave A5 Stripe runtime overrides — Tax automatic calculation, Customer
/// Portal configuration, and Radar fraud knobs. Secret keys remain on
/// <see cref="BillingSettings"/> (StripeSecretKey / StripeWebhookSecret) so
/// existing call-sites don't need rewiring; this record carries the
/// non-secret Tax/Portal/Radar tunables in one place.
/// </summary>
public sealed record StripeSettings(
    string? SecretKey,
    string? PublishableKey,
    string? WebhookSecret,
    bool TaxAutomaticEnabled,
    IReadOnlyList<string> TaxRegistrations,
    string? CustomerPortalConfigurationId,
    bool RadarHighRiskCountryAllowReview,
    string? RadarBlockEmailDomainsCsv);

/// <summary>
/// Wave A2 — Live Classes runtime knobs. Currently a single feature flag for
/// the AI recording-processing pipeline; defaults <b>off</b> so the gateway
/// never makes platform AI calls until an admin explicitly enables it.
/// </summary>
public sealed record LiveClassSettings(
    bool AiRecordingProcessingEnabled);

/// <summary>
/// 2026-05-28 audit fix — Speaking Whisper transcription overrides. Lets an
/// admin rotate the OpenAI API key from the admin UI without an app restart.
/// Decrypted by the provider — callers see plain values. When no DB override
/// is set, falls back to `Speaking:Whisper:*` in appsettings.
/// </summary>
public sealed record SpeakingWhisperSettings(
    string? ApiKey,
    string BaseUrl,
    string Model,
    bool IsConfigured);

/// <summary>
/// Speaking LiveKit configuration — live tutor rooms + egress recording.
/// Admin-configurable from the admin panel without env-file edits.
/// </summary>
public sealed record SpeakingLiveKitSettings(
    string Provider,
    string? ApiKey,
    string? ApiSecret,
    string? WssUrl,
    string? WebhookSigningSecret,
    string? EgressBucket,
    int DefaultMaxDurationSeconds,
    bool EgressEnabled,
    bool IsEnabled);

/// <summary>
/// Speaking AI provider settings — Anthropic (scoring + patient turns) and
/// ElevenLabs (AI patient TTS).
/// </summary>
public sealed record SpeakingAiSettings(
    string? AnthropicApiKey,
    string? ElevenLabsApiKey,
    bool IsAnthropicConfigured,
    bool IsElevenLabsConfigured);

/// <summary>
/// Speaking AWS S3 recording storage settings.
/// </summary>
public sealed record SpeakingStorageSettings(
    string? AwsAccessKeyId,
    string? AwsSecretAccessKey,
    string Region,
    string? Bucket,
    bool IsConfigured);

/// <summary>
/// Speaking compliance settings — consent versioning + retention windows.
/// </summary>
public sealed record SpeakingComplianceSettings(
    string CurrentConsentVersion,
    string CurrentLiveVideoConsentVersion,
    int RetentionDaysDefault,
    int RetentionDaysWhenTutorReviewed,
    int AuditLogRetentionDays);

/// <summary>
/// Speaking feature flags — controls the full v2 module rollout.
/// </summary>
public sealed record SpeakingFeatureSettings(
    bool SpeakingV2Enabled);

/// <summary>Checkout.com payment gateway settings (DB-over-env merged).
/// Secrets decrypted; <see cref="IsConfigured"/> gates live calls.</summary>
public sealed record CheckoutComSettings(
    string ApiBaseUrl,
    string? SecretKey,
    string? PublicKey,
    string? ProcessingChannelId,
    string? WebhookSecret,
    string? SuccessUrl,
    string? CancelUrl)
{
    public bool IsConfigured => !string.IsNullOrWhiteSpace(SecretKey);
}

/// <summary>Paymob payment gateway settings (DB-over-env merged).</summary>
public sealed record PaymobSettings(
    string ApiBaseUrl,
    string? ApiKey,
    string? MerchantId,
    string? HmacSecret,
    IReadOnlyDictionary<string, int> IntegrationIds,
    int IframeId,
    string? SuccessUrl,
    string? CancelUrl)
{
    public bool IsConfigured => !string.IsNullOrWhiteSpace(ApiKey) && !string.IsNullOrWhiteSpace(MerchantId);
}

/// <summary>PayTabs payment gateway settings (DB-over-env merged).</summary>
public sealed record PayTabsSettings(
    string ApiBaseUrl,
    string? ServerKey,
    string? ProfileId,
    string? WebhookSecret,
    string? SuccessUrl,
    string? CancelUrl)
{
    public bool IsConfigured => !string.IsNullOrWhiteSpace(ServerKey) && !string.IsNullOrWhiteSpace(ProfileId);
}

/// <summary>
/// EasyKash payment gateway settings (DB-over-env merged). Secrets decrypted.
/// <see cref="IsConfigured"/> requires BOTH the API key and the HMAC secret —
/// the HMAC secret only exists after the merchant saves the Callback URL +
/// payment methods in the EasyKash dashboard, so the gateway stays hidden at
/// checkout until it can both create AND verify a payment.
/// </summary>
public sealed record EasyKashSettings(
    string ApiBaseUrl,
    string? ApiKey,
    string? HmacSecret,
    IReadOnlyList<int> PaymentOptions,
    string CurrencyMode,
    string? SuccessUrl,
    string? CancelUrl)
{
    public bool IsConfigured => !string.IsNullOrWhiteSpace(ApiKey) && !string.IsNullOrWhiteSpace(HmacSecret);

    /// <summary>True when the admin wants amounts converted to EGP before charging.</summary>
    public bool ConvertToEgp => string.Equals(CurrencyMode, "egp", StringComparison.OrdinalIgnoreCase);

    public static EasyKashSettings Unconfigured { get; } =
        new("https://back.easykash.net", null, null, System.Array.Empty<int>(), "passthrough", null, null);
}

/// <summary>Soketi realtime websocket push settings (DB-over-env merged).</summary>
public sealed record SoketiSettings(
    string Host,
    int Port,
    string AppId,
    string AppKey,
    string? AppSecret,
    bool UseTls,
    bool Enabled);

/// <summary>
/// Data-retention sweeper windows (DB-over-env merged). Windows are exposed as
/// <see cref="TimeSpan"/> so the worker logic is unchanged; a value of
/// <see cref="TimeSpan.Zero"/> disables the sweep for that table.
/// </summary>
public sealed record DataRetentionSettings(
    TimeSpan AnalyticsEvents,
    TimeSpan AuditEvents,
    TimeSpan PaymentWebhookEvents,
    TimeSpan PaymentWebhookPiiNullOutAge,
    TimeSpan NotificationDeliveryAttempts,
    TimeSpan SweepInterval,
    int BatchSize);

/// <summary>Expert auto-assignment loop tunables (DB-over-env merged).</summary>
public sealed record ExpertAutoAssignmentSettings(
    bool Enabled,
    int PollingIntervalSeconds,
    int SlaEscalationIntervalSeconds,
    int SlaHoursStandard,
    int SlaHoursExpress,
    int MaxActiveAssignmentsPerExpert,
    int LookbackHoursForLoad,
    int BatchSize);

/// <summary>Password-policy enforcement settings (DB-over-env merged).</summary>
public sealed record PasswordPolicySettings(
    int MinimumLength,
    bool RequireMixedCase,
    bool RequireDigit,
    bool RequireSymbol,
    bool BreachCheckEnabled,
    string BreachApiBaseUrl,
    TimeSpan BreachApiTimeout);

/// <summary>
/// AI Assistant orchestration tunables (DB-over-env merged). Covers the ReAct
/// loop, circuit breaker, command/backup limits, and embedding/indexing knobs.
/// AI provider CREDENTIALS are NOT here — those live in AiProviderRegistry.
/// </summary>
public sealed record AiAssistantSettings(
    bool GlobalEnabled,
    bool RequireApprovalAlways,
    int MaxIterations,
    int MaxContextMessages,
    int BackupRetentionDays,
    long MaxWriteFileSizeBytes,
    int CommandTimeoutSeconds,
    int CircuitBreakerMaxFailures,
    int CircuitBreakerFailureWindowSeconds,
    int CircuitBreakerMaxWrites,
    int CircuitBreakerWriteWindowSeconds,
    string EmbeddingModel,
    int MaxChunkTokens);

/// <summary>
/// AI gateway / tooling non-credential knobs (DB-over-env merged). Merges the
/// legacy <c>AI:*</c> (AiProviderOptions) and <c>AiTool:*</c> (AiToolOptions)
/// env settings. The provider API key is excluded (handled by AiProviderRegistry).
/// <see cref="AllowedExternalHosts"/> is parsed from the stored CSV.
/// </summary>
public sealed record AiGatewaySettings(
    string ProviderId,
    string BaseUrl,
    string DefaultModel,
    string ReasoningEffort,
    int DefaultMaxTokens,
    double DefaultTemperature,
    int MaxToolCallsPerCompletion,
    int FeatureGrantCacheSeconds,
    string AllowedExternalHostsCsv,
    IReadOnlyList<string> AllowedExternalHosts,
    int ExternalNetworkPerUserDailyCalls,
    int ExternalNetworkTimeoutMilliseconds,
    int ExternalNetworkMaxResponseBytes);

/// <summary>
/// Writing module V2 feature flags + coach/queue/OCR tunables (DB-over-env
/// merged). <see cref="GcvApiKey"/> is the only secret (decrypted by the
/// provider). TessdataPath / V2Seeder stay env-only and are excluded.
/// </summary>
public sealed record WritingSettings(
    bool CronsEnabled,
    bool CoachEnabled,
    decimal CoachDailyCostCapPerLearnerUsd,
    int CoachMaxHintsPerSession,
    int CoachMinSecondsBetweenHints,
    string? GcvApiKey,
    bool OcrEnabled,
    bool AppealsEnabled,
    int TutorReviewQueueMaxDepth,
    int TutorReviewMaxWaitHours,
    int MaxDailyPlanRegenerationsPerDay,
    int GradeIdempotencyTtlHours);

/// <summary>
/// Platform public host identities (DB-over-env merged). Used to build absolute
/// API/web URLs for external auth callbacks and CSRF origin validation.
/// </summary>
public sealed record PlatformSettings(
    string? PublicApiBaseUrl,
    string? PublicWebBaseUrl,
    string FallbackEmailDomain);

/// <summary>
/// Messaging billing-notification channels (DB-over-env merged): Twilio SMS and
/// Meta WhatsApp Business Cloud. <see cref="TwilioAuthToken"/> and
/// <see cref="WhatsAppAccessToken"/> are secrets decrypted by the provider;
/// <see cref="TwilioAccountSid"/> is a public identifier (not encrypted).
/// <see cref="IsTwilioConfigured"/> / <see cref="IsWhatsAppConfigured"/> gate
/// live sends in the channel consumers.
/// </summary>
public sealed record MessagingSettings(
    bool TwilioEnabled,
    string TwilioApiBaseUrl,
    string? TwilioAccountSid,
    string? TwilioAuthToken,
    string? TwilioFromNumber,
    string? TwilioMessagingServiceSid,
    bool WhatsAppEnabled,
    string WhatsAppApiBaseUrl,
    string? WhatsAppAccessToken,
    string? WhatsAppPhoneNumberId,
    string? WhatsAppFallbackTemplateName,
    bool IsTwilioConfigured,
    bool IsWhatsAppConfigured);

/// <summary>
/// FX / currency provider settings (DB-over-env merged). <see cref="ApiKey"/>
/// is the only secret (decrypted by the provider). When the API key/URL are
/// absent, <c>FxRateService</c> uses offline seed rates.
/// </summary>
public sealed record FxSettings(
    string BaseCurrency,
    string? ApiKey,
    string? ApiBaseUrl,
    bool DynamicPricingEnabled);

/// <summary>
/// Storage (S3 / object store) settings (DB-over-env merged). <see cref="AccessKeyId"/>
/// and <see cref="SecretAccessKey"/> are secrets (decrypted by the provider).
/// Filesystem paths (LocalRootPath / *Subpath) stay env-only and are excluded.
/// <see cref="IsConfigured"/> gates S3 mode. Provider is NOT runtime-switchable
/// (singleton IFileStorage); only credentials/bucket/endpoint/region are.
/// </summary>
public sealed record StorageSettings(
    string Provider,
    string? BucketName,
    string? EndpointUrl,
    string? AccessKeyId,
    string? SecretAccessKey,
    string AwsRegion,
    int SignedReadTtlSeconds,
    long MaxAudioBytes,
    long MaxPdfBytes,
    long MaxImageBytes,
    long MaxZipBytes,
    int MaxZipEntries,
    long MaxZipEntryBytes,
    long MaxZipUncompressedBytes,
    double MaxZipCompressionRatio,
    long ChunkSizeBytes,
    int StagingTtlHours)
{
    public bool IsConfigured => Provider.Equals("s3", StringComparison.OrdinalIgnoreCase)
        && !string.IsNullOrWhiteSpace(AccessKeyId)
        && !string.IsNullOrWhiteSpace(SecretAccessKey)
        && !string.IsNullOrWhiteSpace(BucketName);
}

/// <summary>
/// PDF text-extraction settings (DB-over-env merged). <see cref="AzureApiKey"/>
/// is the only secret (decrypted by the provider; env-fallback when DB is null).
/// </summary>
public sealed record PdfExtractionSettings(
    string Provider,
    string AzureEndpoint,
    string? AzureApiKey,
    int MinTextLengthForSuccess);

/// <summary>
/// Pronunciation NON-credential settings (DB-over-env merged): provider
/// selector, region/locale, Whisper/Gemini base-urls + models, audio limits,
/// retention, and free-tier gating. The Azure/Whisper/Gemini API KEYS are NOT
/// here — they are registry-backed via <c>IPronunciationCredentialResolver</c>
/// (AiProviderRegistry rows azure-phoneme / whisper-asr / gemini-pronunciation-audio).
/// </summary>
public sealed record PronunciationSettings(
    string Provider,
    string AzureSpeechRegion,
    string AzureLocale,
    string WhisperBaseUrl,
    string WhisperModel,
    string GeminiBaseUrl,
    string GeminiModel,
    long MaxAudioBytes,
    int AudioRetentionDays,
    int FreeTierWeeklyAttemptLimit,
    int FreeTierWindowDays);

/// <summary>
/// Safe AuthTokenOptions subset (DB-over-env merged): access/refresh/OTP
/// lifetimes (as <see cref="TimeSpan"/>) and the authenticator issuer label.
/// Signing keys, Issuer, and Audience stay env-only (trust anchors / bootstrap)
/// and are intentionally excluded.
/// </summary>
public sealed record AuthTokenSettings(
    TimeSpan AccessTokenLifetime,
    TimeSpan RefreshTokenLifetime,
    TimeSpan OtpLifetime,
    string? AuthenticatorIssuer);
