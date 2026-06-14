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
";
}
