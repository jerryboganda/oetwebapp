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
        if (TrySetPlain(d.AuthFromAddress, v => row.AuthFromAddress = v, "email.authFromAddress", changed)) { }
        if (TrySetPlain(d.AuthFromName, v => row.AuthFromName = v, "email.authFromName", changed)) { }
        if (TrySetPlain(d.MarketingFromAddress, v => row.MarketingFromAddress = v, "email.marketingFromAddress", changed)) { }
        if (TrySetPlain(d.MarketingFromName, v => row.MarketingFromName = v, "email.marketingFromName", changed)) { }
        if (TrySetPlain(d.ProductFromAddress, v => row.ProductFromAddress = v, "email.productFromAddress", changed)) { }
        if (TrySetPlain(d.ProductFromName, v => row.ProductFromName = v, "email.productFromName", changed)) { }
        if (TrySetPlain(d.SupportFromAddress, v => row.SupportFromAddress = v, "email.supportFromAddress", changed)) { }
        if (TrySetPlain(d.SupportFromName, v => row.SupportFromName = v, "email.supportFromName", changed)) { }
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
        if (TrySetSecret(d.FcmServiceAccountJson, p, v => row.FcmServiceAccountJsonEncrypted = v, "push.fcmServiceAccountJson", changed)) { }
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

    private static void ApplyPlacement(RuntimeSettingsRow row, RuntimeSettingsPlacementUpdate? d,
        List<string> changed)
    {
        if (d is null) return;
        if (TrySetNullableBool(d.PlacementEnabled, v => row.PlacementEnabled = v, "placement.placementEnabled", changed)) { }
        if (TrySetNullableBool(d.BetaOnly, v => row.PlacementBetaOnly = v, "placement.betaOnly", changed)) { }
        if (TrySetPlain(d.BetaEmails, v => row.PlacementBetaEmails = v, "placement.betaEmails", changed)) { }
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

    private static void ApplyBunnyStream(RuntimeSettingsRow row, RuntimeSettingsBunnyStreamUpdate? d,
        IRuntimeSettingsProvider p, List<string> changed)
    {
        if (d is null) return;
        if (TrySetNullableBool(d.Enabled, v => row.BunnyStreamEnabled = v, "bunnyStream.enabled", changed)) { }
        if (TrySetPlain(d.LibraryId, v => row.BunnyStreamLibraryId = v, "bunnyStream.libraryId", changed)) { }
        if (TrySetSecret(d.ApiKey, p, v => row.BunnyStreamApiKeyEncrypted = v, "bunnyStream.apiKey", changed)) { }
        if (TrySetPlain(d.CdnHostname, v => row.BunnyStreamCdnHostname = v, "bunnyStream.cdnHostname", changed)) { }
        if (TrySetSecret(d.TokenAuthKey, p, v => row.BunnyStreamTokenAuthKeyEncrypted = v, "bunnyStream.tokenAuthKey", changed)) { }
        if (TrySetSecret(d.WebhookSecret, p, v => row.BunnyStreamWebhookSecretEncrypted = v, "bunnyStream.webhookSecret", changed)) { }
        if (TrySetPlain(d.CollectionId, v => row.BunnyStreamCollectionId = v, "bunnyStream.collectionId", changed)) { }
        if (TrySetNullableInt(d.PlaybackTokenTtlSeconds, v => row.BunnyStreamPlaybackTokenTtlSeconds = v, "bunnyStream.playbackTokenTtlSeconds", changed, min: 300, max: 86_400)) { }

        if (d.VideoAttestationKeysJson is not null && d.VideoAttestationKeysJson != SecretMask)
        {
            // Validate the JSON map shape before encrypting — a malformed map
            // would silently disable playback attestation.
            if (d.VideoAttestationKeysJson.Length > 0)
            {
                try
                {
                    var parsed = JsonSerializer.Deserialize<Dictionary<string, string>>(d.VideoAttestationKeysJson);
                    if (parsed is null || parsed.Count == 0 || parsed.Any(kv =>
                            string.IsNullOrWhiteSpace(kv.Key) || !kv.Key.Contains(':') || string.IsNullOrWhiteSpace(kv.Value)))
                    {
                        throw new RuntimeSettingsValidationException(
                            "bunnyStream.videoAttestationKeys must be a JSON map of \"platform:keyId\" → hex secret.");
                    }
                }
                catch (JsonException)
                {
                    throw new RuntimeSettingsValidationException(
                        "bunnyStream.videoAttestationKeys must be valid JSON.");
                }
            }
            if (TrySetSecret(d.VideoAttestationKeysJson, p, v => row.VideoAttestationKeysEncrypted = v, "bunnyStream.videoAttestationKeys", changed)) { }
        }
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

    private static void ApplyEasyKash(RuntimeSettingsRow row, RuntimeSettingsEasyKashUpdate? d,
        IRuntimeSettingsProvider p, List<string> changed)
    {
        if (d is null) return;
        if (TrySetPlain(d.ApiBaseUrl, v => row.EasyKashApiBaseUrl = v, "easyKash.apiBaseUrl", changed)) { }
        if (TrySetSecret(d.ApiKey, p, v => row.EasyKashApiKeyEncrypted = v, "easyKash.apiKey", changed)) { }
        if (TrySetSecret(d.HmacSecret, p, v => row.EasyKashHmacSecretEncrypted = v, "easyKash.hmacSecret", changed)) { }
        if (TrySetPlain(d.PaymentOptionsCsv, v => row.EasyKashPaymentOptionsCsv = v, "easyKash.paymentOptionsCsv", changed)) { }
        if (TrySetPlain(d.CurrencyMode, v => row.EasyKashCurrencyMode = v, "easyKash.currencyMode", changed)) { }
        if (TrySetPlain(d.SuccessUrl, v => row.EasyKashSuccessUrl = v, "easyKash.successUrl", changed)) { }
        if (TrySetPlain(d.CancelUrl, v => row.EasyKashCancelUrl = v, "easyKash.cancelUrl", changed)) { }
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

    private static void ApplyVideoProtection(RuntimeSettingsRow row, RuntimeSettingsVideoProtectionUpdate? d, List<string> changed)
    {
        if (d is null) return;
        if (TrySetNullableBool(d.RevokeOnCaptureDetected, v => row.VideoProtectionRevokeOnCaptureDetected = v, "videoProtection.revokeOnCaptureDetected", changed)) { }
        if (TrySetNullableBool(d.BlockRootedDevices, v => row.VideoProtectionBlockRootedDevices = v, "videoProtection.blockRootedDevices", changed)) { }
        if (TrySetNullableBool(d.BlockEmulators, v => row.VideoProtectionBlockEmulators = v, "videoProtection.blockEmulators", changed)) { }
    }

    private static void ApplySecurity(
        RuntimeSettingsRow row,
        RuntimeSettingsSecurityUpdate? d,
        IRuntimeSettingsProvider p,
        List<string> changed)
    {
        if (d is null) return;
        if (TrySetNullableBool(d.SingleActiveSessionEnabled, v => row.SecuritySingleActiveSessionEnabled = v, "security.singleActiveSessionEnabled", changed)) { }
        if (d.RiskMode is not null)
        {
            if (!ValidRiskModes.Contains(d.RiskMode))
            {
                throw new RuntimeSettingsValidationException("security.riskMode must be one of: off, log_only, enforce.");
            }
            row.SecurityRiskMode = d.RiskMode;
            changed.Add("security.riskMode");
        }
        if (TrySetNullableBool(d.TrustedDeviceRequired, v => row.SecurityTrustedDeviceRequired = v, "security.trustedDeviceRequired", changed)) { }
        if (TrySetNullableInt(d.DeviceChangeWindowDays, v => row.SecurityDeviceChangeWindowDays = v, "security.deviceChangeWindowDays", changed, min: 1, max: 365)) { }
        if (TrySetNullableInt(d.DeviceChangeMaxPerWindow, v => row.SecurityDeviceChangeMaxPerWindow = v, "security.deviceChangeMaxPerWindow", changed, min: 1, max: 100)) { }
        if (TrySetNullableInt(d.InactiveSessionTimeoutDays, v => row.SecurityInactiveSessionTimeoutDays = v, "security.inactiveSessionTimeoutDays", changed, min: 1, max: 365)) { }
        if (TrySetNullableBool(d.RequireVerifiedEmailForLearners, v => row.SecurityRequireVerifiedEmailForLearners = v, "security.requireVerifiedEmailForLearners", changed)) { }
        if (d.CountryAllowList is not null)
        {
            var codes = d.CountryAllowList
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(c => c.ToUpperInvariant())
                .Distinct()
                .ToArray();
            if (codes.Any(c => c.Length != 2 || !c.All(char.IsAsciiLetterUpper)))
            {
                throw new RuntimeSettingsValidationException("security.countryAllowList must be comma-separated 2-letter ISO country codes (e.g. \"EG,SA,AE\").");
            }
            var normalized = string.Join(',', codes);
            if (normalized.Length > 512)
            {
                throw new RuntimeSettingsValidationException("security.countryAllowList is too long (max 512 characters).");
            }
            row.SecurityCountryAllowList = normalized.Length > 0 ? normalized : null;
            changed.Add("security.countryAllowList");
        }
        if (d.CountryAllowListMode is not null)
        {
            if (!ValidCountryAllowListModes.Contains(d.CountryAllowListMode))
            {
                throw new RuntimeSettingsValidationException("security.countryAllowListMode must be one of: off, step_up, block.");
            }
            row.SecurityCountryAllowListMode = d.CountryAllowListMode;
            changed.Add("security.countryAllowListMode");
        }
        if (d.DeviceVerificationExemptEmails is not null)
        {
            string[] emails;
            try
            {
                emails = d.DeviceVerificationExemptEmails
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Select(AuthEmailAddress.NormalizeOrThrow)
                    .Distinct()
                    .ToArray();
            }
            catch (ApiException)
            {
                throw new RuntimeSettingsValidationException(
                    "security.deviceVerificationExemptEmails must be a comma-separated list of valid email addresses.");
            }
            var normalized = string.Join(',', emails);
            if (normalized.Length > 2000)
            {
                throw new RuntimeSettingsValidationException("security.deviceVerificationExemptEmails is too long (max 2000 characters).");
            }
            row.SecurityDeviceVerificationExemptEmails = normalized.Length > 0 ? normalized : null;
            changed.Add("security.deviceVerificationExemptEmails");
        }
        if (d.IpIntelligenceProvider is not null)
        {
            var normalized = d.IpIntelligenceProvider.Trim().ToLowerInvariant();
            if (normalized is not (IpIntelligenceProviders.Off or IpIntelligenceProviders.Ipinfo))
            {
                throw new RuntimeSettingsValidationException(
                    "security.ipIntelligenceProvider must be one of: off, ipinfo.");
            }
            row.SecurityIpIntelligenceProvider = normalized;
            changed.Add("security.ipIntelligenceProvider");
        }
        if (TrySetSecret(
                d.IpinfoToken,
                p,
                v => row.SecurityIpinfoTokenEncrypted = v,
                "security.ipinfoToken",
                changed)) { }
    }

    private static void ApplyDataRetention(RuntimeSettingsRow row, RuntimeSettingsDataRetentionUpdate? d, List<string> changed)
    {
        if (d is null) return;
        if (TrySetNullableInt(d.AnalyticsEventsDays, v => row.DataRetentionAnalyticsEventsDays = v, "dataRetention.analyticsEventsDays", changed, min: 0, max: 36500)) { }
        if (TrySetNullableInt(d.AuditEventsDays, v => row.DataRetentionAuditEventsDays = v, "dataRetention.auditEventsDays", changed, min: 0, max: 36500)) { }
        if (TrySetNullableInt(d.PaymentWebhookEventsDays, v => row.DataRetentionPaymentWebhookEventsDays = v, "dataRetention.paymentWebhookEventsDays", changed, min: 0, max: 36500)) { }
        if (TrySetNullableInt(d.PaymentWebhookPiiNullOutAgeDays, v => row.DataRetentionPaymentWebhookPiiNullOutAgeDays = v, "dataRetention.paymentWebhookPiiNullOutAgeDays", changed, min: 0, max: 36500)) { }
        if (TrySetNullableInt(d.NotificationDeliveryAttemptsDays, v => row.DataRetentionNotificationDeliveryAttemptsDays = v, "dataRetention.notificationDeliveryAttemptsDays", changed, min: 0, max: 36500)) { }
        if (TrySetNullableInt(d.SecurityEventsDays, v => row.DataRetentionSecurityEventsDays = v, "dataRetention.securityEventsDays", changed, min: 0, max: 36500)) { }
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

    private static void ApplySupport(RuntimeSettingsRow row, RuntimeSettingsSupportUpdate? d, List<string> changed)
    {
        if (d is null) return;
        ValidateWhatsAppNumber(d.WhatsAppNumber, "support.whatsAppNumber");
        if (TrySetPlain(NormalizeWhatsAppNumber(d.WhatsAppNumber), v => row.SupportWhatsAppNumber = v, "support.whatsAppNumber", changed)) { }
        if (TrySetPlain(d.WhatsAppProofTemplate, v => row.SupportWhatsAppProofTemplate = v, "support.whatsAppProofTemplate", changed)) { }
    }

    private static void ApplyFirebaseOtp(RuntimeSettingsRow row, RuntimeSettingsFirebaseOtpUpdate? d,
        IRuntimeSettingsProvider p, List<string> changed)
    {
        if (d is null) return;
        if (TrySetNullableBool(d.Enabled, v => row.FirebaseOtpEnabled = v, "firebaseOtp.enabled", changed)) { }
        if (TrySetNullableBool(d.SmsEnabled, v => row.FirebaseOtpSmsEnabled = v, "firebaseOtp.smsEnabled", changed)) { }
        if (TrySetNullableBool(d.EmailLinksEnabled, v => row.FirebaseOtpEmailLinksEnabled = v, "firebaseOtp.emailLinksEnabled", changed)) { }
        if (TrySetNullableBool(d.FallbackToBrevo, v => row.FirebaseOtpFallbackToBrevo = v, "firebaseOtp.fallbackToBrevo", changed)) { }
        if (TrySetPlain(d.ProjectId, v => row.FirebaseOtpProjectId = v, "firebaseOtp.projectId", changed)) { }
        if (TrySetPlain(d.AuthDomain, v => row.FirebaseOtpAuthDomain = v, "firebaseOtp.authDomain", changed)) { }
        if (TrySetSecret(d.WebApiKey, p, v => row.FirebaseOtpWebApiKeyEncrypted = v, "firebaseOtp.webApiKey", changed)) { }
    }

    /// <summary>
    /// The number is embedded in a <c>wa.me/&lt;number&gt;</c> deep link, which accepts
    /// digits only — no '+', spaces or dashes. Reject anything else here rather than
    /// ship a silently dead proof button on every package.
    /// </summary>
    private static void ValidateWhatsAppNumber(string? value, string key)
    {
        if (value is null || value == SecretMask) return;
        var normalized = NormalizeWhatsAppNumber(value);
        if (string.IsNullOrEmpty(normalized)) return; // empty clears the override
        if (normalized.Length is < 6 or > 20 || !normalized.All(char.IsAsciiDigit))
        {
            throw new RuntimeSettingsValidationException(
                $"{key} must be 6-20 digits in international format without '+' (e.g. 447961725989).");
        }
    }

    /// <summary>Strips the punctuation admins paste from a phone keypad ('+', spaces,
    /// dashes, parentheses) so the stored value is always wa.me-ready.</summary>
    private static string? NormalizeWhatsAppNumber(string? value)
    {
        if (value is null || value == SecretMask) return value;
        return new string(value.Where(ch => !char.IsWhiteSpace(ch) && ch is not ('+' or '-' or '(' or ')')).ToArray());
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
                    // Use the same web (camelCase, case-insensitive) options the GET serializes
                    // with — otherwise the round-tripped {"amount":..,"credits":..} binds to all
                    // zeros under default case-sensitive PascalCase and falsely fails validation.
                    var parsed = JsonSerializer.Deserialize<List<WalletTopUpTierOption>>(trimmed, JsonSupport.Options);
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
}
