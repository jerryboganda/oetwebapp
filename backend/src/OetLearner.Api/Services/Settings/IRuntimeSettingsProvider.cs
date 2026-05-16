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
    string? StripeCancelUrl);

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
    string? FcmProjectId);
