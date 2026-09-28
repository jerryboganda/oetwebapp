using System.Security.Claims;
using System.Text.Json;
using System.Net.Sockets;
using Google.Apis.Auth.OAuth2;
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

public static partial class AdminRuntimeSettingsEndpoints
{
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
                authFromAddress = settings.Email.AuthFromAddress,
                authFromName = settings.Email.AuthFromName,
                marketingFromAddress = settings.Email.MarketingFromAddress,
                marketingFromName = settings.Email.MarketingFromName,
                productFromAddress = settings.Email.ProductFromAddress,
                productFromName = settings.Email.ProductFromName,
                supportFromAddress = settings.Email.SupportFromAddress,
                supportFromName = settings.Email.SupportFromName,
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
                fcmServiceAccountJson = MaskPlainSecret(settings.Push.FcmServiceAccountJson),
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
            placement = new
            {
                placementEnabled = settings.Placement.PlacementEnabled,
                betaOnly = settings.Placement.BetaOnly,
                betaEmails = settings.Placement.BetaEmails,
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
            bunnyStream = new
            {
                enabled = settings.BunnyStream.Enabled,
                libraryId = settings.BunnyStream.LibraryId,
                apiKey = MaskPlainSecret(settings.BunnyStream.ApiKey),
                cdnHostname = settings.BunnyStream.CdnHostname,
                tokenAuthKey = MaskPlainSecret(settings.BunnyStream.TokenAuthKey),
                webhookSecret = MaskPlainSecret(settings.BunnyStream.WebhookSecret),
                collectionId = settings.BunnyStream.CollectionId,
                playbackTokenTtlSeconds = settings.BunnyStream.PlaybackTokenTtlSeconds,
                isConfigured = settings.BunnyStream.IsConfigured,
                // Attestation key map is write-only: only its configured state
                // and the platform:keyId identifiers are ever surfaced.
                videoAttestationKeys = settings.VideoAttestation.IsConfigured ? SecretMask : string.Empty,
                videoAttestationKeyIds = settings.VideoAttestation.Keys.Keys.Order(StringComparer.Ordinal).ToArray(),
            },
            videoProtection = new
            {
                revokeOnCaptureDetected = settings.VideoProtection.RevokeOnCaptureDetected,
                blockRootedDevices = settings.VideoProtection.BlockRootedDevices,
                blockEmulators = settings.VideoProtection.BlockEmulators,
            },
            security = new
            {
                singleActiveSessionEnabled = settings.Security.SingleActiveSessionEnabled,
                riskMode = settings.Security.RiskMode,
                trustedDeviceRequired = settings.Security.TrustedDeviceRequired,
                deviceChangeWindowDays = settings.Security.DeviceChangeWindowDays,
                deviceChangeMaxPerWindow = settings.Security.DeviceChangeMaxPerWindow,
                inactiveSessionTimeoutDays = settings.Security.InactiveSessionTimeoutDays,
                requireVerifiedEmailForLearners = settings.Security.RequireVerifiedEmailForLearners,
                countryAllowList = settings.Security.CountryAllowList,
                countryAllowListMode = settings.Security.CountryAllowListMode,
                deviceVerificationExemptEmails = settings.Security.DeviceVerificationExemptEmails,
                ipIntelligenceProvider = settings.IpIntelligence.Provider,
                ipinfoToken = MaskPlainSecret(settings.IpIntelligence.IpinfoToken),
                ipIntelligenceConfigured = settings.IpIntelligence.IsConfigured,
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
            easyKash = new
            {
                apiBaseUrl = settings.EasyKash.ApiBaseUrl,
                apiKey = MaskPlainSecret(settings.EasyKash.ApiKey),
                hmacSecret = MaskPlainSecret(settings.EasyKash.HmacSecret),
                paymentOptionsCsv = settings.EasyKash.PaymentOptions.Count > 0
                    ? string.Join(",", settings.EasyKash.PaymentOptions)
                    : null,
                currencyMode = settings.EasyKash.CurrencyMode,
                successUrl = settings.EasyKash.SuccessUrl,
                cancelUrl = settings.EasyKash.CancelUrl,
                isConfigured = settings.EasyKash.IsConfigured,
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
                securityEventsDays = (int)Math.Round(settings.DataRetention.SecurityEvents.TotalDays),
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
            // The support number is public (it is printed next to every package),
            // so it is returned in plaintext — unlike every secret above.
            support = new
            {
                whatsAppNumber = settings.Support.WhatsAppNumber,
                whatsAppProofTemplate = settings.Support.WhatsAppProofTemplate,
                isWhatsAppConfigured = settings.Support.IsWhatsAppConfigured,
            },
            firebaseOtp = new
            {
                enabled = settings.FirebaseOtp.Enabled,
                smsEnabled = settings.FirebaseOtp.SmsEnabled,
                emailLinksEnabled = settings.FirebaseOtp.EmailLinksEnabled,
                fallbackToBrevo = settings.FirebaseOtp.FallbackToBrevo,
                projectId = settings.FirebaseOtp.ProjectId,
                authDomain = settings.FirebaseOtp.AuthDomain,
                webApiKey = MaskPlainSecret(settings.FirebaseOtp.WebApiKey),
                isSmsConfigured = settings.FirebaseOtp.IsSmsConfigured,
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
                authFromAddress = r.AuthFromAddress,
                authFromName = r.AuthFromName,
                marketingFromAddress = r.MarketingFromAddress,
                marketingFromName = r.MarketingFromName,
                productFromAddress = r.ProductFromAddress,
                productFromName = r.ProductFromName,
                supportFromAddress = r.SupportFromAddress,
                supportFromName = r.SupportFromName,
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
                fcmServiceAccountJson = MaskSecret(r.FcmServiceAccountJsonEncrypted),
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
            placement = new
            {
                placementEnabled = r.PlacementEnabled,
                betaOnly = r.PlacementBetaOnly,
                betaEmails = r.PlacementBetaEmails,
            },
            updatedBy = r.UpdatedByUserName,
            updatedByUserId = r.UpdatedByUserId,
            updatedAt = r.UpdatedAt == default ? (DateTimeOffset?)null : r.UpdatedAt,
        };

    private static string MaskSecret(string? cipher)
        => string.IsNullOrEmpty(cipher) ? string.Empty : SecretMask;

    private static string MaskPlainSecret(string? value)
        => string.IsNullOrWhiteSpace(value) ? string.Empty : SecretMask;
}
