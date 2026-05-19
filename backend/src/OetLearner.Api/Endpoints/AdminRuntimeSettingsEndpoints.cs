using System.Security.Claims;
using System.Text.Json;
using System.Net.Sockets;
using Microsoft.Extensions.Options;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Configuration;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services;
using OetLearner.Api.Services.Content;
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
                return Results.Ok(BuildResponse(row));
            })
            .WithAdminRead("AdminSystemAdmin");

        admin.MapPut("/runtime-settings", async Task<IResult> (
                HttpContext http,
                RuntimeSettingsUpdateRequest request,
                LearnerDbContext db,
                IRuntimeSettingsProvider provider,
                IWebHostEnvironment env,
                IOptions<UploadScannerOptions> scannerOptions,
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
                try
                {
                    ApplyEmail(row, request.Email, provider, changedKeys);
                    ApplyBilling(row, request.Billing, provider, changedKeys);
                    ApplySentry(row, request.Sentry, changedKeys);
                    ApplyBackup(row, request.Backup, provider, changedKeys);
                    ApplyOAuth(row, request.OAuth, provider, changedKeys);
                    ApplyPush(row, request.Push, provider, changedKeys);
                    ApplyUploadScanner(row, request.UploadScanner, env, scannerOptions.Value, changedKeys);
                }
                catch (RuntimeSettingsValidationException ex)
                {
                    return Results.BadRequest(new { message = ex.Message });
                }

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

                return Results.Ok(BuildResponse(row));
            })
            .WithAdminWrite("AdminSystemAdmin");

        admin.MapPost("/runtime-settings/test/{sectionId}", async Task<IResult> (
                string sectionId,
                HttpContext http,
                LearnerDbContext db,
                IRuntimeSettingsProvider provider,
                IWebHostEnvironment env,
                IOptions<UploadScannerOptions> scannerOptions,
                TimeProvider clock,
                CancellationToken ct) =>
            {
                var normalized = NormalizeSectionId(sectionId);
                if (normalized is null)
                    return Results.BadRequest(new { message = $"Unknown integration section '{sectionId}'." });

                var testedAt = clock.GetUtcNow();
                var result = await TestSectionAsync(normalized, provider, env, scannerOptions.Value, testedAt, ct);
                db.AuditEvents.Add(new AuditEvent
                {
                    Id = Guid.NewGuid().ToString("N"),
                    ActorId = http.AdminId(),
                    ActorName = http.AdminName(),
                    Action = "RuntimeSettingsTested",
                    ResourceType = "RuntimeSettings",
                    ResourceId = "default",
                    Details = JsonSupport.Serialize(new { section = normalized, status = result.Status }),
                    OccurredAt = testedAt,
                });
                await db.SaveChangesAsync(ct);
                return Results.Ok(result);
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

    private static object BuildResponse(RuntimeSettingsRow r)
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
                paypalClientId = r.PayPalClientId,
                paypalClientSecret = MaskSecret(r.PayPalClientSecretEncrypted),
                paypalWebhookId = MaskSecret(r.PayPalWebhookIdEncrypted),
                paypalSuccessUrl = r.PayPalSuccessUrl,
                paypalCancelUrl = r.PayPalCancelUrl,
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
                vapidSubject = r.VapidSubject,
                vapidPublicKey = r.VapidPublicKey,
                vapidPrivateKey = MaskSecret(r.VapidPrivateKeyEncrypted),
            },
            uploadScanner = new
            {
                provider = r.UploadScannerProvider,
                host = r.UploadScannerHost,
                port = r.UploadScannerPort,
                timeoutSeconds = r.UploadScannerTimeoutSeconds,
                failClosedOnError = r.UploadScannerFailClosedOnError,
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
        if (TrySetNullableInt(d.BrevoEmailVerificationTemplateId, v => row.BrevoEmailVerificationTemplateId = v, "email.brevoEmailVerificationTemplateId", changed)) { }
        if (TrySetNullableInt(d.BrevoPasswordResetTemplateId, v => row.BrevoPasswordResetTemplateId = v, "email.brevoPasswordResetTemplateId", changed)) { }
        if (TrySetPlain(d.SmtpHost, v => row.SmtpHost = v, "email.smtpHost", changed)) { }
        if (TrySetNullableInt(d.SmtpPort, v => row.SmtpPort = v, "email.smtpPort", changed, min: 1, max: 65535)) { }
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
        if (TrySetPlain(d.PayPalClientId, v => row.PayPalClientId = v, "billing.paypalClientId", changed)) { }
        if (TrySetSecret(d.PayPalClientSecret, p, v => row.PayPalClientSecretEncrypted = v, "billing.paypalClientSecret", changed)) { }
        if (TrySetSecret(d.PayPalWebhookId, p, v => row.PayPalWebhookIdEncrypted = v, "billing.paypalWebhookId", changed)) { }
        if (TrySetPlain(d.PayPalSuccessUrl, v => row.PayPalSuccessUrl = v, "billing.paypalSuccessUrl", changed)) { }
        if (TrySetPlain(d.PayPalCancelUrl, v => row.PayPalCancelUrl = v, "billing.paypalCancelUrl", changed)) { }
    }

    private static void ApplySentry(RuntimeSettingsRow row, RuntimeSettingsSentryUpdate? d, List<string> changed)
    {
        if (d is null) return;
        if (TrySetPlain(d.Dsn, v => row.SentryDsn = v, "sentry.dsn", changed)) { }
        if (TrySetPlain(d.Environment, v => row.SentryEnvironment = v, "sentry.environment", changed)) { }
        if (TrySetNullableDouble(d.SampleRate, v => row.SentrySampleRate = v, "sentry.sampleRate", changed, min: 0, max: 1)) { }
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
        if (TrySetPlain(d.VapidSubject, v => row.VapidSubject = v, "push.vapidSubject", changed)) { }
        if (TrySetPlain(d.VapidPublicKey, v => row.VapidPublicKey = v, "push.vapidPublicKey", changed)) { }
        if (TrySetSecret(d.VapidPrivateKey, p, v => row.VapidPrivateKeyEncrypted = v, "push.vapidPrivateKey", changed)) { }
    }

    private static void ApplyUploadScanner(
        RuntimeSettingsRow row,
        RuntimeSettingsUploadScannerUpdate? d,
        IWebHostEnvironment env,
        UploadScannerOptions scannerOptions,
        List<string> changed)
    {
        if (d is null) return;
        if (d.Provider is not null)
        {
            if (d.Provider != SecretMask)
            {
                var provider = d.Provider.Trim();
                if (provider.Length == 0)
                {
                    row.UploadScannerProvider = null;
                }
                else if (provider.Equals("noop", StringComparison.OrdinalIgnoreCase)
                         || provider.Equals("clamav", StringComparison.OrdinalIgnoreCase))
                {
                    if (env.IsProduction() && provider.Equals("noop", StringComparison.OrdinalIgnoreCase))
                    {
                        throw new RuntimeSettingsValidationException("uploadScanner.provider cannot be 'noop' in production.");
                    }
                    row.UploadScannerProvider = provider.ToLowerInvariant();
                }
                else
                {
                    throw new RuntimeSettingsValidationException("uploadScanner.provider must be 'noop' or 'clamav'.");
                }
                changed.Add("uploadScanner.provider");
            }
        }

        if (TrySetPlain(d.Host, v => row.UploadScannerHost = v, "uploadScanner.host", changed)) { }
        if (TrySetNullableInt(d.Port, v => row.UploadScannerPort = v, "uploadScanner.port", changed, min: 1, max: 65535)) { }
        if (TrySetNullableInt(d.TimeoutSeconds, v => row.UploadScannerTimeoutSeconds = v, "uploadScanner.timeoutSeconds", changed, min: 1, max: 120)) { }
        if (TrySetNullableBool(d.FailClosedOnError, v => row.UploadScannerFailClosedOnError = v, "uploadScanner.failClosedOnError", changed)) { }

        if (env.IsProduction())
        {
            var effectiveProvider = row.UploadScannerProvider ?? scannerOptions.Provider ?? "noop";
            if (!effectiveProvider.Equals("clamav", StringComparison.OrdinalIgnoreCase))
                throw new RuntimeSettingsValidationException("uploadScanner.provider must remain 'clamav' in production.");

            var effectiveFailClosed = row.UploadScannerFailClosedOnError ?? scannerOptions.FailClosedOnError;
            if (!effectiveFailClosed)
                throw new RuntimeSettingsValidationException("uploadScanner.failClosedOnError cannot be false in production.");

            var effectiveHost = row.UploadScannerHost ?? scannerOptions.Host;
            var effectivePort = row.UploadScannerPort ?? scannerOptions.Port;
            var endpointReason = UploadScannerEndpointGuard.GetUnsafeEndpointReason(
                effectiveHost,
                effectivePort,
                scannerOptions.Host,
                scannerOptions.Port,
                requireDeploymentEndpoint: true);
            if (endpointReason is not null)
                throw new RuntimeSettingsValidationException($"uploadScanner.host is invalid: {endpointReason}");
        }
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

    private static bool TrySetNullableInt(JsonElement? input, Action<int?> setter, string key, List<string> changed, int? min = null, int? max = null)
    {
        if (!TryReadNullableNumber(input, key, element => element.GetInt32(), out var value))
            return false;

        if (value is not null)
        {
            if (min is not null && value < min)
                throw new RuntimeSettingsValidationException($"{key} must be greater than or equal to {min}.");
            if (max is not null && value > max)
                throw new RuntimeSettingsValidationException($"{key} must be less than or equal to {max}.");
        }
        setter(value);
        changed.Add(key);
        return true;
    }

    private static bool TrySetNullableDouble(JsonElement? input, Action<double?> setter, string key, List<string> changed, double? min = null, double? max = null)
    {
        if (!TryReadNullableNumber(input, key, element => element.GetDouble(), out var value))
            return false;

        if (value is not null)
        {
            if (min is not null && value < min)
                throw new RuntimeSettingsValidationException($"{key} must be greater than or equal to {min}.");
            if (max is not null && value > max)
                throw new RuntimeSettingsValidationException($"{key} must be less than or equal to {max}.");
        }
        setter(value);
        changed.Add(key);
        return true;
    }

    private static bool TrySetNullableBool(JsonElement? input, Action<bool?> setter, string key, List<string> changed)
    {
        if (input is null) return false;
        var element = input.Value;
        if (element.ValueKind is JsonValueKind.Null)
        {
            setter(null);
            changed.Add(key);
            return true;
        }

        if (element.ValueKind is JsonValueKind.String)
        {
            var raw = element.GetString();
            if (raw == string.Empty)
            {
                setter(null);
                changed.Add(key);
                return true;
            }
            if (bool.TryParse(raw, out var parsed))
            {
                setter(parsed);
                changed.Add(key);
                return true;
            }
        }

        if (element.ValueKind is JsonValueKind.True or JsonValueKind.False)
        {
            setter(element.GetBoolean());
            changed.Add(key);
            return true;
        }

        throw new RuntimeSettingsValidationException($"{key} must be a boolean or null.");
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
            throw new RuntimeSettingsValidationException($"{key} must be a number or null.");

        try
        {
            value = reader(element);
            return true;
        }
        catch (FormatException ex)
        {
            throw new RuntimeSettingsValidationException($"{key} must be a valid number.", ex);
        }
        catch (InvalidOperationException ex)
        {
            throw new RuntimeSettingsValidationException($"{key} must be a valid number.", ex);
        }
    }

    private static string? NormalizeSectionId(string? sectionId)
    {
        var normalized = sectionId?.Trim().ToLowerInvariant();
        return normalized is "email" or "billing" or "sentry" or "backup" or "oauth" or "push" or "uploadscanner"
            ? normalized
            : normalized == "upload-scanner" ? "uploadscanner" : null;
    }

    private static async Task<RuntimeSettingsIntegrationTestResponse> TestSectionAsync(
        string sectionId,
        IRuntimeSettingsProvider provider,
        IWebHostEnvironment env,
        UploadScannerOptions scannerOptions,
        DateTimeOffset testedAt,
        CancellationToken ct)
    {
        var settings = await provider.GetAsync(ct);
        return sectionId switch
        {
            "email" => HasAny(settings.Email.BrevoApiKey, settings.Email.SmtpHost)
                ? Ok(sectionId, "Email configuration is present. No live email was sent.", testedAt)
                : Failed(sectionId, "Configure Brevo or SMTP before enabling email delivery.", testedAt),
            "billing" => HasAll(settings.Billing.StripeSecretKey, settings.Billing.StripePublishableKey, settings.Billing.StripeWebhookSecret)
                ? Ok(sectionId, "Stripe keys and webhook secret are configured. No live charge was created.", testedAt)
                : Failed(sectionId, "Configure Stripe secret, publishable key, and webhook secret.", testedAt),
            "sentry" => Uri.TryCreate(settings.Sentry.Dsn, UriKind.Absolute, out var sentryUri)
                        && sentryUri.Scheme == Uri.UriSchemeHttps
                ? Ok(sectionId, "Sentry DSN format is valid. No event was sent.", testedAt)
                : Failed(sectionId, "Configure a valid https:// Sentry DSN.", testedAt),
            "backup" => HasAll(settings.Backup.S3Url, settings.Backup.AwsAccessKeyId, settings.Backup.AwsSecretAccessKey, settings.Backup.GpgPassphrase)
                ? Ok(sectionId, "Backup destination and encryption settings are present. No backup was run.", testedAt)
                : Failed(sectionId, "Configure S3/R2 destination, credentials, and GPG passphrase.", testedAt),
            "oauth" => HasAll(settings.OAuth.GoogleClientId, settings.OAuth.GoogleClientSecret)
                       || HasAll(settings.OAuth.FacebookAppId, settings.OAuth.FacebookAppSecret)
                ? Ok(sectionId, "At least one OAuth provider is configured. No sign-in was attempted.", testedAt)
                : Failed(sectionId, "Configure Google or Facebook OAuth credentials. Apple fields are stored for future use but are not an active sign-in provider yet.", testedAt),
            "push" => HasAll(settings.Push.VapidSubject, settings.Push.VapidPublicKey, settings.Push.VapidPrivateKey)
                      || HasAll(settings.Push.FcmProjectId, settings.Push.FcmServerKey)
                      || HasAll(settings.Push.ApnsBundleId, settings.Push.ApnsTeamId, settings.Push.ApnsKeyId, settings.Push.ApnsAuthKey)
                ? Ok(sectionId, "At least one push provider is configured. No notification was sent.", testedAt)
                : Failed(sectionId, "Configure browser VAPID, FCM, or APNs credentials before enabling push.", testedAt),
            "uploadscanner" => await TestUploadScannerAsync(settings.UploadScanner, env, scannerOptions, sectionId, testedAt, ct),
            _ => Failed(sectionId, "Unknown integration section.", testedAt),
        };
    }

    private static async Task<RuntimeSettingsIntegrationTestResponse> TestUploadScannerAsync(
        UploadScannerSettings settings,
        IWebHostEnvironment env,
        UploadScannerOptions scannerOptions,
        string sectionId,
        DateTimeOffset testedAt,
        CancellationToken ct)
    {
        if (!settings.Provider.Equals("clamav", StringComparison.OrdinalIgnoreCase))
            return Failed(sectionId, "Upload scanner provider is not set to clamav.", testedAt);
        if (env.IsProduction() && !settings.FailClosedOnError)
            return Failed(sectionId, "ClamAV scan failures must remain fail-closed in production.", testedAt);
        var endpointReason = UploadScannerEndpointGuard.GetUnsafeEndpointReason(
            settings.Host,
            settings.Port,
            scannerOptions.Host,
            scannerOptions.Port,
            requireDeploymentEndpoint: env.IsProduction());
        if (endpointReason is not null)
            return Failed(sectionId, endpointReason, testedAt);

        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(settings.TimeoutSeconds, 1, 10)));
            using var tcp = new TcpClient();
            await tcp.ConnectAsync(settings.Host, settings.Port, timeoutCts.Token);
            return Ok(sectionId, "ClamAV TCP endpoint accepted a connection.", testedAt);
        }
        catch (Exception ex) when (ex is SocketException or TimeoutException or OperationCanceledException)
        {
            return Failed(sectionId, "ClamAV endpoint is unreachable.", testedAt);
        }
    }

    private static RuntimeSettingsIntegrationTestResponse Ok(string section, string message, DateTimeOffset testedAt)
        => new(section, "ok", message, testedAt);

    private static RuntimeSettingsIntegrationTestResponse Failed(string section, string message, DateTimeOffset testedAt)
        => new(section, "failed", message, testedAt);

    private static bool HasAny(params string?[] values)
        => values.Any(v => !string.IsNullOrWhiteSpace(v));

    private static bool HasAll(params string?[] values)
        => values.All(v => !string.IsNullOrWhiteSpace(v));

    private sealed class RuntimeSettingsValidationException : InvalidOperationException
    {
        public RuntimeSettingsValidationException(string message) : base(message) { }
        public RuntimeSettingsValidationException(string message, Exception innerException) : base(message, innerException) { }
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
    public RuntimeSettingsUploadScannerUpdate? UploadScanner { get; set; }
}

public sealed class RuntimeSettingsEmailUpdate
{
    public string? BrevoApiKey { get; set; }
    public JsonElement? BrevoEmailVerificationTemplateId { get; set; }
    public JsonElement? BrevoPasswordResetTemplateId { get; set; }
    public string? SmtpHost { get; set; }
    public JsonElement? SmtpPort { get; set; }
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
    public string? PayPalClientId { get; set; }
    public string? PayPalClientSecret { get; set; }
    public string? PayPalWebhookId { get; set; }
    public string? PayPalSuccessUrl { get; set; }
    public string? PayPalCancelUrl { get; set; }
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
    public string? VapidSubject { get; set; }
    public string? VapidPublicKey { get; set; }
    public string? VapidPrivateKey { get; set; }
}

public sealed class RuntimeSettingsUploadScannerUpdate
{
    public string? Provider { get; set; }
    public string? Host { get; set; }
    public JsonElement? Port { get; set; }
    public JsonElement? TimeoutSeconds { get; set; }
    public JsonElement? FailClosedOnError { get; set; }
}

public sealed record RuntimeSettingsIntegrationTestResponse(
    string Section,
    string Status,
    string Message,
    DateTimeOffset TestedAt);
