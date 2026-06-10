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
    string? UpdatedByUserId,
    string? UpdatedByUserName,
    DateTimeOffset? UpdatedAt);

public sealed record EmailSettings(
    string? BrevoApiKey,
    int? BrevoEmailVerificationTemplateId,
    int? BrevoPasswordResetTemplateId,
    string? SmtpHost,
    int? SmtpPort,
    string? SmtpUsername,
    string? SmtpPassword,
    string? SmtpFromAddress,
    string? SmtpFromName);

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
    string? PayPalCancelUrl);

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
    string? FacebookAppSecret);

public sealed record PushSettings(
    string? ApnsKeyId,
    string? ApnsTeamId,
    string? ApnsBundleId,
    string? ApnsAuthKey,
    string? FcmServerKey,
    string? FcmProjectId,
    string? VapidSubject = null,
    string? VapidPublicKey = null,
    string? VapidPrivateKey = null);

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

/// <summary>Soketi realtime websocket push settings (DB-over-env merged).</summary>
public sealed record SoketiSettings(
    string Host,
    int Port,
    string AppId,
    string AppKey,
    string? AppSecret,
    bool UseTls,
    bool Enabled);
