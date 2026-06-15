using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace OetLearner.Api.Domain;

/// <summary>
/// Runtime overrides for cross-cutting infrastructure secrets and tunables
/// (Brevo / SMTP, Stripe, Sentry, Backup S3, OAuth providers, Push, Upload scanner, Zoom). Singleton
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

    // ── Email partial-coverage gap (Wave 3) ────────────────────────
    // Extends the existing Email/Brevo + SMTP coverage with the remaining
    // template IDs, the Brevo webhook secret, and the Brevo/SMTP master flags.
    // Brevo:BaseUrl / FromEmail / FromName + the core SMTP host/port/etc stay as
    // the existing columns above and are intentionally not duplicated.
    public int? BrevoWelcomeTemplateId { get; set; }
    public int? BrevoPasswordChangedTemplateId { get; set; }
    public int? BrevoMfaEnabledTemplateId { get; set; }
    public int? BrevoAdminInviteTemplateId { get; set; }
    public int? BrevoSecurityAlertTemplateId { get; set; }
    public int? BrevoReviewCompletedTemplateId { get; set; }
    public string? BrevoWebhookSecretEncrypted { get; set; }
    public bool? BrevoEnabled { get; set; }
    public bool? SmtpEnabled { get; set; }
    public bool? SmtpEnableSsl { get; set; }

    // ── Billing / Stripe ───────────────────────────────────────────
    public string? StripeSecretKeyEncrypted { get; set; }
    [MaxLength(256)] public string? StripePublishableKey { get; set; }
    public string? StripeWebhookSecretEncrypted { get; set; }
    [MaxLength(1024)] public string? StripeSuccessUrl { get; set; }
    [MaxLength(1024)] public string? StripeCancelUrl { get; set; }
    /// <summary>
    /// Public app/web base URL (e.g. https://app.oetwithdrhesham.co.uk) used to build
    /// absolute checkout success/cancel return URLs without depending on the
    /// <c>Platform:PublicWebBaseUrl</c> environment variable. Falls back to that env var
    /// when null. Lets admins drive Stripe checkout entirely from the settings UI.
    /// </summary>
    [MaxLength(1024)] public string? BillingPublicAppBaseUrl { get; set; }

    // ── Stripe Tax / Customer Portal / Radar (Wave A5) ─────────────
    public bool? StripeTaxAutomaticEnabled { get; set; }
    /// <summary>CSV of Stripe tax registration codes (e.g., "UK_VAT,EU_OSS,AU_GST").</summary>
    [MaxLength(1024)] public string? StripeTaxRegistrationsCsv { get; set; }
    [MaxLength(128)] public string? StripeCustomerPortalConfigurationId { get; set; }
    public bool? StripeRadarHighRiskCountryAllowReview { get; set; }
    [MaxLength(1024)] public string? StripeRadarBlockEmailDomainsCsv { get; set; }

    [MaxLength(256)] public string? PayPalClientId { get; set; }
    public string? PayPalClientSecretEncrypted { get; set; }
    public string? PayPalWebhookIdEncrypted { get; set; }
    [MaxLength(1024)] public string? PayPalSuccessUrl { get; set; }
    [MaxLength(1024)] public string? PayPalCancelUrl { get; set; }
    /// <summary>
    /// Whether PayPal Expanded checkout may render embedded Advanced Card Fields (type the
    /// card directly on our page). Null = use the env/default (true). Admins flip this off
    /// instantly — without a deploy — if the live account loses Advanced Cards eligibility;
    /// the embedded UI then degrades to PayPal/Venmo/Pay Later buttons only.
    /// </summary>
    public bool? PayPalAdvancedCardsEnabled { get; set; }

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
    [MaxLength(256)] public string? VapidSubject { get; set; }
    [MaxLength(512)] public string? VapidPublicKey { get; set; }
    public string? VapidPrivateKeyEncrypted { get; set; }

    // ── Upload scanner / ClamAV ────────────────────────────────────
    [MaxLength(32)] public string? UploadScannerProvider { get; set; }
    [MaxLength(256)] public string? UploadScannerHost { get; set; }
    public int? UploadScannerPort { get; set; }
    public int? UploadScannerTimeoutSeconds { get; set; }
    public bool? UploadScannerFailClosedOnError { get; set; }

    // ── Zoom live classes ──────────────────────────────────────────
    public bool? ZoomEnabled { get; set; }
    [MaxLength(256)] public string? ZoomAccountId { get; set; }
    [MaxLength(256)] public string? ZoomClientId { get; set; }
    public string? ZoomClientSecretEncrypted { get; set; }
    [MaxLength(512)] public string? ZoomApiBaseUrl { get; set; }
    [MaxLength(512)] public string? ZoomTokenUrl { get; set; }
    [MaxLength(256)] public string? ZoomHostUserId { get; set; }
    [MaxLength(256)] public string? ZoomMeetingSdkKey { get; set; }
    public string? ZoomMeetingSdkSecretEncrypted { get; set; }
    public string? ZoomWebhookSecretTokenEncrypted { get; set; }
    public int? ZoomWebhookRetryToleranceSeconds { get; set; }
    public bool? ZoomAllowSandboxFallback { get; set; }

    // ── Live Class AI Pipeline (Wave A2) ──────────────────────────
    /// <summary>
    /// Feature flag for the AI recording-processing pipeline (transcribe,
    /// summarise, translate, embed). When <c>null</c> or <c>false</c> the
    /// background-job bodies short-circuit cleanly without making any AI
    /// calls. Defaults <c>false</c> (off) until an admin enables it.
    /// </summary>
    public bool? LiveClassesAiRecordingProcessingEnabled { get; set; }

    // ── Speaking Whisper transcription (RULE_40 tone pipeline) ─────
    // 2026-05-28 audit fix — Dr. Hesham asked for the Whisper API key to be
    // configurable from the admin panel. Stored encrypted via the existing
    // RuntimeSettings.Secret.v1 protector. When null the OpenAiWhisperSpeakingProvider
    // falls back to the appsettings (`Speaking:Whisper:ApiKey`) or, ultimately,
    // the MockSpeakingTranscriptionProvider.
    public string? SpeakingWhisperApiKeyEncrypted { get; set; }
    [MaxLength(512)] public string? SpeakingWhisperBaseUrl { get; set; }
    [MaxLength(64)] public string? SpeakingWhisperModel { get; set; }

    // ── Speaking LiveKit (live tutor rooms + egress recording) ─────
    [MaxLength(32)] public string? SpeakingLiveKitProvider { get; set; }
    public string? SpeakingLiveKitApiKeyEncrypted { get; set; }
    public string? SpeakingLiveKitApiSecretEncrypted { get; set; }
    [MaxLength(512)] public string? SpeakingLiveKitWssUrl { get; set; }
    public string? SpeakingLiveKitWebhookSigningSecretEncrypted { get; set; }
    [MaxLength(256)] public string? SpeakingLiveKitEgressBucket { get; set; }
    public int? SpeakingLiveKitDefaultMaxDurationSeconds { get; set; }
    public bool? SpeakingLiveKitEgressEnabled { get; set; }

    // ── Speaking AI (Anthropic — scoring + patient turns) ──────────
    public string? SpeakingAnthropicApiKeyEncrypted { get; set; }

    // ── Speaking ElevenLabs (AI patient TTS) ───────────────────────
    public string? SpeakingElevenLabsApiKeyEncrypted { get; set; }

    // ── Speaking AWS S3 (recording archive) ────────────────────────
    [MaxLength(256)] public string? SpeakingAwsAccessKeyId { get; set; }
    public string? SpeakingAwsSecretAccessKeyEncrypted { get; set; }
    [MaxLength(64)] public string? SpeakingAwsRegion { get; set; }
    [MaxLength(256)] public string? SpeakingAwsBucket { get; set; }

    // ── Speaking Compliance (consent versioning + retention) ───────
    [MaxLength(64)] public string? SpeakingComplianceCurrentConsentVersion { get; set; }
    [MaxLength(64)] public string? SpeakingComplianceCurrentLiveVideoConsentVersion { get; set; }
    public int? SpeakingComplianceRetentionDaysDefault { get; set; }
    public int? SpeakingComplianceRetentionDaysWhenTutorReviewed { get; set; }
    public int? SpeakingComplianceAuditLogRetentionDays { get; set; }

    // ── Speaking Feature Flag ──────────────────────────────────────
    public bool? SpeakingV2Enabled { get; set; }

    // ── Checkout.com (premium MENA + global cards) ─────────────────
    [MaxLength(512)] public string? CheckoutComApiBaseUrl { get; set; }
    public string? CheckoutComSecretKeyEncrypted { get; set; }
    [MaxLength(256)] public string? CheckoutComPublicKey { get; set; }
    [MaxLength(128)] public string? CheckoutComProcessingChannelId { get; set; }
    public string? CheckoutComWebhookSecretEncrypted { get; set; }
    [MaxLength(1024)] public string? CheckoutComSuccessUrl { get; set; }
    [MaxLength(1024)] public string? CheckoutComCancelUrl { get; set; }

    // ── Paymob (Egypt) ─────────────────────────────────────────────
    [MaxLength(512)] public string? PaymobApiBaseUrl { get; set; }
    public string? PaymobApiKeyEncrypted { get; set; }
    [MaxLength(128)] public string? PaymobMerchantId { get; set; }
    public string? PaymobHmacSecretEncrypted { get; set; }
    /// <summary>JSON map of method name → integration id (e.g. {"card":123}).</summary>
    [MaxLength(1024)] public string? PaymobIntegrationIdsJson { get; set; }
    public int? PaymobIframeId { get; set; }
    [MaxLength(1024)] public string? PaymobSuccessUrl { get; set; }
    [MaxLength(1024)] public string? PaymobCancelUrl { get; set; }

    // ── PayTabs (Gulf/Egypt) ───────────────────────────────────────
    [MaxLength(512)] public string? PayTabsApiBaseUrl { get; set; }
    public string? PayTabsServerKeyEncrypted { get; set; }
    [MaxLength(128)] public string? PayTabsProfileId { get; set; }
    public string? PayTabsWebhookSecretEncrypted { get; set; }
    [MaxLength(1024)] public string? PayTabsSuccessUrl { get; set; }
    [MaxLength(1024)] public string? PayTabsCancelUrl { get; set; }

    // ── Soketi (realtime websocket push) ───────────────────────────
    [MaxLength(256)] public string? SoketiHost { get; set; }
    public int? SoketiPort { get; set; }
    [MaxLength(128)] public string? SoketiAppId { get; set; }
    [MaxLength(256)] public string? SoketiAppKey { get; set; }
    public string? SoketiAppSecretEncrypted { get; set; }
    public bool? SoketiUseTls { get; set; }
    public bool? SoketiEnabled { get; set; }

    // ── Data retention sweeper (high-volume event tables) ──────────
    // Retention windows are stored as whole days; the sweep cadence as whole
    // hours. null on any field falls back to the env/appsettings default in
    // DataRetentionOptions. A retention of 0 disables the sweep for that table.
    public int? DataRetentionAnalyticsEventsDays { get; set; }
    public int? DataRetentionAuditEventsDays { get; set; }
    public int? DataRetentionPaymentWebhookEventsDays { get; set; }
    public int? DataRetentionPaymentWebhookPiiNullOutAgeDays { get; set; }
    public int? DataRetentionNotificationDeliveryAttemptsDays { get; set; }
    public int? DataRetentionSweepIntervalHours { get; set; }
    public int? DataRetentionBatchSize { get; set; }

    // ── Expert auto-assignment loop (Writing review queue) ─────────
    public bool? ExpertAutoAssignmentEnabled { get; set; }
    public int? ExpertAutoAssignmentPollingIntervalSeconds { get; set; }
    public int? ExpertAutoAssignmentSlaEscalationIntervalSeconds { get; set; }
    public int? ExpertAutoAssignmentSlaHoursStandard { get; set; }
    public int? ExpertAutoAssignmentSlaHoursExpress { get; set; }
    public int? ExpertAutoAssignmentMaxActiveAssignmentsPerExpert { get; set; }
    public int? ExpertAutoAssignmentLookbackHoursForLoad { get; set; }
    public int? ExpertAutoAssignmentBatchSize { get; set; }

    // ── Password policy (complexity + HIBP breach check) ───────────
    public int? PasswordPolicyMinimumLength { get; set; }
    public bool? PasswordPolicyRequireMixedCase { get; set; }
    public bool? PasswordPolicyRequireDigit { get; set; }
    public bool? PasswordPolicyRequireSymbol { get; set; }
    public bool? PasswordPolicyBreachCheckEnabled { get; set; }
    [MaxLength(512)] public string? PasswordPolicyBreachApiBaseUrl { get; set; }
    public int? PasswordPolicyBreachApiTimeoutSeconds { get; set; }

    // ── AI Assistant (orchestration tunables — Wave 2) ─────────────
    // Orchestration logic knobs for the codebase AI assistant. AI provider
    // CREDENTIALS live in AiProviderRegistry, never here. AllowedRoots /
    // IndexExcludePatterns stay env-only (security boundary) and are excluded.
    public bool? AiAssistantGlobalEnabled { get; set; }
    public bool? AiAssistantRequireApprovalAlways { get; set; }
    public int? AiAssistantMaxIterations { get; set; }
    public int? AiAssistantMaxContextMessages { get; set; }
    public int? AiAssistantBackupRetentionDays { get; set; }
    public long? AiAssistantMaxWriteFileSizeBytes { get; set; }
    public int? AiAssistantCommandTimeoutSeconds { get; set; }
    public int? AiAssistantCircuitBreakerMaxFailures { get; set; }
    public int? AiAssistantCircuitBreakerFailureWindowSeconds { get; set; }
    public int? AiAssistantCircuitBreakerMaxWrites { get; set; }
    public int? AiAssistantCircuitBreakerWriteWindowSeconds { get; set; }
    public string? AiAssistantEmbeddingModel { get; set; }
    public int? AiAssistantMaxChunkTokens { get; set; }

    // ── AI gateway / tooling knobs (Wave 2) ────────────────────────
    // Non-credential AiProviderOptions (AI:*) + AiToolOptions (AiTool:*).
    // AI:ApiKey is EXCLUDED — handled by AiProviderRegistry (encrypted row).
    [MaxLength(64)] public string? AiProviderProviderId { get; set; }
    [MaxLength(512)] public string? AiProviderBaseUrl { get; set; }
    [MaxLength(128)] public string? AiProviderDefaultModel { get; set; }
    [MaxLength(16)] public string? AiProviderReasoningEffort { get; set; }
    public int? AiProviderDefaultMaxTokens { get; set; }
    public double? AiProviderDefaultTemperature { get; set; }
    public int? AiToolMaxToolCallsPerCompletion { get; set; }
    public int? AiToolFeatureGrantCacheSeconds { get; set; }
    [MaxLength(1024)] public string? AiToolAllowedExternalHostsCsv { get; set; }
    public int? AiToolExternalNetworkPerUserDailyCalls { get; set; }
    public int? AiToolExternalNetworkTimeoutMilliseconds { get; set; }
    public int? AiToolExternalNetworkMaxResponseBytes { get; set; }

    // ── Writing module V2 (Wave 2) ─────────────────────────────────
    // Feature flags + coach/queue/OCR tunables. GcvApiKey is the only secret.
    // TessdataPath + V2Seeder.Enabled stay env-only (excluded).
    public bool? WritingCronsEnabled { get; set; }
    public bool? WritingCoachEnabled { get; set; }
    [Column(TypeName = "numeric(10,2)")] public decimal? WritingCoachDailyCostCapPerLearnerUsd { get; set; }
    public int? WritingCoachMaxHintsPerSession { get; set; }
    public int? WritingCoachMinSecondsBetweenHints { get; set; }
    public string? WritingGcvApiKeyEncrypted { get; set; }
    public bool? WritingOcrEnabled { get; set; }
    public bool? WritingAppealsEnabled { get; set; }
    public int? WritingTutorReviewQueueMaxDepth { get; set; }
    public int? WritingTutorReviewMaxWaitHours { get; set; }
    public int? WritingMaxDailyPlanRegenerationsPerDay { get; set; }
    public int? WritingGradeIdempotencyTtlHours { get; set; }

    // ── Platform (public host URLs — Wave 2) ───────────────────────
    [MaxLength(1024)] public string? PublicApiBaseUrl { get; set; }
    [MaxLength(1024)] public string? PublicWebBaseUrl { get; set; }
    [MaxLength(256)] public string? FallbackEmailDomain { get; set; }

    // ── Messaging (Twilio SMS / WhatsApp Business Cloud — Wave 3) ───
    // Billing notification channels. AuthToken / AccessToken are SECRETS
    // (encrypted at rest). AccountSid is a public identifier (not encrypted).
    public bool? TwilioEnabled { get; set; }
    [MaxLength(512)] public string? TwilioApiBaseUrl { get; set; }
    [MaxLength(256)] public string? TwilioAccountSid { get; set; }
    public string? TwilioAuthTokenEncrypted { get; set; }
    [MaxLength(32)] public string? TwilioFromNumber { get; set; }
    [MaxLength(256)] public string? TwilioMessagingServiceSid { get; set; }
    public bool? WhatsAppEnabled { get; set; }
    [MaxLength(512)] public string? WhatsAppApiBaseUrl { get; set; }
    public string? WhatsAppAccessTokenEncrypted { get; set; }
    [MaxLength(256)] public string? WhatsAppPhoneNumberId { get; set; }
    [MaxLength(256)] public string? WhatsAppFallbackTemplateName { get; set; }

    // ── Audit ──────────────────────────────────────────────────────
    [MaxLength(64)]
    public string? UpdatedByUserId { get; set; }

    [MaxLength(128)]
    public string? UpdatedByUserName { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}
