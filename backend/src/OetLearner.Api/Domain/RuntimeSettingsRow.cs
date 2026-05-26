using System.ComponentModel.DataAnnotations;

namespace OetLearner.Api.Domain;

/// <summary>
/// Runtime overrides for cross-cutting infrastructure secrets and tunables
/// (Brevo / SMTP, Stripe, Sentry, Backup S3, OAuth providers, Push, Upload scanner, Zoom). Singleton
/// row keyed on <c>Id == "default"</c> — admins edit this via
/// <c>PUT /v1/admin/runtime-settings</c>. At request time, the
/// <see cref="OetLearner.Api.Services.Settings.IRuntimeSettingsProvider"/>
/// merges these overrides on top of the env-var / appsettings defaults and
/// returns a computed effective view.
///
/// Secret fields (API keys, client secrets, webhook secrets, private keys)
/// are encrypted at rest via ASP.NET Data Protection with the purpose
/// <c>RuntimeSettings.Secret.v1</c> — never stored plain. They always carry
/// the <c>XxxEncrypted</c> suffix to make the storage contract obvious at
/// the type level.
///
/// All override fields are nullable: <c>null</c> means "no override — fall
/// back to the env/appsettings value".
/// </summary>
public class RuntimeSettingsRow
{
    [Key]
    [MaxLength(32)]
    public string Id { get; set; } = "default";

    // ── Email / Brevo ──────────────────────────────────────────────
    public string? BrevoApiKeyEncrypted { get; set; }
    public int? BrevoEmailVerificationTemplateId { get; set; }
    public int? BrevoPasswordResetTemplateId { get; set; }

    [MaxLength(256)] public string? SmtpHost { get; set; }
    public int? SmtpPort { get; set; }
    [MaxLength(256)] public string? SmtpUsername { get; set; }
    public string? SmtpPasswordEncrypted { get; set; }
    [MaxLength(256)] public string? SmtpFromAddress { get; set; }
    [MaxLength(256)] public string? SmtpFromName { get; set; }

    // ── Billing / Stripe ───────────────────────────────────────────
    public string? StripeSecretKeyEncrypted { get; set; }
    [MaxLength(256)] public string? StripePublishableKey { get; set; }
    public string? StripeWebhookSecretEncrypted { get; set; }
    [MaxLength(1024)] public string? StripeSuccessUrl { get; set; }
    [MaxLength(1024)] public string? StripeCancelUrl { get; set; }

    // ── Stripe Tax / Customer Portal / Radar (Wave A5) ─────────────
    public bool? StripeTaxAutomaticEnabled { get; set; }
    /// <summary>CSV of Stripe tax registration codes (e.g., "UK_VAT,EU_OSS,AU_GST").</summary>
    [MaxLength(1024)] public string? StripeTaxRegistrationsCsv { get; set; }
    [MaxLength(128)] public string? StripeCustomerPortalConfigurationId { get; set; }
    public bool? StripeRadarHighRiskCountryAllowReview { get; set; }
    [MaxLength(1024)] public string? StripeRadarBlockEmailDomainsCsv { get; set; }

    [MaxLength(256)] public string? PayPalClientId { get; set; }
    public string? PayPalClientSecretEncrypted { get; set; }
    public string? PayPalWebhookIdEncrypted { get; set; }
    [MaxLength(1024)] public string? PayPalSuccessUrl { get; set; }
    [MaxLength(1024)] public string? PayPalCancelUrl { get; set; }

    // ── Sentry ─────────────────────────────────────────────────────
    [MaxLength(512)] public string? SentryDsn { get; set; }
    [MaxLength(64)] public string? SentryEnvironment { get; set; }
    public double? SentrySampleRate { get; set; }

    // ── Backup S3 ──────────────────────────────────────────────────
    [MaxLength(1024)] public string? BackupS3Url { get; set; }
    [MaxLength(256)] public string? BackupAwsAccessKeyId { get; set; }
    public string? BackupAwsSecretAccessKeyEncrypted { get; set; }
    public string? BackupGpgPassphraseEncrypted { get; set; }
    [MaxLength(1024)] public string? BackupAlertWebhook { get; set; }

    // ── OAuth providers ────────────────────────────────────────────
    [MaxLength(256)] public string? GoogleClientId { get; set; }
    public string? GoogleClientSecretEncrypted { get; set; }

    [MaxLength(256)] public string? AppleClientId { get; set; }
    [MaxLength(64)] public string? AppleTeamId { get; set; }
    [MaxLength(64)] public string? AppleKeyId { get; set; }
    public string? ApplePrivateKeyEncrypted { get; set; }

    [MaxLength(256)] public string? FacebookAppId { get; set; }
    public string? FacebookAppSecretEncrypted { get; set; }

    // ── Push (APNs / FCM) ──────────────────────────────────────────
    [MaxLength(64)] public string? ApnsKeyId { get; set; }
    [MaxLength(64)] public string? ApnsTeamId { get; set; }
    [MaxLength(256)] public string? ApnsBundleId { get; set; }
    public string? ApnsAuthKeyEncrypted { get; set; }
    public string? FcmServerKeyEncrypted { get; set; }
    [MaxLength(256)] public string? FcmProjectId { get; set; }
    [MaxLength(256)] public string? VapidSubject { get; set; }
    [MaxLength(512)] public string? VapidPublicKey { get; set; }
    public string? VapidPrivateKeyEncrypted { get; set; }

    // ── Upload scanner / ClamAV ────────────────────────────────────
    [MaxLength(32)] public string? UploadScannerProvider { get; set; }
    [MaxLength(256)] public string? UploadScannerHost { get; set; }
    public int? UploadScannerPort { get; set; }
    public int? UploadScannerTimeoutSeconds { get; set; }
    public bool? UploadScannerFailClosedOnError { get; set; }

    // ── Zoom live classes ──────────────────────────────────────────
    public bool? ZoomEnabled { get; set; }
    [MaxLength(256)] public string? ZoomAccountId { get; set; }
    [MaxLength(256)] public string? ZoomClientId { get; set; }
    public string? ZoomClientSecretEncrypted { get; set; }
    [MaxLength(512)] public string? ZoomApiBaseUrl { get; set; }
    [MaxLength(512)] public string? ZoomTokenUrl { get; set; }
    [MaxLength(256)] public string? ZoomHostUserId { get; set; }
    [MaxLength(256)] public string? ZoomMeetingSdkKey { get; set; }
    public string? ZoomMeetingSdkSecretEncrypted { get; set; }
    public string? ZoomWebhookSecretTokenEncrypted { get; set; }
    public int? ZoomWebhookRetryToleranceSeconds { get; set; }
    public bool? ZoomAllowSandboxFallback { get; set; }

    // ── Live Class AI Pipeline (Wave A2) ──────────────────────────
    /// <summary>
    /// Feature flag for the AI recording-processing pipeline (transcribe,
    /// summarise, translate, embed). When <c>null</c> or <c>false</c> the
    /// background-job bodies short-circuit cleanly without making any AI
    /// calls. Defaults <c>false</c> (off) until an admin enables it.
    /// </summary>
    public bool? LiveClassesAiRecordingProcessingEnabled { get; set; }

    // ── Audit ──────────────────────────────────────────────────────
    [MaxLength(64)]
    public string? UpdatedByUserId { get; set; }

    [MaxLength(128)]
    public string? UpdatedByUserName { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}
