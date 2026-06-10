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
using OetLearner.Api.Services.Pronunciation;
using OetLearner.Api.Services.Rulebook;
using OetLearner.Api.Services.Settings;

namespace OetLearner.Api.Endpoints;

/// <summary>
/// Admin endpoints for managing runtime infrastructure settings (Email/Brevo,
/// Stripe, Sentry, Backup S3, OAuth providers, Push, Zoom). These map onto the
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
                var settings = await provider.GetAsync(ct);
                return Results.Ok(BuildResponse(settings));
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
                    ApplyZoom(row, request.Zoom, provider, env, changedKeys);
                    ApplyStripe(row, request.Stripe, provider, changedKeys);
                    ApplySpeakingWhisper(row, request.SpeakingWhisper, provider, changedKeys);
                    ApplySpeakingLiveKit(row, request.SpeakingLiveKit, provider, changedKeys);
                    ApplySpeakingAi(row, request.SpeakingAi, provider, changedKeys);
                    ApplySpeakingStorage(row, request.SpeakingStorage, provider, changedKeys);
                    ApplySpeakingCompliance(row, request.SpeakingCompliance, changedKeys);
                    ApplySpeakingFeatures(row, request.SpeakingFeatures, changedKeys);
                    ApplyCheckoutCom(row, request.CheckoutCom, provider, changedKeys);
                    ApplyPaymob(row, request.Paymob, provider, changedKeys);
                    ApplyPayTabs(row, request.PayTabs, provider, changedKeys);
                    ApplySoketi(row, request.Soketi, provider, changedKeys);
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

                return Results.Ok(BuildResponse(await provider.GetAsync(ct)));
            })
            .WithAdminWrite("AdminSystemAdmin");

        admin.MapPost("/runtime-settings/test/{sectionId}", async Task<IResult> (
                string sectionId,
                HttpContext http,
                LearnerDbContext db,
                IRuntimeSettingsProvider provider,
                IWebHostEnvironment env,
                IOptions<UploadScannerOptions> scannerOptions,
                IPronunciationCredentialResolver whisperRegistry,
                IHttpClientFactory httpClientFactory,
                TimeProvider clock,
                CancellationToken ct) =>
            {
                var normalized = NormalizeSectionId(sectionId);
                if (normalized is null)
                    return Results.BadRequest(new { message = $"Unknown integration section '{sectionId}'." });

                var testedAt = clock.GetUtcNow();
                var result = await TestSectionAsync(normalized, provider, env, scannerOptions.Value, whisperRegistry, httpClientFactory, testedAt, ct);
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

    private static object BuildResponse(EffectiveSettings settings)
        => new
        {
            email = new
            {
                brevoApiKey = MaskPlainSecret(settings.Email.BrevoApiKey),
                brevoEmailVerificationTemplateId = settings.Email.BrevoEmailVerificationTemplateId,
                brevoPasswordResetTemplateId = settings.Email.BrevoPasswordResetTemplateId,
                smtpHost = settings.Email.SmtpHost,
                smtpPort = settings.Email.SmtpPort,
                smtpUsername = settings.Email.SmtpUsername,
                smtpPassword = MaskPlainSecret(settings.Email.SmtpPassword),
                smtpFromAddress = settings.Email.SmtpFromAddress,
                smtpFromName = settings.Email.SmtpFromName,
            },
            billing = new
            {
                stripeSecretKey = MaskPlainSecret(settings.Billing.StripeSecretKey),
                stripePublishableKey = settings.Billing.StripePublishableKey,
                stripeWebhookSecret = MaskPlainSecret(settings.Billing.StripeWebhookSecret),
                stripeSuccessUrl = settings.Billing.StripeSuccessUrl,
                stripeCancelUrl = settings.Billing.StripeCancelUrl,
                paypalClientId = settings.Billing.PayPalClientId,
                paypalClientSecret = MaskPlainSecret(settings.Billing.PayPalClientSecret),
                paypalWebhookId = MaskPlainSecret(settings.Billing.PayPalWebhookId),
                paypalSuccessUrl = settings.Billing.PayPalSuccessUrl,
                paypalCancelUrl = settings.Billing.PayPalCancelUrl,
            },
            sentry = new
            {
                dsn = settings.Sentry.Dsn,
                environment = settings.Sentry.Environment,
                sampleRate = settings.Sentry.SampleRate,
            },
            backup = new
            {
                s3Url = settings.Backup.S3Url,
                awsAccessKeyId = settings.Backup.AwsAccessKeyId,
                awsSecretAccessKey = MaskPlainSecret(settings.Backup.AwsSecretAccessKey),
                gpgPassphrase = MaskPlainSecret(settings.Backup.GpgPassphrase),
                alertWebhook = settings.Backup.AlertWebhook,
            },
            oauth = new
            {
                googleClientId = settings.OAuth.GoogleClientId,
                googleClientSecret = MaskPlainSecret(settings.OAuth.GoogleClientSecret),
                appleClientId = settings.OAuth.AppleClientId,
                appleTeamId = settings.OAuth.AppleTeamId,
                appleKeyId = settings.OAuth.AppleKeyId,
                applePrivateKey = MaskPlainSecret(settings.OAuth.ApplePrivateKey),
                facebookAppId = settings.OAuth.FacebookAppId,
                facebookAppSecret = MaskPlainSecret(settings.OAuth.FacebookAppSecret),
            },
            push = new
            {
                apnsKeyId = settings.Push.ApnsKeyId,
                apnsTeamId = settings.Push.ApnsTeamId,
                apnsBundleId = settings.Push.ApnsBundleId,
                apnsAuthKey = MaskPlainSecret(settings.Push.ApnsAuthKey),
                fcmServerKey = MaskPlainSecret(settings.Push.FcmServerKey),
                fcmProjectId = settings.Push.FcmProjectId,
                vapidSubject = settings.Push.VapidSubject,
                vapidPublicKey = settings.Push.VapidPublicKey,
                vapidPrivateKey = MaskPlainSecret(settings.Push.VapidPrivateKey),
            },
            uploadScanner = new
            {
                provider = settings.UploadScanner.Provider,
                host = settings.UploadScanner.Host,
                port = settings.UploadScanner.Port,
                timeoutSeconds = settings.UploadScanner.TimeoutSeconds,
                failClosedOnError = settings.UploadScanner.FailClosedOnError,
            },
            zoom = new
            {
                enabled = settings.Zoom.Enabled,
                accountId = settings.Zoom.AccountId,
                clientId = settings.Zoom.ClientId,
                clientSecret = MaskPlainSecret(settings.Zoom.ClientSecret),
                apiBaseUrl = settings.Zoom.ApiBaseUrl,
                tokenUrl = settings.Zoom.TokenUrl,
                hostUserId = settings.Zoom.HostUserId,
                meetingSdkKey = settings.Zoom.MeetingSdkKey,
                meetingSdkSecret = MaskPlainSecret(settings.Zoom.MeetingSdkSecret),
                webhookSecretToken = MaskPlainSecret(settings.Zoom.WebhookSecretToken),
                webhookRetryToleranceSeconds = settings.Zoom.WebhookRetryToleranceSeconds,
                allowSandboxFallback = settings.Zoom.AllowSandboxFallback,
            },
            stripe = new
            {
                secretKey = MaskPlainSecret(settings.Stripe.SecretKey),
                publishableKey = settings.Stripe.PublishableKey,
                webhookSecret = MaskPlainSecret(settings.Stripe.WebhookSecret),
                taxAutomaticEnabled = settings.Stripe.TaxAutomaticEnabled,
                taxRegistrations = settings.Stripe.TaxRegistrations,
                customerPortalConfigurationId = settings.Stripe.CustomerPortalConfigurationId,
                radarHighRiskCountryAllowReview = settings.Stripe.RadarHighRiskCountryAllowReview,
                radarBlockEmailDomainsCsv = settings.Stripe.RadarBlockEmailDomainsCsv,
            },
            // 2026-05-28 audit fix — Whisper transcription API key for the
            // Speaking module's RULE_40 tone pipeline. Plaintext never leaves
            // the host process; the apiKey is always masked.
            speakingWhisper = new
            {
                apiKey = MaskPlainSecret(settings.SpeakingWhisper.ApiKey),
                baseUrl = settings.SpeakingWhisper.BaseUrl,
                model = settings.SpeakingWhisper.Model,
                isConfigured = settings.SpeakingWhisper.IsConfigured,
            },
            speakingLiveKit = new
            {
                provider = settings.SpeakingLiveKit.Provider,
                apiKey = MaskPlainSecret(settings.SpeakingLiveKit.ApiKey),
                apiSecret = MaskPlainSecret(settings.SpeakingLiveKit.ApiSecret),
                wssUrl = settings.SpeakingLiveKit.WssUrl,
                webhookSigningSecret = MaskPlainSecret(settings.SpeakingLiveKit.WebhookSigningSecret),
                egressBucket = settings.SpeakingLiveKit.EgressBucket,
                defaultMaxDurationSeconds = settings.SpeakingLiveKit.DefaultMaxDurationSeconds,
                egressEnabled = settings.SpeakingLiveKit.EgressEnabled,
                isEnabled = settings.SpeakingLiveKit.IsEnabled,
            },
            speakingAi = new
            {
                anthropicApiKey = MaskPlainSecret(settings.SpeakingAi.AnthropicApiKey),
                elevenLabsApiKey = MaskPlainSecret(settings.SpeakingAi.ElevenLabsApiKey),
                isAnthropicConfigured = settings.SpeakingAi.IsAnthropicConfigured,
                isElevenLabsConfigured = settings.SpeakingAi.IsElevenLabsConfigured,
            },
            speakingStorage = new
            {
                awsAccessKeyId = settings.SpeakingStorage.AwsAccessKeyId,
                awsSecretAccessKey = MaskPlainSecret(settings.SpeakingStorage.AwsSecretAccessKey),
                region = settings.SpeakingStorage.Region,
                bucket = settings.SpeakingStorage.Bucket,
                isConfigured = settings.SpeakingStorage.IsConfigured,
            },
            speakingCompliance = new
            {
                currentConsentVersion = settings.SpeakingCompliance.CurrentConsentVersion,
                currentLiveVideoConsentVersion = settings.SpeakingCompliance.CurrentLiveVideoConsentVersion,
                retentionDaysDefault = settings.SpeakingCompliance.RetentionDaysDefault,
                retentionDaysWhenTutorReviewed = settings.SpeakingCompliance.RetentionDaysWhenTutorReviewed,
                auditLogRetentionDays = settings.SpeakingCompliance.AuditLogRetentionDays,
            },
            speakingFeatures = new
            {
                speakingV2Enabled = settings.SpeakingFeatures.SpeakingV2Enabled,
            },
            checkoutCom = new
            {
                apiBaseUrl = settings.CheckoutCom.ApiBaseUrl,
                secretKey = MaskPlainSecret(settings.CheckoutCom.SecretKey),
                publicKey = settings.CheckoutCom.PublicKey,
                processingChannelId = settings.CheckoutCom.ProcessingChannelId,
                webhookSecret = MaskPlainSecret(settings.CheckoutCom.WebhookSecret),
                successUrl = settings.CheckoutCom.SuccessUrl,
                cancelUrl = settings.CheckoutCom.CancelUrl,
                isConfigured = settings.CheckoutCom.IsConfigured,
            },
            paymob = new
            {
                apiBaseUrl = settings.Paymob.ApiBaseUrl,
                apiKey = MaskPlainSecret(settings.Paymob.ApiKey),
                merchantId = settings.Paymob.MerchantId,
                hmacSecret = MaskPlainSecret(settings.Paymob.HmacSecret),
                integrationIdsJson = settings.Paymob.IntegrationIds.Count > 0
                    ? JsonSupport.Serialize(settings.Paymob.IntegrationIds)
                    : null,
                iframeId = settings.Paymob.IframeId,
                successUrl = settings.Paymob.SuccessUrl,
                cancelUrl = settings.Paymob.CancelUrl,
                isConfigured = settings.Paymob.IsConfigured,
            },
            payTabs = new
            {
                apiBaseUrl = settings.PayTabs.ApiBaseUrl,
                serverKey = MaskPlainSecret(settings.PayTabs.ServerKey),
                profileId = settings.PayTabs.ProfileId,
                webhookSecret = MaskPlainSecret(settings.PayTabs.WebhookSecret),
                successUrl = settings.PayTabs.SuccessUrl,
                cancelUrl = settings.PayTabs.CancelUrl,
                isConfigured = settings.PayTabs.IsConfigured,
            },
            soketi = new
            {
                host = settings.Soketi.Host,
                port = settings.Soketi.Port,
                appId = settings.Soketi.AppId,
                appKey = settings.Soketi.AppKey,
                appSecret = MaskPlainSecret(settings.Soketi.AppSecret),
                useTls = settings.Soketi.UseTls,
                enabled = settings.Soketi.Enabled,
            },
            updatedBy = settings.UpdatedByUserName,
            updatedByUserId = settings.UpdatedByUserId,
            updatedAt = settings.UpdatedAt,
        };

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
            zoom = new
            {
                enabled = r.ZoomEnabled,
                accountId = r.ZoomAccountId,
                clientId = r.ZoomClientId,
                clientSecret = MaskSecret(r.ZoomClientSecretEncrypted),
                apiBaseUrl = r.ZoomApiBaseUrl,
                tokenUrl = r.ZoomTokenUrl,
                hostUserId = r.ZoomHostUserId,
                meetingSdkKey = r.ZoomMeetingSdkKey,
                meetingSdkSecret = MaskSecret(r.ZoomMeetingSdkSecretEncrypted),
                webhookSecretToken = MaskSecret(r.ZoomWebhookSecretTokenEncrypted),
                webhookRetryToleranceSeconds = r.ZoomWebhookRetryToleranceSeconds,
                allowSandboxFallback = r.ZoomAllowSandboxFallback,
            },
            stripe = new
            {
                secretKey = MaskSecret(r.StripeSecretKeyEncrypted),
                publishableKey = r.StripePublishableKey,
                webhookSecret = MaskSecret(r.StripeWebhookSecretEncrypted),
                taxAutomaticEnabled = r.StripeTaxAutomaticEnabled,
                taxRegistrations = string.IsNullOrWhiteSpace(r.StripeTaxRegistrationsCsv)
                    ? Array.Empty<string>()
                    : r.StripeTaxRegistrationsCsv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
                customerPortalConfigurationId = r.StripeCustomerPortalConfigurationId,
                radarHighRiskCountryAllowReview = r.StripeRadarHighRiskCountryAllowReview,
                radarBlockEmailDomainsCsv = r.StripeRadarBlockEmailDomainsCsv,
            },
            speakingWhisper = new
            {
                apiKey = MaskSecret(r.SpeakingWhisperApiKeyEncrypted),
                baseUrl = r.SpeakingWhisperBaseUrl,
                model = r.SpeakingWhisperModel,
                isConfigured = !string.IsNullOrEmpty(r.SpeakingWhisperApiKeyEncrypted),
            },
            speakingLiveKit = new
            {
                provider = r.SpeakingLiveKitProvider,
                apiKey = MaskSecret(r.SpeakingLiveKitApiKeyEncrypted),
                apiSecret = MaskSecret(r.SpeakingLiveKitApiSecretEncrypted),
                wssUrl = r.SpeakingLiveKitWssUrl,
                webhookSigningSecret = MaskSecret(r.SpeakingLiveKitWebhookSigningSecretEncrypted),
                egressBucket = r.SpeakingLiveKitEgressBucket,
                defaultMaxDurationSeconds = r.SpeakingLiveKitDefaultMaxDurationSeconds,
                egressEnabled = r.SpeakingLiveKitEgressEnabled,
                isEnabled = !string.Equals(r.SpeakingLiveKitProvider, "disabled", StringComparison.OrdinalIgnoreCase)
                            && !string.IsNullOrEmpty(r.SpeakingLiveKitApiKeyEncrypted),
            },
            speakingAi = new
            {
                anthropicApiKey = MaskSecret(r.SpeakingAnthropicApiKeyEncrypted),
                elevenLabsApiKey = MaskSecret(r.SpeakingElevenLabsApiKeyEncrypted),
                isAnthropicConfigured = !string.IsNullOrEmpty(r.SpeakingAnthropicApiKeyEncrypted),
                isElevenLabsConfigured = !string.IsNullOrEmpty(r.SpeakingElevenLabsApiKeyEncrypted),
            },
            speakingStorage = new
            {
                awsAccessKeyId = r.SpeakingAwsAccessKeyId,
                awsSecretAccessKey = MaskSecret(r.SpeakingAwsSecretAccessKeyEncrypted),
                region = r.SpeakingAwsRegion,
                bucket = r.SpeakingAwsBucket,
                isConfigured = !string.IsNullOrEmpty(r.SpeakingAwsAccessKeyId) && !string.IsNullOrEmpty(r.SpeakingAwsSecretAccessKeyEncrypted),
            },
            speakingCompliance = new
            {
                currentConsentVersion = r.SpeakingComplianceCurrentConsentVersion,
                currentLiveVideoConsentVersion = r.SpeakingComplianceCurrentLiveVideoConsentVersion,
                retentionDaysDefault = r.SpeakingComplianceRetentionDaysDefault,
                retentionDaysWhenTutorReviewed = r.SpeakingComplianceRetentionDaysWhenTutorReviewed,
                auditLogRetentionDays = r.SpeakingComplianceAuditLogRetentionDays,
            },
            speakingFeatures = new
            {
                speakingV2Enabled = r.SpeakingV2Enabled,
            },
            updatedBy = r.UpdatedByUserName,
            updatedByUserId = r.UpdatedByUserId,
            updatedAt = r.UpdatedAt == default ? (DateTimeOffset?)null : r.UpdatedAt,
        };

    private static string MaskSecret(string? cipher)
        => string.IsNullOrEmpty(cipher) ? string.Empty : SecretMask;

    private static string MaskPlainSecret(string? value)
        => string.IsNullOrWhiteSpace(value) ? string.Empty : SecretMask;

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

    private static void ApplyStripe(RuntimeSettingsRow row, RuntimeSettingsStripeUpdate? d,
        IRuntimeSettingsProvider p, List<string> changed)
    {
        if (d is null) return;

        // Secret key + webhook secret share storage with the legacy Billing
        // section: same column, same encryption. Either section is allowed to
        // write them; the PUT contract documents the Stripe section as
        // canonical going forward.
        if (TrySetSecret(d.SecretKey, p, v => row.StripeSecretKeyEncrypted = v, "stripe.secretKey", changed)) { }
        if (TrySetPlain(d.PublishableKey, v => row.StripePublishableKey = v, "stripe.publishableKey", changed)) { }
        if (TrySetSecret(d.WebhookSecret, p, v => row.StripeWebhookSecretEncrypted = v, "stripe.webhookSecret", changed)) { }
        if (TrySetNullableBool(d.TaxAutomaticEnabled, v => row.StripeTaxAutomaticEnabled = v, "stripe.taxAutomaticEnabled", changed)) { }

        if (d.TaxRegistrations is not null)
        {
            // Trim, dedupe, upper-case. Empty list clears the override; null
            // (omitted) leaves it unchanged.
            var cleaned = d.TaxRegistrations
                .Where(static r => !string.IsNullOrWhiteSpace(r))
                .Select(static r => r.Trim().ToUpperInvariant())
                .Distinct()
                .ToArray();
            row.StripeTaxRegistrationsCsv = cleaned.Length == 0 ? null : string.Join(",", cleaned);
            changed.Add("stripe.taxRegistrations");
        }

        if (TrySetPlain(d.CustomerPortalConfigurationId, v => row.StripeCustomerPortalConfigurationId = v,
            "stripe.customerPortalConfigurationId", changed)) { }
        if (TrySetNullableBool(d.RadarHighRiskCountryAllowReview, v => row.StripeRadarHighRiskCountryAllowReview = v,
            "stripe.radarHighRiskCountryAllowReview", changed)) { }
        if (TrySetPlain(d.RadarBlockEmailDomainsCsv, v => row.StripeRadarBlockEmailDomainsCsv = v,
            "stripe.radarBlockEmailDomainsCsv", changed)) { }
    }

    /// <summary>
    /// 2026-05-28 audit fix — apply Speaking Whisper transcription overrides
    /// (RULE_40 tone pipeline). Mirrors the Stripe pattern: encrypted secret +
    /// plain config knobs.
    /// </summary>
    private static void ApplySpeakingWhisper(RuntimeSettingsRow row, RuntimeSettingsSpeakingWhisperUpdate? d,
        IRuntimeSettingsProvider p, List<string> changed)
    {
        if (d is null) return;
        if (TrySetSecret(d.ApiKey, p, v => row.SpeakingWhisperApiKeyEncrypted = v, "speakingWhisper.apiKey", changed)) { }
        if (TrySetPlain(d.BaseUrl, v => row.SpeakingWhisperBaseUrl = v, "speakingWhisper.baseUrl", changed)) { }
        if (TrySetPlain(d.Model, v => row.SpeakingWhisperModel = v, "speakingWhisper.model", changed)) { }
    }

    private static void ApplySpeakingLiveKit(RuntimeSettingsRow row, RuntimeSettingsSpeakingLiveKitUpdate? d,
        IRuntimeSettingsProvider p, List<string> changed)
    {
        if (d is null) return;
        if (TrySetPlain(d.Provider, v => row.SpeakingLiveKitProvider = v, "speakingLiveKit.provider", changed)) { }
        if (TrySetSecret(d.ApiKey, p, v => row.SpeakingLiveKitApiKeyEncrypted = v, "speakingLiveKit.apiKey", changed)) { }
        if (TrySetSecret(d.ApiSecret, p, v => row.SpeakingLiveKitApiSecretEncrypted = v, "speakingLiveKit.apiSecret", changed)) { }
        if (TrySetPlain(d.WssUrl, v => row.SpeakingLiveKitWssUrl = v, "speakingLiveKit.wssUrl", changed)) { }
        if (TrySetSecret(d.WebhookSigningSecret, p, v => row.SpeakingLiveKitWebhookSigningSecretEncrypted = v, "speakingLiveKit.webhookSigningSecret", changed)) { }
        if (TrySetPlain(d.EgressBucket, v => row.SpeakingLiveKitEgressBucket = v, "speakingLiveKit.egressBucket", changed)) { }
        if (TrySetNullableInt(d.DefaultMaxDurationSeconds, v => row.SpeakingLiveKitDefaultMaxDurationSeconds = v, "speakingLiveKit.defaultMaxDurationSeconds", changed, min: 60, max: 7200)) { }
        if (TrySetNullableBool(d.EgressEnabled, v => row.SpeakingLiveKitEgressEnabled = v, "speakingLiveKit.egressEnabled", changed)) { }
    }

    private static void ApplySpeakingAi(RuntimeSettingsRow row, RuntimeSettingsSpeakingAiUpdate? d,
        IRuntimeSettingsProvider p, List<string> changed)
    {
        if (d is null) return;
        if (TrySetSecret(d.AnthropicApiKey, p, v => row.SpeakingAnthropicApiKeyEncrypted = v, "speakingAi.anthropicApiKey", changed)) { }
        if (TrySetSecret(d.ElevenLabsApiKey, p, v => row.SpeakingElevenLabsApiKeyEncrypted = v, "speakingAi.elevenLabsApiKey", changed)) { }
    }

    private static void ApplySpeakingStorage(RuntimeSettingsRow row, RuntimeSettingsSpeakingStorageUpdate? d,
        IRuntimeSettingsProvider p, List<string> changed)
    {
        if (d is null) return;
        if (TrySetPlain(d.AwsAccessKeyId, v => row.SpeakingAwsAccessKeyId = v, "speakingStorage.awsAccessKeyId", changed)) { }
        if (TrySetSecret(d.AwsSecretAccessKey, p, v => row.SpeakingAwsSecretAccessKeyEncrypted = v, "speakingStorage.awsSecretAccessKey", changed)) { }
        if (TrySetPlain(d.Region, v => row.SpeakingAwsRegion = v, "speakingStorage.region", changed)) { }
        if (TrySetPlain(d.Bucket, v => row.SpeakingAwsBucket = v, "speakingStorage.bucket", changed)) { }
    }

    private static void ApplySpeakingCompliance(RuntimeSettingsRow row, RuntimeSettingsSpeakingComplianceUpdate? d,
        List<string> changed)
    {
        if (d is null) return;
        if (TrySetPlain(d.CurrentConsentVersion, v => row.SpeakingComplianceCurrentConsentVersion = v, "speakingCompliance.currentConsentVersion", changed)) { }
        if (TrySetPlain(d.CurrentLiveVideoConsentVersion, v => row.SpeakingComplianceCurrentLiveVideoConsentVersion = v, "speakingCompliance.currentLiveVideoConsentVersion", changed)) { }
        if (TrySetNullableInt(d.RetentionDaysDefault, v => row.SpeakingComplianceRetentionDaysDefault = v, "speakingCompliance.retentionDaysDefault", changed, min: 1, max: 36500)) { }
        if (TrySetNullableInt(d.RetentionDaysWhenTutorReviewed, v => row.SpeakingComplianceRetentionDaysWhenTutorReviewed = v, "speakingCompliance.retentionDaysWhenTutorReviewed", changed, min: 1, max: 36500)) { }
        if (TrySetNullableInt(d.AuditLogRetentionDays, v => row.SpeakingComplianceAuditLogRetentionDays = v, "speakingCompliance.auditLogRetentionDays", changed, min: 1, max: 36500)) { }
    }

    private static void ApplySpeakingFeatures(RuntimeSettingsRow row, RuntimeSettingsSpeakingFeaturesUpdate? d,
        List<string> changed)
    {
        if (d is null) return;
        if (TrySetNullableBool(d.SpeakingV2Enabled, v => row.SpeakingV2Enabled = v, "speakingFeatures.speakingV2Enabled", changed)) { }
    }

    private static void ApplyCheckoutCom(RuntimeSettingsRow row, RuntimeSettingsCheckoutComUpdate? d,
        IRuntimeSettingsProvider p, List<string> changed)
    {
        if (d is null) return;
        if (TrySetPlain(d.ApiBaseUrl, v => row.CheckoutComApiBaseUrl = v, "checkoutCom.apiBaseUrl", changed)) { }
        if (TrySetSecret(d.SecretKey, p, v => row.CheckoutComSecretKeyEncrypted = v, "checkoutCom.secretKey", changed)) { }
        if (TrySetPlain(d.PublicKey, v => row.CheckoutComPublicKey = v, "checkoutCom.publicKey", changed)) { }
        if (TrySetPlain(d.ProcessingChannelId, v => row.CheckoutComProcessingChannelId = v, "checkoutCom.processingChannelId", changed)) { }
        if (TrySetSecret(d.WebhookSecret, p, v => row.CheckoutComWebhookSecretEncrypted = v, "checkoutCom.webhookSecret", changed)) { }
        if (TrySetPlain(d.SuccessUrl, v => row.CheckoutComSuccessUrl = v, "checkoutCom.successUrl", changed)) { }
        if (TrySetPlain(d.CancelUrl, v => row.CheckoutComCancelUrl = v, "checkoutCom.cancelUrl", changed)) { }
    }

    private static void ApplyPaymob(RuntimeSettingsRow row, RuntimeSettingsPaymobUpdate? d,
        IRuntimeSettingsProvider p, List<string> changed)
    {
        if (d is null) return;
        if (TrySetPlain(d.ApiBaseUrl, v => row.PaymobApiBaseUrl = v, "paymob.apiBaseUrl", changed)) { }
        if (TrySetSecret(d.ApiKey, p, v => row.PaymobApiKeyEncrypted = v, "paymob.apiKey", changed)) { }
        if (TrySetPlain(d.MerchantId, v => row.PaymobMerchantId = v, "paymob.merchantId", changed)) { }
        if (TrySetSecret(d.HmacSecret, p, v => row.PaymobHmacSecretEncrypted = v, "paymob.hmacSecret", changed)) { }
        if (TrySetPlain(d.IntegrationIdsJson, v => row.PaymobIntegrationIdsJson = v, "paymob.integrationIdsJson", changed)) { }
        if (TrySetNullableInt(d.IframeId, v => row.PaymobIframeId = v, "paymob.iframeId", changed, min: 0, max: int.MaxValue)) { }
        if (TrySetPlain(d.SuccessUrl, v => row.PaymobSuccessUrl = v, "paymob.successUrl", changed)) { }
        if (TrySetPlain(d.CancelUrl, v => row.PaymobCancelUrl = v, "paymob.cancelUrl", changed)) { }
    }

    private static void ApplyPayTabs(RuntimeSettingsRow row, RuntimeSettingsPayTabsUpdate? d,
        IRuntimeSettingsProvider p, List<string> changed)
    {
        if (d is null) return;
        if (TrySetPlain(d.ApiBaseUrl, v => row.PayTabsApiBaseUrl = v, "payTabs.apiBaseUrl", changed)) { }
        if (TrySetSecret(d.ServerKey, p, v => row.PayTabsServerKeyEncrypted = v, "payTabs.serverKey", changed)) { }
        if (TrySetPlain(d.ProfileId, v => row.PayTabsProfileId = v, "payTabs.profileId", changed)) { }
        if (TrySetSecret(d.WebhookSecret, p, v => row.PayTabsWebhookSecretEncrypted = v, "payTabs.webhookSecret", changed)) { }
        if (TrySetPlain(d.SuccessUrl, v => row.PayTabsSuccessUrl = v, "payTabs.successUrl", changed)) { }
        if (TrySetPlain(d.CancelUrl, v => row.PayTabsCancelUrl = v, "payTabs.cancelUrl", changed)) { }
    }

    private static void ApplySoketi(RuntimeSettingsRow row, RuntimeSettingsSoketiUpdate? d,
        IRuntimeSettingsProvider p, List<string> changed)
    {
        if (d is null) return;
        if (TrySetPlain(d.Host, v => row.SoketiHost = v, "soketi.host", changed)) { }
        if (TrySetNullableInt(d.Port, v => row.SoketiPort = v, "soketi.port", changed, min: 1, max: 65535)) { }
        if (TrySetPlain(d.AppId, v => row.SoketiAppId = v, "soketi.appId", changed)) { }
        if (TrySetPlain(d.AppKey, v => row.SoketiAppKey = v, "soketi.appKey", changed)) { }
        if (TrySetSecret(d.AppSecret, p, v => row.SoketiAppSecretEncrypted = v, "soketi.appSecret", changed)) { }
        if (TrySetNullableBool(d.UseTls, v => row.SoketiUseTls = v, "soketi.useTls", changed)) { }
        if (TrySetNullableBool(d.Enabled, v => row.SoketiEnabled = v, "soketi.enabled", changed)) { }
    }

    private static void ApplyZoom(RuntimeSettingsRow row, RuntimeSettingsZoomUpdate? d,
        IRuntimeSettingsProvider p, IWebHostEnvironment env, List<string> changed)
    {
        if (d is null) return;
        if (TrySetNullableBool(d.Enabled, v => row.ZoomEnabled = v, "zoom.enabled", changed)) { }
        if (TrySetPlain(d.AccountId, v => row.ZoomAccountId = v, "zoom.accountId", changed)) { }
        if (TrySetPlain(d.ClientId, v => row.ZoomClientId = v, "zoom.clientId", changed)) { }
        if (TrySetSecret(d.ClientSecret, p, v => row.ZoomClientSecretEncrypted = v, "zoom.clientSecret", changed)) { }
        if (TrySetPlain(d.ApiBaseUrl, v => row.ZoomApiBaseUrl = v, "zoom.apiBaseUrl", changed)) { }
        if (TrySetPlain(d.TokenUrl, v => row.ZoomTokenUrl = v, "zoom.tokenUrl", changed)) { }
        if (TrySetPlain(d.HostUserId, v => row.ZoomHostUserId = v, "zoom.hostUserId", changed)) { }
        if (TrySetPlain(d.MeetingSdkKey, v => row.ZoomMeetingSdkKey = v, "zoom.meetingSdkKey", changed)) { }
        if (TrySetSecret(d.MeetingSdkSecret, p, v => row.ZoomMeetingSdkSecretEncrypted = v, "zoom.meetingSdkSecret", changed)) { }
        if (TrySetSecret(d.WebhookSecretToken, p, v => row.ZoomWebhookSecretTokenEncrypted = v, "zoom.webhookSecretToken", changed)) { }
        if (TrySetNullableInt(d.WebhookRetryToleranceSeconds, v => row.ZoomWebhookRetryToleranceSeconds = v, "zoom.webhookRetryToleranceSeconds", changed, min: 60, max: 3600)) { }
        if (TrySetNullableBool(d.AllowSandboxFallback, v => row.ZoomAllowSandboxFallback = v, "zoom.allowSandboxFallback", changed)) { }

        if (env.IsProduction() && row.ZoomAllowSandboxFallback == true)
        {
            throw new RuntimeSettingsValidationException("zoom.allowSandboxFallback cannot be enabled in production.");
        }

        ValidateZoomUrl(row.ZoomApiBaseUrl, "zoom.apiBaseUrl", ["api.zoom.us", "api.zoom.com"]);
        ValidateZoomUrl(row.ZoomTokenUrl, "zoom.tokenUrl", ["zoom.us", "zoom.com"]);
    }

    private static void ValidateZoomUrl(string? value, string key, IReadOnlyCollection<string> allowedHosts)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
        {
            throw new RuntimeSettingsValidationException($"{key} must be an https:// URL.");
        }

        if (!allowedHosts.Any(host => string.Equals(host, uri.Host, StringComparison.OrdinalIgnoreCase)))
        {
            throw new RuntimeSettingsValidationException($"{key} must use an official Zoom host.");
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
        return normalized is "email" or "billing" or "sentry" or "backup" or "oauth" or "push" or "uploadscanner" or "zoom" or "stripe" or "speakinglivekit" or "speakingai" or "speakingstorage" or "speakingcompliance" or "speakingfeatures" or "speakingwhisper" or "checkoutcom" or "paymob" or "paytabs" or "soketi"
            ? normalized
            : normalized == "upload-scanner" ? "uploadscanner" : null;
    }

    private static async Task<RuntimeSettingsIntegrationTestResponse> TestSectionAsync(
        string sectionId,
        IRuntimeSettingsProvider provider,
        IWebHostEnvironment env,
        UploadScannerOptions scannerOptions,
        IPronunciationCredentialResolver whisperRegistry,
        IHttpClientFactory httpClientFactory,
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
            "zoom" => settings.Zoom.Enabled
                      && HasAll(settings.Zoom.AccountId, settings.Zoom.ClientId, settings.Zoom.ClientSecret, settings.Zoom.HostUserId)
                ? Ok(sectionId, "Zoom server-to-server OAuth settings are configured. No meeting was created.", testedAt)
                : Failed(sectionId, "Configure Zoom account, client, client secret, and host user before enabling live classes.", testedAt),
            "stripe" => HasAll(settings.Stripe.SecretKey, settings.Stripe.PublishableKey, settings.Stripe.WebhookSecret)
                ? Ok(sectionId, "Stripe Tax/Portal/Radar runtime settings appear configured. No live API calls were made.", testedAt)
                : Failed(sectionId, "Configure Stripe secret key, publishable key, and webhook secret before enabling Tax/Radar.", testedAt),
            "speakingwhisper" => whisperRegistry.IsRegistryConfigured("whisper-asr")
                ? Ok(sectionId, "Whisper is active via the whisper-asr row in Admin → AI Providers (covers Speaking, Pronunciation, and Conversation). This legacy field is unused while that row has a key.", testedAt)
                : settings.SpeakingWhisper.IsConfigured
                    ? Ok(sectionId, "Whisper is active via this legacy field. No transcription was performed. Tip: move the key to the whisper-asr row in AI Providers to cover all speech-to-text with one key.", testedAt)
                    : Failed(sectionId, "Configure Whisper in the whisper-asr row (Admin → AI Providers) to cover all speech-to-text, or set a key here.", testedAt),
            "speakinglivekit" => settings.SpeakingLiveKit.IsEnabled
                ? Ok(sectionId, "LiveKit is configured and enabled. No room was created.", testedAt)
                : Failed(sectionId, "Configure LiveKit provider, API key, and API secret to enable live tutor rooms.", testedAt),
            "speakingai" => settings.SpeakingAi.IsAnthropicConfigured
                ? Ok(sectionId, "Anthropic API key is configured for Speaking AI scoring. No AI call was made.", testedAt)
                : Failed(sectionId, "Configure the Anthropic API key for Speaking AI scoring and patient turns.", testedAt),
            "speakingstorage" => settings.SpeakingStorage.IsConfigured
                ? Ok(sectionId, "AWS S3 storage is configured for speaking recordings. No upload was performed.", testedAt)
                : Failed(sectionId, "Configure AWS access key, secret, and bucket for speaking recording storage.", testedAt),
            "speakingcompliance" => Ok(sectionId, "Speaking compliance settings are configured via defaults or admin overrides.", testedAt),
            "speakingfeatures" => Ok(sectionId, $"Speaking V2 feature flag is {(settings.SpeakingFeatures.SpeakingV2Enabled ? "enabled" : "disabled")}.", testedAt),
            "checkoutcom" => await TestCheckoutComAsync(settings.CheckoutCom, httpClientFactory, sectionId, testedAt, ct),
            "paymob" => await TestPaymobAsync(settings.Paymob, httpClientFactory, sectionId, testedAt, ct),
            "paytabs" => await TestPayTabsAsync(settings.PayTabs, httpClientFactory, sectionId, testedAt, ct),
            "soketi" => await TestSoketiAsync(settings.Soketi, httpClientFactory, sectionId, testedAt, ct),
            _ => Failed(sectionId, "Unknown integration section.", testedAt),
        };
    }

    // ── Live, non-destructive payment/Soketi probes (no money moves) ───────
    private static async Task<RuntimeSettingsIntegrationTestResponse> TestCheckoutComAsync(
        CheckoutComSettings s, IHttpClientFactory httpClientFactory, string sectionId, DateTimeOffset testedAt, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(s.SecretKey))
            return Failed(sectionId, "Configure the Checkout.com secret key.", testedAt);
        var unsafeReason = AiProviderConnectionTester.GetUnsafeBaseUrlReason(s.ApiBaseUrl);
        if (unsafeReason is not null) return Failed(sectionId, unsafeReason, testedAt);
        return await ProbeAsync(httpClientFactory, sectionId, testedAt, s.SecretKey, ct, () =>
        {
            // Read-only list endpoint — validates the secret without moving money.
            var req = new HttpRequestMessage(HttpMethod.Get, new Uri(new Uri(EnsureSlash(s.ApiBaseUrl)), "workflows"));
            req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", s.SecretKey);
            return req;
        });
    }

    private static async Task<RuntimeSettingsIntegrationTestResponse> TestPaymobAsync(
        PaymobSettings s, IHttpClientFactory httpClientFactory, string sectionId, DateTimeOffset testedAt, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(s.ApiKey))
            return Failed(sectionId, "Configure the Paymob API key.", testedAt);
        var unsafeReason = AiProviderConnectionTester.GetUnsafeBaseUrlReason(s.ApiBaseUrl);
        if (unsafeReason is not null) return Failed(sectionId, unsafeReason, testedAt);
        return await ProbeAsync(httpClientFactory, sectionId, testedAt, s.ApiKey, ct, () =>
        {
            // Token issuance only — no order/payment is created.
            var req = new HttpRequestMessage(HttpMethod.Post, new Uri(new Uri(EnsureSlash(s.ApiBaseUrl)), "api/auth/tokens"));
            req.Content = System.Net.Http.Json.JsonContent.Create(new { api_key = s.ApiKey });
            return req;
        });
    }

    private static async Task<RuntimeSettingsIntegrationTestResponse> TestPayTabsAsync(
        PayTabsSettings s, IHttpClientFactory httpClientFactory, string sectionId, DateTimeOffset testedAt, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(s.ServerKey) || string.IsNullOrWhiteSpace(s.ProfileId))
            return Failed(sectionId, "Configure the PayTabs server key and profile id.", testedAt);
        var unsafeReason = AiProviderConnectionTester.GetUnsafeBaseUrlReason(s.ApiBaseUrl);
        if (unsafeReason is not null) return Failed(sectionId, unsafeReason, testedAt);
        // Querying a nonexistent transaction is read-only: a valid key yields a
        // "not found"-style 2xx/4xx with a body, a bad key yields 401/403.
        return await ProbeAsync(httpClientFactory, sectionId, testedAt, s.ServerKey, ct, () =>
        {
            var req = new HttpRequestMessage(HttpMethod.Post, new Uri(new Uri(EnsureSlash(s.ApiBaseUrl)), "payment/query"));
            req.Headers.TryAddWithoutValidation("Authorization", s.ServerKey);
            req.Content = System.Net.Http.Json.JsonContent.Create(new { profile_id = s.ProfileId, tran_ref = "TEST-0" });
            return req;
        });
    }

    private static async Task<RuntimeSettingsIntegrationTestResponse> TestSoketiAsync(
        SoketiSettings s, IHttpClientFactory httpClientFactory, string sectionId, DateTimeOffset testedAt, CancellationToken ct)
    {
        if (!s.Enabled) return Ok(sectionId, "Soketi is disabled. Enable it to dispatch realtime push.", testedAt);
        if (string.IsNullOrWhiteSpace(s.AppSecret))
            return Failed(sectionId, "Configure the Soketi app secret.", testedAt);
        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(10));
            var path = $"/apps/{s.AppId}/channels";
            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
            var query = SoketiPusherSigner.BuildSignedQueryString("GET", path, timestamp, s.AppKey, s.AppSecret!, string.Empty);
            var scheme = s.UseTls ? "https" : "http";
            var url = $"{scheme}://{s.Host}:{s.Port}{path}?{query}";
            var client = httpClientFactory.CreateClient("Soketi");
            using var resp = await client.GetAsync(url, timeoutCts.Token);
            return (int)resp.StatusCode is >= 200 and < 300
                ? Ok(sectionId, "Soketi accepted the signed request.", testedAt)
                : Failed(sectionId, $"Soketi returned HTTP {(int)resp.StatusCode}.", testedAt);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException)
        {
            return Failed(sectionId, "Soketi endpoint is unreachable.", testedAt);
        }
    }

    /// <summary>Send a probe request with a 10s timeout and classify the result
    /// (2xx = ok; 401/403 = auth; else failed). Error text is secret-redacted.</summary>
    private static async Task<RuntimeSettingsIntegrationTestResponse> ProbeAsync(
        IHttpClientFactory httpClientFactory, string sectionId, DateTimeOffset testedAt,
        string secretToRedact, CancellationToken ct, Func<HttpRequestMessage> buildRequest)
    {
        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(10));
            var client = httpClientFactory.CreateClient("RuntimeSettingsTest");
            using var req = buildRequest();
            using var resp = await client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, timeoutCts.Token);
            if ((int)resp.StatusCode is >= 200 and < 300)
                return Ok(sectionId, "Credentials accepted. No money was moved.", testedAt);
            if (resp.StatusCode is System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden)
                return Failed(sectionId, $"Authentication rejected (HTTP {(int)resp.StatusCode}). Check the key.", testedAt);
            // A 4xx that is not auth (e.g. "transaction not found") still proves
            // the credential was accepted by the gateway.
            if ((int)resp.StatusCode is >= 400 and < 500)
                return Ok(sectionId, $"Gateway reachable; credential accepted (HTTP {(int)resp.StatusCode}).", testedAt);
            return Failed(sectionId, $"Gateway returned HTTP {(int)resp.StatusCode}.", testedAt);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException)
        {
            var msg = AiProviderConnectionTester.RedactSecrets(ex.Message, secretToRedact) ?? "Endpoint unreachable.";
            return Failed(sectionId, msg.Length > 200 ? msg[..200] : msg, testedAt);
        }
    }

    private static string EnsureSlash(string url) => url.EndsWith('/') ? url : url + "/";

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
    public RuntimeSettingsZoomUpdate? Zoom { get; set; }
    public RuntimeSettingsStripeUpdate? Stripe { get; set; }
    public RuntimeSettingsSpeakingWhisperUpdate? SpeakingWhisper { get; set; }
    public RuntimeSettingsSpeakingLiveKitUpdate? SpeakingLiveKit { get; set; }
    public RuntimeSettingsSpeakingAiUpdate? SpeakingAi { get; set; }
    public RuntimeSettingsSpeakingStorageUpdate? SpeakingStorage { get; set; }
    public RuntimeSettingsSpeakingComplianceUpdate? SpeakingCompliance { get; set; }
    public RuntimeSettingsSpeakingFeaturesUpdate? SpeakingFeatures { get; set; }
    public RuntimeSettingsCheckoutComUpdate? CheckoutCom { get; set; }
    public RuntimeSettingsPaymobUpdate? Paymob { get; set; }
    public RuntimeSettingsPayTabsUpdate? PayTabs { get; set; }
    public RuntimeSettingsSoketiUpdate? Soketi { get; set; }
}

/// <summary>Checkout.com payment gateway overrides.</summary>
public sealed class RuntimeSettingsCheckoutComUpdate
{
    public string? ApiBaseUrl { get; set; }
    public string? SecretKey { get; set; }
    public string? PublicKey { get; set; }
    public string? ProcessingChannelId { get; set; }
    public string? WebhookSecret { get; set; }
    public string? SuccessUrl { get; set; }
    public string? CancelUrl { get; set; }
}

/// <summary>Paymob payment gateway overrides.</summary>
public sealed class RuntimeSettingsPaymobUpdate
{
    public string? ApiBaseUrl { get; set; }
    public string? ApiKey { get; set; }
    public string? MerchantId { get; set; }
    public string? HmacSecret { get; set; }
    /// <summary>JSON map of method → integration id, e.g. {"card":123}.</summary>
    public string? IntegrationIdsJson { get; set; }
    public JsonElement? IframeId { get; set; }
    public string? SuccessUrl { get; set; }
    public string? CancelUrl { get; set; }
}

/// <summary>PayTabs payment gateway overrides.</summary>
public sealed class RuntimeSettingsPayTabsUpdate
{
    public string? ApiBaseUrl { get; set; }
    public string? ServerKey { get; set; }
    public string? ProfileId { get; set; }
    public string? WebhookSecret { get; set; }
    public string? SuccessUrl { get; set; }
    public string? CancelUrl { get; set; }
}

/// <summary>Soketi realtime websocket push overrides.</summary>
public sealed class RuntimeSettingsSoketiUpdate
{
    public string? Host { get; set; }
    public JsonElement? Port { get; set; }
    public string? AppId { get; set; }
    public string? AppKey { get; set; }
    public string? AppSecret { get; set; }
    public JsonElement? UseTls { get; set; }
    public JsonElement? Enabled { get; set; }
}

/// <summary>2026-05-28 audit fix — Speaking Whisper transcription overrides.</summary>
public sealed class RuntimeSettingsSpeakingWhisperUpdate
{
    /// <summary>OpenAI API key (plaintext on input; stored encrypted). "********" sentinel leaves unchanged; empty string clears.</summary>
    public string? ApiKey { get; set; }
    public string? BaseUrl { get; set; }
    public string? Model { get; set; }
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

public sealed class RuntimeSettingsZoomUpdate
{
    public JsonElement? Enabled { get; set; }
    public string? AccountId { get; set; }
    public string? ClientId { get; set; }
    public string? ClientSecret { get; set; }
    public string? ApiBaseUrl { get; set; }
    public string? TokenUrl { get; set; }
    public string? HostUserId { get; set; }
    public string? MeetingSdkKey { get; set; }
    public string? MeetingSdkSecret { get; set; }
    public string? WebhookSecretToken { get; set; }
    public JsonElement? WebhookRetryToleranceSeconds { get; set; }
    public JsonElement? AllowSandboxFallback { get; set; }
}

/// <summary>Wave A5 — Stripe Tax / Customer Portal / Radar overrides.</summary>
public sealed class RuntimeSettingsStripeUpdate
{
    public string? SecretKey { get; set; }
    public string? PublishableKey { get; set; }
    public string? WebhookSecret { get; set; }
    public JsonElement? TaxAutomaticEnabled { get; set; }
    /// <summary>
    /// Tax-registration codes (e.g., ["UK_VAT", "EU_OSS", "AU_GST"]). Omit
    /// to leave unchanged; pass an empty array to clear the override.
    /// </summary>
    public List<string>? TaxRegistrations { get; set; }
    public string? CustomerPortalConfigurationId { get; set; }
    public JsonElement? RadarHighRiskCountryAllowReview { get; set; }
    public string? RadarBlockEmailDomainsCsv { get; set; }
}

/// <summary>Speaking LiveKit — live tutor rooms + egress recording.</summary>
public sealed class RuntimeSettingsSpeakingLiveKitUpdate
{
    public string? Provider { get; set; }
    public string? ApiKey { get; set; }
    public string? ApiSecret { get; set; }
    public string? WssUrl { get; set; }
    public string? WebhookSigningSecret { get; set; }
    public string? EgressBucket { get; set; }
    public JsonElement? DefaultMaxDurationSeconds { get; set; }
    public JsonElement? EgressEnabled { get; set; }
}

/// <summary>Speaking AI providers — Anthropic (scoring + patient turns) and ElevenLabs (TTS).</summary>
public sealed class RuntimeSettingsSpeakingAiUpdate
{
    public string? AnthropicApiKey { get; set; }
    public string? ElevenLabsApiKey { get; set; }
}

/// <summary>Speaking AWS S3 recording storage.</summary>
public sealed class RuntimeSettingsSpeakingStorageUpdate
{
    public string? AwsAccessKeyId { get; set; }
    public string? AwsSecretAccessKey { get; set; }
    public string? Region { get; set; }
    public string? Bucket { get; set; }
}

/// <summary>Speaking compliance — consent versioning + retention windows.</summary>
public sealed class RuntimeSettingsSpeakingComplianceUpdate
{
    public string? CurrentConsentVersion { get; set; }
    public string? CurrentLiveVideoConsentVersion { get; set; }
    public JsonElement? RetentionDaysDefault { get; set; }
    public JsonElement? RetentionDaysWhenTutorReviewed { get; set; }
    public JsonElement? AuditLogRetentionDays { get; set; }
}

/// <summary>Speaking feature flags.</summary>
public sealed class RuntimeSettingsSpeakingFeaturesUpdate
{
    public JsonElement? SpeakingV2Enabled { get; set; }
}

public sealed record RuntimeSettingsIntegrationTestResponse(
    string Section,
    string Status,
    string Message,
    DateTimeOffset TestedAt);
