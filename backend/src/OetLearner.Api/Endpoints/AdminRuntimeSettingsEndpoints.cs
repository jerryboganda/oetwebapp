using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services;
using OetLearner.Api.Services.Settings;

namespace OetLearner.Api.Endpoints;

/// <summary>
/// Admin endpoints for managing runtime infrastructure settings (Email/Brevo,
/// Stripe, Sentry, Backup S3, OAuth providers, Push). These map onto the
/// <see cref="RuntimeSettingsRow"/> singleton (Id = "default") and let
/// platform admins rotate secrets without editing <c>.env.production</c>.
///
/// Contract:
/// <list type="bullet">
///   <item>GET returns the merged effective view with secrets <b>masked</b>
///   as <c>"********"</c> when present, empty string when absent. Plaintext
///   secret material never leaves the host process.</item>
///   <item>PUT accepts a partial sectioned payload. Per-field semantics:
///   <c>null</c> = leave unchanged; <c>"********"</c> sentinel = leave secret
///   unchanged; empty string = clear the DB override; any other value =
///   set (and encrypt if it is a secret).</item>
///   <item>Both routes require the <c>AdminSystemAdmin</c> policy.</item>
///   <item>PUT writes one <see cref="AuditEvent"/> with action
///   <c>RuntimeSettingsUpdated</c>. The audit payload lists the <i>keys</i>
///   that changed only — it MUST NOT contain secret values.</item>
/// </list>
/// </summary>
public static class AdminRuntimeSettingsEndpoints
{
    private const string SecretMask = "********";

    /// <summary>
    /// Wire <c>GET</c> and <c>PUT /runtime-settings</c> into the supplied admin
    /// route group. The group is expected to already enforce <c>AdminOnly</c>
    /// + per-user rate limiting (matches the contract of the group built by
    /// <see cref="AdminEndpoints.MapAdminEndpoints"/>).
    /// </summary>
    public static IEndpointRouteBuilder MapAdminRuntimeSettings(this IEndpointRouteBuilder admin)
    {
        admin.MapGet("/runtime-settings", async (
                IRuntimeSettingsProvider provider,
                CancellationToken ct) =>
            {
                var row = await provider.GetRawAsync(ct);
                return Results.Ok(BuildResponse(row, provider));
            })
            .WithAdminRead("AdminSystemAdmin");

        admin.MapPut("/runtime-settings", async (
                HttpContext http,
                RuntimeSettingsUpdateRequest request,
                LearnerDbContext db,
                IRuntimeSettingsProvider provider,
                CancellationToken ct) =>
            {
                var row = await db.RuntimeSettings.FirstOrDefaultAsync(r => r.Id == "default", ct);
                var now = DateTimeOffset.UtcNow;
                if (row is null)
                {
                    row = new RuntimeSettingsRow { Id = "default", UpdatedAt = now };
                    db.RuntimeSettings.Add(row);
                }

                var changedKeys = new List<string>();
                ApplyEmail(row, request.Email, provider, changedKeys);
                ApplyBilling(row, request.Billing, provider, changedKeys);
                ApplySentry(row, request.Sentry, changedKeys);
                ApplyBackup(row, request.Backup, provider, changedKeys);
                ApplyOAuth(row, request.OAuth, provider, changedKeys);
                ApplyPush(row, request.Push, provider, changedKeys);

                row.UpdatedAt = now;
                row.UpdatedByUserId = http.AdminId();
                row.UpdatedByUserName = http.AdminName();

                db.AuditEvents.Add(new AuditEvent
                {
                    Id = Guid.NewGuid().ToString("N"),
                    ActorId = row.UpdatedByUserId ?? "system",
                    ActorName = row.UpdatedByUserName ?? "system",
                    Action = "RuntimeSettingsUpdated",
                    ResourceType = "RuntimeSettings",
                    ResourceId = row.Id,
                    // SECURITY: only the changed key names are persisted,
                    // never the values. Secret material must never appear in
                    // audit storage.
                    Details = JsonSupport.Serialize(new { changedKeys }),
                    OccurredAt = now,
                });

                await db.SaveChangesAsync(ct);
                provider.Invalidate();

                return Results.Ok(new { ok = true, updatedAt = now, changedKeys });
            })
            .WithAdminWrite("AdminSystemAdmin");

        return admin;
    }

    private static string AdminId(this HttpContext httpContext)
        => httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier)
           ?? throw new InvalidOperationException("Authenticated admin id is required.");

    private static string AdminName(this HttpContext httpContext)
        => httpContext.User.FindFirstValue(ClaimTypes.Name) ?? "Admin";

    // ── Response shaping ───────────────────────────────────────────

    private static object BuildResponse(RuntimeSettingsRow r, IRuntimeSettingsProvider provider)
        => new
        {
            email = new
            {
                brevoApiKey = MaskSecret(r.BrevoApiKeyEncrypted),
                brevoEmailVerificationTemplateId = r.BrevoEmailVerificationTemplateId,
                brevoPasswordResetTemplateId = r.BrevoPasswordResetTemplateId,
                smtpHost = r.SmtpHost,
                smtpPort = r.SmtpPort,
                smtpUsername = r.SmtpUsername,
                smtpPassword = MaskSecret(r.SmtpPasswordEncrypted),
                smtpFromAddress = r.SmtpFromAddress,
                smtpFromName = r.SmtpFromName,
            },
            billing = new
            {
                stripeSecretKey = MaskSecret(r.StripeSecretKeyEncrypted),
                stripePublishableKey = r.StripePublishableKey,
                stripeWebhookSecret = MaskSecret(r.StripeWebhookSecretEncrypted),
                stripeSuccessUrl = r.StripeSuccessUrl,
                stripeCancelUrl = r.StripeCancelUrl,
            },
            sentry = new
            {
                dsn = r.SentryDsn,
                environment = r.SentryEnvironment,
                sampleRate = r.SentrySampleRate,
            },
            backup = new
            {
                s3Url = r.BackupS3Url,
                awsAccessKeyId = r.BackupAwsAccessKeyId,
                awsSecretAccessKey = MaskSecret(r.BackupAwsSecretAccessKeyEncrypted),
                gpgPassphrase = MaskSecret(r.BackupGpgPassphraseEncrypted),
                alertWebhook = r.BackupAlertWebhook,
            },
            oauth = new
            {
                googleClientId = r.GoogleClientId,
                googleClientSecret = MaskSecret(r.GoogleClientSecretEncrypted),
                appleClientId = r.AppleClientId,
                appleTeamId = r.AppleTeamId,
                appleKeyId = r.AppleKeyId,
                applePrivateKey = MaskSecret(r.ApplePrivateKeyEncrypted),
                facebookAppId = r.FacebookAppId,
                facebookAppSecret = MaskSecret(r.FacebookAppSecretEncrypted),
            },
            push = new
            {
                apnsKeyId = r.ApnsKeyId,
                apnsTeamId = r.ApnsTeamId,
                apnsBundleId = r.ApnsBundleId,
                apnsAuthKey = MaskSecret(r.ApnsAuthKeyEncrypted),
                fcmServerKey = MaskSecret(r.FcmServerKeyEncrypted),
                fcmProjectId = r.FcmProjectId,
            },
            updatedBy = r.UpdatedByUserName,
            updatedByUserId = r.UpdatedByUserId,
            updatedAt = r.UpdatedAt == default ? (DateTimeOffset?)null : r.UpdatedAt,
        };

    private static string MaskSecret(string? cipher)
        => string.IsNullOrEmpty(cipher) ? string.Empty : SecretMask;

    // ── Per-section appliers ───────────────────────────────────────

    private static void ApplyEmail(RuntimeSettingsRow row, RuntimeSettingsEmailUpdate? d,
        IRuntimeSettingsProvider p, List<string> changed)
    {
        if (d is null) return;
        if (TrySetSecret(d.BrevoApiKey, p, v => row.BrevoApiKeyEncrypted = v, "email.brevoApiKey", changed)) { }
        if (d.BrevoEmailVerificationTemplateId.HasValue) { row.BrevoEmailVerificationTemplateId = d.BrevoEmailVerificationTemplateId; changed.Add("email.brevoEmailVerificationTemplateId"); }
        if (d.BrevoPasswordResetTemplateId.HasValue) { row.BrevoPasswordResetTemplateId = d.BrevoPasswordResetTemplateId; changed.Add("email.brevoPasswordResetTemplateId"); }
        if (TrySetPlain(d.SmtpHost, v => row.SmtpHost = v, "email.smtpHost", changed)) { }
        if (d.SmtpPort.HasValue) { row.SmtpPort = d.SmtpPort; changed.Add("email.smtpPort"); }
        if (TrySetPlain(d.SmtpUsername, v => row.SmtpUsername = v, "email.smtpUsername", changed)) { }
        if (TrySetSecret(d.SmtpPassword, p, v => row.SmtpPasswordEncrypted = v, "email.smtpPassword", changed)) { }
        if (TrySetPlain(d.SmtpFromAddress, v => row.SmtpFromAddress = v, "email.smtpFromAddress", changed)) { }
        if (TrySetPlain(d.SmtpFromName, v => row.SmtpFromName = v, "email.smtpFromName", changed)) { }
    }

    private static void ApplyBilling(RuntimeSettingsRow row, RuntimeSettingsBillingUpdate? d,
        IRuntimeSettingsProvider p, List<string> changed)
    {
        if (d is null) return;
        if (TrySetSecret(d.StripeSecretKey, p, v => row.StripeSecretKeyEncrypted = v, "billing.stripeSecretKey", changed)) { }
        if (TrySetPlain(d.StripePublishableKey, v => row.StripePublishableKey = v, "billing.stripePublishableKey", changed)) { }
        if (TrySetSecret(d.StripeWebhookSecret, p, v => row.StripeWebhookSecretEncrypted = v, "billing.stripeWebhookSecret", changed)) { }
        if (TrySetPlain(d.StripeSuccessUrl, v => row.StripeSuccessUrl = v, "billing.stripeSuccessUrl", changed)) { }
        if (TrySetPlain(d.StripeCancelUrl, v => row.StripeCancelUrl = v, "billing.stripeCancelUrl", changed)) { }
    }

    private static void ApplySentry(RuntimeSettingsRow row, RuntimeSettingsSentryUpdate? d, List<string> changed)
    {
        if (d is null) return;
        if (TrySetPlain(d.Dsn, v => row.SentryDsn = v, "sentry.dsn", changed)) { }
        if (TrySetPlain(d.Environment, v => row.SentryEnvironment = v, "sentry.environment", changed)) { }
        if (d.SampleRate.HasValue) { row.SentrySampleRate = d.SampleRate; changed.Add("sentry.sampleRate"); }
    }

    private static void ApplyBackup(RuntimeSettingsRow row, RuntimeSettingsBackupUpdate? d,
        IRuntimeSettingsProvider p, List<string> changed)
    {
        if (d is null) return;
        if (TrySetPlain(d.S3Url, v => row.BackupS3Url = v, "backup.s3Url", changed)) { }
        if (TrySetPlain(d.AwsAccessKeyId, v => row.BackupAwsAccessKeyId = v, "backup.awsAccessKeyId", changed)) { }
        if (TrySetSecret(d.AwsSecretAccessKey, p, v => row.BackupAwsSecretAccessKeyEncrypted = v, "backup.awsSecretAccessKey", changed)) { }
        if (TrySetSecret(d.GpgPassphrase, p, v => row.BackupGpgPassphraseEncrypted = v, "backup.gpgPassphrase", changed)) { }
        if (TrySetPlain(d.AlertWebhook, v => row.BackupAlertWebhook = v, "backup.alertWebhook", changed)) { }
    }

    private static void ApplyOAuth(RuntimeSettingsRow row, RuntimeSettingsOAuthUpdate? d,
        IRuntimeSettingsProvider p, List<string> changed)
    {
        if (d is null) return;
        if (TrySetPlain(d.GoogleClientId, v => row.GoogleClientId = v, "oauth.googleClientId", changed)) { }
        if (TrySetSecret(d.GoogleClientSecret, p, v => row.GoogleClientSecretEncrypted = v, "oauth.googleClientSecret", changed)) { }
        if (TrySetPlain(d.AppleClientId, v => row.AppleClientId = v, "oauth.appleClientId", changed)) { }
        if (TrySetPlain(d.AppleTeamId, v => row.AppleTeamId = v, "oauth.appleTeamId", changed)) { }
        if (TrySetPlain(d.AppleKeyId, v => row.AppleKeyId = v, "oauth.appleKeyId", changed)) { }
        if (TrySetSecret(d.ApplePrivateKey, p, v => row.ApplePrivateKeyEncrypted = v, "oauth.applePrivateKey", changed)) { }
        if (TrySetPlain(d.FacebookAppId, v => row.FacebookAppId = v, "oauth.facebookAppId", changed)) { }
        if (TrySetSecret(d.FacebookAppSecret, p, v => row.FacebookAppSecretEncrypted = v, "oauth.facebookAppSecret", changed)) { }
    }

    private static void ApplyPush(RuntimeSettingsRow row, RuntimeSettingsPushUpdate? d,
        IRuntimeSettingsProvider p, List<string> changed)
    {
        if (d is null) return;
        if (TrySetPlain(d.ApnsKeyId, v => row.ApnsKeyId = v, "push.apnsKeyId", changed)) { }
        if (TrySetPlain(d.ApnsTeamId, v => row.ApnsTeamId = v, "push.apnsTeamId", changed)) { }
        if (TrySetPlain(d.ApnsBundleId, v => row.ApnsBundleId = v, "push.apnsBundleId", changed)) { }
        if (TrySetSecret(d.ApnsAuthKey, p, v => row.ApnsAuthKeyEncrypted = v, "push.apnsAuthKey", changed)) { }
        if (TrySetSecret(d.FcmServerKey, p, v => row.FcmServerKeyEncrypted = v, "push.fcmServerKey", changed)) { }
        if (TrySetPlain(d.FcmProjectId, v => row.FcmProjectId = v, "push.fcmProjectId", changed)) { }
    }

    // ── Per-field semantics ───────────────────────────────────────
    // null     => do nothing (leave stored value untouched)
    // "********" => secret-mask sentinel: do nothing
    // ""       => clear stored value
    // other    => set (encrypting if secret)

    private static bool TrySetPlain(string? input, Action<string?> setter, string key, List<string> changed)
    {
        if (input is null) return false;
        if (input == SecretMask) return false; // tolerate mask even for plain — safe no-op
        setter(input.Length == 0 ? null : input);
        changed.Add(key);
        return true;
    }

    private static bool TrySetSecret(string? input, IRuntimeSettingsProvider p, Action<string?> setter, string key, List<string> changed)
    {
        if (input is null) return false;
        if (input == SecretMask) return false;
        setter(input.Length == 0 ? null : p.Protect(input));
        changed.Add(key);
        return true;
    }
}

// ── Wire payload contracts ────────────────────────────────────────

/// <summary>Top-level PUT payload. Any omitted section means "do not touch that section".</summary>
public sealed class RuntimeSettingsUpdateRequest
{
    public RuntimeSettingsEmailUpdate? Email { get; set; }
    public RuntimeSettingsBillingUpdate? Billing { get; set; }
    public RuntimeSettingsSentryUpdate? Sentry { get; set; }
    public RuntimeSettingsBackupUpdate? Backup { get; set; }
    public RuntimeSettingsOAuthUpdate? OAuth { get; set; }
    public RuntimeSettingsPushUpdate? Push { get; set; }
}

public sealed class RuntimeSettingsEmailUpdate
{
    public string? BrevoApiKey { get; set; }
    public int? BrevoEmailVerificationTemplateId { get; set; }
    public int? BrevoPasswordResetTemplateId { get; set; }
    public string? SmtpHost { get; set; }
    public int? SmtpPort { get; set; }
    public string? SmtpUsername { get; set; }
    public string? SmtpPassword { get; set; }
    public string? SmtpFromAddress { get; set; }
    public string? SmtpFromName { get; set; }
}

public sealed class RuntimeSettingsBillingUpdate
{
    public string? StripeSecretKey { get; set; }
    public string? StripePublishableKey { get; set; }
    public string? StripeWebhookSecret { get; set; }
    public string? StripeSuccessUrl { get; set; }
    public string? StripeCancelUrl { get; set; }
}

public sealed class RuntimeSettingsSentryUpdate
{
    public string? Dsn { get; set; }
    public string? Environment { get; set; }
    public double? SampleRate { get; set; }
}

public sealed class RuntimeSettingsBackupUpdate
{
    public string? S3Url { get; set; }
    public string? AwsAccessKeyId { get; set; }
    public string? AwsSecretAccessKey { get; set; }
    public string? GpgPassphrase { get; set; }
    public string? AlertWebhook { get; set; }
}

public sealed class RuntimeSettingsOAuthUpdate
{
    public string? GoogleClientId { get; set; }
    public string? GoogleClientSecret { get; set; }
    public string? AppleClientId { get; set; }
    public string? AppleTeamId { get; set; }
    public string? AppleKeyId { get; set; }
    public string? ApplePrivateKey { get; set; }
    public string? FacebookAppId { get; set; }
    public string? FacebookAppSecret { get; set; }
}

public sealed class RuntimeSettingsPushUpdate
{
    public string? ApnsKeyId { get; set; }
    public string? ApnsTeamId { get; set; }
    public string? ApnsBundleId { get; set; }
    public string? ApnsAuthKey { get; set; }
    public string? FcmServerKey { get; set; }
    public string? FcmProjectId { get; set; }
}
