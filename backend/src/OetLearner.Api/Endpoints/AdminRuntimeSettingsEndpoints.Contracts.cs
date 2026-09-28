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
    public RuntimeSettingsPlacementUpdate? Placement { get; set; }
    public RuntimeSettingsCheckoutComUpdate? CheckoutCom { get; set; }
    public RuntimeSettingsBunnyStreamUpdate? BunnyStream { get; set; }
    public RuntimeSettingsVideoProtectionUpdate? VideoProtection { get; set; }
    public RuntimeSettingsSecurityUpdate? Security { get; set; }
    public RuntimeSettingsPaymobUpdate? Paymob { get; set; }
    public RuntimeSettingsPayTabsUpdate? PayTabs { get; set; }
    public RuntimeSettingsEasyKashUpdate? EasyKash { get; set; }
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
    public RuntimeSettingsSupportUpdate? Support { get; set; }
    public RuntimeSettingsFirebaseOtpUpdate? FirebaseOtp { get; set; }
}

/// <summary>Firebase Phone Auth as an SMS OTP transport only. Never the login
/// / session authority. WebApiKey is stored encrypted; GET returns the mask.</summary>
public sealed class RuntimeSettingsFirebaseOtpUpdate
{
    public JsonElement? Enabled { get; set; }
    public JsonElement? SmsEnabled { get; set; }
    public JsonElement? EmailLinksEnabled { get; set; }
    public JsonElement? FallbackToBrevo { get; set; }
    public string? ProjectId { get; set; }
    public string? AuthDomain { get; set; }
    public string? WebApiKey { get; set; }
}

/// <summary>Public support channel (WhatsApp proof number + deep-link template).
/// Neither field is a secret — both are returned in plaintext by GET.</summary>
public sealed class RuntimeSettingsSupportUpdate
{
    /// <summary>International format, digits only ("" clears the override and falls
    /// back to the PLATFORM_WHATSAPP constant).</summary>
    public string? WhatsAppNumber { get; set; }
    public string? WhatsAppProofTemplate { get; set; }
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
    public JsonElement? SecurityEventsDays { get; set; }
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

/// <summary>Bunny Stream (Video Library) overrides. ApiKey / TokenAuthKey /
/// WebhookSecret / VideoAttestationKeysJson are secrets ("********" leaves
/// unchanged; "" clears; anything else is validated then encrypted).</summary>
public sealed class RuntimeSettingsBunnyStreamUpdate
{
    public JsonElement? Enabled { get; set; }
    public string? LibraryId { get; set; }
    public string? ApiKey { get; set; }
    public string? CdnHostname { get; set; }
    public string? TokenAuthKey { get; set; }
    public string? WebhookSecret { get; set; }
    public string? CollectionId { get; set; }
    public JsonElement? PlaybackTokenTtlSeconds { get; set; }
    /// <summary>JSON map {"tauri:v1":"&lt;hex&gt;", ...} for playback attestation.</summary>
    public string? VideoAttestationKeysJson { get; set; }
}

/// <summary>Video capture-protection policy (Course Platform Security Requirements §2).</summary>
public sealed class RuntimeSettingsVideoProtectionUpdate
{
    public JsonElement? RevokeOnCaptureDetected { get; set; }
    public JsonElement? BlockRootedDevices { get; set; }
    public JsonElement? BlockEmulators { get; set; }
}

/// <summary>Account-security policy (Course Platform Security Requirements §3).</summary>
public sealed class RuntimeSettingsSecurityUpdate
{
    public JsonElement? SingleActiveSessionEnabled { get; set; }
    /// <summary>"off" | "log_only" | "enforce" — see SecurityRiskModes.</summary>
    public string? RiskMode { get; set; }
    /// <summary>Mandatory device-binding gate. Keep enabled; the frontend
    /// device-challenge flow and secure device-id initialization are shipped.</summary>
    public JsonElement? TrustedDeviceRequired { get; set; }
    public JsonElement? DeviceChangeWindowDays { get; set; }
    public JsonElement? DeviceChangeMaxPerWindow { get; set; }
    /// <summary>Idle sessions past this many days are revoked by AuthDataRetentionWorker (spec §4.2).</summary>
    public JsonElement? InactiveSessionTimeoutDays { get; set; }
    /// <summary>Spec §4.2 hard gate: learner API access requires a verified email.</summary>
    public JsonElement? RequireVerifiedEmailForLearners { get; set; }
    /// <summary>Comma-separated 2-letter ISO country codes; "" clears the list.</summary>
    public string? CountryAllowList { get; set; }
    /// <summary>"off" | "step_up" | "block" — see SecurityCountryAllowListModes.</summary>
    public string? CountryAllowListMode { get; set; }
    /// <summary>Comma-separated emails fully exempt from device-verification
    /// OTP and risk step-up (owner/staff accounts). "" clears the list.</summary>
    public string? DeviceVerificationExemptEmails { get; set; }
    /// <summary>"off" | "ipinfo". IPinfo Core or better supplies privacy flags.</summary>
    public string? IpIntelligenceProvider { get; set; }
    /// <summary>Write-only IPinfo API token. "********" keeps the existing value.</summary>
    public string? IpinfoToken { get; set; }
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

/// <summary>EasyKash payment gateway overrides.</summary>
public sealed class RuntimeSettingsEasyKashUpdate
{
    public string? ApiBaseUrl { get; set; }
    public string? ApiKey { get; set; }
    public string? HmacSecret { get; set; }
    /// <summary>CSV of EasyKash payment-method ids to offer (e.g. "2,4,5"). Empty = all dashboard-enabled.</summary>
    public string? PaymentOptionsCsv { get; set; }
    /// <summary>"passthrough" (charge quote currency) or "egp" (FX-convert to EGP).</summary>
    public string? CurrencyMode { get; set; }
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
    public string? AuthFromAddress { get; set; }
    public string? AuthFromName { get; set; }
    public string? MarketingFromAddress { get; set; }
    public string? MarketingFromName { get; set; }
    public string? ProductFromAddress { get; set; }
    public string? ProductFromName { get; set; }
    public string? SupportFromAddress { get; set; }
    public string? SupportFromName { get; set; }
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
    public string? FcmServiceAccountJson { get; set; }
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

/// <summary>Placement test rollout flags.</summary>
public sealed class RuntimeSettingsPlacementUpdate
{
    public JsonElement? PlacementEnabled { get; set; }
    public JsonElement? BetaOnly { get; set; }
    /// <summary>Full-replace comma/semicolon-separated allowlist; empty
    /// string clears it.</summary>
    public string? BetaEmails { get; set; }
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
