using System.ComponentModel.DataAnnotations;

namespace OetLearner.Api.Domain;

/// <summary>
/// Runtime overrides for cross-cutting infrastructure secrets and tunables
/// (Brevo / SMTP, Stripe, Sentry, Backup S3, OAuth providers, Push). Singleton
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

    // ── Audit ──────────────────────────────────────────────────────
    [MaxLength(64)]
    public string? UpdatedByUserId { get; set; }

    [MaxLength(128)]
    public string? UpdatedByUserName { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}
