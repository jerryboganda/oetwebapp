using System.Security.Claims;
using System.Text.Json;
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
                var effective = await provider.GetAsync(ct);
                return Results.Ok(BuildResponse(effective));
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
                var effective = await provider.GetAsync(ct);

                return Results.Ok(BuildResponse(effective));
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

    private static object BuildResponse(EffectiveSettings effective)
        => new
        {
            email = new
            {
                brevoEnabled = effective.Email.BrevoEnabled,
                brevoApiKey = MaskSecretValue(effective.Email.BrevoApiKey),
                brevoEmailVerificationTemplateId = effective.Email.BrevoEmailVerificationTemplateId,
                brevoPasswordResetTemplateId = effective.Email.BrevoPasswordResetTemplateId,
                brevoWelcomeTemplateId = effective.Email.BrevoWelcomeTemplateId,
                brevoPasswordChangedTemplateId = effective.Email.BrevoPasswordChangedTemplateId,
                brevoMfaEnabledTemplateId = effective.Email.BrevoMfaEnabledTemplateId,
                brevoAdminInviteTemplateId = effective.Email.BrevoAdminInviteTemplateId,
                brevoSecurityAlertTemplateId = effective.Email.BrevoSecurityAlertTemplateId,
                brevoReviewCompletedTemplateId = effective.Email.BrevoReviewCompletedTemplateId,
                smtpEnabled = effective.Email.SmtpEnabled,
                smtpHost = effective.Email.SmtpHost,
                smtpPort = effective.Email.SmtpPort,
                smtpUsername = effective.Email.SmtpUsername,
                smtpPassword = MaskSecretValue(effective.Email.SmtpPassword),
                smtpEnableSsl = effective.Email.SmtpEnableSsl,
                smtpFromAddress = effective.Email.SmtpFromAddress,
                smtpFromName = effective.Email.SmtpFromName,
            },
            billing = new
            {
                stripeSecretKey = MaskSecretValue(effective.Billing.StripeSecretKey),
                stripePublishableKey = effective.Billing.StripePublishableKey,
                stripeWebhookSecret = MaskSecretValue(effective.Billing.StripeWebhookSecret),
                stripeSuccessUrl = effective.Billing.StripeSuccessUrl,
                stripeCancelUrl = effective.Billing.StripeCancelUrl,
            },
            sentry = new
            {
                dsn = effective.Sentry.Dsn,
                environment = effective.Sentry.Environment,
                sampleRate = effective.Sentry.SampleRate,
            },
            backup = new
            {
                s3Url = effective.Backup.S3Url,
                awsAccessKeyId = effective.Backup.AwsAccessKeyId,
                awsSecretAccessKey = MaskSecretValue(effective.Backup.AwsSecretAccessKey),
                gpgPassphrase = MaskSecretValue(effective.Backup.GpgPassphrase),
                alertWebhook = effective.Backup.AlertWebhook,
            },
            oauth = new
            {
                googleEnabled = effective.OAuth.GoogleEnabled,
                googleClientId = effective.OAuth.GoogleClientId,
                googleClientSecret = MaskSecretValue(effective.OAuth.GoogleClientSecret),
                appleClientId = effective.OAuth.AppleClientId,
                appleTeamId = effective.OAuth.AppleTeamId,
                appleKeyId = effective.OAuth.AppleKeyId,
                applePrivateKey = MaskSecretValue(effective.OAuth.ApplePrivateKey),
                facebookEnabled = effective.OAuth.FacebookEnabled,
                facebookAppId = effective.OAuth.FacebookAppId,
                facebookAppSecret = MaskSecretValue(effective.OAuth.FacebookAppSecret),
                linkedInEnabled = effective.OAuth.LinkedInEnabled,
                linkedInClientId = effective.OAuth.LinkedInClientId,
                linkedInClientSecret = MaskSecretValue(effective.OAuth.LinkedInClientSecret),
            },
            push = new
            {
                webPushEnabled = effective.Push.WebPushEnabled,
                webPushSubject = effective.Push.WebPushSubject,
                webPushPublicKey = effective.Push.WebPushPublicKey,
                webPushPrivateKey = MaskSecretValue(effective.Push.WebPushPrivateKey),
                apnsKeyId = effective.Push.ApnsKeyId,
                apnsTeamId = effective.Push.ApnsTeamId,
                apnsBundleId = effective.Push.ApnsBundleId,
                apnsAuthKey = MaskSecretValue(effective.Push.ApnsAuthKey),
                fcmServerKey = MaskSecretValue(effective.Push.FcmServerKey),
                fcmProjectId = effective.Push.FcmProjectId,
            },
            updatedBy = effective.UpdatedByUserName,
            updatedByUserId = effective.UpdatedByUserId,
            updatedAt = effective.UpdatedAt,
        };

    private static string MaskSecretValue(string? secret)
        => string.IsNullOrEmpty(secret) ? string.Empty : SecretMask;

    // ── Per-section appliers ───────────────────────────────────────

    private static void ApplyEmail(RuntimeSettingsRow row, RuntimeSettingsEmailUpdate? d,
        IRuntimeSettingsProvider p, List<string> changed)
    {
        if (d is null) return;
        if (TrySetNullableBool(d.BrevoEnabled, v => row.BrevoEnabled = v, "email.brevoEnabled", changed)) { }
        if (TrySetSecret(d.BrevoApiKey, p, v => row.BrevoApiKeyEncrypted = v, "email.brevoApiKey", changed)) { }
        if (TrySetNullableInt(d.BrevoEmailVerificationTemplateId, v => row.BrevoEmailVerificationTemplateId = v, "email.brevoEmailVerificationTemplateId", changed)) { }
        if (TrySetNullableInt(d.BrevoPasswordResetTemplateId, v => row.BrevoPasswordResetTemplateId = v, "email.brevoPasswordResetTemplateId", changed)) { }
        if (TrySetNullableInt(d.BrevoWelcomeTemplateId, v => row.BrevoWelcomeTemplateId = v, "email.brevoWelcomeTemplateId", changed)) { }
        if (TrySetNullableInt(d.BrevoPasswordChangedTemplateId, v => row.BrevoPasswordChangedTemplateId = v, "email.brevoPasswordChangedTemplateId", changed)) { }
        if (TrySetNullableInt(d.BrevoMfaEnabledTemplateId, v => row.BrevoMfaEnabledTemplateId = v, "email.brevoMfaEnabledTemplateId", changed)) { }
        if (TrySetNullableInt(d.BrevoAdminInviteTemplateId, v => row.BrevoAdminInviteTemplateId = v, "email.brevoAdminInviteTemplateId", changed)) { }
        if (TrySetNullableInt(d.BrevoSecurityAlertTemplateId, v => row.BrevoSecurityAlertTemplateId = v, "email.brevoSecurityAlertTemplateId", changed)) { }
        if (TrySetNullableInt(d.BrevoReviewCompletedTemplateId, v => row.BrevoReviewCompletedTemplateId = v, "email.brevoReviewCompletedTemplateId", changed)) { }
        if (TrySetNullableBool(d.SmtpEnabled, v => row.SmtpEnabled = v, "email.smtpEnabled", changed)) { }
        if (TrySetPlain(d.SmtpHost, v => row.SmtpHost = v, "email.smtpHost", changed)) { }
        if (TrySetNullableInt(d.SmtpPort, v => row.SmtpPort = v, "email.smtpPort", changed)) { }
        if (TrySetPlain(d.SmtpUsername, v => row.SmtpUsername = v, "email.smtpUsername", changed)) { }
        if (TrySetSecret(d.SmtpPassword, p, v => row.SmtpPasswordEncrypted = v, "email.smtpPassword", changed)) { }
        if (TrySetNullableBool(d.SmtpEnableSsl, v => row.SmtpEnableSsl = v, "email.smtpEnableSsl", changed)) { }
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
        if (TrySetNullableDouble(d.SampleRate, v => row.SentrySampleRate = v, "sentry.sampleRate", changed)) { }
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
        if (TrySetNullableBool(d.GoogleEnabled, v => row.GoogleEnabled = v, "oauth.googleEnabled", changed)) { }
        if (TrySetPlain(d.GoogleClientId, v => row.GoogleClientId = v, "oauth.googleClientId", changed)) { }
        if (TrySetSecret(d.GoogleClientSecret, p, v => row.GoogleClientSecretEncrypted = v, "oauth.googleClientSecret", changed)) { }
        if (TrySetPlain(d.AppleClientId, v => row.AppleClientId = v, "oauth.appleClientId", changed)) { }
        if (TrySetPlain(d.AppleTeamId, v => row.AppleTeamId = v, "oauth.appleTeamId", changed)) { }
        if (TrySetPlain(d.AppleKeyId, v => row.AppleKeyId = v, "oauth.appleKeyId", changed)) { }
        if (TrySetSecret(d.ApplePrivateKey, p, v => row.ApplePrivateKeyEncrypted = v, "oauth.applePrivateKey", changed)) { }
        if (TrySetNullableBool(d.FacebookEnabled, v => row.FacebookEnabled = v, "oauth.facebookEnabled", changed)) { }
        if (TrySetPlain(d.FacebookAppId, v => row.FacebookAppId = v, "oauth.facebookAppId", changed)) { }
        if (TrySetSecret(d.FacebookAppSecret, p, v => row.FacebookAppSecretEncrypted = v, "oauth.facebookAppSecret", changed)) { }
        if (TrySetNullableBool(d.LinkedInEnabled, v => row.LinkedInEnabled = v, "oauth.linkedInEnabled", changed)) { }
        if (TrySetPlain(d.LinkedInClientId, v => row.LinkedInClientId = v, "oauth.linkedInClientId", changed)) { }
        if (TrySetSecret(d.LinkedInClientSecret, p, v => row.LinkedInClientSecretEncrypted = v, "oauth.linkedInClientSecret", changed)) { }
    }

    private static void ApplyPush(RuntimeSettingsRow row, RuntimeSettingsPushUpdate? d,
        IRuntimeSettingsProvider p, List<string> changed)
    {
        if (d is null) return;
        if (TrySetNullableBool(d.WebPushEnabled, v => row.WebPushEnabled = v, "push.webPushEnabled", changed)) { }
        if (TrySetPlain(d.WebPushSubject, v => row.WebPushSubject = v, "push.webPushSubject", changed)) { }
        if (TrySetPlain(d.WebPushPublicKey, v => row.WebPushPublicKey = v, "push.webPushPublicKey", changed)) { }
        if (TrySetSecret(d.WebPushPrivateKey, p, v => row.WebPushPrivateKeyEncrypted = v, "push.webPushPrivateKey", changed)) { }
        if (TrySetPlain(d.ApnsKeyId, v => row.ApnsKeyId = v, "push.apnsKeyId", changed)) { }
        if (TrySetPlain(d.ApnsTeamId, v => row.ApnsTeamId = v, "push.apnsTeamId", changed)) { }
        if (TrySetPlain(d.ApnsBundleId, v => row.ApnsBundleId = v, "push.apnsBundleId", changed)) { }
        if (TrySetSecret(d.ApnsAuthKey, p, v => row.ApnsAuthKeyEncrypted = v, "push.apnsAuthKey", changed)) { }
        if (TrySetSecret(d.FcmServerKey, p, v => row.FcmServerKeyEncrypted = v, "push.fcmServerKey", changed)) { }
        if (TrySetPlain(d.FcmProjectId, v => row.FcmProjectId = v, "push.fcmProjectId", changed)) { }
    }

    // ── Per-field semantics ───────────────────────────────────────
    // strings: null => do nothing (leave stored value untouched)
    // "********" => secret-mask sentinel: do nothing
    // ""       => clear stored value
    // other    => set (encrypting if secret)
    // nullable numbers: omitted => do nothing; null or "" => clear override; number => set

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

    private static bool TrySetNullableInt(JsonElement? input, Action<int?> setter, string key, List<string> changed)
    {
        if (!TryReadNullableNumber(input, key, element => element.GetInt32(), out var value))
            return false;

        setter(value);
        changed.Add(key);
        return true;
    }

    private static bool TrySetNullableDouble(JsonElement? input, Action<double?> setter, string key, List<string> changed)
    {
        if (!TryReadNullableNumber(input, key, element => element.GetDouble(), out var value))
            return false;

        setter(value);
        changed.Add(key);
        return true;
    }

    private static bool TrySetNullableBool(JsonElement? input, Action<bool?> setter, string key, List<string> changed)
    {
        if (!TryReadNullableBool(input, key, out var value))
            return false;

        setter(value);
        changed.Add(key);
        return true;
    }

    private static bool TryReadNullableBool(JsonElement? input, string key, out bool? value)
    {
        value = null;
        if (input is null) return false; // omitted => leave unchanged

        var element = input.Value;
        if (element.ValueKind is JsonValueKind.Null)
            return true; // explicit null => clear override

        if (element.ValueKind is JsonValueKind.String)
        {
            var s = element.GetString();
            if (s == string.Empty)
                return true; // tolerate legacy empty-string clears from HTML inputs
            if (bool.TryParse(s, out var parsed))
            {
                value = parsed;
                return true;
            }
        }

        if (element.ValueKind is JsonValueKind.True)
        {
            value = true;
            return true;
        }

        if (element.ValueKind is JsonValueKind.False)
        {
            value = false;
            return true;
        }

        throw new InvalidOperationException($"{key} must be a boolean or null.");
    }

    private static bool TryReadNullableNumber<T>(JsonElement? input, string key, Func<JsonElement, T> reader, out T? value)
        where T : struct
    {
        value = null;
        if (input is null) return false; // omitted => leave unchanged

        var element = input.Value;
        if (element.ValueKind is JsonValueKind.Null)
            return true; // explicit null => clear override

        if (element.ValueKind is JsonValueKind.String && element.GetString() == string.Empty)
            return true; // tolerate legacy empty-string clears from HTML number inputs

        if (element.ValueKind is not JsonValueKind.Number)
            throw new InvalidOperationException($"{key} must be a number or null.");

        try
        {
            value = reader(element);
            return true;
        }
        catch (FormatException ex)
        {
            throw new InvalidOperationException($"{key} must be a valid number.", ex);
        }
        catch (InvalidOperationException ex)
        {
            throw new InvalidOperationException($"{key} must be a valid number.", ex);
        }
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
    public JsonElement? BrevoEnabled { get; set; }
    public string? BrevoApiKey { get; set; }
    public JsonElement? BrevoEmailVerificationTemplateId { get; set; }
    public JsonElement? BrevoPasswordResetTemplateId { get; set; }
    public JsonElement? BrevoWelcomeTemplateId { get; set; }
    public JsonElement? BrevoPasswordChangedTemplateId { get; set; }
    public JsonElement? BrevoMfaEnabledTemplateId { get; set; }
    public JsonElement? BrevoAdminInviteTemplateId { get; set; }
    public JsonElement? BrevoSecurityAlertTemplateId { get; set; }
    public JsonElement? BrevoReviewCompletedTemplateId { get; set; }
    public JsonElement? SmtpEnabled { get; set; }
    public string? SmtpHost { get; set; }
    public JsonElement? SmtpPort { get; set; }
    public string? SmtpUsername { get; set; }
    public string? SmtpPassword { get; set; }
    public JsonElement? SmtpEnableSsl { get; set; }
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
    public JsonElement? SampleRate { get; set; }
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
    public JsonElement? GoogleEnabled { get; set; }
    public string? GoogleClientId { get; set; }
    public string? GoogleClientSecret { get; set; }
    public string? AppleClientId { get; set; }
    public string? AppleTeamId { get; set; }
    public string? AppleKeyId { get; set; }
    public string? ApplePrivateKey { get; set; }
    public JsonElement? FacebookEnabled { get; set; }
    public string? FacebookAppId { get; set; }
    public string? FacebookAppSecret { get; set; }
    public JsonElement? LinkedInEnabled { get; set; }
    public string? LinkedInClientId { get; set; }
    public string? LinkedInClientSecret { get; set; }
}

public sealed class RuntimeSettingsPushUpdate
{
    public JsonElement? WebPushEnabled { get; set; }
    public string? WebPushSubject { get; set; }
    public string? WebPushPublicKey { get; set; }
    public string? WebPushPrivateKey { get; set; }
    public string? ApnsKeyId { get; set; }
    public string? ApnsTeamId { get; set; }
    public string? ApnsBundleId { get; set; }
    public string? ApnsAuthKey { get; set; }
    public string? FcmServerKey { get; set; }
    public string? FcmProjectId { get; set; }
}
