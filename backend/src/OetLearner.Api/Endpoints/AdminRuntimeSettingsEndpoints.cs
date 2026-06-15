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
                    ApplyDataRetention(row, request.DataRetention, changedKeys);
                    ApplyExpertAutoAssignment(row, request.ExpertAutoAssignment, changedKeys);
                    ApplyPasswordPolicy(row, request.PasswordPolicy, changedKeys);
                    ApplyAiAssistant(row, request.AiAssistant, changedKeys);
                    ApplyAiGateway(row, request.AiGateway, changedKeys);
                    ApplyWriting(row, request.Writing, provider, changedKeys);
                    ApplyPlatform(row, request.Platform, changedKeys);
                    ApplyMessaging(row, request.Messaging, provider, changedKeys);
                    ApplyFx(row, request.Fx, provider, changedKeys);
                    ApplyBillingCore(row, request.BillingCore, changedKeys);
                    ApplyStorage(row, request.Storage, provider, changedKeys);
                    ApplyPdfExtraction(row, request.PdfExtraction, provider, changedKeys);
                    ApplyPronunciation(row, request.Pronunciation, changedKeys);
                    ApplyAuthTokens(row, request.AuthTokens, changedKeys);
                    ApplyWebPush(row, request.WebPush, changedKeys);
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
                // ── Email partial-coverage gap (Wave 3) ──
                brevoWelcomeTemplateId = settings.Email.BrevoWelcomeTemplateId,
                brevoPasswordChangedTemplateId = settings.Email.BrevoPasswordChangedTemplateId,
                brevoMfaEnabledTemplateId = settings.Email.BrevoMfaEnabledTemplateId,
                brevoAdminInviteTemplateId = settings.Email.BrevoAdminInviteTemplateId,
                brevoSecurityAlertTemplateId = settings.Email.BrevoSecurityAlertTemplateId,
                brevoReviewCompletedTemplateId = settings.Email.BrevoReviewCompletedTemplateId,
                brevoWebhookSecret = MaskPlainSecret(settings.Email.BrevoWebhookSecret),
                brevoEnabled = settings.Email.BrevoEnabled,
                smtpEnabled = settings.Email.SmtpEnabled,
                smtpEnableSsl = settings.Email.SmtpEnableSsl,
            },
            billing = new
            {
                stripeSecretKey = MaskPlainSecret(settings.Billing.StripeSecretKey),
                stripePublishableKey = settings.Billing.StripePublishableKey,
                stripeWebhookSecret = MaskPlainSecret(settings.Billing.StripeWebhookSecret),
                stripeSuccessUrl = settings.Billing.StripeSuccessUrl,
                stripeCancelUrl = settings.Billing.StripeCancelUrl,
                publicAppBaseUrl = settings.Billing.PublicAppBaseUrl,
                paypalClientId = settings.Billing.PayPalClientId,
                paypalClientSecret = MaskPlainSecret(settings.Billing.PayPalClientSecret),
                paypalWebhookId = MaskPlainSecret(settings.Billing.PayPalWebhookId),
                paypalSuccessUrl = settings.Billing.PayPalSuccessUrl,
                paypalCancelUrl = settings.Billing.PayPalCancelUrl,
                paypalAdvancedCardsEnabled = settings.Billing.PayPalAdvancedCardsEnabled,
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
                // ── Auth external providers (Wave 4) — LinkedIn + toggles ──
                linkedInClientId = MaskPlainSecret(settings.OAuth.LinkedInClientId),
                linkedInClientSecret = MaskPlainSecret(settings.OAuth.LinkedInClientSecret),
                linkedInEnabled = settings.OAuth.LinkedInEnabled,
                googleAuthEnabled = settings.OAuth.GoogleAuthEnabled,
                facebookAuthEnabled = settings.OAuth.FacebookAuthEnabled,
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
                // ── Web push enablement (Wave 4) ──
                webPushEnabled = settings.Push.WebPushEnabled,
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
            dataRetention = new
            {
                analyticsEventsDays = (int)Math.Round(settings.DataRetention.AnalyticsEvents.TotalDays),
                auditEventsDays = (int)Math.Round(settings.DataRetention.AuditEvents.TotalDays),
                paymentWebhookEventsDays = (int)Math.Round(settings.DataRetention.PaymentWebhookEvents.TotalDays),
                paymentWebhookPiiNullOutAgeDays = (int)Math.Round(settings.DataRetention.PaymentWebhookPiiNullOutAge.TotalDays),
                notificationDeliveryAttemptsDays = (int)Math.Round(settings.DataRetention.NotificationDeliveryAttempts.TotalDays),
                sweepIntervalHours = (int)Math.Round(settings.DataRetention.SweepInterval.TotalHours),
                batchSize = settings.DataRetention.BatchSize,
            },
            expertAutoAssignment = new
            {
                enabled = settings.ExpertAutoAssignment.Enabled,
                pollingIntervalSeconds = settings.ExpertAutoAssignment.PollingIntervalSeconds,
                slaEscalationIntervalSeconds = settings.ExpertAutoAssignment.SlaEscalationIntervalSeconds,
                slaHoursStandard = settings.ExpertAutoAssignment.SlaHoursStandard,
                slaHoursExpress = settings.ExpertAutoAssignment.SlaHoursExpress,
                maxActiveAssignmentsPerExpert = settings.ExpertAutoAssignment.MaxActiveAssignmentsPerExpert,
                lookbackHoursForLoad = settings.ExpertAutoAssignment.LookbackHoursForLoad,
                batchSize = settings.ExpertAutoAssignment.BatchSize,
            },
            passwordPolicy = new
            {
                minimumLength = settings.PasswordPolicy.MinimumLength,
                requireMixedCase = settings.PasswordPolicy.RequireMixedCase,
                requireDigit = settings.PasswordPolicy.RequireDigit,
                requireSymbol = settings.PasswordPolicy.RequireSymbol,
                breachCheckEnabled = settings.PasswordPolicy.BreachCheckEnabled,
                breachApiBaseUrl = settings.PasswordPolicy.BreachApiBaseUrl,
                breachApiTimeoutSeconds = (int)Math.Round(settings.PasswordPolicy.BreachApiTimeout.TotalSeconds),
            },
            aiAssistant = new
            {
                globalEnabled = settings.AiAssistant.GlobalEnabled,
                requireApprovalAlways = settings.AiAssistant.RequireApprovalAlways,
                maxIterations = settings.AiAssistant.MaxIterations,
                maxContextMessages = settings.AiAssistant.MaxContextMessages,
                backupRetentionDays = settings.AiAssistant.BackupRetentionDays,
                maxWriteFileSizeBytes = settings.AiAssistant.MaxWriteFileSizeBytes,
                commandTimeoutSeconds = settings.AiAssistant.CommandTimeoutSeconds,
                circuitBreakerMaxFailures = settings.AiAssistant.CircuitBreakerMaxFailures,
                circuitBreakerFailureWindowSeconds = settings.AiAssistant.CircuitBreakerFailureWindowSeconds,
                circuitBreakerMaxWrites = settings.AiAssistant.CircuitBreakerMaxWrites,
                circuitBreakerWriteWindowSeconds = settings.AiAssistant.CircuitBreakerWriteWindowSeconds,
                embeddingModel = settings.AiAssistant.EmbeddingModel,
                maxChunkTokens = settings.AiAssistant.MaxChunkTokens,
            },
            aiGateway = new
            {
                aiProviderProviderId = settings.AiGateway.ProviderId,
                aiProviderBaseUrl = settings.AiGateway.BaseUrl,
                aiProviderDefaultModel = settings.AiGateway.DefaultModel,
                aiProviderReasoningEffort = settings.AiGateway.ReasoningEffort,
                aiProviderDefaultMaxTokens = settings.AiGateway.DefaultMaxTokens,
                aiProviderDefaultTemperature = settings.AiGateway.DefaultTemperature,
                aiToolMaxToolCallsPerCompletion = settings.AiGateway.MaxToolCallsPerCompletion,
                aiToolFeatureGrantCacheSeconds = settings.AiGateway.FeatureGrantCacheSeconds,
                aiToolAllowedExternalHostsCsv = settings.AiGateway.AllowedExternalHostsCsv,
                aiToolExternalNetworkPerUserDailyCalls = settings.AiGateway.ExternalNetworkPerUserDailyCalls,
                aiToolExternalNetworkTimeoutMilliseconds = settings.AiGateway.ExternalNetworkTimeoutMilliseconds,
                aiToolExternalNetworkMaxResponseBytes = settings.AiGateway.ExternalNetworkMaxResponseBytes,
            },
            writing = new
            {
                cronsEnabled = settings.Writing.CronsEnabled,
                coachEnabled = settings.Writing.CoachEnabled,
                coachDailyCostCapPerLearnerUsd = settings.Writing.CoachDailyCostCapPerLearnerUsd,
                coachMaxHintsPerSession = settings.Writing.CoachMaxHintsPerSession,
                coachMinSecondsBetweenHints = settings.Writing.CoachMinSecondsBetweenHints,
                gcvApiKey = MaskPlainSecret(settings.Writing.GcvApiKey),
                ocrEnabled = settings.Writing.OcrEnabled,
                appealsEnabled = settings.Writing.AppealsEnabled,
                tutorReviewQueueMaxDepth = settings.Writing.TutorReviewQueueMaxDepth,
                tutorReviewMaxWaitHours = settings.Writing.TutorReviewMaxWaitHours,
                maxDailyPlanRegenerationsPerDay = settings.Writing.MaxDailyPlanRegenerationsPerDay,
                gradeIdempotencyTtlHours = settings.Writing.GradeIdempotencyTtlHours,
            },
            platform = new
            {
                publicApiBaseUrl = settings.Platform.PublicApiBaseUrl,
                publicWebBaseUrl = settings.Platform.PublicWebBaseUrl,
                fallbackEmailDomain = settings.Platform.FallbackEmailDomain,
            },
            messaging = new
            {
                twilioEnabled = settings.Messaging.TwilioEnabled,
                twilioApiBaseUrl = settings.Messaging.TwilioApiBaseUrl,
                twilioAccountSid = settings.Messaging.TwilioAccountSid,
                twilioAuthToken = MaskPlainSecret(settings.Messaging.TwilioAuthToken),
                twilioFromNumber = settings.Messaging.TwilioFromNumber,
                twilioMessagingServiceSid = settings.Messaging.TwilioMessagingServiceSid,
                whatsAppEnabled = settings.Messaging.WhatsAppEnabled,
                whatsAppApiBaseUrl = settings.Messaging.WhatsAppApiBaseUrl,
                whatsAppAccessToken = MaskPlainSecret(settings.Messaging.WhatsAppAccessToken),
                whatsAppPhoneNumberId = settings.Messaging.WhatsAppPhoneNumberId,
                whatsAppFallbackTemplateName = settings.Messaging.WhatsAppFallbackTemplateName,
                isTwilioConfigured = settings.Messaging.IsTwilioConfigured,
                isWhatsAppConfigured = settings.Messaging.IsWhatsAppConfigured,
            },
            fx = new
            {
                baseCurrency = settings.Fx.BaseCurrency,
                apiKey = MaskPlainSecret(settings.Fx.ApiKey),
                apiBaseUrl = settings.Fx.ApiBaseUrl,
                dynamicPricingEnabled = settings.Fx.DynamicPricingEnabled,
            },
            billingCore = new
            {
                checkoutBaseUrl = settings.Billing.CheckoutBaseUrl,
                webhookMaxAgeSeconds = settings.Billing.WebhookMaxAgeSeconds,
                webhookMaxAttempts = settings.Billing.WebhookMaxAttempts,
                defaultCurrency = settings.Billing.DefaultCurrency,
                defaultRegion = settings.Billing.DefaultRegion,
                walletCurrency = settings.Billing.WalletCurrency,
                walletTopUpTiersJson = settings.Billing.WalletTopUpTiers is { Count: > 0 }
                    ? JsonSupport.Serialize(settings.Billing.WalletTopUpTiers)
                    : null,
                paypalUseSandbox = settings.Billing.PayPalUseSandbox,
                paypalApiBaseUrl = settings.Billing.PayPalApiBaseUrl,
            },
            storage = new
            {
                provider = settings.Storage.Provider,
                bucketName = settings.Storage.BucketName,
                endpointUrl = settings.Storage.EndpointUrl,
                accessKeyId = MaskPlainSecret(settings.Storage.AccessKeyId),
                secretAccessKey = MaskPlainSecret(settings.Storage.SecretAccessKey),
                awsRegion = settings.Storage.AwsRegion,
                signedReadTtlSeconds = settings.Storage.SignedReadTtlSeconds,
                maxAudioBytes = settings.Storage.MaxAudioBytes,
                maxPdfBytes = settings.Storage.MaxPdfBytes,
                maxImageBytes = settings.Storage.MaxImageBytes,
                maxZipBytes = settings.Storage.MaxZipBytes,
                maxZipEntries = settings.Storage.MaxZipEntries,
                maxZipEntryBytes = settings.Storage.MaxZipEntryBytes,
                maxZipUncompressedBytes = settings.Storage.MaxZipUncompressedBytes,
                maxZipCompressionRatio = settings.Storage.MaxZipCompressionRatio,
                chunkSizeBytes = settings.Storage.ChunkSizeBytes,
                stagingTtlHours = settings.Storage.StagingTtlHours,
                isConfigured = settings.Storage.IsConfigured,
            },
            pdfExtraction = new
            {
                provider = settings.PdfExtraction.Provider,
                azureEndpoint = settings.PdfExtraction.AzureEndpoint,
                azureApiKey = MaskPlainSecret(settings.PdfExtraction.AzureApiKey),
                minTextLengthForSuccess = settings.PdfExtraction.MinTextLengthForSuccess,
            },
            pronunciation = new
            {
                provider = settings.Pronunciation.Provider,
                azureSpeechRegion = settings.Pronunciation.AzureSpeechRegion,
                azureLocale = settings.Pronunciation.AzureLocale,
                whisperBaseUrl = settings.Pronunciation.WhisperBaseUrl,
                whisperModel = settings.Pronunciation.WhisperModel,
                geminiBaseUrl = settings.Pronunciation.GeminiBaseUrl,
                geminiModel = settings.Pronunciation.GeminiModel,
                maxAudioBytes = settings.Pronunciation.MaxAudioBytes,
                audioRetentionDays = settings.Pronunciation.AudioRetentionDays,
                freeTierWeeklyAttemptLimit = settings.Pronunciation.FreeTierWeeklyAttemptLimit,
                freeTierWindowDays = settings.Pronunciation.FreeTierWindowDays,
            },
            authTokens = new
            {
                accessTokenLifetimeSeconds = (int)Math.Round(settings.AuthTokens.AccessTokenLifetime.TotalSeconds),
                refreshTokenLifetimeSeconds = (int)Math.Round(settings.AuthTokens.RefreshTokenLifetime.TotalSeconds),
                otpLifetimeSeconds = (int)Math.Round(settings.AuthTokens.OtpLifetime.TotalSeconds),
                authenticatorIssuer = settings.AuthTokens.AuthenticatorIssuer,
            },
            // Web push enablement is also surfaced under push (above) for display;
            // this dedicated group is the canonical PUT target (request.WebPush).
            webPush = new
            {
                enabled = settings.Push.WebPushEnabled,
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
                publicAppBaseUrl = r.BillingPublicAppBaseUrl,
                paypalClientId = r.PayPalClientId,
                paypalClientSecret = MaskSecret(r.PayPalClientSecretEncrypted),
                paypalWebhookId = MaskSecret(r.PayPalWebhookIdEncrypted),
                paypalSuccessUrl = r.PayPalSuccessUrl,
                paypalCancelUrl = r.PayPalCancelUrl,
                paypalAdvancedCardsEnabled = r.PayPalAdvancedCardsEnabled,
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
        // ── Email partial-coverage gap (Wave 3) ──
        if (TrySetNullableInt(d.BrevoWelcomeTemplateId, v => row.BrevoWelcomeTemplateId = v, "email.brevoWelcomeTemplateId", changed)) { }
        if (TrySetNullableInt(d.BrevoPasswordChangedTemplateId, v => row.BrevoPasswordChangedTemplateId = v, "email.brevoPasswordChangedTemplateId", changed)) { }
        if (TrySetNullableInt(d.BrevoMfaEnabledTemplateId, v => row.BrevoMfaEnabledTemplateId = v, "email.brevoMfaEnabledTemplateId", changed)) { }
        if (TrySetNullableInt(d.BrevoAdminInviteTemplateId, v => row.BrevoAdminInviteTemplateId = v, "email.brevoAdminInviteTemplateId", changed)) { }
        if (TrySetNullableInt(d.BrevoSecurityAlertTemplateId, v => row.BrevoSecurityAlertTemplateId = v, "email.brevoSecurityAlertTemplateId", changed)) { }
        if (TrySetNullableInt(d.BrevoReviewCompletedTemplateId, v => row.BrevoReviewCompletedTemplateId = v, "email.brevoReviewCompletedTemplateId", changed)) { }
        if (TrySetSecret(d.BrevoWebhookSecret, p, v => row.BrevoWebhookSecretEncrypted = v, "email.brevoWebhookSecret", changed)) { }
        if (TrySetNullableBool(d.BrevoEnabled, v => row.BrevoEnabled = v, "email.brevoEnabled", changed)) { }
        if (TrySetNullableBool(d.SmtpEnabled, v => row.SmtpEnabled = v, "email.smtpEnabled", changed)) { }
        if (TrySetNullableBool(d.SmtpEnableSsl, v => row.SmtpEnableSsl = v, "email.smtpEnableSsl", changed)) { }
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
        if (TrySetPlain(d.PublicAppBaseUrl, v => row.BillingPublicAppBaseUrl = v, "billing.publicAppBaseUrl", changed)) { }
        if (TrySetPlain(d.PayPalClientId, v => row.PayPalClientId = v, "billing.paypalClientId", changed)) { }
        if (TrySetSecret(d.PayPalClientSecret, p, v => row.PayPalClientSecretEncrypted = v, "billing.paypalClientSecret", changed)) { }
        if (TrySetSecret(d.PayPalWebhookId, p, v => row.PayPalWebhookIdEncrypted = v, "billing.paypalWebhookId", changed)) { }
        if (TrySetPlain(d.PayPalSuccessUrl, v => row.PayPalSuccessUrl = v, "billing.paypalSuccessUrl", changed)) { }
        if (TrySetPlain(d.PayPalCancelUrl, v => row.PayPalCancelUrl = v, "billing.paypalCancelUrl", changed)) { }
        if (d.PayPalAdvancedCardsEnabled.HasValue)
        {
            row.PayPalAdvancedCardsEnabled = d.PayPalAdvancedCardsEnabled;
            changed.Add("billing.paypalAdvancedCardsEnabled");
        }
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
        // ── Auth external providers (Wave 4) — LinkedIn (the genuine gap) +
        // per-provider Enabled toggles. LinkedIn ClientId + ClientSecret are
        // both secrets (encrypted at rest).
        if (TrySetSecret(d.LinkedInClientId, p, v => row.LinkedInClientIdEncrypted = v, "oauth.linkedInClientId", changed)) { }
        if (TrySetSecret(d.LinkedInClientSecret, p, v => row.LinkedInClientSecretEncrypted = v, "oauth.linkedInClientSecret", changed)) { }
        if (TrySetNullableBool(d.LinkedInEnabled, v => row.LinkedInEnabled = v, "oauth.linkedInEnabled", changed)) { }
        if (TrySetNullableBool(d.GoogleAuthEnabled, v => row.GoogleAuthEnabled = v, "oauth.googleAuthEnabled", changed)) { }
        if (TrySetNullableBool(d.FacebookAuthEnabled, v => row.FacebookAuthEnabled = v, "oauth.facebookAuthEnabled", changed)) { }
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

    private static void ApplyDataRetention(RuntimeSettingsRow row, RuntimeSettingsDataRetentionUpdate? d, List<string> changed)
    {
        if (d is null) return;
        if (TrySetNullableInt(d.AnalyticsEventsDays, v => row.DataRetentionAnalyticsEventsDays = v, "dataRetention.analyticsEventsDays", changed, min: 0, max: 36500)) { }
        if (TrySetNullableInt(d.AuditEventsDays, v => row.DataRetentionAuditEventsDays = v, "dataRetention.auditEventsDays", changed, min: 0, max: 36500)) { }
        if (TrySetNullableInt(d.PaymentWebhookEventsDays, v => row.DataRetentionPaymentWebhookEventsDays = v, "dataRetention.paymentWebhookEventsDays", changed, min: 0, max: 36500)) { }
        if (TrySetNullableInt(d.PaymentWebhookPiiNullOutAgeDays, v => row.DataRetentionPaymentWebhookPiiNullOutAgeDays = v, "dataRetention.paymentWebhookPiiNullOutAgeDays", changed, min: 0, max: 36500)) { }
        if (TrySetNullableInt(d.NotificationDeliveryAttemptsDays, v => row.DataRetentionNotificationDeliveryAttemptsDays = v, "dataRetention.notificationDeliveryAttemptsDays", changed, min: 0, max: 36500)) { }
        if (TrySetNullableInt(d.SweepIntervalHours, v => row.DataRetentionSweepIntervalHours = v, "dataRetention.sweepIntervalHours", changed, min: 1, max: 8760)) { }
        if (TrySetNullableInt(d.BatchSize, v => row.DataRetentionBatchSize = v, "dataRetention.batchSize", changed, min: 1, max: 1_000_000)) { }
    }

    private static void ApplyExpertAutoAssignment(RuntimeSettingsRow row, RuntimeSettingsExpertAutoAssignmentUpdate? d, List<string> changed)
    {
        if (d is null) return;
        if (TrySetNullableBool(d.Enabled, v => row.ExpertAutoAssignmentEnabled = v, "expertAutoAssignment.enabled", changed)) { }
        if (TrySetNullableInt(d.PollingIntervalSeconds, v => row.ExpertAutoAssignmentPollingIntervalSeconds = v, "expertAutoAssignment.pollingIntervalSeconds", changed, min: 1, max: 86400)) { }
        if (TrySetNullableInt(d.SlaEscalationIntervalSeconds, v => row.ExpertAutoAssignmentSlaEscalationIntervalSeconds = v, "expertAutoAssignment.slaEscalationIntervalSeconds", changed, min: 1, max: 86400)) { }
        if (TrySetNullableInt(d.SlaHoursStandard, v => row.ExpertAutoAssignmentSlaHoursStandard = v, "expertAutoAssignment.slaHoursStandard", changed, min: 1, max: 8760)) { }
        if (TrySetNullableInt(d.SlaHoursExpress, v => row.ExpertAutoAssignmentSlaHoursExpress = v, "expertAutoAssignment.slaHoursExpress", changed, min: 1, max: 8760)) { }
        if (TrySetNullableInt(d.MaxActiveAssignmentsPerExpert, v => row.ExpertAutoAssignmentMaxActiveAssignmentsPerExpert = v, "expertAutoAssignment.maxActiveAssignmentsPerExpert", changed, min: 1, max: 10000)) { }
        if (TrySetNullableInt(d.LookbackHoursForLoad, v => row.ExpertAutoAssignmentLookbackHoursForLoad = v, "expertAutoAssignment.lookbackHoursForLoad", changed, min: 1, max: 8760)) { }
        if (TrySetNullableInt(d.BatchSize, v => row.ExpertAutoAssignmentBatchSize = v, "expertAutoAssignment.batchSize", changed, min: 1, max: 100000)) { }
    }

    private static void ApplyPasswordPolicy(RuntimeSettingsRow row, RuntimeSettingsPasswordPolicyUpdate? d, List<string> changed)
    {
        if (d is null) return;
        if (TrySetNullableInt(d.MinimumLength, v => row.PasswordPolicyMinimumLength = v, "passwordPolicy.minimumLength", changed, min: 1, max: 1024)) { }
        if (TrySetNullableBool(d.RequireMixedCase, v => row.PasswordPolicyRequireMixedCase = v, "passwordPolicy.requireMixedCase", changed)) { }
        if (TrySetNullableBool(d.RequireDigit, v => row.PasswordPolicyRequireDigit = v, "passwordPolicy.requireDigit", changed)) { }
        if (TrySetNullableBool(d.RequireSymbol, v => row.PasswordPolicyRequireSymbol = v, "passwordPolicy.requireSymbol", changed)) { }
        if (TrySetNullableBool(d.BreachCheckEnabled, v => row.PasswordPolicyBreachCheckEnabled = v, "passwordPolicy.breachCheckEnabled", changed)) { }
        if (d.BreachApiBaseUrl is not null && d.BreachApiBaseUrl != SecretMask)
        {
            var trimmed = d.BreachApiBaseUrl.Trim();
            if (trimmed.Length > 0 && (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri)
                || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)))
            {
                throw new RuntimeSettingsValidationException("passwordPolicy.breachApiBaseUrl must be an http(s):// URL.");
            }
        }
        if (TrySetPlain(d.BreachApiBaseUrl, v => row.PasswordPolicyBreachApiBaseUrl = v, "passwordPolicy.breachApiBaseUrl", changed)) { }
        if (TrySetNullableInt(d.BreachApiTimeoutSeconds, v => row.PasswordPolicyBreachApiTimeoutSeconds = v, "passwordPolicy.breachApiTimeoutSeconds", changed, min: 1, max: 60)) { }
    }

    private static void ApplyAiAssistant(RuntimeSettingsRow row, RuntimeSettingsAiAssistantUpdate? d, List<string> changed)
    {
        if (d is null) return;
        if (TrySetNullableBool(d.GlobalEnabled, v => row.AiAssistantGlobalEnabled = v, "aiAssistant.globalEnabled", changed)) { }
        if (TrySetNullableBool(d.RequireApprovalAlways, v => row.AiAssistantRequireApprovalAlways = v, "aiAssistant.requireApprovalAlways", changed)) { }
        if (TrySetNullableInt(d.MaxIterations, v => row.AiAssistantMaxIterations = v, "aiAssistant.maxIterations", changed, min: 1, max: 1000)) { }
        if (TrySetNullableInt(d.MaxContextMessages, v => row.AiAssistantMaxContextMessages = v, "aiAssistant.maxContextMessages", changed, min: 1, max: 10000)) { }
        if (TrySetNullableInt(d.BackupRetentionDays, v => row.AiAssistantBackupRetentionDays = v, "aiAssistant.backupRetentionDays", changed, min: 0, max: 36500)) { }
        if (TrySetNullableLong(d.MaxWriteFileSizeBytes, v => row.AiAssistantMaxWriteFileSizeBytes = v, "aiAssistant.maxWriteFileSizeBytes", changed, min: 1, max: 1_073_741_824)) { }
        if (TrySetNullableInt(d.CommandTimeoutSeconds, v => row.AiAssistantCommandTimeoutSeconds = v, "aiAssistant.commandTimeoutSeconds", changed, min: 1, max: 86400)) { }
        if (TrySetNullableInt(d.CircuitBreakerMaxFailures, v => row.AiAssistantCircuitBreakerMaxFailures = v, "aiAssistant.circuitBreakerMaxFailures", changed, min: 1, max: 10000)) { }
        if (TrySetNullableInt(d.CircuitBreakerFailureWindowSeconds, v => row.AiAssistantCircuitBreakerFailureWindowSeconds = v, "aiAssistant.circuitBreakerFailureWindowSeconds", changed, min: 1, max: 86400)) { }
        if (TrySetNullableInt(d.CircuitBreakerMaxWrites, v => row.AiAssistantCircuitBreakerMaxWrites = v, "aiAssistant.circuitBreakerMaxWrites", changed, min: 1, max: 100000)) { }
        if (TrySetNullableInt(d.CircuitBreakerWriteWindowSeconds, v => row.AiAssistantCircuitBreakerWriteWindowSeconds = v, "aiAssistant.circuitBreakerWriteWindowSeconds", changed, min: 1, max: 86400)) { }
        if (TrySetPlain(d.EmbeddingModel, v => row.AiAssistantEmbeddingModel = v, "aiAssistant.embeddingModel", changed)) { }
        if (TrySetNullableInt(d.MaxChunkTokens, v => row.AiAssistantMaxChunkTokens = v, "aiAssistant.maxChunkTokens", changed, min: 1, max: 100000)) { }
    }

    private static void ApplyAiGateway(RuntimeSettingsRow row, RuntimeSettingsAiGatewayUpdate? d, List<string> changed)
    {
        if (d is null) return;
        if (TrySetPlain(d.AiProviderProviderId, v => row.AiProviderProviderId = v, "aiGateway.aiProviderProviderId", changed)) { }
        if (TrySetPlain(d.AiProviderBaseUrl, v => row.AiProviderBaseUrl = v, "aiGateway.aiProviderBaseUrl", changed)) { }
        if (TrySetPlain(d.AiProviderDefaultModel, v => row.AiProviderDefaultModel = v, "aiGateway.aiProviderDefaultModel", changed)) { }
        // ReasoningEffort allows the empty string as a real value (non-reasoning
        // models). Empty input therefore clears the override back to the env value.
        if (TrySetPlain(d.AiProviderReasoningEffort, v => row.AiProviderReasoningEffort = v, "aiGateway.aiProviderReasoningEffort", changed)) { }
        if (TrySetNullableInt(d.AiProviderDefaultMaxTokens, v => row.AiProviderDefaultMaxTokens = v, "aiGateway.aiProviderDefaultMaxTokens", changed, min: 1, max: 1_000_000)) { }
        if (TrySetNullableDouble(d.AiProviderDefaultTemperature, v => row.AiProviderDefaultTemperature = v, "aiGateway.aiProviderDefaultTemperature", changed, min: 0, max: 1)) { }
        if (TrySetNullableInt(d.AiToolMaxToolCallsPerCompletion, v => row.AiToolMaxToolCallsPerCompletion = v, "aiGateway.aiToolMaxToolCallsPerCompletion", changed, min: 1, max: 1000)) { }
        if (TrySetNullableInt(d.AiToolFeatureGrantCacheSeconds, v => row.AiToolFeatureGrantCacheSeconds = v, "aiGateway.aiToolFeatureGrantCacheSeconds", changed, min: 1, max: 86400)) { }
        if (TrySetPlain(d.AiToolAllowedExternalHostsCsv, v => row.AiToolAllowedExternalHostsCsv = v, "aiGateway.aiToolAllowedExternalHostsCsv", changed)) { }
        if (TrySetNullableInt(d.AiToolExternalNetworkPerUserDailyCalls, v => row.AiToolExternalNetworkPerUserDailyCalls = v, "aiGateway.aiToolExternalNetworkPerUserDailyCalls", changed, min: 0, max: 1_000_000)) { }
        if (TrySetNullableInt(d.AiToolExternalNetworkTimeoutMilliseconds, v => row.AiToolExternalNetworkTimeoutMilliseconds = v, "aiGateway.aiToolExternalNetworkTimeoutMilliseconds", changed, min: 1, max: 60000)) { }
        if (TrySetNullableInt(d.AiToolExternalNetworkMaxResponseBytes, v => row.AiToolExternalNetworkMaxResponseBytes = v, "aiGateway.aiToolExternalNetworkMaxResponseBytes", changed, min: 1, max: 10_485_760)) { }
    }

    private static void ApplyWriting(RuntimeSettingsRow row, RuntimeSettingsWritingUpdate? d,
        IRuntimeSettingsProvider p, List<string> changed)
    {
        if (d is null) return;
        if (TrySetNullableBool(d.CronsEnabled, v => row.WritingCronsEnabled = v, "writing.cronsEnabled", changed)) { }
        if (TrySetNullableBool(d.CoachEnabled, v => row.WritingCoachEnabled = v, "writing.coachEnabled", changed)) { }
        if (TrySetNullableDecimal(d.CoachDailyCostCapPerLearnerUsd, v => row.WritingCoachDailyCostCapPerLearnerUsd = v, "writing.coachDailyCostCapPerLearnerUsd", changed, min: 0, max: 100000)) { }
        if (TrySetNullableInt(d.CoachMaxHintsPerSession, v => row.WritingCoachMaxHintsPerSession = v, "writing.coachMaxHintsPerSession", changed, min: 1, max: 100000)) { }
        if (TrySetNullableInt(d.CoachMinSecondsBetweenHints, v => row.WritingCoachMinSecondsBetweenHints = v, "writing.coachMinSecondsBetweenHints", changed, min: 0, max: 86400)) { }
        if (TrySetSecret(d.GcvApiKey, p, v => row.WritingGcvApiKeyEncrypted = v, "writing.gcvApiKey", changed)) { }
        if (TrySetNullableBool(d.OcrEnabled, v => row.WritingOcrEnabled = v, "writing.ocrEnabled", changed)) { }
        if (TrySetNullableBool(d.AppealsEnabled, v => row.WritingAppealsEnabled = v, "writing.appealsEnabled", changed)) { }
        if (TrySetNullableInt(d.TutorReviewQueueMaxDepth, v => row.WritingTutorReviewQueueMaxDepth = v, "writing.tutorReviewQueueMaxDepth", changed, min: 1, max: 1_000_000)) { }
        if (TrySetNullableInt(d.TutorReviewMaxWaitHours, v => row.WritingTutorReviewMaxWaitHours = v, "writing.tutorReviewMaxWaitHours", changed, min: 1, max: 8760)) { }
        if (TrySetNullableInt(d.MaxDailyPlanRegenerationsPerDay, v => row.WritingMaxDailyPlanRegenerationsPerDay = v, "writing.maxDailyPlanRegenerationsPerDay", changed, min: 0, max: 100000)) { }
        if (TrySetNullableInt(d.GradeIdempotencyTtlHours, v => row.WritingGradeIdempotencyTtlHours = v, "writing.gradeIdempotencyTtlHours", changed, min: 1, max: 8760)) { }
    }

    private static void ApplyPlatform(RuntimeSettingsRow row, RuntimeSettingsPlatformUpdate? d, List<string> changed)
    {
        if (d is null) return;
        ValidatePlatformUrl(d.PublicApiBaseUrl, "platform.publicApiBaseUrl");
        ValidatePlatformUrl(d.PublicWebBaseUrl, "platform.publicWebBaseUrl");
        if (TrySetPlain(d.PublicApiBaseUrl, v => row.PublicApiBaseUrl = v, "platform.publicApiBaseUrl", changed)) { }
        if (TrySetPlain(d.PublicWebBaseUrl, v => row.PublicWebBaseUrl = v, "platform.publicWebBaseUrl", changed)) { }
        if (TrySetPlain(d.FallbackEmailDomain, v => row.FallbackEmailDomain = v, "platform.fallbackEmailDomain", changed)) { }
    }

    private static void ApplyMessaging(RuntimeSettingsRow row, RuntimeSettingsMessagingUpdate? d,
        IRuntimeSettingsProvider p, List<string> changed)
    {
        if (d is null) return;
        ValidatePlatformUrl(d.TwilioApiBaseUrl, "messaging.twilioApiBaseUrl");
        ValidatePlatformUrl(d.WhatsAppApiBaseUrl, "messaging.whatsAppApiBaseUrl");
        if (TrySetNullableBool(d.TwilioEnabled, v => row.TwilioEnabled = v, "messaging.twilioEnabled", changed)) { }
        if (TrySetPlain(d.TwilioApiBaseUrl, v => row.TwilioApiBaseUrl = v, "messaging.twilioApiBaseUrl", changed)) { }
        if (TrySetPlain(d.TwilioAccountSid, v => row.TwilioAccountSid = v, "messaging.twilioAccountSid", changed)) { }
        if (TrySetSecret(d.TwilioAuthToken, p, v => row.TwilioAuthTokenEncrypted = v, "messaging.twilioAuthToken", changed)) { }
        if (TrySetPlain(d.TwilioFromNumber, v => row.TwilioFromNumber = v, "messaging.twilioFromNumber", changed)) { }
        if (TrySetPlain(d.TwilioMessagingServiceSid, v => row.TwilioMessagingServiceSid = v, "messaging.twilioMessagingServiceSid", changed)) { }
        if (TrySetNullableBool(d.WhatsAppEnabled, v => row.WhatsAppEnabled = v, "messaging.whatsAppEnabled", changed)) { }
        if (TrySetPlain(d.WhatsAppApiBaseUrl, v => row.WhatsAppApiBaseUrl = v, "messaging.whatsAppApiBaseUrl", changed)) { }
        if (TrySetSecret(d.WhatsAppAccessToken, p, v => row.WhatsAppAccessTokenEncrypted = v, "messaging.whatsAppAccessToken", changed)) { }
        if (TrySetPlain(d.WhatsAppPhoneNumberId, v => row.WhatsAppPhoneNumberId = v, "messaging.whatsAppPhoneNumberId", changed)) { }
        if (TrySetPlain(d.WhatsAppFallbackTemplateName, v => row.WhatsAppFallbackTemplateName = v, "messaging.whatsAppFallbackTemplateName", changed)) { }
    }

    // ── Wave 4 appliers (FX / Billing core / Storage / PDF / Pronunciation /
    //    Auth tokens / Web push) ────────────────────────────────────
    private static void ApplyFx(RuntimeSettingsRow row, RuntimeSettingsFxUpdate? d,
        IRuntimeSettingsProvider p, List<string> changed)
    {
        if (d is null) return;
        if (TrySetPlain(d.BaseCurrency, v => row.FxBaseCurrency = v, "fx.baseCurrency", changed)) { }
        if (TrySetSecret(d.ApiKey, p, v => row.FxApiKeyEncrypted = v, "fx.apiKey", changed)) { }
        if (TrySetPlain(d.ApiBaseUrl, v => row.FxApiBaseUrl = v, "fx.apiBaseUrl", changed)) { }
        if (TrySetNullableBool(d.DynamicPricingEnabled, v => row.FxDynamicPricingEnabled = v, "fx.dynamicPricingEnabled", changed)) { }
    }

    private static void ApplyBillingCore(RuntimeSettingsRow row, RuntimeSettingsBillingCoreUpdate? d, List<string> changed)
    {
        if (d is null) return;
        if (TrySetPlain(d.CheckoutBaseUrl, v => row.BillingCheckoutBaseUrl = v, "billingCore.checkoutBaseUrl", changed)) { }
        if (TrySetNullableInt(d.WebhookMaxAgeSeconds, v => row.BillingWebhookMaxAgeSeconds = v, "billingCore.webhookMaxAgeSeconds", changed, min: 1, max: 3600)) { }
        if (TrySetNullableInt(d.WebhookMaxAttempts, v => row.BillingWebhookMaxAttempts = v, "billingCore.webhookMaxAttempts", changed, min: 1, max: 1000)) { }
        if (TrySetPlain(d.DefaultCurrency, v => row.BillingDefaultCurrency = v, "billingCore.defaultCurrency", changed)) { }
        if (TrySetPlain(d.DefaultRegion, v => row.BillingDefaultRegion = v, "billingCore.defaultRegion", changed)) { }
        if (TrySetPlain(d.WalletCurrency, v => row.WalletCurrency = v, "billingCore.walletCurrency", changed)) { }
        if (d.WalletTopUpTiersJson is not null && d.WalletTopUpTiersJson != SecretMask)
        {
            var trimmed = d.WalletTopUpTiersJson.Trim();
            if (trimmed.Length > 0)
            {
                try
                {
                    var parsed = JsonSerializer.Deserialize<List<WalletTopUpTierOption>>(trimmed);
                    if (parsed is null || parsed.Count == 0 || parsed.Any(t => t.Amount <= 0 || t.Credits < 0 || t.Bonus < 0))
                        throw new RuntimeSettingsValidationException("billingCore.walletTopUpTiersJson must be a JSON array of {Amount>0,Credits>=0,Bonus>=0} tiers.");
                }
                catch (JsonException ex)
                {
                    throw new RuntimeSettingsValidationException("billingCore.walletTopUpTiersJson must be valid JSON.", ex);
                }
            }
        }
        if (TrySetPlain(d.WalletTopUpTiersJson, v => row.WalletTopUpTiersJson = v, "billingCore.walletTopUpTiersJson", changed)) { }
        if (TrySetNullableBool(d.PayPalUseSandbox, v => row.PayPalUseSandbox = v, "billingCore.paypalUseSandbox", changed)) { }
        if (TrySetPlain(d.PayPalApiBaseUrl, v => row.PayPalApiBaseUrl = v, "billingCore.paypalApiBaseUrl", changed)) { }
    }

    private static void ApplyStorage(RuntimeSettingsRow row, RuntimeSettingsStorageUpdate? d,
        IRuntimeSettingsProvider p, List<string> changed)
    {
        if (d is null) return;
        if (TrySetPlain(d.Provider, v => row.StorageProvider = v, "storage.provider", changed)) { }
        if (TrySetPlain(d.BucketName, v => row.StorageBucketName = v, "storage.bucketName", changed)) { }
        if (TrySetPlain(d.EndpointUrl, v => row.StorageEndpointUrl = v, "storage.endpointUrl", changed)) { }
        if (TrySetSecret(d.AccessKeyId, p, v => row.StorageAccessKeyIdEncrypted = v, "storage.accessKeyId", changed)) { }
        if (TrySetSecret(d.SecretAccessKey, p, v => row.StorageSecretAccessKeyEncrypted = v, "storage.secretAccessKey", changed)) { }
        if (TrySetPlain(d.AwsRegion, v => row.StorageAwsRegion = v, "storage.awsRegion", changed)) { }
        if (TrySetNullableInt(d.SignedReadTtlSeconds, v => row.StorageSignedReadTtlSeconds = v, "storage.signedReadTtlSeconds", changed, min: 1, max: 604800)) { }
        if (TrySetNullableLong(d.MaxAudioBytes, v => row.StorageContentUploadMaxAudioBytes = v, "storage.maxAudioBytes", changed, min: 1, max: 10L * 1024 * 1024 * 1024)) { }
        if (TrySetNullableLong(d.MaxPdfBytes, v => row.StorageContentUploadMaxPdfBytes = v, "storage.maxPdfBytes", changed, min: 1, max: 10L * 1024 * 1024 * 1024)) { }
        if (TrySetNullableLong(d.MaxImageBytes, v => row.StorageContentUploadMaxImageBytes = v, "storage.maxImageBytes", changed, min: 1, max: 10L * 1024 * 1024 * 1024)) { }
        if (TrySetNullableLong(d.MaxZipBytes, v => row.StorageContentUploadMaxZipBytes = v, "storage.maxZipBytes", changed, min: 1, max: 50L * 1024 * 1024 * 1024)) { }
        if (TrySetNullableInt(d.MaxZipEntries, v => row.StorageContentUploadMaxZipEntries = v, "storage.maxZipEntries", changed, min: 1, max: 10_000_000)) { }
        if (TrySetNullableLong(d.MaxZipEntryBytes, v => row.StorageContentUploadMaxZipEntryBytes = v, "storage.maxZipEntryBytes", changed, min: 1, max: 50L * 1024 * 1024 * 1024)) { }
        if (TrySetNullableLong(d.MaxZipUncompressedBytes, v => row.StorageContentUploadMaxZipUncompressedBytes = v, "storage.maxZipUncompressedBytes", changed, min: 1, max: 100L * 1024 * 1024 * 1024)) { }
        if (TrySetNullableDouble(d.MaxZipCompressionRatio, v => row.StorageContentUploadMaxZipCompressionRatio = v, "storage.maxZipCompressionRatio", changed, min: 1, max: 100000)) { }
        if (TrySetNullableLong(d.ChunkSizeBytes, v => row.StorageContentUploadChunkSizeBytes = v, "storage.chunkSizeBytes", changed, min: 1, max: 1L * 1024 * 1024 * 1024)) { }
        if (TrySetNullableInt(d.StagingTtlHours, v => row.StorageContentUploadStagingTtlHours = v, "storage.stagingTtlHours", changed, min: 1, max: 8760)) { }
    }

    private static void ApplyPdfExtraction(RuntimeSettingsRow row, RuntimeSettingsPdfExtractionUpdate? d,
        IRuntimeSettingsProvider p, List<string> changed)
    {
        if (d is null) return;
        if (TrySetPlain(d.Provider, v => row.PdfExtractionProvider = v, "pdfExtraction.provider", changed)) { }
        if (TrySetPlain(d.AzureEndpoint, v => row.PdfExtractionAzureEndpoint = v, "pdfExtraction.azureEndpoint", changed)) { }
        if (TrySetSecret(d.AzureApiKey, p, v => row.PdfExtractionAzureApiKeyEncrypted = v, "pdfExtraction.azureApiKey", changed)) { }
        if (TrySetNullableInt(d.MinTextLengthForSuccess, v => row.PdfExtractionMinTextLengthForSuccess = v, "pdfExtraction.minTextLengthForSuccess", changed, min: 1, max: 1_000_000)) { }
    }

    private static void ApplyPronunciation(RuntimeSettingsRow row, RuntimeSettingsPronunciationUpdate? d, List<string> changed)
    {
        if (d is null) return;
        if (TrySetPlain(d.Provider, v => row.PronunciationProvider = v, "pronunciation.provider", changed)) { }
        if (TrySetPlain(d.AzureSpeechRegion, v => row.PronunciationAzureSpeechRegion = v, "pronunciation.azureSpeechRegion", changed)) { }
        if (TrySetPlain(d.AzureLocale, v => row.PronunciationAzureLocale = v, "pronunciation.azureLocale", changed)) { }
        if (TrySetPlain(d.WhisperBaseUrl, v => row.PronunciationWhisperBaseUrl = v, "pronunciation.whisperBaseUrl", changed)) { }
        if (TrySetPlain(d.WhisperModel, v => row.PronunciationWhisperModel = v, "pronunciation.whisperModel", changed)) { }
        if (TrySetPlain(d.GeminiBaseUrl, v => row.PronunciationGeminiBaseUrl = v, "pronunciation.geminiBaseUrl", changed)) { }
        if (TrySetPlain(d.GeminiModel, v => row.PronunciationGeminiModel = v, "pronunciation.geminiModel", changed)) { }
        if (TrySetNullableLong(d.MaxAudioBytes, v => row.PronunciationMaxAudioBytes = v, "pronunciation.maxAudioBytes", changed, min: 1, max: 1L * 1024 * 1024 * 1024)) { }
        if (TrySetNullableInt(d.AudioRetentionDays, v => row.PronunciationAudioRetentionDays = v, "pronunciation.audioRetentionDays", changed, min: 1, max: 36500)) { }
        // -1 disables throttling, so allow -1 as the lower bound.
        if (TrySetNullableInt(d.FreeTierWeeklyAttemptLimit, v => row.PronunciationFreeTierWeeklyAttemptLimit = v, "pronunciation.freeTierWeeklyAttemptLimit", changed, min: -1, max: 1_000_000)) { }
        if (TrySetNullableInt(d.FreeTierWindowDays, v => row.PronunciationFreeTierWindowDays = v, "pronunciation.freeTierWindowDays", changed, min: 1, max: 365)) { }
    }

    private static void ApplyAuthTokens(RuntimeSettingsRow row, RuntimeSettingsAuthTokensUpdate? d, List<string> changed)
    {
        if (d is null) return;
        if (TrySetNullableInt(d.AccessTokenLifetimeSeconds, v => row.AuthTokenAccessTokenLifetimeSeconds = v, "authTokens.accessTokenLifetimeSeconds", changed, min: 1, max: 86400)) { }
        if (TrySetNullableInt(d.RefreshTokenLifetimeSeconds, v => row.AuthTokenRefreshTokenLifetimeSeconds = v, "authTokens.refreshTokenLifetimeSeconds", changed, min: 1, max: 31536000)) { }
        if (TrySetNullableInt(d.OtpLifetimeSeconds, v => row.AuthTokenOtpLifetimeSeconds = v, "authTokens.otpLifetimeSeconds", changed, min: 1, max: 86400)) { }
        if (TrySetPlain(d.AuthenticatorIssuer, v => row.AuthTokenAuthenticatorIssuer = v, "authTokens.authenticatorIssuer", changed)) { }
    }

    private static void ApplyWebPush(RuntimeSettingsRow row, RuntimeSettingsWebPushUpdate? d, List<string> changed)
    {
        if (d is null) return;
        if (TrySetNullableBool(d.Enabled, v => row.WebPushEnabled = v, "webPush.enabled", changed)) { }
    }

    private static void ValidatePlatformUrl(string? value, string key)
    {
        if (value is null || value == SecretMask) return;
        var trimmed = value.Trim();
        if (trimmed.Length == 0) return; // empty clears the override
        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new RuntimeSettingsValidationException($"{key} must be an http(s):// URL.");
        }
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

    private static bool TrySetNullableLong(JsonElement? input, Action<long?> setter, string key, List<string> changed, long? min = null, long? max = null)
    {
        if (!TryReadNullableNumber(input, key, element => element.GetInt64(), out var value))
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

    private static bool TrySetNullableDecimal(JsonElement? input, Action<decimal?> setter, string key, List<string> changed, decimal? min = null, decimal? max = null)
    {
        if (!TryReadNullableNumber(input, key, element => element.GetDecimal(), out var value))
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
        return normalized is "email" or "billing" or "paypal" or "sentry" or "backup" or "oauth" or "push" or "uploadscanner" or "zoom" or "stripe" or "speakinglivekit" or "speakingai" or "speakingstorage" or "speakingcompliance" or "speakingfeatures" or "speakingwhisper" or "checkoutcom" or "paymob" or "paytabs" or "soketi" or "dataretention" or "expertautoassignment" or "passwordpolicy" or "aiassistant" or "aigateway" or "writing" or "platform" or "messaging" or "fx" or "billingcore" or "storage" or "pdfextraction" or "pronunciation" or "authtokens" or "webpush"
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
            "paypal" => HasAll(settings.Billing.PayPalClientId, settings.Billing.PayPalClientSecret)
                ? Ok(sectionId, $"PayPal Client ID and Secret are configured (embedded card fields {(settings.Billing.PayPalAdvancedCardsEnabled ? "enabled" : "disabled")}). Learners will be offered PayPal at checkout. No live order was created.", testedAt)
                : Failed(sectionId, "Configure PayPal Client ID and Secret (and the Webhook ID) to enable PayPal checkout.", testedAt),
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
            "dataretention" => Ok(sectionId, $"Retention windows resolved (audit {settings.DataRetention.AuditEvents.TotalDays:0}d, webhooks {settings.DataRetention.PaymentWebhookEvents.TotalDays:0}d). Sweep every {settings.DataRetention.SweepInterval.TotalHours:0}h, batch {settings.DataRetention.BatchSize}.", testedAt),
            "expertautoassignment" => settings.ExpertAutoAssignment.Enabled
                ? Ok(sectionId, $"Auto-assignment enabled. SLA {settings.ExpertAutoAssignment.SlaHoursStandard}h standard / {settings.ExpertAutoAssignment.SlaHoursExpress}h express; max {settings.ExpertAutoAssignment.MaxActiveAssignmentsPerExpert} per expert.", testedAt)
                : Ok(sectionId, "Auto-assignment is disabled. Writing reviews stay in the manual queue.", testedAt),
            "passwordpolicy" => Uri.TryCreate(settings.PasswordPolicy.BreachApiBaseUrl, UriKind.Absolute, out var hibpUri) && hibpUri.Scheme == Uri.UriSchemeHttps
                ? Ok(sectionId, $"Policy: min {settings.PasswordPolicy.MinimumLength} chars; breach check {(settings.PasswordPolicy.BreachCheckEnabled ? "on" : "off")}. Breach API URL is valid https.", testedAt)
                : Failed(sectionId, "Breach API base URL must be a valid https:// URL.", testedAt),
            "aiassistant" => settings.AiAssistant.GlobalEnabled
                ? Ok(sectionId, $"AI Assistant enabled. Max {settings.AiAssistant.MaxIterations} iterations, {settings.AiAssistant.MaxContextMessages} context messages; approval-always {(settings.AiAssistant.RequireApprovalAlways ? "on" : "off")}.", testedAt)
                : Ok(sectionId, "AI Assistant is disabled (master kill switch off).", testedAt),
            "aigateway" => Uri.TryCreate(settings.AiGateway.BaseUrl, UriKind.Absolute, out var aiUri) && (aiUri.Scheme == Uri.UriSchemeHttps || aiUri.Scheme == Uri.UriSchemeHttp)
                ? Ok(sectionId, $"Gateway base URL is valid. Provider {settings.AiGateway.ProviderId}, model {settings.AiGateway.DefaultModel}, max {settings.AiGateway.MaxToolCallsPerCompletion} tool calls/completion. Provider API key is managed in Admin → AI Providers.", testedAt)
                : Failed(sectionId, "AI gateway base URL must be a valid http(s):// URL.", testedAt),
            "writing" => Ok(sectionId, $"Writing: crons {(settings.Writing.CronsEnabled ? "on" : "off")}, coach {(settings.Writing.CoachEnabled ? "on" : "off")}, OCR {(settings.Writing.OcrEnabled ? "on" : "off")}, appeals {(settings.Writing.AppealsEnabled ? "on" : "off")}. GCV OCR fallback {(string.IsNullOrWhiteSpace(settings.Writing.GcvApiKey) ? "not configured (jobs mark manual_required)" : "configured")}.", testedAt),
            "platform" => string.IsNullOrWhiteSpace(settings.Platform.PublicWebBaseUrl) || string.IsNullOrWhiteSpace(settings.Platform.PublicApiBaseUrl)
                ? Failed(sectionId, "Configure both Public API Base URL and Public Web Base URL for external auth callbacks.", testedAt)
                : Uri.TryCreate(settings.Platform.PublicApiBaseUrl, UriKind.Absolute, out _) && Uri.TryCreate(settings.Platform.PublicWebBaseUrl, UriKind.Absolute, out _)
                    ? Ok(sectionId, $"Public host URLs are valid. Fallback email domain: {settings.Platform.FallbackEmailDomain}.", testedAt)
                    : Failed(sectionId, "Public API/Web base URLs must be valid absolute URLs.", testedAt),
            // Non-destructive config-presence probe — no live SMS/WhatsApp is sent.
            "messaging" => (!settings.Messaging.TwilioEnabled && !settings.Messaging.WhatsAppEnabled)
                ? Ok(sectionId, "Messaging channels are disabled. No SMS/WhatsApp notifications will be sent.", testedAt)
                : (settings.Messaging.TwilioEnabled && !settings.Messaging.IsTwilioConfigured)
                    ? Failed(sectionId, "Twilio is enabled but not fully configured. Set Account SID and Auth Token.", testedAt)
                    : (settings.Messaging.WhatsAppEnabled && !settings.Messaging.IsWhatsAppConfigured)
                        ? Failed(sectionId, "WhatsApp is enabled but not fully configured. Set Access Token and Phone Number ID.", testedAt)
                        : Ok(sectionId, $"Messaging configured: Twilio SMS {(settings.Messaging.IsTwilioConfigured ? "ready" : "off")}, WhatsApp {(settings.Messaging.IsWhatsAppConfigured ? "ready" : "off")}. No message was sent.", testedAt),
            "fx" => string.IsNullOrWhiteSpace(settings.Fx.ApiKey) || string.IsNullOrWhiteSpace(settings.Fx.ApiBaseUrl)
                ? Ok(sectionId, $"No FX provider configured — offline seed rates are used (base {settings.Fx.BaseCurrency}). Dynamic pricing {(settings.Fx.DynamicPricingEnabled ? "on" : "off")}.", testedAt)
                : Uri.TryCreate(settings.Fx.ApiBaseUrl, UriKind.Absolute, out _)
                    ? Ok(sectionId, $"FX provider key + base URL configured (base {settings.Fx.BaseCurrency}). No live rate fetch was performed.", testedAt)
                    : Failed(sectionId, "FX provider base URL must be a valid absolute URL.", testedAt),
            "billingcore" => Ok(sectionId, $"Billing core: default {settings.Billing.DefaultCurrency}/{settings.Billing.DefaultRegion}, wallet {settings.Billing.WalletCurrency} ({settings.Billing.WalletTopUpTiers.Count} tiers); webhook max-age {settings.Billing.WebhookMaxAgeSeconds}s, max {settings.Billing.WebhookMaxAttempts} attempts; PayPal {(settings.Billing.PayPalUseSandbox ? "sandbox" : "production")}.", testedAt),
            "storage" => settings.Storage.Provider.Equals("s3", StringComparison.OrdinalIgnoreCase)
                ? (settings.Storage.IsConfigured
                    ? Ok(sectionId, $"S3 storage configured (bucket {settings.Storage.BucketName}, region {settings.Storage.AwsRegion}). No object was read or written.", testedAt)
                    : Failed(sectionId, "Storage provider is 's3' but bucket, access key, or secret key is missing.", testedAt))
                : Ok(sectionId, "Storage provider is 'local'. S3 credentials are not required.", testedAt),
            "pdfextraction" => settings.PdfExtraction.Provider.Equals("azure", StringComparison.OrdinalIgnoreCase)
                    || settings.PdfExtraction.Provider.Equals("auto", StringComparison.OrdinalIgnoreCase)
                ? (string.IsNullOrWhiteSpace(settings.PdfExtraction.AzureEndpoint) || string.IsNullOrWhiteSpace(settings.PdfExtraction.AzureApiKey)
                    ? Ok(sectionId, $"PDF extraction provider '{settings.PdfExtraction.Provider}' — Azure OCR not configured; PdfPig-only extraction will be used.", testedAt)
                    : Ok(sectionId, $"PDF extraction provider '{settings.PdfExtraction.Provider}' with Azure OCR configured. No document was processed.", testedAt))
                : Ok(sectionId, $"PDF extraction provider is '{settings.PdfExtraction.Provider}'.", testedAt),
            "pronunciation" => Ok(sectionId, $"Pronunciation provider '{settings.Pronunciation.Provider}' (locale {settings.Pronunciation.AzureLocale}). API keys are managed in Admin → AI Providers. Free tier: {settings.Pronunciation.FreeTierWeeklyAttemptLimit} attempts / {settings.Pronunciation.FreeTierWindowDays}d.", testedAt),
            "authtokens" => Ok(sectionId, $"Access token {settings.AuthTokens.AccessTokenLifetime.TotalMinutes:0}m, refresh {settings.AuthTokens.RefreshTokenLifetime.TotalDays:0.##}d, OTP {settings.AuthTokens.OtpLifetime.TotalMinutes:0}m. Issuer: {settings.AuthTokens.AuthenticatorIssuer ?? "(env default)"}.", testedAt),
            "webpush" => settings.Push.WebPushEnabled
                ? (HasAll(settings.Push.VapidSubject, settings.Push.VapidPublicKey, settings.Push.VapidPrivateKey)
                    ? Ok(sectionId, "Web push is enabled and VAPID keys are configured (Push section).", testedAt)
                    : Failed(sectionId, "Web push is enabled but VAPID keys are missing. Configure them in the Push section.", testedAt))
                : Ok(sectionId, "Web push is disabled. Enable it (with VAPID keys) to allow browser notifications.", testedAt),
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
    public RuntimeSettingsDataRetentionUpdate? DataRetention { get; set; }
    public RuntimeSettingsExpertAutoAssignmentUpdate? ExpertAutoAssignment { get; set; }
    public RuntimeSettingsPasswordPolicyUpdate? PasswordPolicy { get; set; }
    public RuntimeSettingsAiAssistantUpdate? AiAssistant { get; set; }
    public RuntimeSettingsAiGatewayUpdate? AiGateway { get; set; }
    public RuntimeSettingsWritingUpdate? Writing { get; set; }
    public RuntimeSettingsPlatformUpdate? Platform { get; set; }
    public RuntimeSettingsMessagingUpdate? Messaging { get; set; }
    // ── Wave 4 ─────────────────────────────────────────────────────
    public RuntimeSettingsFxUpdate? Fx { get; set; }
    public RuntimeSettingsBillingCoreUpdate? BillingCore { get; set; }
    public RuntimeSettingsStorageUpdate? Storage { get; set; }
    public RuntimeSettingsPdfExtractionUpdate? PdfExtraction { get; set; }
    public RuntimeSettingsPronunciationUpdate? Pronunciation { get; set; }
    public RuntimeSettingsAuthTokensUpdate? AuthTokens { get; set; }
    public RuntimeSettingsWebPushUpdate? WebPush { get; set; }
}

/// <summary>AI Assistant orchestration tunables (Wave 2).</summary>
public sealed class RuntimeSettingsAiAssistantUpdate
{
    public JsonElement? GlobalEnabled { get; set; }
    public JsonElement? RequireApprovalAlways { get; set; }
    public JsonElement? MaxIterations { get; set; }
    public JsonElement? MaxContextMessages { get; set; }
    public JsonElement? BackupRetentionDays { get; set; }
    public JsonElement? MaxWriteFileSizeBytes { get; set; }
    public JsonElement? CommandTimeoutSeconds { get; set; }
    public JsonElement? CircuitBreakerMaxFailures { get; set; }
    public JsonElement? CircuitBreakerFailureWindowSeconds { get; set; }
    public JsonElement? CircuitBreakerMaxWrites { get; set; }
    public JsonElement? CircuitBreakerWriteWindowSeconds { get; set; }
    public string? EmbeddingModel { get; set; }
    public JsonElement? MaxChunkTokens { get; set; }
}

/// <summary>AI gateway / tooling non-credential knobs (Wave 2). API key excluded.</summary>
public sealed class RuntimeSettingsAiGatewayUpdate
{
    public string? AiProviderProviderId { get; set; }
    public string? AiProviderBaseUrl { get; set; }
    public string? AiProviderDefaultModel { get; set; }
    public string? AiProviderReasoningEffort { get; set; }
    public JsonElement? AiProviderDefaultMaxTokens { get; set; }
    public JsonElement? AiProviderDefaultTemperature { get; set; }
    public JsonElement? AiToolMaxToolCallsPerCompletion { get; set; }
    public JsonElement? AiToolFeatureGrantCacheSeconds { get; set; }
    public string? AiToolAllowedExternalHostsCsv { get; set; }
    public JsonElement? AiToolExternalNetworkPerUserDailyCalls { get; set; }
    public JsonElement? AiToolExternalNetworkTimeoutMilliseconds { get; set; }
    public JsonElement? AiToolExternalNetworkMaxResponseBytes { get; set; }
}

/// <summary>Writing module V2 feature flags + coach/queue/OCR tunables (Wave 2).</summary>
public sealed class RuntimeSettingsWritingUpdate
{
    public JsonElement? CronsEnabled { get; set; }
    public JsonElement? CoachEnabled { get; set; }
    public JsonElement? CoachDailyCostCapPerLearnerUsd { get; set; }
    public JsonElement? CoachMaxHintsPerSession { get; set; }
    public JsonElement? CoachMinSecondsBetweenHints { get; set; }
    /// <summary>Google Cloud Vision API key (plaintext on input; stored encrypted). "********" leaves unchanged; "" clears.</summary>
    public string? GcvApiKey { get; set; }
    public JsonElement? OcrEnabled { get; set; }
    public JsonElement? AppealsEnabled { get; set; }
    public JsonElement? TutorReviewQueueMaxDepth { get; set; }
    public JsonElement? TutorReviewMaxWaitHours { get; set; }
    public JsonElement? MaxDailyPlanRegenerationsPerDay { get; set; }
    public JsonElement? GradeIdempotencyTtlHours { get; set; }
}

/// <summary>Platform public host URLs (Wave 2).</summary>
public sealed class RuntimeSettingsPlatformUpdate
{
    public string? PublicApiBaseUrl { get; set; }
    public string? PublicWebBaseUrl { get; set; }
    public string? FallbackEmailDomain { get; set; }
}

/// <summary>Messaging (Twilio SMS / WhatsApp Business Cloud) channels (Wave 3).
/// AuthToken / AccessToken are secrets ("********" leaves unchanged; "" clears);
/// AccountSid is a public identifier.</summary>
public sealed class RuntimeSettingsMessagingUpdate
{
    public JsonElement? TwilioEnabled { get; set; }
    public string? TwilioApiBaseUrl { get; set; }
    public string? TwilioAccountSid { get; set; }
    public string? TwilioAuthToken { get; set; }
    public string? TwilioFromNumber { get; set; }
    public string? TwilioMessagingServiceSid { get; set; }
    public JsonElement? WhatsAppEnabled { get; set; }
    public string? WhatsAppApiBaseUrl { get; set; }
    public string? WhatsAppAccessToken { get; set; }
    public string? WhatsAppPhoneNumberId { get; set; }
    public string? WhatsAppFallbackTemplateName { get; set; }
}

/// <summary>Data-retention sweeper windows (days / hours / batch size).</summary>
public sealed class RuntimeSettingsDataRetentionUpdate
{
    public JsonElement? AnalyticsEventsDays { get; set; }
    public JsonElement? AuditEventsDays { get; set; }
    public JsonElement? PaymentWebhookEventsDays { get; set; }
    public JsonElement? PaymentWebhookPiiNullOutAgeDays { get; set; }
    public JsonElement? NotificationDeliveryAttemptsDays { get; set; }
    public JsonElement? SweepIntervalHours { get; set; }
    public JsonElement? BatchSize { get; set; }
}

/// <summary>Expert auto-assignment loop tunables.</summary>
public sealed class RuntimeSettingsExpertAutoAssignmentUpdate
{
    public JsonElement? Enabled { get; set; }
    public JsonElement? PollingIntervalSeconds { get; set; }
    public JsonElement? SlaEscalationIntervalSeconds { get; set; }
    public JsonElement? SlaHoursStandard { get; set; }
    public JsonElement? SlaHoursExpress { get; set; }
    public JsonElement? MaxActiveAssignmentsPerExpert { get; set; }
    public JsonElement? LookbackHoursForLoad { get; set; }
    public JsonElement? BatchSize { get; set; }
}

/// <summary>Password-policy enforcement (complexity + HIBP breach check).</summary>
public sealed class RuntimeSettingsPasswordPolicyUpdate
{
    public JsonElement? MinimumLength { get; set; }
    public JsonElement? RequireMixedCase { get; set; }
    public JsonElement? RequireDigit { get; set; }
    public JsonElement? RequireSymbol { get; set; }
    public JsonElement? BreachCheckEnabled { get; set; }
    public string? BreachApiBaseUrl { get; set; }
    public JsonElement? BreachApiTimeoutSeconds { get; set; }
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
    // ── Email partial-coverage gap (Wave 3) ──
    public JsonElement? BrevoWelcomeTemplateId { get; set; }
    public JsonElement? BrevoPasswordChangedTemplateId { get; set; }
    public JsonElement? BrevoMfaEnabledTemplateId { get; set; }
    public JsonElement? BrevoAdminInviteTemplateId { get; set; }
    public JsonElement? BrevoSecurityAlertTemplateId { get; set; }
    public JsonElement? BrevoReviewCompletedTemplateId { get; set; }
    /// <summary>Brevo webhook HMAC secret (plaintext on input; stored encrypted). "********" leaves unchanged; "" clears.</summary>
    public string? BrevoWebhookSecret { get; set; }
    public JsonElement? BrevoEnabled { get; set; }
    public JsonElement? SmtpEnabled { get; set; }
    public JsonElement? SmtpEnableSsl { get; set; }
}

public sealed class RuntimeSettingsBillingUpdate
{
    public string? StripeSecretKey { get; set; }
    public string? StripePublishableKey { get; set; }
    public string? StripeWebhookSecret { get; set; }
    public string? StripeSuccessUrl { get; set; }
    public string? StripeCancelUrl { get; set; }
    public string? PublicAppBaseUrl { get; set; }
    public string? PayPalClientId { get; set; }
    public string? PayPalClientSecret { get; set; }
    public string? PayPalWebhookId { get; set; }
    public string? PayPalSuccessUrl { get; set; }
    public string? PayPalCancelUrl { get; set; }
    public bool? PayPalAdvancedCardsEnabled { get; set; }
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
    // ── Auth external providers (Wave 4) — LinkedIn (secret id + secret) +
    // per-provider Enabled toggles. "********" leaves a secret unchanged; "" clears.
    public string? LinkedInClientId { get; set; }
    public string? LinkedInClientSecret { get; set; }
    public JsonElement? LinkedInEnabled { get; set; }
    public JsonElement? GoogleAuthEnabled { get; set; }
    public JsonElement? FacebookAuthEnabled { get; set; }
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

// ── Wave 4 wire contracts ─────────────────────────────────────────

/// <summary>FX / currency provider overrides (Wave 4). ApiKey is a secret
/// ("********" leaves unchanged; "" clears).</summary>
public sealed class RuntimeSettingsFxUpdate
{
    public string? BaseCurrency { get; set; }
    public string? ApiKey { get; set; }
    public string? ApiBaseUrl { get; set; }
    public JsonElement? DynamicPricingEnabled { get; set; }
}

/// <summary>Billing core (non-gateway) overrides (Wave 4). Gateway credentials
/// live in the Billing/Stripe/CheckoutCom/Paymob/PayTabs sections.</summary>
public sealed class RuntimeSettingsBillingCoreUpdate
{
    public string? CheckoutBaseUrl { get; set; }
    public JsonElement? WebhookMaxAgeSeconds { get; set; }
    public JsonElement? WebhookMaxAttempts { get; set; }
    public string? DefaultCurrency { get; set; }
    public string? DefaultRegion { get; set; }
    public string? WalletCurrency { get; set; }
    /// <summary>JSON array of wallet tier objects. Leave blank to use appsettings defaults.</summary>
    public string? WalletTopUpTiersJson { get; set; }
    public JsonElement? PayPalUseSandbox { get; set; }
    public string? PayPalApiBaseUrl { get; set; }
}

/// <summary>Storage (S3 / object store) overrides (Wave 4). AccessKeyId +
/// SecretAccessKey are secrets. Filesystem paths stay env-only (excluded).</summary>
public sealed class RuntimeSettingsStorageUpdate
{
    public string? Provider { get; set; }
    public string? BucketName { get; set; }
    public string? EndpointUrl { get; set; }
    public string? AccessKeyId { get; set; }
    public string? SecretAccessKey { get; set; }
    public string? AwsRegion { get; set; }
    public JsonElement? SignedReadTtlSeconds { get; set; }
    public JsonElement? MaxAudioBytes { get; set; }
    public JsonElement? MaxPdfBytes { get; set; }
    public JsonElement? MaxImageBytes { get; set; }
    public JsonElement? MaxZipBytes { get; set; }
    public JsonElement? MaxZipEntries { get; set; }
    public JsonElement? MaxZipEntryBytes { get; set; }
    public JsonElement? MaxZipUncompressedBytes { get; set; }
    public JsonElement? MaxZipCompressionRatio { get; set; }
    public JsonElement? ChunkSizeBytes { get; set; }
    public JsonElement? StagingTtlHours { get; set; }
}

/// <summary>PDF text-extraction overrides (Wave 4). AzureApiKey is a secret.</summary>
public sealed class RuntimeSettingsPdfExtractionUpdate
{
    public string? Provider { get; set; }
    public string? AzureEndpoint { get; set; }
    public string? AzureApiKey { get; set; }
    public JsonElement? MinTextLengthForSuccess { get; set; }
}

/// <summary>Pronunciation NON-credential overrides (Wave 4). The Azure/Whisper/
/// Gemini API keys are registry-backed (Admin → AI Providers), not here.</summary>
public sealed class RuntimeSettingsPronunciationUpdate
{
    public string? Provider { get; set; }
    public string? AzureSpeechRegion { get; set; }
    public string? AzureLocale { get; set; }
    public string? WhisperBaseUrl { get; set; }
    public string? WhisperModel { get; set; }
    public string? GeminiBaseUrl { get; set; }
    public string? GeminiModel { get; set; }
    public JsonElement? MaxAudioBytes { get; set; }
    public JsonElement? AudioRetentionDays { get; set; }
    public JsonElement? FreeTierWeeklyAttemptLimit { get; set; }
    public JsonElement? FreeTierWindowDays { get; set; }
}

/// <summary>Safe auth-token lifetime overrides (Wave 4). Signing keys / Issuer /
/// Audience stay env-only (trust anchors) and are excluded.</summary>
public sealed class RuntimeSettingsAuthTokensUpdate
{
    public JsonElement? AccessTokenLifetimeSeconds { get; set; }
    public JsonElement? RefreshTokenLifetimeSeconds { get; set; }
    public JsonElement? OtpLifetimeSeconds { get; set; }
    public string? AuthenticatorIssuer { get; set; }
}

/// <summary>Web push enablement (Wave 4). VAPID keys live in the Push section.</summary>
public sealed class RuntimeSettingsWebPushUpdate
{
    public JsonElement? Enabled { get; set; }
}

public sealed record RuntimeSettingsIntegrationTestResponse(
    string Section,
    string Status,
    string Message,
    DateTimeOffset TestedAt);
