namespace OetLearner.Api.Services.Settings;

/// <summary>
/// Idempotent DDL that guarantees the additive <see cref="OetLearner.Api.Domain.RuntimeSettingsRow"/>
/// override columns for the CheckoutCom / Paymob / PayTabs payment gateways and
/// the Soketi realtime push server exist. Executed at startup right after
/// <c>MigrateAsync</c> as a belt-and-suspenders for the (rare) case where the
/// matching EF migration has not been recorded on the live database yet — the
/// singleton <see cref="IRuntimeSettingsProvider"/> reads these columns during
/// the same startup, so a missing column would otherwise crash boot.
///
/// <para>
/// Every statement is <c>ADD COLUMN IF NOT EXISTS</c> on nullable columns, so
/// running it when the migration already applied is a harmless no-op. Keep in
/// exact sync with migration <c>20260705090000_AddPaymentAndSoketiRuntimeSettings</c>.
/// </para>
/// </summary>
public static class RuntimeSettingsSchemaSelfHeal
{
    public const string Sql = @"
ALTER TABLE ""RuntimeSettings"" ADD COLUMN IF NOT EXISTS ""BillingPublicAppBaseUrl"" character varying(1024);

-- Email partial-coverage gap (Wave 3)
ALTER TABLE ""RuntimeSettings"" ADD COLUMN IF NOT EXISTS ""BrevoWelcomeTemplateId"" integer;
ALTER TABLE ""RuntimeSettings"" ADD COLUMN IF NOT EXISTS ""BrevoPasswordChangedTemplateId"" integer;
ALTER TABLE ""RuntimeSettings"" ADD COLUMN IF NOT EXISTS ""BrevoMfaEnabledTemplateId"" integer;
ALTER TABLE ""RuntimeSettings"" ADD COLUMN IF NOT EXISTS ""BrevoAdminInviteTemplateId"" integer;
ALTER TABLE ""RuntimeSettings"" ADD COLUMN IF NOT EXISTS ""BrevoSecurityAlertTemplateId"" integer;
ALTER TABLE ""RuntimeSettings"" ADD COLUMN IF NOT EXISTS ""BrevoReviewCompletedTemplateId"" integer;
ALTER TABLE ""RuntimeSettings"" ADD COLUMN IF NOT EXISTS ""BrevoWebhookSecretEncrypted"" text;
ALTER TABLE ""RuntimeSettings"" ADD COLUMN IF NOT EXISTS ""BrevoEnabled"" boolean;
ALTER TABLE ""RuntimeSettings"" ADD COLUMN IF NOT EXISTS ""SmtpEnabled"" boolean;
ALTER TABLE ""RuntimeSettings"" ADD COLUMN IF NOT EXISTS ""SmtpEnableSsl"" boolean;

ALTER TABLE ""RuntimeSettings"" ADD COLUMN IF NOT EXISTS ""CheckoutComApiBaseUrl"" character varying(512);
ALTER TABLE ""RuntimeSettings"" ADD COLUMN IF NOT EXISTS ""CheckoutComSecretKeyEncrypted"" text;
ALTER TABLE ""RuntimeSettings"" ADD COLUMN IF NOT EXISTS ""CheckoutComPublicKey"" character varying(256);
ALTER TABLE ""RuntimeSettings"" ADD COLUMN IF NOT EXISTS ""CheckoutComProcessingChannelId"" character varying(128);
ALTER TABLE ""RuntimeSettings"" ADD COLUMN IF NOT EXISTS ""CheckoutComWebhookSecretEncrypted"" text;
ALTER TABLE ""RuntimeSettings"" ADD COLUMN IF NOT EXISTS ""CheckoutComSuccessUrl"" character varying(1024);
ALTER TABLE ""RuntimeSettings"" ADD COLUMN IF NOT EXISTS ""CheckoutComCancelUrl"" character varying(1024);

ALTER TABLE ""RuntimeSettings"" ADD COLUMN IF NOT EXISTS ""PaymobApiBaseUrl"" character varying(512);
ALTER TABLE ""RuntimeSettings"" ADD COLUMN IF NOT EXISTS ""PaymobApiKeyEncrypted"" text;
ALTER TABLE ""RuntimeSettings"" ADD COLUMN IF NOT EXISTS ""PaymobMerchantId"" character varying(128);
ALTER TABLE ""RuntimeSettings"" ADD COLUMN IF NOT EXISTS ""PaymobHmacSecretEncrypted"" text;
ALTER TABLE ""RuntimeSettings"" ADD COLUMN IF NOT EXISTS ""PaymobIntegrationIdsJson"" character varying(1024);
ALTER TABLE ""RuntimeSettings"" ADD COLUMN IF NOT EXISTS ""PaymobIframeId"" integer;
ALTER TABLE ""RuntimeSettings"" ADD COLUMN IF NOT EXISTS ""PaymobSuccessUrl"" character varying(1024);
ALTER TABLE ""RuntimeSettings"" ADD COLUMN IF NOT EXISTS ""PaymobCancelUrl"" character varying(1024);

ALTER TABLE ""RuntimeSettings"" ADD COLUMN IF NOT EXISTS ""PayTabsApiBaseUrl"" character varying(512);
ALTER TABLE ""RuntimeSettings"" ADD COLUMN IF NOT EXISTS ""PayTabsServerKeyEncrypted"" text;
ALTER TABLE ""RuntimeSettings"" ADD COLUMN IF NOT EXISTS ""PayTabsProfileId"" character varying(128);
ALTER TABLE ""RuntimeSettings"" ADD COLUMN IF NOT EXISTS ""PayTabsWebhookSecretEncrypted"" text;
ALTER TABLE ""RuntimeSettings"" ADD COLUMN IF NOT EXISTS ""PayTabsSuccessUrl"" character varying(1024);
ALTER TABLE ""RuntimeSettings"" ADD COLUMN IF NOT EXISTS ""PayTabsCancelUrl"" character varying(1024);

ALTER TABLE ""RuntimeSettings"" ADD COLUMN IF NOT EXISTS ""EasyKashApiBaseUrl"" character varying(512);
ALTER TABLE ""RuntimeSettings"" ADD COLUMN IF NOT EXISTS ""EasyKashApiKeyEncrypted"" text;
ALTER TABLE ""RuntimeSettings"" ADD COLUMN IF NOT EXISTS ""EasyKashHmacSecretEncrypted"" text;
ALTER TABLE ""RuntimeSettings"" ADD COLUMN IF NOT EXISTS ""EasyKashPaymentOptionsCsv"" character varying(256);
ALTER TABLE ""RuntimeSettings"" ADD COLUMN IF NOT EXISTS ""EasyKashCurrencyMode"" character varying(32);
ALTER TABLE ""RuntimeSettings"" ADD COLUMN IF NOT EXISTS ""EasyKashSuccessUrl"" character varying(1024);
ALTER TABLE ""RuntimeSettings"" ADD COLUMN IF NOT EXISTS ""EasyKashCancelUrl"" character varying(1024);

ALTER TABLE ""RuntimeSettings"" ADD COLUMN IF NOT EXISTS ""SoketiHost"" character varying(256);
ALTER TABLE ""RuntimeSettings"" ADD COLUMN IF NOT EXISTS ""SoketiPort"" integer;
ALTER TABLE ""RuntimeSettings"" ADD COLUMN IF NOT EXISTS ""SoketiAppId"" character varying(128);
ALTER TABLE ""RuntimeSettings"" ADD COLUMN IF NOT EXISTS ""SoketiAppKey"" character varying(256);
ALTER TABLE ""RuntimeSettings"" ADD COLUMN IF NOT EXISTS ""SoketiAppSecretEncrypted"" text;
ALTER TABLE ""RuntimeSettings"" ADD COLUMN IF NOT EXISTS ""SoketiUseTls"" boolean;
ALTER TABLE ""RuntimeSettings"" ADD COLUMN IF NOT EXISTS ""SoketiEnabled"" boolean;

ALTER TABLE ""RuntimeSettings"" ADD COLUMN IF NOT EXISTS ""DataRetentionAnalyticsEventsDays"" integer;
ALTER TABLE ""RuntimeSettings"" ADD COLUMN IF NOT EXISTS ""DataRetentionAuditEventsDays"" integer;
ALTER TABLE ""RuntimeSettings"" ADD COLUMN IF NOT EXISTS ""DataRetentionPaymentWebhookEventsDays"" integer;
ALTER TABLE ""RuntimeSettings"" ADD COLUMN IF NOT EXISTS ""DataRetentionPaymentWebhookPiiNullOutAgeDays"" integer;
ALTER TABLE ""RuntimeSettings"" ADD COLUMN IF NOT EXISTS ""DataRetentionNotificationDeliveryAttemptsDays"" integer;
ALTER TABLE ""RuntimeSettings"" ADD COLUMN IF NOT EXISTS ""DataRetentionSweepIntervalHours"" integer;
ALTER TABLE ""RuntimeSettings"" ADD COLUMN IF NOT EXISTS ""DataRetentionBatchSize"" integer;

ALTER TABLE ""RuntimeSettings"" ADD COLUMN IF NOT EXISTS ""ExpertAutoAssignmentEnabled"" boolean;
ALTER TABLE ""RuntimeSettings"" ADD COLUMN IF NOT EXISTS ""ExpertAutoAssignmentPollingIntervalSeconds"" integer;
ALTER TABLE ""RuntimeSettings"" ADD COLUMN IF NOT EXISTS ""ExpertAutoAssignmentSlaEscalationIntervalSeconds"" integer;
ALTER TABLE ""RuntimeSettings"" ADD COLUMN IF NOT EXISTS ""ExpertAutoAssignmentSlaHoursStandard"" integer;
ALTER TABLE ""RuntimeSettings"" ADD COLUMN IF NOT EXISTS ""ExpertAutoAssignmentSlaHoursExpress"" integer;
ALTER TABLE ""RuntimeSettings"" ADD COLUMN IF NOT EXISTS ""ExpertAutoAssignmentMaxActiveAssignmentsPerExpert"" integer;
ALTER TABLE ""RuntimeSettings"" ADD COLUMN IF NOT EXISTS ""ExpertAutoAssignmentLookbackHoursForLoad"" integer;
ALTER TABLE ""RuntimeSettings"" ADD COLUMN IF NOT EXISTS ""ExpertAutoAssignmentBatchSize"" integer;

ALTER TABLE ""RuntimeSettings"" ADD COLUMN IF NOT EXISTS ""PasswordPolicyMinimumLength"" integer;
ALTER TABLE ""RuntimeSettings"" ADD COLUMN IF NOT EXISTS ""PasswordPolicyRequireMixedCase"" boolean;
ALTER TABLE ""RuntimeSettings"" ADD COLUMN IF NOT EXISTS ""PasswordPolicyRequireDigit"" boolean;
ALTER TABLE ""RuntimeSettings"" ADD COLUMN IF NOT EXISTS ""PasswordPolicyRequireSymbol"" boolean;
ALTER TABLE ""RuntimeSettings"" ADD COLUMN IF NOT EXISTS ""PasswordPolicyBreachCheckEnabled"" boolean;
ALTER TABLE ""RuntimeSettings"" ADD COLUMN IF NOT EXISTS ""PasswordPolicyBreachApiBaseUrl"" character varying(512);
ALTER TABLE ""RuntimeSettings"" ADD COLUMN IF NOT EXISTS ""PasswordPolicyBreachApiTimeoutSeconds"" integer;

ALTER TABLE ""RuntimeSettings"" ADD COLUMN IF NOT EXISTS ""AiAssistantGlobalEnabled"" boolean;
ALTER TABLE ""RuntimeSettings"" ADD COLUMN IF NOT EXISTS ""AiAssistantRequireApprovalAlways"" boolean;
ALTER TABLE ""RuntimeSettings"" ADD COLUMN IF NOT EXISTS ""AiAssistantMaxIterations"" integer;
ALTER TABLE ""RuntimeSettings"" ADD COLUMN IF NOT EXISTS ""AiAssistantMaxContextMessages"" integer;
ALTER TABLE ""RuntimeSettings"" ADD COLUMN IF NOT EXISTS ""AiAssistantBackupRetentionDays"" integer;
ALTER TABLE ""RuntimeSettings"" ADD COLUMN IF NOT EXISTS ""AiAssistantMaxWriteFileSizeBytes"" bigint;
ALTER TABLE ""RuntimeSettings"" ADD COLUMN IF NOT EXISTS ""AiAssistantCommandTimeoutSeconds"" integer;
ALTER TABLE ""RuntimeSettings"" ADD COLUMN IF NOT EXISTS ""AiAssistantCircuitBreakerMaxFailures"" integer;
ALTER TABLE ""RuntimeSettings"" ADD COLUMN IF NOT EXISTS ""AiAssistantCircuitBreakerFailureWindowSeconds"" integer;
ALTER TABLE ""RuntimeSettings"" ADD COLUMN IF NOT EXISTS ""AiAssistantCircuitBreakerMaxWrites"" integer;
ALTER TABLE ""RuntimeSettings"" ADD COLUMN IF NOT EXISTS ""AiAssistantCircuitBreakerWriteWindowSeconds"" integer;
ALTER TABLE ""RuntimeSettings"" ADD COLUMN IF NOT EXISTS ""AiAssistantEmbeddingModel"" text;
ALTER TABLE ""RuntimeSettings"" ADD COLUMN IF NOT EXISTS ""AiAssistantMaxChunkTokens"" integer;

ALTER TABLE ""RuntimeSettings"" ADD COLUMN IF NOT EXISTS ""AiProviderProviderId"" character varying(64);
ALTER TABLE ""RuntimeSettings"" ADD COLUMN IF NOT EXISTS ""AiProviderBaseUrl"" character varying(512);
ALTER TABLE ""RuntimeSettings"" ADD COLUMN IF NOT EXISTS ""AiProviderDefaultModel"" character varying(128);
ALTER TABLE ""RuntimeSettings"" ADD COLUMN IF NOT EXISTS ""AiProviderReasoningEffort"" character varying(16);
ALTER TABLE ""RuntimeSettings"" ADD COLUMN IF NOT EXISTS ""AiProviderDefaultMaxTokens"" integer;
ALTER TABLE ""RuntimeSettings"" ADD COLUMN IF NOT EXISTS ""AiProviderDefaultTemperature"" double precision;
ALTER TABLE ""RuntimeSettings"" ADD COLUMN IF NOT EXISTS ""AiToolMaxToolCallsPerCompletion"" integer;
ALTER TABLE ""RuntimeSettings"" ADD COLUMN IF NOT EXISTS ""AiToolFeatureGrantCacheSeconds"" integer;
ALTER TABLE ""RuntimeSettings"" ADD COLUMN IF NOT EXISTS ""AiToolAllowedExternalHostsCsv"" character varying(1024);
ALTER TABLE ""RuntimeSettings"" ADD COLUMN IF NOT EXISTS ""AiToolExternalNetworkPerUserDailyCalls"" integer;
ALTER TABLE ""RuntimeSettings"" ADD COLUMN IF NOT EXISTS ""AiToolExternalNetworkTimeoutMilliseconds"" integer;
ALTER TABLE ""RuntimeSettings"" ADD COLUMN IF NOT EXISTS ""AiToolExternalNetworkMaxResponseBytes"" integer;

ALTER TABLE ""RuntimeSettings"" ADD COLUMN IF NOT EXISTS ""WritingCronsEnabled"" boolean;
ALTER TABLE ""RuntimeSettings"" ADD COLUMN IF NOT EXISTS ""WritingCoachEnabled"" boolean;
ALTER TABLE ""RuntimeSettings"" ADD COLUMN IF NOT EXISTS ""WritingCoachDailyCostCapPerLearnerUsd"" numeric(10,2);
ALTER TABLE ""RuntimeSettings"" ADD COLUMN IF NOT EXISTS ""WritingCoachMaxHintsPerSession"" integer;
ALTER TABLE ""RuntimeSettings"" ADD COLUMN IF NOT EXISTS ""WritingCoachMinSecondsBetweenHints"" integer;
ALTER TABLE ""RuntimeSettings"" ADD COLUMN IF NOT EXISTS ""WritingGcvApiKeyEncrypted"" text;
ALTER TABLE ""RuntimeSettings"" ADD COLUMN IF NOT EXISTS ""WritingOcrEnabled"" boolean;
ALTER TABLE ""RuntimeSettings"" ADD COLUMN IF NOT EXISTS ""WritingAppealsEnabled"" boolean;
ALTER TABLE ""RuntimeSettings"" ADD COLUMN IF NOT EXISTS ""WritingTutorReviewQueueMaxDepth"" integer;
ALTER TABLE ""RuntimeSettings"" ADD COLUMN IF NOT EXISTS ""WritingTutorReviewMaxWaitHours"" integer;
ALTER TABLE ""RuntimeSettings"" ADD COLUMN IF NOT EXISTS ""WritingMaxDailyPlanRegenerationsPerDay"" integer;
ALTER TABLE ""RuntimeSettings"" ADD COLUMN IF NOT EXISTS ""WritingGradeIdempotencyTtlHours"" integer;

ALTER TABLE ""RuntimeSettings"" ADD COLUMN IF NOT EXISTS ""PublicApiBaseUrl"" character varying(1024);
ALTER TABLE ""RuntimeSettings"" ADD COLUMN IF NOT EXISTS ""PublicWebBaseUrl"" character varying(1024);
ALTER TABLE ""RuntimeSettings"" ADD COLUMN IF NOT EXISTS ""FallbackEmailDomain"" character varying(256);

-- Messaging (Twilio SMS / WhatsApp Business Cloud — Wave 3)
ALTER TABLE ""RuntimeSettings"" ADD COLUMN IF NOT EXISTS ""TwilioEnabled"" boolean;
ALTER TABLE ""RuntimeSettings"" ADD COLUMN IF NOT EXISTS ""TwilioApiBaseUrl"" character varying(512);
ALTER TABLE ""RuntimeSettings"" ADD COLUMN IF NOT EXISTS ""TwilioAccountSid"" character varying(256);
ALTER TABLE ""RuntimeSettings"" ADD COLUMN IF NOT EXISTS ""TwilioAuthTokenEncrypted"" text;
ALTER TABLE ""RuntimeSettings"" ADD COLUMN IF NOT EXISTS ""TwilioFromNumber"" character varying(32);
ALTER TABLE ""RuntimeSettings"" ADD COLUMN IF NOT EXISTS ""TwilioMessagingServiceSid"" character varying(256);
ALTER TABLE ""RuntimeSettings"" ADD COLUMN IF NOT EXISTS ""WhatsAppEnabled"" boolean;
ALTER TABLE ""RuntimeSettings"" ADD COLUMN IF NOT EXISTS ""WhatsAppApiBaseUrl"" character varying(512);
ALTER TABLE ""RuntimeSettings"" ADD COLUMN IF NOT EXISTS ""WhatsAppAccessTokenEncrypted"" text;
ALTER TABLE ""RuntimeSettings"" ADD COLUMN IF NOT EXISTS ""WhatsAppPhoneNumberId"" character varying(256);
ALTER TABLE ""RuntimeSettings"" ADD COLUMN IF NOT EXISTS ""WhatsAppFallbackTemplateName"" character varying(256);

-- FX / Currency (Wave 4)
ALTER TABLE ""RuntimeSettings"" ADD COLUMN IF NOT EXISTS ""FxBaseCurrency"" character varying(3);
ALTER TABLE ""RuntimeSettings"" ADD COLUMN IF NOT EXISTS ""FxApiKeyEncrypted"" text;
ALTER TABLE ""RuntimeSettings"" ADD COLUMN IF NOT EXISTS ""FxApiBaseUrl"" character varying(512);
ALTER TABLE ""RuntimeSettings"" ADD COLUMN IF NOT EXISTS ""FxDynamicPricingEnabled"" boolean;

-- Billing Core (non-gateway — Wave 4)
ALTER TABLE ""RuntimeSettings"" ADD COLUMN IF NOT EXISTS ""BillingCheckoutBaseUrl"" character varying(1024);
ALTER TABLE ""RuntimeSettings"" ADD COLUMN IF NOT EXISTS ""BillingWebhookMaxAgeSeconds"" integer;
ALTER TABLE ""RuntimeSettings"" ADD COLUMN IF NOT EXISTS ""BillingWebhookMaxAttempts"" integer;
ALTER TABLE ""RuntimeSettings"" ADD COLUMN IF NOT EXISTS ""BillingDefaultCurrency"" character varying(10);
ALTER TABLE ""RuntimeSettings"" ADD COLUMN IF NOT EXISTS ""BillingDefaultRegion"" character varying(64);
ALTER TABLE ""RuntimeSettings"" ADD COLUMN IF NOT EXISTS ""WalletCurrency"" character varying(10);
ALTER TABLE ""RuntimeSettings"" ADD COLUMN IF NOT EXISTS ""WalletTopUpTiersJson"" text;
ALTER TABLE ""RuntimeSettings"" ADD COLUMN IF NOT EXISTS ""PayPalUseSandbox"" boolean;
ALTER TABLE ""RuntimeSettings"" ADD COLUMN IF NOT EXISTS ""PayPalApiBaseUrl"" character varying(512);

-- Storage (S3 / object store — Wave 4)
ALTER TABLE ""RuntimeSettings"" ADD COLUMN IF NOT EXISTS ""StorageProvider"" character varying(32);
ALTER TABLE ""RuntimeSettings"" ADD COLUMN IF NOT EXISTS ""StorageBucketName"" character varying(256);
ALTER TABLE ""RuntimeSettings"" ADD COLUMN IF NOT EXISTS ""StorageEndpointUrl"" character varying(512);
ALTER TABLE ""RuntimeSettings"" ADD COLUMN IF NOT EXISTS ""StorageAccessKeyIdEncrypted"" text;
ALTER TABLE ""RuntimeSettings"" ADD COLUMN IF NOT EXISTS ""StorageSecretAccessKeyEncrypted"" text;
ALTER TABLE ""RuntimeSettings"" ADD COLUMN IF NOT EXISTS ""StorageAwsRegion"" character varying(64);
ALTER TABLE ""RuntimeSettings"" ADD COLUMN IF NOT EXISTS ""StorageSignedReadTtlSeconds"" integer;
ALTER TABLE ""RuntimeSettings"" ADD COLUMN IF NOT EXISTS ""StorageContentUploadMaxAudioBytes"" bigint;
ALTER TABLE ""RuntimeSettings"" ADD COLUMN IF NOT EXISTS ""StorageContentUploadMaxPdfBytes"" bigint;
ALTER TABLE ""RuntimeSettings"" ADD COLUMN IF NOT EXISTS ""StorageContentUploadMaxImageBytes"" bigint;
ALTER TABLE ""RuntimeSettings"" ADD COLUMN IF NOT EXISTS ""StorageContentUploadMaxZipBytes"" bigint;
ALTER TABLE ""RuntimeSettings"" ADD COLUMN IF NOT EXISTS ""StorageContentUploadMaxZipEntries"" integer;
ALTER TABLE ""RuntimeSettings"" ADD COLUMN IF NOT EXISTS ""StorageContentUploadMaxZipEntryBytes"" bigint;
ALTER TABLE ""RuntimeSettings"" ADD COLUMN IF NOT EXISTS ""StorageContentUploadMaxZipUncompressedBytes"" bigint;
ALTER TABLE ""RuntimeSettings"" ADD COLUMN IF NOT EXISTS ""StorageContentUploadMaxZipCompressionRatio"" double precision;
ALTER TABLE ""RuntimeSettings"" ADD COLUMN IF NOT EXISTS ""StorageContentUploadChunkSizeBytes"" bigint;
ALTER TABLE ""RuntimeSettings"" ADD COLUMN IF NOT EXISTS ""StorageContentUploadStagingTtlHours"" integer;

-- PDF Extraction & Pronunciation (Wave 4)
ALTER TABLE ""RuntimeSettings"" ADD COLUMN IF NOT EXISTS ""PdfExtractionProvider"" character varying(32);
ALTER TABLE ""RuntimeSettings"" ADD COLUMN IF NOT EXISTS ""PdfExtractionAzureEndpoint"" character varying(512);
ALTER TABLE ""RuntimeSettings"" ADD COLUMN IF NOT EXISTS ""PdfExtractionAzureApiKeyEncrypted"" text;
ALTER TABLE ""RuntimeSettings"" ADD COLUMN IF NOT EXISTS ""PdfExtractionMinTextLengthForSuccess"" integer;
ALTER TABLE ""RuntimeSettings"" ADD COLUMN IF NOT EXISTS ""PronunciationProvider"" character varying(32);
ALTER TABLE ""RuntimeSettings"" ADD COLUMN IF NOT EXISTS ""PronunciationAzureSpeechRegion"" character varying(64);
ALTER TABLE ""RuntimeSettings"" ADD COLUMN IF NOT EXISTS ""PronunciationAzureLocale"" character varying(16);
ALTER TABLE ""RuntimeSettings"" ADD COLUMN IF NOT EXISTS ""PronunciationWhisperBaseUrl"" character varying(512);
ALTER TABLE ""RuntimeSettings"" ADD COLUMN IF NOT EXISTS ""PronunciationWhisperModel"" character varying(64);
ALTER TABLE ""RuntimeSettings"" ADD COLUMN IF NOT EXISTS ""PronunciationGeminiBaseUrl"" character varying(512);
ALTER TABLE ""RuntimeSettings"" ADD COLUMN IF NOT EXISTS ""PronunciationGeminiModel"" character varying(64);
ALTER TABLE ""RuntimeSettings"" ADD COLUMN IF NOT EXISTS ""PronunciationMaxAudioBytes"" bigint;
ALTER TABLE ""RuntimeSettings"" ADD COLUMN IF NOT EXISTS ""PronunciationAudioRetentionDays"" integer;
ALTER TABLE ""RuntimeSettings"" ADD COLUMN IF NOT EXISTS ""PronunciationFreeTierWeeklyAttemptLimit"" integer;
ALTER TABLE ""RuntimeSettings"" ADD COLUMN IF NOT EXISTS ""PronunciationFreeTierWindowDays"" integer;

-- Auth — External providers (LinkedIn) + per-provider toggles (Wave 4)
ALTER TABLE ""RuntimeSettings"" ADD COLUMN IF NOT EXISTS ""LinkedInClientIdEncrypted"" text;
ALTER TABLE ""RuntimeSettings"" ADD COLUMN IF NOT EXISTS ""LinkedInClientSecretEncrypted"" text;
ALTER TABLE ""RuntimeSettings"" ADD COLUMN IF NOT EXISTS ""LinkedInEnabled"" boolean;
ALTER TABLE ""RuntimeSettings"" ADD COLUMN IF NOT EXISTS ""GoogleAuthEnabled"" boolean;
ALTER TABLE ""RuntimeSettings"" ADD COLUMN IF NOT EXISTS ""FacebookAuthEnabled"" boolean;

-- Auth tokens (safe AuthTokenOptions subset — Wave 4)
ALTER TABLE ""RuntimeSettings"" ADD COLUMN IF NOT EXISTS ""AuthTokenAccessTokenLifetimeSeconds"" integer;
ALTER TABLE ""RuntimeSettings"" ADD COLUMN IF NOT EXISTS ""AuthTokenRefreshTokenLifetimeSeconds"" integer;
ALTER TABLE ""RuntimeSettings"" ADD COLUMN IF NOT EXISTS ""AuthTokenOtpLifetimeSeconds"" integer;
ALTER TABLE ""RuntimeSettings"" ADD COLUMN IF NOT EXISTS ""AuthTokenAuthenticatorIssuer"" character varying(512);

-- Web push enablement (Wave 4)
ALTER TABLE ""RuntimeSettings"" ADD COLUMN IF NOT EXISTS ""WebPushEnabled"" boolean;

-- Catalog storefront presentation (admin CMS) -- sync with migration 20260708000000
ALTER TABLE ""RuntimeSettings"" ADD COLUMN IF NOT EXISTS ""CatalogPresentationJson"" text;

-- Bunny Stream (Video Library) + playback attestation -- sync with migration 20260718090000
ALTER TABLE ""RuntimeSettings"" ADD COLUMN IF NOT EXISTS ""BunnyStreamLibraryId"" character varying(32);
ALTER TABLE ""RuntimeSettings"" ADD COLUMN IF NOT EXISTS ""BunnyStreamApiKeyEncrypted"" text;
ALTER TABLE ""RuntimeSettings"" ADD COLUMN IF NOT EXISTS ""BunnyStreamCdnHostname"" character varying(256);
ALTER TABLE ""RuntimeSettings"" ADD COLUMN IF NOT EXISTS ""BunnyStreamTokenAuthKeyEncrypted"" text;
ALTER TABLE ""RuntimeSettings"" ADD COLUMN IF NOT EXISTS ""BunnyStreamWebhookSecretEncrypted"" text;
ALTER TABLE ""RuntimeSettings"" ADD COLUMN IF NOT EXISTS ""BunnyStreamCollectionId"" character varying(64);
ALTER TABLE ""RuntimeSettings"" ADD COLUMN IF NOT EXISTS ""BunnyStreamPlaybackTokenTtlSeconds"" integer;
ALTER TABLE ""RuntimeSettings"" ADD COLUMN IF NOT EXISTS ""BunnyStreamEnabled"" boolean;
ALTER TABLE ""RuntimeSettings"" ADD COLUMN IF NOT EXISTS ""VideoAttestationKeysEncrypted"" text;
";
}
