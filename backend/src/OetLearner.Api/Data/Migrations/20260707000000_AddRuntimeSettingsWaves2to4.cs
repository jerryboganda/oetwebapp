using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OetLearner.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddRuntimeSettingsWaves2to4 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "AiAssistantBackupRetentionDays",
                table: "RuntimeSettings",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "AiAssistantCircuitBreakerFailureWindowSeconds",
                table: "RuntimeSettings",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "AiAssistantCircuitBreakerMaxFailures",
                table: "RuntimeSettings",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "AiAssistantCircuitBreakerMaxWrites",
                table: "RuntimeSettings",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "AiAssistantCircuitBreakerWriteWindowSeconds",
                table: "RuntimeSettings",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "AiAssistantCommandTimeoutSeconds",
                table: "RuntimeSettings",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AiAssistantEmbeddingModel",
                table: "RuntimeSettings",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "AiAssistantGlobalEnabled",
                table: "RuntimeSettings",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "AiAssistantMaxChunkTokens",
                table: "RuntimeSettings",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "AiAssistantMaxContextMessages",
                table: "RuntimeSettings",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "AiAssistantMaxIterations",
                table: "RuntimeSettings",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "AiAssistantMaxWriteFileSizeBytes",
                table: "RuntimeSettings",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "AiAssistantRequireApprovalAlways",
                table: "RuntimeSettings",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AiProviderBaseUrl",
                table: "RuntimeSettings",
                type: "character varying(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "AiProviderDefaultMaxTokens",
                table: "RuntimeSettings",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AiProviderDefaultModel",
                table: "RuntimeSettings",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "AiProviderDefaultTemperature",
                table: "RuntimeSettings",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AiProviderProviderId",
                table: "RuntimeSettings",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AiProviderReasoningEffort",
                table: "RuntimeSettings",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AiToolAllowedExternalHostsCsv",
                table: "RuntimeSettings",
                type: "character varying(1024)",
                maxLength: 1024,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "AiToolExternalNetworkMaxResponseBytes",
                table: "RuntimeSettings",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "AiToolExternalNetworkPerUserDailyCalls",
                table: "RuntimeSettings",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "AiToolExternalNetworkTimeoutMilliseconds",
                table: "RuntimeSettings",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "AiToolFeatureGrantCacheSeconds",
                table: "RuntimeSettings",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "AiToolMaxToolCallsPerCompletion",
                table: "RuntimeSettings",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "AuthTokenAccessTokenLifetimeSeconds",
                table: "RuntimeSettings",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AuthTokenAuthenticatorIssuer",
                table: "RuntimeSettings",
                type: "character varying(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "AuthTokenOtpLifetimeSeconds",
                table: "RuntimeSettings",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "AuthTokenRefreshTokenLifetimeSeconds",
                table: "RuntimeSettings",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BillingCheckoutBaseUrl",
                table: "RuntimeSettings",
                type: "character varying(1024)",
                maxLength: 1024,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BillingDefaultCurrency",
                table: "RuntimeSettings",
                type: "character varying(10)",
                maxLength: 10,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BillingDefaultRegion",
                table: "RuntimeSettings",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "BillingWebhookMaxAgeSeconds",
                table: "RuntimeSettings",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "BillingWebhookMaxAttempts",
                table: "RuntimeSettings",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "BrevoAdminInviteTemplateId",
                table: "RuntimeSettings",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "BrevoEnabled",
                table: "RuntimeSettings",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "BrevoMfaEnabledTemplateId",
                table: "RuntimeSettings",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "BrevoPasswordChangedTemplateId",
                table: "RuntimeSettings",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "BrevoReviewCompletedTemplateId",
                table: "RuntimeSettings",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "BrevoSecurityAlertTemplateId",
                table: "RuntimeSettings",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BrevoWebhookSecretEncrypted",
                table: "RuntimeSettings",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "BrevoWelcomeTemplateId",
                table: "RuntimeSettings",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "FacebookAuthEnabled",
                table: "RuntimeSettings",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FallbackEmailDomain",
                table: "RuntimeSettings",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FxApiBaseUrl",
                table: "RuntimeSettings",
                type: "character varying(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FxApiKeyEncrypted",
                table: "RuntimeSettings",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FxBaseCurrency",
                table: "RuntimeSettings",
                type: "character varying(3)",
                maxLength: 3,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "FxDynamicPricingEnabled",
                table: "RuntimeSettings",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "GoogleAuthEnabled",
                table: "RuntimeSettings",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LinkedInClientIdEncrypted",
                table: "RuntimeSettings",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LinkedInClientSecretEncrypted",
                table: "RuntimeSettings",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "LinkedInEnabled",
                table: "RuntimeSettings",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PayPalApiBaseUrl",
                table: "RuntimeSettings",
                type: "character varying(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "PayPalUseSandbox",
                table: "RuntimeSettings",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PdfExtractionAzureApiKeyEncrypted",
                table: "RuntimeSettings",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PdfExtractionAzureEndpoint",
                table: "RuntimeSettings",
                type: "character varying(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PdfExtractionMinTextLengthForSuccess",
                table: "RuntimeSettings",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PdfExtractionProvider",
                table: "RuntimeSettings",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PronunciationAudioRetentionDays",
                table: "RuntimeSettings",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PronunciationAzureLocale",
                table: "RuntimeSettings",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PronunciationAzureSpeechRegion",
                table: "RuntimeSettings",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PronunciationFreeTierWeeklyAttemptLimit",
                table: "RuntimeSettings",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PronunciationFreeTierWindowDays",
                table: "RuntimeSettings",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PronunciationGeminiBaseUrl",
                table: "RuntimeSettings",
                type: "character varying(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PronunciationGeminiModel",
                table: "RuntimeSettings",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "PronunciationMaxAudioBytes",
                table: "RuntimeSettings",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PronunciationProvider",
                table: "RuntimeSettings",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PronunciationWhisperBaseUrl",
                table: "RuntimeSettings",
                type: "character varying(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PronunciationWhisperModel",
                table: "RuntimeSettings",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PublicApiBaseUrl",
                table: "RuntimeSettings",
                type: "character varying(1024)",
                maxLength: 1024,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PublicWebBaseUrl",
                table: "RuntimeSettings",
                type: "character varying(1024)",
                maxLength: 1024,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "SmtpEnableSsl",
                table: "RuntimeSettings",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "SmtpEnabled",
                table: "RuntimeSettings",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "StorageAccessKeyIdEncrypted",
                table: "RuntimeSettings",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "StorageAwsRegion",
                table: "RuntimeSettings",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "StorageBucketName",
                table: "RuntimeSettings",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "StorageContentUploadChunkSizeBytes",
                table: "RuntimeSettings",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "StorageContentUploadMaxAudioBytes",
                table: "RuntimeSettings",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "StorageContentUploadMaxImageBytes",
                table: "RuntimeSettings",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "StorageContentUploadMaxPdfBytes",
                table: "RuntimeSettings",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "StorageContentUploadMaxZipBytes",
                table: "RuntimeSettings",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "StorageContentUploadMaxZipCompressionRatio",
                table: "RuntimeSettings",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "StorageContentUploadMaxZipEntries",
                table: "RuntimeSettings",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "StorageContentUploadMaxZipEntryBytes",
                table: "RuntimeSettings",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "StorageContentUploadMaxZipUncompressedBytes",
                table: "RuntimeSettings",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "StorageContentUploadStagingTtlHours",
                table: "RuntimeSettings",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "StorageEndpointUrl",
                table: "RuntimeSettings",
                type: "character varying(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "StorageProvider",
                table: "RuntimeSettings",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "StorageSecretAccessKeyEncrypted",
                table: "RuntimeSettings",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "StorageSignedReadTtlSeconds",
                table: "RuntimeSettings",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TwilioAccountSid",
                table: "RuntimeSettings",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TwilioApiBaseUrl",
                table: "RuntimeSettings",
                type: "character varying(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TwilioAuthTokenEncrypted",
                table: "RuntimeSettings",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "TwilioEnabled",
                table: "RuntimeSettings",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TwilioFromNumber",
                table: "RuntimeSettings",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TwilioMessagingServiceSid",
                table: "RuntimeSettings",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "WalletCurrency",
                table: "RuntimeSettings",
                type: "character varying(10)",
                maxLength: 10,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "WalletTopUpTiersJson",
                table: "RuntimeSettings",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "WebPushEnabled",
                table: "RuntimeSettings",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "WhatsAppAccessTokenEncrypted",
                table: "RuntimeSettings",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "WhatsAppApiBaseUrl",
                table: "RuntimeSettings",
                type: "character varying(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "WhatsAppEnabled",
                table: "RuntimeSettings",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "WhatsAppFallbackTemplateName",
                table: "RuntimeSettings",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "WhatsAppPhoneNumberId",
                table: "RuntimeSettings",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "WritingAppealsEnabled",
                table: "RuntimeSettings",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "WritingCoachDailyCostCapPerLearnerUsd",
                table: "RuntimeSettings",
                type: "numeric(10,2)",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "WritingCoachEnabled",
                table: "RuntimeSettings",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "WritingCoachMaxHintsPerSession",
                table: "RuntimeSettings",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "WritingCoachMinSecondsBetweenHints",
                table: "RuntimeSettings",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "WritingCronsEnabled",
                table: "RuntimeSettings",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "WritingGcvApiKeyEncrypted",
                table: "RuntimeSettings",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "WritingGradeIdempotencyTtlHours",
                table: "RuntimeSettings",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "WritingMaxDailyPlanRegenerationsPerDay",
                table: "RuntimeSettings",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "WritingOcrEnabled",
                table: "RuntimeSettings",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "WritingTutorReviewMaxWaitHours",
                table: "RuntimeSettings",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "WritingTutorReviewQueueMaxDepth",
                table: "RuntimeSettings",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AiAssistantBackupRetentionDays",
                table: "RuntimeSettings");

            migrationBuilder.DropColumn(
                name: "AiAssistantCircuitBreakerFailureWindowSeconds",
                table: "RuntimeSettings");

            migrationBuilder.DropColumn(
                name: "AiAssistantCircuitBreakerMaxFailures",
                table: "RuntimeSettings");

            migrationBuilder.DropColumn(
                name: "AiAssistantCircuitBreakerMaxWrites",
                table: "RuntimeSettings");

            migrationBuilder.DropColumn(
                name: "AiAssistantCircuitBreakerWriteWindowSeconds",
                table: "RuntimeSettings");

            migrationBuilder.DropColumn(
                name: "AiAssistantCommandTimeoutSeconds",
                table: "RuntimeSettings");

            migrationBuilder.DropColumn(
                name: "AiAssistantEmbeddingModel",
                table: "RuntimeSettings");

            migrationBuilder.DropColumn(
                name: "AiAssistantGlobalEnabled",
                table: "RuntimeSettings");

            migrationBuilder.DropColumn(
                name: "AiAssistantMaxChunkTokens",
                table: "RuntimeSettings");

            migrationBuilder.DropColumn(
                name: "AiAssistantMaxContextMessages",
                table: "RuntimeSettings");

            migrationBuilder.DropColumn(
                name: "AiAssistantMaxIterations",
                table: "RuntimeSettings");

            migrationBuilder.DropColumn(
                name: "AiAssistantMaxWriteFileSizeBytes",
                table: "RuntimeSettings");

            migrationBuilder.DropColumn(
                name: "AiAssistantRequireApprovalAlways",
                table: "RuntimeSettings");

            migrationBuilder.DropColumn(
                name: "AiProviderBaseUrl",
                table: "RuntimeSettings");

            migrationBuilder.DropColumn(
                name: "AiProviderDefaultMaxTokens",
                table: "RuntimeSettings");

            migrationBuilder.DropColumn(
                name: "AiProviderDefaultModel",
                table: "RuntimeSettings");

            migrationBuilder.DropColumn(
                name: "AiProviderDefaultTemperature",
                table: "RuntimeSettings");

            migrationBuilder.DropColumn(
                name: "AiProviderProviderId",
                table: "RuntimeSettings");

            migrationBuilder.DropColumn(
                name: "AiProviderReasoningEffort",
                table: "RuntimeSettings");

            migrationBuilder.DropColumn(
                name: "AiToolAllowedExternalHostsCsv",
                table: "RuntimeSettings");

            migrationBuilder.DropColumn(
                name: "AiToolExternalNetworkMaxResponseBytes",
                table: "RuntimeSettings");

            migrationBuilder.DropColumn(
                name: "AiToolExternalNetworkPerUserDailyCalls",
                table: "RuntimeSettings");

            migrationBuilder.DropColumn(
                name: "AiToolExternalNetworkTimeoutMilliseconds",
                table: "RuntimeSettings");

            migrationBuilder.DropColumn(
                name: "AiToolFeatureGrantCacheSeconds",
                table: "RuntimeSettings");

            migrationBuilder.DropColumn(
                name: "AiToolMaxToolCallsPerCompletion",
                table: "RuntimeSettings");

            migrationBuilder.DropColumn(
                name: "AuthTokenAccessTokenLifetimeSeconds",
                table: "RuntimeSettings");

            migrationBuilder.DropColumn(
                name: "AuthTokenAuthenticatorIssuer",
                table: "RuntimeSettings");

            migrationBuilder.DropColumn(
                name: "AuthTokenOtpLifetimeSeconds",
                table: "RuntimeSettings");

            migrationBuilder.DropColumn(
                name: "AuthTokenRefreshTokenLifetimeSeconds",
                table: "RuntimeSettings");

            migrationBuilder.DropColumn(
                name: "BillingCheckoutBaseUrl",
                table: "RuntimeSettings");

            migrationBuilder.DropColumn(
                name: "BillingDefaultCurrency",
                table: "RuntimeSettings");

            migrationBuilder.DropColumn(
                name: "BillingDefaultRegion",
                table: "RuntimeSettings");

            migrationBuilder.DropColumn(
                name: "BillingWebhookMaxAgeSeconds",
                table: "RuntimeSettings");

            migrationBuilder.DropColumn(
                name: "BillingWebhookMaxAttempts",
                table: "RuntimeSettings");

            migrationBuilder.DropColumn(
                name: "BrevoAdminInviteTemplateId",
                table: "RuntimeSettings");

            migrationBuilder.DropColumn(
                name: "BrevoEnabled",
                table: "RuntimeSettings");

            migrationBuilder.DropColumn(
                name: "BrevoMfaEnabledTemplateId",
                table: "RuntimeSettings");

            migrationBuilder.DropColumn(
                name: "BrevoPasswordChangedTemplateId",
                table: "RuntimeSettings");

            migrationBuilder.DropColumn(
                name: "BrevoReviewCompletedTemplateId",
                table: "RuntimeSettings");

            migrationBuilder.DropColumn(
                name: "BrevoSecurityAlertTemplateId",
                table: "RuntimeSettings");

            migrationBuilder.DropColumn(
                name: "BrevoWebhookSecretEncrypted",
                table: "RuntimeSettings");

            migrationBuilder.DropColumn(
                name: "BrevoWelcomeTemplateId",
                table: "RuntimeSettings");

            migrationBuilder.DropColumn(
                name: "FacebookAuthEnabled",
                table: "RuntimeSettings");

            migrationBuilder.DropColumn(
                name: "FallbackEmailDomain",
                table: "RuntimeSettings");

            migrationBuilder.DropColumn(
                name: "FxApiBaseUrl",
                table: "RuntimeSettings");

            migrationBuilder.DropColumn(
                name: "FxApiKeyEncrypted",
                table: "RuntimeSettings");

            migrationBuilder.DropColumn(
                name: "FxBaseCurrency",
                table: "RuntimeSettings");

            migrationBuilder.DropColumn(
                name: "FxDynamicPricingEnabled",
                table: "RuntimeSettings");

            migrationBuilder.DropColumn(
                name: "GoogleAuthEnabled",
                table: "RuntimeSettings");

            migrationBuilder.DropColumn(
                name: "LinkedInClientIdEncrypted",
                table: "RuntimeSettings");

            migrationBuilder.DropColumn(
                name: "LinkedInClientSecretEncrypted",
                table: "RuntimeSettings");

            migrationBuilder.DropColumn(
                name: "LinkedInEnabled",
                table: "RuntimeSettings");

            migrationBuilder.DropColumn(
                name: "PayPalApiBaseUrl",
                table: "RuntimeSettings");

            migrationBuilder.DropColumn(
                name: "PayPalUseSandbox",
                table: "RuntimeSettings");

            migrationBuilder.DropColumn(
                name: "PdfExtractionAzureApiKeyEncrypted",
                table: "RuntimeSettings");

            migrationBuilder.DropColumn(
                name: "PdfExtractionAzureEndpoint",
                table: "RuntimeSettings");

            migrationBuilder.DropColumn(
                name: "PdfExtractionMinTextLengthForSuccess",
                table: "RuntimeSettings");

            migrationBuilder.DropColumn(
                name: "PdfExtractionProvider",
                table: "RuntimeSettings");

            migrationBuilder.DropColumn(
                name: "PronunciationAudioRetentionDays",
                table: "RuntimeSettings");

            migrationBuilder.DropColumn(
                name: "PronunciationAzureLocale",
                table: "RuntimeSettings");

            migrationBuilder.DropColumn(
                name: "PronunciationAzureSpeechRegion",
                table: "RuntimeSettings");

            migrationBuilder.DropColumn(
                name: "PronunciationFreeTierWeeklyAttemptLimit",
                table: "RuntimeSettings");

            migrationBuilder.DropColumn(
                name: "PronunciationFreeTierWindowDays",
                table: "RuntimeSettings");

            migrationBuilder.DropColumn(
                name: "PronunciationGeminiBaseUrl",
                table: "RuntimeSettings");

            migrationBuilder.DropColumn(
                name: "PronunciationGeminiModel",
                table: "RuntimeSettings");

            migrationBuilder.DropColumn(
                name: "PronunciationMaxAudioBytes",
                table: "RuntimeSettings");

            migrationBuilder.DropColumn(
                name: "PronunciationProvider",
                table: "RuntimeSettings");

            migrationBuilder.DropColumn(
                name: "PronunciationWhisperBaseUrl",
                table: "RuntimeSettings");

            migrationBuilder.DropColumn(
                name: "PronunciationWhisperModel",
                table: "RuntimeSettings");

            migrationBuilder.DropColumn(
                name: "PublicApiBaseUrl",
                table: "RuntimeSettings");

            migrationBuilder.DropColumn(
                name: "PublicWebBaseUrl",
                table: "RuntimeSettings");

            migrationBuilder.DropColumn(
                name: "SmtpEnableSsl",
                table: "RuntimeSettings");

            migrationBuilder.DropColumn(
                name: "SmtpEnabled",
                table: "RuntimeSettings");

            migrationBuilder.DropColumn(
                name: "StorageAccessKeyIdEncrypted",
                table: "RuntimeSettings");

            migrationBuilder.DropColumn(
                name: "StorageAwsRegion",
                table: "RuntimeSettings");

            migrationBuilder.DropColumn(
                name: "StorageBucketName",
                table: "RuntimeSettings");

            migrationBuilder.DropColumn(
                name: "StorageContentUploadChunkSizeBytes",
                table: "RuntimeSettings");

            migrationBuilder.DropColumn(
                name: "StorageContentUploadMaxAudioBytes",
                table: "RuntimeSettings");

            migrationBuilder.DropColumn(
                name: "StorageContentUploadMaxImageBytes",
                table: "RuntimeSettings");

            migrationBuilder.DropColumn(
                name: "StorageContentUploadMaxPdfBytes",
                table: "RuntimeSettings");

            migrationBuilder.DropColumn(
                name: "StorageContentUploadMaxZipBytes",
                table: "RuntimeSettings");

            migrationBuilder.DropColumn(
                name: "StorageContentUploadMaxZipCompressionRatio",
                table: "RuntimeSettings");

            migrationBuilder.DropColumn(
                name: "StorageContentUploadMaxZipEntries",
                table: "RuntimeSettings");

            migrationBuilder.DropColumn(
                name: "StorageContentUploadMaxZipEntryBytes",
                table: "RuntimeSettings");

            migrationBuilder.DropColumn(
                name: "StorageContentUploadMaxZipUncompressedBytes",
                table: "RuntimeSettings");

            migrationBuilder.DropColumn(
                name: "StorageContentUploadStagingTtlHours",
                table: "RuntimeSettings");

            migrationBuilder.DropColumn(
                name: "StorageEndpointUrl",
                table: "RuntimeSettings");

            migrationBuilder.DropColumn(
                name: "StorageProvider",
                table: "RuntimeSettings");

            migrationBuilder.DropColumn(
                name: "StorageSecretAccessKeyEncrypted",
                table: "RuntimeSettings");

            migrationBuilder.DropColumn(
                name: "StorageSignedReadTtlSeconds",
                table: "RuntimeSettings");

            migrationBuilder.DropColumn(
                name: "TwilioAccountSid",
                table: "RuntimeSettings");

            migrationBuilder.DropColumn(
                name: "TwilioApiBaseUrl",
                table: "RuntimeSettings");

            migrationBuilder.DropColumn(
                name: "TwilioAuthTokenEncrypted",
                table: "RuntimeSettings");

            migrationBuilder.DropColumn(
                name: "TwilioEnabled",
                table: "RuntimeSettings");

            migrationBuilder.DropColumn(
                name: "TwilioFromNumber",
                table: "RuntimeSettings");

            migrationBuilder.DropColumn(
                name: "TwilioMessagingServiceSid",
                table: "RuntimeSettings");

            migrationBuilder.DropColumn(
                name: "WalletCurrency",
                table: "RuntimeSettings");

            migrationBuilder.DropColumn(
                name: "WalletTopUpTiersJson",
                table: "RuntimeSettings");

            migrationBuilder.DropColumn(
                name: "WebPushEnabled",
                table: "RuntimeSettings");

            migrationBuilder.DropColumn(
                name: "WhatsAppAccessTokenEncrypted",
                table: "RuntimeSettings");

            migrationBuilder.DropColumn(
                name: "WhatsAppApiBaseUrl",
                table: "RuntimeSettings");

            migrationBuilder.DropColumn(
                name: "WhatsAppEnabled",
                table: "RuntimeSettings");

            migrationBuilder.DropColumn(
                name: "WhatsAppFallbackTemplateName",
                table: "RuntimeSettings");

            migrationBuilder.DropColumn(
                name: "WhatsAppPhoneNumberId",
                table: "RuntimeSettings");

            migrationBuilder.DropColumn(
                name: "WritingAppealsEnabled",
                table: "RuntimeSettings");

            migrationBuilder.DropColumn(
                name: "WritingCoachDailyCostCapPerLearnerUsd",
                table: "RuntimeSettings");

            migrationBuilder.DropColumn(
                name: "WritingCoachEnabled",
                table: "RuntimeSettings");

            migrationBuilder.DropColumn(
                name: "WritingCoachMaxHintsPerSession",
                table: "RuntimeSettings");

            migrationBuilder.DropColumn(
                name: "WritingCoachMinSecondsBetweenHints",
                table: "RuntimeSettings");

            migrationBuilder.DropColumn(
                name: "WritingCronsEnabled",
                table: "RuntimeSettings");

            migrationBuilder.DropColumn(
                name: "WritingGcvApiKeyEncrypted",
                table: "RuntimeSettings");

            migrationBuilder.DropColumn(
                name: "WritingGradeIdempotencyTtlHours",
                table: "RuntimeSettings");

            migrationBuilder.DropColumn(
                name: "WritingMaxDailyPlanRegenerationsPerDay",
                table: "RuntimeSettings");

            migrationBuilder.DropColumn(
                name: "WritingOcrEnabled",
                table: "RuntimeSettings");

            migrationBuilder.DropColumn(
                name: "WritingTutorReviewMaxWaitHours",
                table: "RuntimeSettings");

            migrationBuilder.DropColumn(
                name: "WritingTutorReviewQueueMaxDepth",
                table: "RuntimeSettings");
        }
    }
}
