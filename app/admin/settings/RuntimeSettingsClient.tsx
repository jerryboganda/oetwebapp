'use client';

import { useCallback, useEffect, useId, useMemo, useState, type ReactNode } from 'react';
import { ChevronDown, ChevronRight, Eye, EyeOff, Lock, Save } from 'lucide-react';
import { Toast } from '@/components/ui/alert';
import { apiClient } from '@/lib/api';
import { useAuth } from '@/contexts/auth-context';
import { AdminPermission, hasPermission } from '@/lib/admin-permissions';
// New admin DS imports
import { AdminSettingsLayout, SettingsNav } from '@/components/admin/layout/admin-settings-layout';
import { Badge } from '@/components/admin/ui/badge';
import { Button } from '@/components/admin/ui/button';
import { Card, CardContent, CardHeader, CardTitle, CardDescription } from '@/components/admin/ui/card';
import { Skeleton } from '@/components/admin/ui/skeleton';
import { EmptyState } from '@/components/admin/ui/empty-state';

/* ───────────────────────── Types ───────────────────────── */

const MASKED = '********' as const;

export interface EmailSettings {
  brevoApiKey: string;
  brevoEmailVerificationTemplateId: number | null;
  brevoPasswordResetTemplateId: number | null;
  smtpHost: string;
  smtpPort: number | null;
  smtpUsername: string;
  smtpPassword: string;
  smtpFromAddress: string;
  smtpFromName: string;
  // ── Email partial-coverage gap (Wave 3) ──
  brevoWelcomeTemplateId: number | null;
  brevoPasswordChangedTemplateId: number | null;
  brevoMfaEnabledTemplateId: number | null;
  brevoAdminInviteTemplateId: number | null;
  brevoSecurityAlertTemplateId: number | null;
  brevoReviewCompletedTemplateId: number | null;
  brevoWebhookSecret: string;
  brevoEnabled: boolean | null;
  smtpEnabled: boolean | null;
  smtpEnableSsl: boolean | null;
}

export interface BillingSettings {
  stripeSecretKey: string;
  stripePublishableKey: string;
  stripeWebhookSecret: string;
  stripeSuccessUrl: string;
  stripeCancelUrl: string;
  publicAppBaseUrl: string;
  paypalClientId: string;
  paypalClientSecret: string;
  paypalWebhookId: string;
  paypalSuccessUrl: string;
  paypalCancelUrl: string;
  paypalAdvancedCardsEnabled: boolean | null;
}

export interface SentrySettings {
  dsn: string;
  environment: string;
  sampleRate: number | null;
}

export interface BackupSettings {
  s3Url: string;
  awsAccessKeyId: string;
  awsSecretAccessKey: string;
  gpgPassphrase: string;
  alertWebhook: string;
}

export interface OAuthSettings {
  googleClientId: string;
  googleClientSecret: string;
  appleClientId: string;
  appleTeamId: string;
  appleKeyId: string;
  applePrivateKey: string;
  facebookAppId: string;
  facebookAppSecret: string;
  // ── Auth external providers (Wave 4) — LinkedIn + per-provider toggles ──
  linkedInClientId: string;
  linkedInClientSecret: string;
  linkedInEnabled: boolean | null;
  googleAuthEnabled: boolean | null;
  facebookAuthEnabled: boolean | null;
}

export interface PushSettings {
  apnsKeyId: string;
  apnsTeamId: string;
  apnsBundleId: string;
  apnsAuthKey: string;
  fcmServerKey: string;
  fcmProjectId: string;
  vapidSubject: string;
  vapidPublicKey: string;
  vapidPrivateKey: string;
}

export interface UploadScannerSettings {
  provider: string;
  host: string;
  port: number | null;
  timeoutSeconds: number | null;
  failClosedOnError: boolean | null;
}

export interface ZoomSettings {
  enabled: boolean | null;
  accountId: string;
  clientId: string;
  clientSecret: string;
  apiBaseUrl: string;
  tokenUrl: string;
  hostUserId: string;
  meetingSdkKey: string;
  meetingSdkSecret: string;
  webhookSecretToken: string;
  webhookRetryToleranceSeconds: number | null;
  allowSandboxFallback: boolean | null;
}

/**
 * 2026-05-28 audit fix — Speaking Whisper transcription configuration.
 * Drives the RULE_40 tone pipeline. apiKey is stored encrypted server-side.
 */
export interface SpeakingWhisperSettings {
  apiKey: string;
  baseUrl: string;
  model: string;
  isConfigured?: boolean | null;
}

export interface SpeakingLiveKitSettings {
  provider: string;
  apiKey: string;
  apiSecret: string;
  wssUrl: string;
  webhookSigningSecret: string;
  egressBucket: string;
  defaultMaxDurationSeconds: number | null;
  egressEnabled: boolean | null;
  isEnabled?: boolean | null;
}

export interface SpeakingAiSettings {
  anthropicApiKey: string;
  elevenLabsApiKey: string;
  isAnthropicConfigured?: boolean | null;
  isElevenLabsConfigured?: boolean | null;
}

export interface SpeakingStorageSettings {
  awsAccessKeyId: string;
  awsSecretAccessKey: string;
  region: string;
  bucket: string;
  isConfigured?: boolean | null;
}

export interface SpeakingComplianceSettingsData {
  currentConsentVersion: string;
  currentLiveVideoConsentVersion: string;
  retentionDaysDefault: number | null;
  retentionDaysWhenTutorReviewed: number | null;
  auditLogRetentionDays: number | null;
}

export interface SpeakingFeaturesSettings {
  speakingV2Enabled: boolean | null;
}

export interface CheckoutComSettings {
  apiBaseUrl: string;
  secretKey: string;
  publicKey: string;
  processingChannelId: string;
  webhookSecret: string;
  successUrl: string;
  cancelUrl: string;
  isConfigured?: boolean | null;
}

export interface BunnyStreamSettings {
  enabled: boolean | null;
  libraryId: string;
  apiKey: string;
  cdnHostname: string;
  tokenAuthKey: string;
  webhookSecret: string;
  collectionId: string;
  playbackTokenTtlSeconds: number | null;
  /** Write field sent on PUT: the raw JSON key map. */
  videoAttestationKeysJson: string;
  /** Read-only from GET: masked indicator that attestation keys are set. */
  videoAttestationKeys?: string;
  /** Read-only from GET: configured "platform:keyId" identifiers. */
  videoAttestationKeyIds?: string[];
  isConfigured?: boolean | null;
}

export interface PaymobSettings {
  apiBaseUrl: string;
  apiKey: string;
  merchantId: string;
  hmacSecret: string;
  integrationIdsJson: string;
  iframeId: number | null;
  successUrl: string;
  cancelUrl: string;
  isConfigured?: boolean | null;
}

export interface EasyKashSettings {
  apiBaseUrl: string;
  apiKey: string;
  hmacSecret: string;
  paymentOptionsCsv: string;
  currencyMode: string;
  successUrl: string;
  cancelUrl: string;
  isConfigured?: boolean | null;
}

export interface PayTabsSettings {
  apiBaseUrl: string;
  serverKey: string;
  profileId: string;
  webhookSecret: string;
  successUrl: string;
  cancelUrl: string;
  isConfigured?: boolean | null;
}

export interface SoketiSettings {
  host: string;
  port: number | null;
  appId: string;
  appKey: string;
  appSecret: string;
  useTls: boolean | null;
  enabled: boolean | null;
}

export interface DataRetentionSettings {
  analyticsEventsDays: number | null;
  auditEventsDays: number | null;
  paymentWebhookEventsDays: number | null;
  paymentWebhookPiiNullOutAgeDays: number | null;
  notificationDeliveryAttemptsDays: number | null;
  sweepIntervalHours: number | null;
  batchSize: number | null;
}

export interface ExpertAutoAssignmentSettings {
  enabled: boolean | null;
  pollingIntervalSeconds: number | null;
  slaEscalationIntervalSeconds: number | null;
  slaHoursStandard: number | null;
  slaHoursExpress: number | null;
  maxActiveAssignmentsPerExpert: number | null;
  lookbackHoursForLoad: number | null;
  batchSize: number | null;
}

export interface PasswordPolicySettings {
  minimumLength: number | null;
  requireMixedCase: boolean | null;
  requireDigit: boolean | null;
  requireSymbol: boolean | null;
  breachCheckEnabled: boolean | null;
  breachApiBaseUrl: string;
  breachApiTimeoutSeconds: number | null;
}

export interface AiAssistantSettings {
  globalEnabled: boolean | null;
  requireApprovalAlways: boolean | null;
  maxIterations: number | null;
  maxContextMessages: number | null;
  backupRetentionDays: number | null;
  maxWriteFileSizeBytes: number | null;
  commandTimeoutSeconds: number | null;
  circuitBreakerMaxFailures: number | null;
  circuitBreakerFailureWindowSeconds: number | null;
  circuitBreakerMaxWrites: number | null;
  circuitBreakerWriteWindowSeconds: number | null;
  embeddingModel: string;
  maxChunkTokens: number | null;
}

export interface AiGatewaySettings {
  aiProviderProviderId: string;
  aiProviderBaseUrl: string;
  aiProviderDefaultModel: string;
  aiProviderReasoningEffort: string;
  aiProviderDefaultMaxTokens: number | null;
  aiProviderDefaultTemperature: number | null;
  aiToolMaxToolCallsPerCompletion: number | null;
  aiToolFeatureGrantCacheSeconds: number | null;
  aiToolAllowedExternalHostsCsv: string;
  aiToolExternalNetworkPerUserDailyCalls: number | null;
  aiToolExternalNetworkTimeoutMilliseconds: number | null;
  aiToolExternalNetworkMaxResponseBytes: number | null;
}

export interface WritingSettings {
  cronsEnabled: boolean | null;
  coachEnabled: boolean | null;
  coachDailyCostCapPerLearnerUsd: number | null;
  coachMaxHintsPerSession: number | null;
  coachMinSecondsBetweenHints: number | null;
  gcvApiKey: string;
  ocrEnabled: boolean | null;
  appealsEnabled: boolean | null;
  tutorReviewQueueMaxDepth: number | null;
  tutorReviewMaxWaitHours: number | null;
  maxDailyPlanRegenerationsPerDay: number | null;
  gradeIdempotencyTtlHours: number | null;
}

export interface PlatformSettings {
  publicApiBaseUrl: string;
  publicWebBaseUrl: string;
  fallbackEmailDomain: string;
}

export interface MessagingSettings {
  twilioEnabled: boolean | null;
  twilioApiBaseUrl: string;
  twilioAccountSid: string;
  twilioAuthToken: string;
  twilioFromNumber: string;
  twilioMessagingServiceSid: string;
  whatsAppEnabled: boolean | null;
  whatsAppApiBaseUrl: string;
  whatsAppAccessToken: string;
  whatsAppPhoneNumberId: string;
  whatsAppFallbackTemplateName: string;
}

export interface FxSettings {
  baseCurrency: string;
  apiKey: string;
  apiBaseUrl: string;
  dynamicPricingEnabled: boolean | null;
}

export interface BillingCoreSettings {
  checkoutBaseUrl: string;
  webhookMaxAgeSeconds: number | null;
  webhookMaxAttempts: number | null;
  defaultCurrency: string;
  defaultRegion: string;
  walletCurrency: string;
  walletTopUpTiersJson: string;
  paypalUseSandbox: boolean | null;
  paypalApiBaseUrl: string;
}

export interface StorageSettings {
  provider: string;
  bucketName: string;
  endpointUrl: string;
  accessKeyId: string;
  secretAccessKey: string;
  awsRegion: string;
  signedReadTtlSeconds: number | null;
  maxAudioBytes: number | null;
  maxPdfBytes: number | null;
  maxImageBytes: number | null;
  maxZipBytes: number | null;
  maxZipEntries: number | null;
  maxZipEntryBytes: number | null;
  maxZipUncompressedBytes: number | null;
  maxZipCompressionRatio: number | null;
  chunkSizeBytes: number | null;
  stagingTtlHours: number | null;
  isConfigured?: boolean | null;
}

export interface PdfExtractionSettings {
  provider: string;
  azureEndpoint: string;
  azureApiKey: string;
  minTextLengthForSuccess: number | null;
}

export interface PronunciationSettings {
  provider: string;
  azureSpeechRegion: string;
  azureLocale: string;
  whisperBaseUrl: string;
  whisperModel: string;
  geminiBaseUrl: string;
  geminiModel: string;
  maxAudioBytes: number | null;
  audioRetentionDays: number | null;
  freeTierWeeklyAttemptLimit: number | null;
  freeTierWindowDays: number | null;
}

export interface AuthTokensSettings {
  accessTokenLifetimeSeconds: number | null;
  refreshTokenLifetimeSeconds: number | null;
  otpLifetimeSeconds: number | null;
  authenticatorIssuer: string;
}

export interface WebPushSettings {
  enabled: boolean | null;
}

// Distinct from MessagingSettings.whatsAppPhoneNumberId (the Meta Cloud API sender
// id, which is not dialable). This is the public wa.me number learners message.
export interface SupportSettings {
  whatsAppNumber: string;
  whatsAppProofTemplate: string;
  isWhatsAppConfigured?: boolean | null;
}

export interface RuntimeSettingsResponse {
  email: EmailSettings;
  billing: BillingSettings;
  sentry: SentrySettings;
  backup: BackupSettings;
  oauth: OAuthSettings;
  push: PushSettings;
  uploadScanner: UploadScannerSettings;
  zoom: ZoomSettings;
  speakingWhisper: SpeakingWhisperSettings;
  speakingLiveKit: SpeakingLiveKitSettings;
  speakingAi: SpeakingAiSettings;
  speakingStorage: SpeakingStorageSettings;
  speakingCompliance: SpeakingComplianceSettingsData;
  speakingFeatures: SpeakingFeaturesSettings;
  checkoutCom: CheckoutComSettings;
  bunnyStream: BunnyStreamSettings;
  paymob: PaymobSettings;
  payTabs: PayTabsSettings;
  easyKash: EasyKashSettings;
  soketi: SoketiSettings;
  dataRetention: DataRetentionSettings;
  expertAutoAssignment: ExpertAutoAssignmentSettings;
  passwordPolicy: PasswordPolicySettings;
  aiAssistant: AiAssistantSettings;
  aiGateway: AiGatewaySettings;
  writing: WritingSettings;
  platform: PlatformSettings;
  messaging: MessagingSettings;
  // ── Wave 4 ──
  fx: FxSettings;
  billingCore: BillingCoreSettings;
  storage: StorageSettings;
  pdfExtraction: PdfExtractionSettings;
  pronunciation: PronunciationSettings;
  authTokens: AuthTokensSettings;
  webPush: WebPushSettings;
  support: SupportSettings;
  updatedBy: string | null;
  updatedByUserId?: string | null;
  updatedAt: string | null;
}

export interface RuntimeSettingsIntegrationTestResponse {
  section: string;
  status: 'ok' | 'failed';
  message: string;
  testedAt: string;
}

type SectionId = 'email' | 'billing' | 'paypal' | 'sentry' | 'backup' | 'oauth' | 'push' | 'uploadScanner' | 'zoom' | 'speakingWhisper' | 'speakingLiveKit' | 'speakingAi' | 'speakingStorage' | 'speakingCompliance' | 'speakingFeatures' | 'checkoutCom' | 'bunnyStream' | 'paymob' | 'payTabs' | 'easyKash' | 'soketi' | 'dataRetention' | 'expertAutoAssignment' | 'passwordPolicy' | 'aiAssistant' | 'aiGateway' | 'writing' | 'platform' | 'messaging' | 'fx' | 'billingCore' | 'storage' | 'pdfExtraction' | 'pronunciation' | 'authTokens' | 'webPush' | 'support';

// The object-valued payload keys (every UI section maps to one of these; the "paypal"
// UI section writes into "billing"). Excludes the scalar audit fields so updateField can
// safely spread the prior section object.
type DataSectionId = Exclude<keyof RuntimeSettingsResponse, 'updatedBy' | 'updatedByUserId' | 'updatedAt'>;

type ToastState = { variant: 'success' | 'error'; message: string } | null;
type TestStatusState = Partial<Record<SectionId, RuntimeSettingsIntegrationTestResponse>>;

/* ───────────────────────── Field metadata ───────────────────────── */

interface FieldDef<TSection> {
  key: keyof TSection & string;
  label: string;
  hint?: string;
  secret?: boolean;
  type?: 'text' | 'number' | 'url' | 'checkbox' | 'select';
  /** Options for a 'select' field. */
  options?: { value: string; label: string }[];
  placeholder?: string;
}

const EMAIL_FIELDS: FieldDef<EmailSettings>[] = [
  { key: 'brevoApiKey', label: 'Brevo API Key', secret: true, hint: 'Server-side Brevo (Sendinblue) API key for transactional email.' },
  { key: 'brevoEmailVerificationTemplateId', label: 'Brevo Email Verification Template ID', hint: 'Numeric template id used for OTP verification emails.' },
  { key: 'brevoPasswordResetTemplateId', label: 'Brevo Password Reset Template ID', hint: 'Numeric template id used for password-reset emails.' },
  { key: 'smtpHost', label: 'SMTP Host', hint: 'Fallback SMTP server hostname.' },
  { key: 'smtpPort', label: 'SMTP Port', type: 'number', hint: 'Typically 587 (STARTTLS) or 465 (TLS).' },
  { key: 'smtpUsername', label: 'SMTP Username' },
  { key: 'smtpPassword', label: 'SMTP Password', secret: true },
  { key: 'smtpFromAddress', label: 'From Address', hint: 'Address used in the From header of outgoing email.' },
  { key: 'smtpFromName', label: 'From Name' },
  // ── Email partial-coverage gap (Wave 3) ──
  { key: 'brevoEnabled', label: 'Brevo Enabled', type: 'checkbox', hint: 'Master toggle for the Brevo email service.' },
  { key: 'smtpEnabled', label: 'SMTP Enabled', type: 'checkbox', hint: 'Master toggle for the SMTP email service.' },
  { key: 'smtpEnableSsl', label: 'SMTP Enable SSL', type: 'checkbox', hint: 'Enable TLS/SSL for SMTP connections (recommended).' },
  { key: 'brevoWelcomeTemplateId', label: 'Brevo Welcome Template ID', type: 'number', hint: 'Template ID for new user welcome emails.' },
  { key: 'brevoPasswordChangedTemplateId', label: 'Brevo Password Changed Template ID', type: 'number', hint: 'Template ID for password-changed confirmation.' },
  { key: 'brevoMfaEnabledTemplateId', label: 'Brevo MFA Enabled Template ID', type: 'number', hint: 'Template ID for MFA activation confirmation.' },
  { key: 'brevoAdminInviteTemplateId', label: 'Brevo Admin Invite Template ID', type: 'number', hint: 'Template ID for admin invitation emails.' },
  { key: 'brevoSecurityAlertTemplateId', label: 'Brevo Security Alert Template ID', type: 'number', hint: 'Template ID for security alerts.' },
  { key: 'brevoReviewCompletedTemplateId', label: 'Brevo Review Completed Template ID', type: 'number', hint: 'Template ID for review-completion notifications.' },
  { key: 'brevoWebhookSecret', label: 'Brevo Webhook Secret', secret: true, hint: 'HMAC secret for validating Brevo webhook signatures (reserved for future use).' },
];

const BILLING_FIELDS: FieldDef<BillingSettings>[] = [
  { key: 'stripeSecretKey', label: 'Stripe Secret Key', secret: true, hint: 'Server-side Stripe key (starts with sk_live_ or sk_test_).' },
  { key: 'stripePublishableKey', label: 'Stripe Publishable Key', hint: 'Client-safe Stripe key (starts with pk_).' },
  { key: 'stripeWebhookSecret', label: 'Stripe Webhook Signing Secret', secret: true, hint: 'Used to verify Stripe webhook signatures (starts with whsec_).' },
  { key: 'stripeSuccessUrl', label: 'Checkout Success URL', type: 'url' },
  { key: 'stripeCancelUrl', label: 'Checkout Cancel URL', type: 'url' },
  { key: 'publicAppBaseUrl', label: 'Public App Base URL', type: 'url', hint: 'e.g. https://app.oetwithdrhesham.co.uk — builds absolute checkout return URLs without an env var. Leave blank to use the server APP_URL.' },
];

const PAYPAL_FIELDS: FieldDef<BillingSettings>[] = [
  { key: 'paypalClientId', label: 'PayPal Client ID', hint: 'REST app client id (Apps & Credentials). Public — also used by the embedded Expanded checkout SDK in the browser.' },
  { key: 'paypalClientSecret', label: 'PayPal Client Secret', secret: true, hint: 'REST app secret. Server-side only — never sent to the browser.' },
  { key: 'paypalWebhookId', label: 'PayPal Webhook ID', secret: true, hint: 'Webhook id used to verify PayPal webhook signatures (Dashboard → Webhooks → your webhook).' },
  { key: 'paypalAdvancedCardsEnabled', label: 'Enable embedded card fields (Advanced Cards)', type: 'checkbox', hint: 'When on, the embedded checkout shows on-page card fields (requires PayPal "Advanced Credit and Debit Card Payments" eligibility). Turn off to show PayPal/Venmo/Pay Later buttons only. Default: on.' },
  { key: 'paypalSuccessUrl', label: 'PayPal Success URL', type: 'url', hint: 'Return URL for the redirect fallback flow (e.g. https://app.oetwithdrhesham.co.uk/billing/payment-return).' },
  { key: 'paypalCancelUrl', label: 'PayPal Cancel URL', type: 'url' },
];

const SENTRY_FIELDS: FieldDef<SentrySettings>[] = [
  { key: 'dsn', label: 'Sentry DSN', hint: 'Project DSN for error reporting.' },
  { key: 'environment', label: 'Sentry Environment', hint: 'e.g. production, staging.' },
  { key: 'sampleRate', label: 'Sentry Sample Rate', type: 'number', hint: 'Number between 0 and 1 (e.g. 0.1 = 10%).' },
];

const BACKUP_FIELDS: FieldDef<BackupSettings>[] = [
  { key: 's3Url', label: 'Backup S3 URL', type: 'url', hint: 's3://bucket/prefix or https-style endpoint.' },
  { key: 'awsAccessKeyId', label: 'AWS Access Key ID' },
  { key: 'awsSecretAccessKey', label: 'AWS Secret Access Key', secret: true },
  { key: 'gpgPassphrase', label: 'GPG Passphrase', secret: true, hint: 'Used to encrypt backup archives.' },
  { key: 'alertWebhook', label: 'Backup Alert Webhook', type: 'url', hint: 'POSTed when a backup fails.' },
];

const OAUTH_FIELDS: FieldDef<OAuthSettings>[] = [
  { key: 'googleClientId', label: 'Google Client ID' },
  { key: 'googleClientSecret', label: 'Google Client Secret', secret: true },
  { key: 'googleAuthEnabled', label: 'Enable Google Sign-In', type: 'checkbox', hint: 'Allow learners/tutors to sign in via Google OAuth.' },
  { key: 'appleClientId', label: 'Apple Client ID', hint: 'The service identifier (e.g. com.example.web).' },
  { key: 'appleTeamId', label: 'Apple Team ID' },
  { key: 'appleKeyId', label: 'Apple Key ID' },
  { key: 'applePrivateKey', label: 'Apple Private Key (.p8 contents)', secret: true },
  { key: 'facebookAppId', label: 'Facebook App ID' },
  { key: 'facebookAppSecret', label: 'Facebook App Secret', secret: true },
  { key: 'facebookAuthEnabled', label: 'Enable Facebook Sign-In', type: 'checkbox', hint: 'Allow learners/tutors to sign in via Facebook OAuth.' },
  // ── Auth external providers (Wave 4) — LinkedIn (the genuine gap) ──
  { key: 'linkedInClientId', label: 'LinkedIn Client ID', secret: true, hint: 'OAuth app client id from your LinkedIn developer console. Stored encrypted.' },
  { key: 'linkedInClientSecret', label: 'LinkedIn Client Secret', secret: true, hint: 'Stored encrypted at rest.' },
  { key: 'linkedInEnabled', label: 'Enable LinkedIn Sign-In', type: 'checkbox', hint: 'Allow learners/tutors to sign in via LinkedIn OAuth.' },
];

const PUSH_FIELDS: FieldDef<PushSettings>[] = [
  { key: 'vapidSubject', label: 'Browser Push VAPID Subject', hint: 'Contact URI, usually mailto:support@example.com.' },
  { key: 'vapidPublicKey', label: 'Browser Push VAPID Public Key' },
  { key: 'vapidPrivateKey', label: 'Browser Push VAPID Private Key', secret: true },
  { key: 'apnsKeyId', label: 'APNs Key ID' },
  { key: 'apnsTeamId', label: 'APNs Team ID' },
  { key: 'apnsBundleId', label: 'APNs Bundle ID', hint: 'iOS app bundle identifier.' },
  { key: 'apnsAuthKey', label: 'APNs Auth Key (.p8 contents)', secret: true },
  { key: 'fcmServerKey', label: 'FCM Server Key', secret: true },
  { key: 'fcmProjectId', label: 'FCM Project ID' },
];

const UPLOAD_SCANNER_FIELDS: FieldDef<UploadScannerSettings>[] = [
  { key: 'provider', label: 'Scanner Provider', hint: 'Use "clamav" for production; "noop" is development-only.' },
  { key: 'host', label: 'ClamAV Host', hint: 'ClamAV daemon host, for example clamav or 127.0.0.1.' },
  { key: 'port', label: 'ClamAV Port', type: 'number', hint: 'Default clamd port is 3310.' },
  { key: 'timeoutSeconds', label: 'Scan Timeout (seconds)', type: 'number', hint: 'Fail fast for slow scanners; valid range is 1-120.' },
  { key: 'failClosedOnError', label: 'Fail closed on scanner errors', type: 'checkbox', hint: 'Reject uploads when ClamAV is unavailable or times out.' },
];

const ZOOM_FIELDS: FieldDef<ZoomSettings>[] = [
  { key: 'enabled', label: 'Enable Zoom live classes', type: 'checkbox', hint: 'Controls Zoom meeting creation and webhook processing for live classes.' },
  { key: 'accountId', label: 'Account ID', hint: 'Zoom Server-to-Server OAuth account id.' },
  { key: 'clientId', label: 'Client ID', hint: 'Zoom Server-to-Server OAuth client id.' },
  { key: 'clientSecret', label: 'Client Secret', secret: true },
  { key: 'apiBaseUrl', label: 'API Base URL', type: 'url', hint: 'Default is https://api.zoom.us/v2.' },
  { key: 'tokenUrl', label: 'Token URL', type: 'url', hint: 'Default is https://zoom.us/oauth/token.' },
  { key: 'hostUserId', label: 'Host User ID', hint: 'Zoom user id or email used to create hosted meetings.' },
  { key: 'meetingSdkKey', label: 'Meeting SDK Key' },
  { key: 'meetingSdkSecret', label: 'Meeting SDK Secret', secret: true },
  { key: 'webhookSecretToken', label: 'Webhook Secret Token', secret: true },
  { key: 'webhookRetryToleranceSeconds', label: 'Webhook Tolerance (seconds)', type: 'number', hint: 'Allowed range is 60-3600.' },
  { key: 'allowSandboxFallback', label: 'Allow sandbox fallback', type: 'checkbox', hint: 'Development-only fallback when Zoom API credentials are unavailable.' },
];

const SPEAKING_WHISPER_FIELDS: FieldDef<SpeakingWhisperSettings>[] = [
  { key: 'apiKey', label: 'OpenAI Whisper API Key', secret: true, hint: 'Used by the Speaking RULE_40 tone pipeline. Starts with sk-…' },
  { key: 'baseUrl', label: 'Whisper API Base URL', type: 'url', hint: 'Default: https://api.openai.com/v1. Change only for self-hosted gateways.' },
  { key: 'model', label: 'Whisper Model', hint: 'Default: whisper-1.' },
];

const SPEAKING_LIVEKIT_FIELDS: FieldDef<SpeakingLiveKitSettings>[] = [
  { key: 'provider', label: 'LiveKit Provider', hint: '"livekit_cloud" to enable, "disabled" to stub out.' },
  { key: 'apiKey', label: 'LiveKit API Key', secret: true, hint: 'Server-side LiveKit Cloud API key.' },
  { key: 'apiSecret', label: 'LiveKit API Secret', secret: true, hint: 'Server-side LiveKit Cloud API secret.' },
  { key: 'wssUrl', label: 'LiveKit WebSocket URL', type: 'url', hint: 'e.g. wss://your-project.livekit.cloud' },
  { key: 'webhookSigningSecret', label: 'Webhook Signing Secret', secret: true, hint: 'HMAC secret for verifying LiveKit webhook payloads.' },
  { key: 'egressBucket', label: 'Egress S3 Bucket', hint: 'S3 bucket name for LiveKit egress recordings.' },
  { key: 'defaultMaxDurationSeconds', label: 'Max Session Duration (seconds)', type: 'number', hint: '60–7200 seconds (default: 1800 = 30 min).' },
  { key: 'egressEnabled', label: 'Enable Egress Recording', type: 'checkbox', hint: 'When enabled, completed sessions are recorded to S3.' },
];

const SPEAKING_AI_FIELDS: FieldDef<SpeakingAiSettings>[] = [
  { key: 'anthropicApiKey', label: 'Anthropic API Key', secret: true, hint: 'Claude Sonnet 4.6 for AI grading + patient turns. Starts with sk-ant-…' },
  { key: 'elevenLabsApiKey', label: 'ElevenLabs API Key', secret: true, hint: 'AI patient TTS voice synthesis.' },
];

const SPEAKING_STORAGE_FIELDS: FieldDef<SpeakingStorageSettings>[] = [
  { key: 'awsAccessKeyId', label: 'AWS Access Key ID', hint: 'IAM user with S3 PutObject/GetObject permissions.' },
  { key: 'awsSecretAccessKey', label: 'AWS Secret Access Key', secret: true },
  { key: 'region', label: 'AWS Region', hint: 'e.g. eu-west-2 (London).' },
  { key: 'bucket', label: 'S3 Bucket Name', hint: 'Bucket for speaking session recordings.' },
];

const SPEAKING_COMPLIANCE_FIELDS: FieldDef<SpeakingComplianceSettingsData>[] = [
  { key: 'currentConsentVersion', label: 'Recording Consent Version', hint: 'Version string shown to learners (e.g. recording.v1).' },
  { key: 'currentLiveVideoConsentVersion', label: 'Live Video Consent Version', hint: 'Version string for live tutor video sessions (e.g. live_video_with_tutor.v1).' },
  { key: 'retentionDaysDefault', label: 'Default Retention (days)', type: 'number', hint: 'Days to retain recordings before deletion (default: 90).' },
  { key: 'retentionDaysWhenTutorReviewed', label: 'Tutor-Reviewed Retention (days)', type: 'number', hint: 'Extended retention when a tutor has reviewed (default: 365).' },
  { key: 'auditLogRetentionDays', label: 'Audit Log Retention (days)', type: 'number', hint: 'Days to retain speaking audit logs (default: 2555 ≈ 7 years).' },
];

const SPEAKING_FEATURES_FIELDS: FieldDef<SpeakingFeaturesSettings>[] = [
  { key: 'speakingV2Enabled', label: 'Enable Speaking V2 Module', type: 'checkbox', hint: 'Feature flag that gates the full Speaking v2 module rollout to learners.' },
];

const CHECKOUT_COM_FIELDS: FieldDef<CheckoutComSettings>[] = [
  { key: 'apiBaseUrl', label: 'API Base URL', type: 'url', hint: 'Default: https://api.checkout.com (use https://api.sandbox.checkout.com for testing).' },
  { key: 'secretKey', label: 'Secret Key', secret: true, hint: 'Server-side Checkout.com key (starts with sk_).' },
  { key: 'publicKey', label: 'Public Key', hint: 'Client-safe key (starts with pk_).' },
  { key: 'processingChannelId', label: 'Processing Channel ID', hint: 'pc_… channel id for hosted payments.' },
  { key: 'webhookSecret', label: 'Webhook Signing Secret', secret: true, hint: 'Verifies Cko-Signature webhook headers.' },
  { key: 'successUrl', label: 'Success URL', type: 'url' },
  { key: 'cancelUrl', label: 'Cancel URL', type: 'url' },
];

const BUNNY_STREAM_FIELDS: FieldDef<BunnyStreamSettings>[] = [
  { key: 'enabled', label: 'Enabled', type: 'checkbox', hint: 'Master toggle. When off, the Video Library stays dormant (uploads/playback return 503 bunny_not_configured).' },
  { key: 'libraryId', label: 'Library ID', hint: 'Numeric Bunny Stream library id (Bunny dashboard → Stream → your library).' },
  { key: 'apiKey', label: 'API Key', secret: true, hint: 'The per-library API key (Stream → library → API). Used for uploads + metadata. Stored encrypted.' },
  { key: 'cdnHostname', label: 'CDN Hostname', hint: 'The pull-zone hostname, e.g. vz-xxxxxxxx-xxx.b-cdn.net.' },
  { key: 'tokenAuthKey', label: 'CDN Token-Auth Key', secret: true, hint: "The pull-zone Token Authentication Key (pull zone → Security). Signs playback URLs. Requires the zone's token authentication to be ON. Stored encrypted." },
  { key: 'webhookSecret', label: 'Webhook Secret', secret: true, hint: 'Shared secret embedded in the Bunny webhook URL as ?secret=… (set the webhook to https://api.oetwithdrhesham.co.uk/v1/webhooks/bunny-stream?secret=<this>). Stored encrypted.' },
  { key: 'collectionId', label: 'Collection ID (optional)', hint: 'Optional Bunny collection to place newly-created videos in.' },
  { key: 'playbackTokenTtlSeconds', label: 'Playback Token TTL (seconds)', type: 'number', hint: 'Signed-URL lifetime, 300–86400. Default 14400 (4 hours).' },
  { key: 'videoAttestationKeysJson', label: 'App Attestation Keys (JSON)', secret: true, hint: 'JSON map of "platform:keyId" → hex secret, e.g. {"tauri:v1":"<hex>","capacitor-android:v1":"<hex>","capacitor-ios:v1":"<hex>"}. Must match the OET_DESKTOP/MOBILE_ATTEST_SECRET baked into the app builds. Enables app-only playback; leave blank to keep playback disabled. Stored encrypted.' },
];

const PAYMOB_FIELDS: FieldDef<PaymobSettings>[] = [
  { key: 'apiBaseUrl', label: 'API Base URL', type: 'url', hint: 'Default: https://accept.paymob.com.' },
  { key: 'apiKey', label: 'API Key', secret: true, hint: 'Paymob secret API key used for auth tokens.' },
  { key: 'merchantId', label: 'Merchant ID' },
  { key: 'hmacSecret', label: 'HMAC Secret', secret: true, hint: 'Verifies the SHA-512 webhook signature.' },
  { key: 'integrationIdsJson', label: 'Integration IDs (JSON)', hint: 'Method→id map, e.g. {"card":123,"fawry":456}.' },
  { key: 'iframeId', label: 'Iframe ID', type: 'number', hint: 'Hosted iframe id for the redirect URL.' },
  { key: 'successUrl', label: 'Success URL', type: 'url' },
  { key: 'cancelUrl', label: 'Cancel URL', type: 'url' },
];

const EASYKASH_FIELDS: FieldDef<EasyKashSettings>[] = [
  { key: 'apiBaseUrl', label: 'API Base URL', type: 'url', hint: 'Default: https://back.easykash.net.' },
  { key: 'apiKey', label: 'API Key', secret: true, hint: 'EasyKash API key — sent in the authorization header (from EasyKash Integration Settings).' },
  { key: 'hmacSecret', label: 'HMAC Secret', secret: true, hint: 'Verifies the SHA-512 callback signature. EasyKash generates it after you save the Callback URL + payment methods in their dashboard.' },
  {
    key: 'currencyMode',
    label: 'Currency Mode',
    type: 'select',
    options: [
      { value: 'passthrough', label: 'Charge quote currency (e.g. GBP)' },
      { value: 'egp', label: 'Convert to EGP' },
    ],
    hint: 'Passthrough charges the displayed price as-is; EGP converts via the live FX rate (unlocks Fawry / wallets / instalments on the EasyKash page).',
  },
  { key: 'paymentOptionsCsv', label: 'Payment Method IDs (CSV)', hint: 'Optional, e.g. "2,4,5" (2=card, 4=wallet, 5=Fawry). Empty = all methods enabled in the EasyKash dashboard.' },
  { key: 'successUrl', label: 'Success URL', type: 'url', hint: 'Optional buyer-return URL override.' },
  { key: 'cancelUrl', label: 'Cancel URL', type: 'url' },
];

const PAYTABS_FIELDS: FieldDef<PayTabsSettings>[] = [
  { key: 'apiBaseUrl', label: 'API Base URL', type: 'url', hint: 'Region-specific, e.g. https://secure.paytabs.com or https://secure-egypt.paytabs.com.' },
  { key: 'serverKey', label: 'Server Key', secret: true, hint: 'Authorization header value for PayTabs API.' },
  { key: 'profileId', label: 'Profile ID', hint: 'Merchant profile id (identifies account + region).' },
  { key: 'webhookSecret', label: 'Webhook Secret', secret: true, hint: 'Verifies the HMAC-SHA256 Signature header.' },
  { key: 'successUrl', label: 'Success URL', type: 'url' },
  { key: 'cancelUrl', label: 'Cancel URL', type: 'url' },
];

const SOKETI_FIELDS: FieldDef<SoketiSettings>[] = [
  { key: 'enabled', label: 'Enable Soketi push', type: 'checkbox', hint: 'Server-side realtime websocket dispatch.' },
  { key: 'host', label: 'Host', hint: 'Soketi server host (e.g. soketi or 127.0.0.1).' },
  { key: 'port', label: 'Port', type: 'number', hint: 'Default 6001.' },
  { key: 'appId', label: 'App ID' },
  { key: 'appKey', label: 'App Key', hint: 'Note: the browser client reads its key from NEXT_PUBLIC_* env — this affects server-side dispatch only.' },
  { key: 'appSecret', label: 'App Secret', secret: true, hint: 'HMAC secret for signing Pusher-protocol requests.' },
  { key: 'useTls', label: 'Use TLS (wss/https)', type: 'checkbox' },
];

const DATA_RETENTION_FIELDS: FieldDef<DataRetentionSettings>[] = [
  { key: 'auditEventsDays', label: 'Audit Events Retention (days)', type: 'number', hint: 'How long admin/billing audit rows are kept. 0 disables the sweep (default 730).' },
  { key: 'analyticsEventsDays', label: 'Analytics Events Retention (days)', type: 'number', hint: 'High-volume product analytics retention. 0 disables (default 365).' },
  { key: 'paymentWebhookEventsDays', label: 'Payment Webhook Retention (days)', type: 'number', hint: 'How long processed gateway webhook rows are kept (default 180).' },
  { key: 'paymentWebhookPiiNullOutAgeDays', label: 'Webhook Payload PII Null-out (days)', type: 'number', hint: 'Age after which webhook payload bodies are nulled while metadata is kept (default 90).' },
  { key: 'notificationDeliveryAttemptsDays', label: 'Notification Attempts Retention (days)', type: 'number', hint: 'How long delivery-attempt rows are kept (default 90).' },
  { key: 'sweepIntervalHours', label: 'Sweep Interval (hours)', type: 'number', hint: 'How often the retention sweeper runs (default 24).' },
  { key: 'batchSize', label: 'Batch Size (rows per table per sweep)', type: 'number', hint: 'Caps rows deleted per table per sweep to avoid long locks (default 5000).' },
];

const EXPERT_AUTO_ASSIGNMENT_FIELDS: FieldDef<ExpertAutoAssignmentSettings>[] = [
  { key: 'enabled', label: 'Enable auto-assignment', type: 'checkbox', hint: 'Auto-assign Writing review requests to the lowest-loaded eligible expert.' },
  { key: 'slaHoursStandard', label: 'Standard SLA (hours)', type: 'number', hint: 'Turnaround target for standard reviews (default 48).' },
  { key: 'slaHoursExpress', label: 'Express SLA (hours)', type: 'number', hint: 'Turnaround target for express reviews (default 12).' },
  { key: 'maxActiveAssignmentsPerExpert', label: 'Max Active Assignments / Expert', type: 'number', hint: 'Load cap per expert before they stop receiving new work (default 8).' },
  { key: 'lookbackHoursForLoad', label: 'Load Lookback (hours)', type: 'number', hint: 'Window used to tally recent completions when balancing load (default 24).' },
  { key: 'batchSize', label: 'Batch Size (per poll)', type: 'number', hint: 'Max pending requests processed per poll cycle (default 50).' },
  { key: 'pollingIntervalSeconds', label: 'Polling Interval (seconds)', type: 'number', hint: 'Cadence of the assignment poll (default 30).' },
  { key: 'slaEscalationIntervalSeconds', label: 'SLA Escalation Interval (seconds)', type: 'number', hint: 'Cadence of the SLA escalation poll (default 60).' },
];

const PASSWORD_POLICY_FIELDS: FieldDef<PasswordPolicySettings>[] = [
  { key: 'minimumLength', label: 'Minimum Length', type: 'number', hint: 'Minimum password length (NIST recommends 8+, default 10).' },
  { key: 'requireMixedCase', label: 'Require mixed case', type: 'checkbox', hint: 'Require both uppercase and lowercase letters.' },
  { key: 'requireDigit', label: 'Require a digit', type: 'checkbox' },
  { key: 'requireSymbol', label: 'Require a symbol', type: 'checkbox' },
  { key: 'breachCheckEnabled', label: 'Enable HIBP breach check', type: 'checkbox', hint: 'k-anonymity check against HaveIBeenPwned. The password never leaves the server. Disable for air-gapped deployments.' },
  { key: 'breachApiBaseUrl', label: 'Breach API Base URL', type: 'url', hint: 'HIBP range API base. Override only for a self-hosted mirror (default https://api.pwnedpasswords.com/).' },
  { key: 'breachApiTimeoutSeconds', label: 'Breach API Timeout (seconds)', type: 'number', hint: 'Fail-open timeout for the breach check (1–60, default 3).' },
];

const AI_ASSISTANT_FIELDS: FieldDef<AiAssistantSettings>[] = [
  { key: 'globalEnabled', label: 'Enable AI Assistant', type: 'checkbox', hint: 'Master kill switch. When false, all AI assistant features are disabled.' },
  { key: 'requireApprovalAlways', label: 'Require approval for all writes', type: 'checkbox', hint: 'When enabled, every write operation requires explicit user approval before being applied. Default is true for safety.' },
  { key: 'maxIterations', label: 'Max ReAct Iterations', type: 'number', hint: 'Maximum number of ReAct loop iterations before forcing a response. Default: 10.' },
  { key: 'maxContextMessages', label: 'Max Context Messages', type: 'number', hint: 'Maximum number of messages to include in the context window. Default: 50.' },
  { key: 'backupRetentionDays', label: 'Backup Retention (days)', type: 'number', hint: 'Days to retain file backups before cleanup. Default: 30.' },
  { key: 'maxWriteFileSizeBytes', label: 'Max Write File Size (bytes)', type: 'number', hint: 'Maximum file size (bytes) that can be written in a single operation. Default: 1048576 (1 MB).' },
  { key: 'commandTimeoutSeconds', label: 'Command Timeout (seconds)', type: 'number', hint: 'Command execution timeout in seconds. Default: 300 (5 minutes).' },
  { key: 'circuitBreakerMaxFailures', label: 'Circuit Breaker: Max Failures', type: 'number', hint: 'Maximum failures within the failure window before circuit breaker pauses requests. Default: 3.' },
  { key: 'circuitBreakerFailureWindowSeconds', label: 'Circuit Breaker: Failure Window (seconds)', type: 'number', hint: 'Time window for failure counting in seconds. Default: 60.' },
  { key: 'circuitBreakerMaxWrites', label: 'Circuit Breaker: Max Writes', type: 'number', hint: 'Maximum writes within the write window before circuit breaker pauses. Default: 10.' },
  { key: 'circuitBreakerWriteWindowSeconds', label: 'Circuit Breaker: Write Window (seconds)', type: 'number', hint: 'Time window for write counting in seconds. Default: 300.' },
  { key: 'embeddingModel', label: 'Embedding Model', type: 'text', hint: 'Embedding model to use for codebase indexing. Default: text-embedding-3-small.' },
  { key: 'maxChunkTokens', label: 'Max Chunk Tokens', type: 'number', hint: 'Maximum chunk size in tokens for tree-sitter code splitting. Default: 512.' },
];

const AI_GATEWAY_FIELDS: FieldDef<AiGatewaySettings>[] = [
  { key: 'aiProviderProviderId', label: 'AI Provider ID', type: 'text', hint: 'Stable code (digitalocean-serverless, openai-platform, anthropic, etc). Fallback: env AI:ProviderId.' },
  { key: 'aiProviderBaseUrl', label: 'AI Provider Base URL', type: 'url', hint: 'OpenAI-compatible endpoint (e.g. https://inference.do-ai.run/v1). Validated for safety. Fallback: env AI:BaseUrl.' },
  { key: 'aiProviderDefaultModel', label: 'Default Model', type: 'text', hint: 'Model ID when none specified (e.g. glm-5). Fallback: env AI:DefaultModel.' },
  { key: 'aiProviderReasoningEffort', label: 'Reasoning Effort (for o-series models)', type: 'text', hint: 'low, medium, or high. Leave blank for non-reasoning models (glm-5). Fallback: env AI:ReasoningEffort.' },
  { key: 'aiProviderDefaultMaxTokens', label: 'Default Max Tokens', type: 'number', hint: 'Completion token limit (e.g. 4096). Fallback: env AI:DefaultMaxTokens.' },
  { key: 'aiProviderDefaultTemperature', label: 'Default Temperature', type: 'number', hint: 'Sampler temperature 0.0–1.0 (e.g. 0.2). Fallback: env AI:DefaultTemperature.' },
  { key: 'aiToolMaxToolCallsPerCompletion', label: 'Max Tool Calls per Completion', type: 'number', hint: 'Agentic loop breaker. When reached, gateway returns without trying more tool calls. Fallback: env AiTool:MaxToolCallsPerCompletion (default 4).' },
  { key: 'aiToolFeatureGrantCacheSeconds', label: 'Feature Grant Cache TTL (seconds)', type: 'number', hint: 'In-memory cache lifetime for per-feature tool grants. Fallback: env AiTool:FeatureGrantCacheSeconds (default 30).' },
  { key: 'aiToolAllowedExternalHostsCsv', label: 'Allowed External Hosts (CSV)', type: 'text', hint: 'Comma-separated hostnames for ExternalNetwork tools (no scheme/path; exact match). Default: api.dictionaryapi.dev. Fallback: env AiTool:AllowedExternalHosts.' },
  { key: 'aiToolExternalNetworkPerUserDailyCalls', label: 'External Network Daily Call Budget per User', type: 'number', hint: 'Max calls/user/day for ExternalNetwork tools (0 = disabled). Fallback: env AiTool:ExternalNetworkPerUserDailyCalls (default 200).' },
  { key: 'aiToolExternalNetworkTimeoutMilliseconds', label: 'External Network HTTP Timeout (ms)', type: 'number', hint: 'Request timeout in milliseconds for external-network tool calls. Fallback: env AiTool:ExternalNetworkTimeoutMilliseconds (default 4000).' },
  { key: 'aiToolExternalNetworkMaxResponseBytes', label: 'External Network Max Response Size (bytes)', type: 'number', hint: 'Max response body size (defends unbounded downloads). Fallback: env AiTool:ExternalNetworkMaxResponseBytes (default 65536 = 64 KB).' },
];

const WRITING_FIELDS: FieldDef<WritingSettings>[] = [
  { key: 'cronsEnabled', label: 'Enable Writing Cron Jobs', type: 'checkbox', hint: 'Master feature flag. When off, all scheduled Writing tasks (coach queue, appeals, daily-plan regen, tutor-queue worker) are disabled. Use during deployments to pause processing.' },
  { key: 'coachEnabled', label: 'Enable Writing Coach (AI Hints)', type: 'checkbox', hint: 'Feature flag for real Haiku coach. When off, coach endpoints return empty hints; learners cannot request coaching. Cost remains $0.' },
  { key: 'coachDailyCostCapPerLearnerUsd', label: 'Coach Daily Cost Cap (USD)', type: 'number', hint: 'Per-learner 24h spend limit for AI hints. Example: 0.5 USD. Rolling window based on AI gateway accounting. Set to 0 to disable cost cap.' },
  { key: 'coachMaxHintsPerSession', label: 'Coach Max Hints Per Session', type: 'number', hint: 'Hard limit on consecutive hints in one (userId, sessionId) pair. Example: 80.' },
  { key: 'coachMinSecondsBetweenHints', label: 'Coach Min Seconds Between Hints', type: 'number', hint: 'Rate-throttle window. Example: 30 seconds. Learner must wait at least this long after the last hint.' },
  { key: 'gcvApiKey', label: 'Google Cloud Vision API Key', secret: true, hint: 'Server-side GCV key for OCR fallback when Tesseract confidence < 95%. Stored encrypted. Leave blank to skip GCV and mark jobs manual_required.' },
  { key: 'ocrEnabled', label: 'Enable OCR Pipeline', type: 'checkbox', hint: 'Feature flag for the OCR job system (local Tesseract + Google Cloud Vision fallback). When off, enqueue returns manual_required without attempting extraction.' },
  { key: 'appealsEnabled', label: 'Enable Grade Appeals', type: 'checkbox', hint: 'Feature flag. When off, learners cannot submit grade appeals. Appeals already in review are unaffected.' },
  { key: 'tutorReviewQueueMaxDepth', label: 'Tutor Queue Max Depth', type: 'number', hint: 'Hard limit on unassigned submissions in queue. When exceeded, the auto-assignment worker pauses. Example: 50.' },
  { key: 'tutorReviewMaxWaitHours', label: 'Tutor Queue Max Wait (hours)', type: 'number', hint: 'SLA window. If a submission sits unassigned longer than this, the queue pauses and escalation alerts fire. Example: 36 hours.' },
  { key: 'maxDailyPlanRegenerationsPerDay', label: 'Max Daily Plan Regens Per Learner', type: 'number', hint: 'Daily budget for adaptive study path regen. Example: 1. Rolling 24h window.' },
  { key: 'gradeIdempotencyTtlHours', label: 'Grade Idempotency Cache TTL (hours)', type: 'number', hint: 'Deduplication window for concurrent grade-submission requests. Example: 24 hours. Keyed by submission id.' },
];

const PLATFORM_FIELDS: FieldDef<PlatformSettings>[] = [
  { key: 'publicApiBaseUrl', label: 'Public API Base URL', type: 'url', hint: 'e.g. https://api.oetwithdrhesham.co.uk. Builds absolute callback URLs for external auth. Leave blank to use the env var Platform:PublicApiBaseUrl.' },
  { key: 'publicWebBaseUrl', label: 'Public Web Base URL', type: 'url', hint: 'e.g. https://app.oetwithdrhesham.co.uk. Used for external auth redirects and cookie CSRF origin validation. Required in production when external auth is enabled.' },
  { key: 'fallbackEmailDomain', label: 'Fallback Email Domain', type: 'text', hint: 'Domain for synthesized learner emails when no external provider email exists (default: example.invalid). Omit the @ symbol.' },
];

const MESSAGING_FIELDS: FieldDef<MessagingSettings>[] = [
  { key: 'twilioEnabled', label: 'Twilio SMS Enabled', type: 'checkbox', hint: 'Enable Twilio SMS billing notifications. Requires Account SID and Auth Token.' },
  { key: 'twilioApiBaseUrl', label: 'Twilio API Base URL', type: 'url', hint: 'Defaults to https://api.twilio.com. Only change if using a custom endpoint.' },
  { key: 'twilioAccountSid', label: 'Twilio Account SID', type: 'text', hint: 'Public account identifier. Find it in the Twilio Console.' },
  { key: 'twilioAuthToken', label: 'Twilio Auth Token', secret: true, hint: 'API authentication token. Stored encrypted. Treat as a secret.' },
  { key: 'twilioFromNumber', label: 'Twilio From Number', type: 'text', hint: 'E.164 phone number (e.g. +1234567890). Leave empty if using a Messaging Service SID.' },
  { key: 'twilioMessagingServiceSid', label: 'Twilio Messaging Service SID', type: 'text', hint: 'Optional. When set, takes precedence over From Number. Useful for multi-number pools.' },
  { key: 'whatsAppEnabled', label: 'WhatsApp Business Cloud Enabled', type: 'checkbox', hint: 'Enable Meta WhatsApp Business API for billing notifications. Requires Access Token and Phone Number ID.' },
  { key: 'whatsAppApiBaseUrl', label: 'WhatsApp API Base URL', type: 'url', hint: 'Defaults to https://graph.facebook.com/v20.0. Only change for a different Meta Graph API version.' },
  { key: 'whatsAppAccessToken', label: 'WhatsApp Access Token', secret: true, hint: 'Meta Business Account access token. Stored encrypted. Treat as a secret.' },
  { key: 'whatsAppPhoneNumberId', label: 'WhatsApp Phone Number ID', type: 'text', hint: 'ID of the WhatsApp Business phone number assigned by Meta.' },
  { key: 'whatsAppFallbackTemplateName', label: 'WhatsApp Fallback Template Name', type: 'text', hint: 'Pre-approved template name for messages outside the 24-hour service window. Leave empty to disable template fallback.' },
];

const FX_FIELDS: FieldDef<FxSettings>[] = [
  { key: 'baseCurrency', label: 'Base Currency', type: 'text', hint: 'ISO 4217 code (e.g. USD, GBP, EUR). Reference currency for all FX pairs. Default USD.' },
  { key: 'apiKey', label: 'FX Provider API Key', secret: true, hint: 'openexchangerates.org app_id or compatible. Empty → offline seed rates. Stored encrypted.' },
  { key: 'apiBaseUrl', label: 'FX Provider Base URL', type: 'url', hint: 'e.g. https://openexchangerates.org/api. Ignored when no API key is set.' },
  { key: 'dynamicPricingEnabled', label: 'Enable Dynamic Pricing', type: 'checkbox', hint: 'When on, checkout amounts are FX-converted to the buyer display currency.' },
];

const BILLING_CORE_FIELDS: FieldDef<BillingCoreSettings>[] = [
  { key: 'checkoutBaseUrl', label: 'Checkout Base URL', type: 'url', hint: 'Base URL used to build hosted-checkout return links. Leave blank to use the env value.' },
  { key: 'webhookMaxAgeSeconds', label: 'Webhook Max Age (seconds)', type: 'number', hint: 'Max tolerated webhook timestamp age before rejection as replay. Default 300. Range 1–3600.' },
  { key: 'webhookMaxAttempts', label: 'Webhook Max Attempts', type: 'number', hint: 'Max local processing retries before dead-letter. Default 5.' },
  { key: 'defaultCurrency', label: 'Default Currency', type: 'text', hint: 'ISO 4217 fallback when region pricing supplies none. Default GBP.' },
  { key: 'defaultRegion', label: 'Default Region', type: 'text', hint: 'Fallback region (e.g. UK, GULF, EGYPT, PAKISTAN, ROW) when country is undetected. Default ROW.' },
  { key: 'walletCurrency', label: 'Wallet Currency', type: 'text', hint: 'ISO 4217 currency for wallet top-ups. Default AUD.' },
  { key: 'walletTopUpTiersJson', label: 'Wallet Top-Up Tiers (JSON)', type: 'text', hint: 'JSON array: [{"Amount":10,"Credits":3,"Bonus":0,"Label":"Starter","IsPopular":false}, ...]. Blank uses appsettings defaults.' },
  // NOTE: "Use PayPal Sandbox" + "PayPal API Base URL" were moved into the
  // "Payments: PayPal" section (next to the credentials) so the live/sandbox switch
  // sits with the keys it applies to. They still persist into billingCore on save.
];

const STORAGE_FIELDS: FieldDef<StorageSettings>[] = [
  { key: 'provider', label: 'Storage Provider', type: 'text', hint: '"local" or "s3". Switching provider requires a restart; only S3 credentials/bucket/endpoint/region are hot-switchable.' },
  { key: 'bucketName', label: 'S3 Bucket Name', type: 'text', hint: 'Required when Provider="s3" (e.g. oet-media).' },
  { key: 'endpointUrl', label: 'S3 Endpoint URL', type: 'url', hint: 'For DigitalOcean Spaces / Cloudflare R2. Omit for AWS S3.' },
  { key: 'accessKeyId', label: 'S3 Access Key ID', secret: true, hint: 'Required when Provider="s3". Stored encrypted; masked in UI.' },
  { key: 'secretAccessKey', label: 'S3 Secret Access Key', secret: true, hint: 'Required when Provider="s3". Stored encrypted; masked in UI.' },
  { key: 'awsRegion', label: 'AWS Region', type: 'text', hint: 'AWS region code (e.g. us-east-1, eu-west-2). Default us-east-1.' },
  { key: 'signedReadTtlSeconds', label: 'Signed Read URL TTL (seconds)', type: 'number', hint: 'TTL for presigned GET URLs. Default 3600 (1 hour).' },
  { key: 'maxAudioBytes', label: 'Max Audio Upload Size (bytes)', type: 'number', hint: 'Max bytes for audio assets / Listening MP3. Default 150 MB.' },
  { key: 'maxPdfBytes', label: 'Max PDF Upload Size (bytes)', type: 'number', hint: 'Max bytes for PDF assets. Default 25 MB.' },
  { key: 'maxImageBytes', label: 'Max Image Upload Size (bytes)', type: 'number', hint: 'Max bytes for image assets / thumbnails. Default 5 MB.' },
  { key: 'maxZipBytes', label: 'Max ZIP Import Size (bytes, compressed)', type: 'number', hint: 'Max compressed bytes for ZIP bulk imports. Default 500 MB.' },
  { key: 'maxZipEntries', label: 'Max ZIP Entries', type: 'number', hint: 'Max files inside one ZIP bulk import. Default 5000.' },
  { key: 'maxZipEntryBytes', label: 'Max ZIP Entry Size (bytes, uncompressed)', type: 'number', hint: 'Max uncompressed bytes for one ZIP entry. Default 150 MB.' },
  { key: 'maxZipUncompressedBytes', label: 'Max ZIP Total Uncompressed (bytes)', type: 'number', hint: 'Max total uncompressed bytes across a ZIP import. Default 2 GB.' },
  { key: 'maxZipCompressionRatio', label: 'Max ZIP Compression Ratio', type: 'number', hint: 'Max uncompressed/compressed ratio (zip-bomb guard). Default 100.' },
  { key: 'chunkSizeBytes', label: 'Chunk Upload Size (bytes)', type: 'number', hint: 'Per-chunk size for chunked uploads. Default 8 MB.' },
  { key: 'stagingTtlHours', label: 'Staging Upload TTL (hours)', type: 'number', hint: 'Hours before incomplete staging uploads are cleaned up. Default 24.' },
];

const PDF_EXTRACTION_FIELDS: FieldDef<PdfExtractionSettings>[] = [
  { key: 'provider', label: 'PDF Extraction Provider', type: 'text', hint: 'noop | pdfpig | azure | auto (default: auto = pdfpig with azure fallback).' },
  { key: 'azureEndpoint', label: 'Azure Document Intelligence Endpoint', type: 'url', hint: 'e.g. https://{name}.cognitiveservices.azure.com/. Empty disables OCR.' },
  { key: 'azureApiKey', label: 'Azure Document Intelligence API Key', secret: true, hint: 'Stored encrypted; masked in UI.' },
  { key: 'minTextLengthForSuccess', label: 'Min Text Length for Success (chars)', type: 'number', hint: 'Below this, retry with Azure OCR if configured. Default 50.' },
];

const PRONUNCIATION_FIELDS: FieldDef<PronunciationSettings>[] = [
  { key: 'provider', label: 'Pronunciation ASR Provider', type: 'text', hint: 'azure | gemini | whisper | mock | auto (default: auto = azure→gemini→whisper). API keys live in Admin → AI Providers.' },
  { key: 'azureSpeechRegion', label: 'Azure Speech Service Region', type: 'text', hint: 'e.g. uksouth, westeurope.' },
  { key: 'azureLocale', label: 'Azure Locale (ASR)', type: 'text', hint: 'e.g. en-GB, en-US. Default en-GB.' },
  { key: 'whisperBaseUrl', label: 'Whisper Base URL', type: 'url', hint: 'OpenAI: https://api.openai.com/v1 or Groq: https://api.groq.com/openai/v1.' },
  { key: 'whisperModel', label: 'Whisper Model', type: 'text', hint: 'e.g. whisper-1, whisper-large-v3. Default whisper-1.' },
  { key: 'geminiBaseUrl', label: 'Gemini Base URL', type: 'url', hint: 'e.g. https://generativelanguage.googleapis.com/v1beta.' },
  { key: 'geminiModel', label: 'Gemini Model', type: 'text', hint: 'e.g. gemini-3.5-flash. Default gemini-3.5-flash.' },
  { key: 'maxAudioBytes', label: 'Max Audio Upload Size (bytes)', type: 'number', hint: 'Default 15 MB (15728640 bytes).' },
  { key: 'audioRetentionDays', label: 'Audio Retention (days)', type: 'number', hint: 'Days to keep learner audio before cleanup. Default 45.' },
  { key: 'freeTierWeeklyAttemptLimit', label: 'Free-Tier Weekly Attempt Limit', type: 'number', hint: 'Attempts per rolling window, -1 to disable. Default 20.' },
  { key: 'freeTierWindowDays', label: 'Free-Tier Window (days)', type: 'number', hint: 'Rolling window for the weekly limit. Default 7.' },
];

const AUTH_TOKENS_FIELDS: FieldDef<AuthTokensSettings>[] = [
  { key: 'accessTokenLifetimeSeconds', label: 'Access Token Lifetime (seconds)', type: 'number', hint: 'How long access tokens remain valid (e.g. 3600 = 1 hour). Blank uses env default.' },
  { key: 'refreshTokenLifetimeSeconds', label: 'Refresh Token Lifetime (seconds)', type: 'number', hint: 'How long refresh tokens remain valid (e.g. 2592000 = 30 days). Blank uses env default.' },
  { key: 'otpLifetimeSeconds', label: 'OTP / MFA Lifetime (seconds)', type: 'number', hint: 'Validity window for one-time passwords (e.g. 300 = 5 min). Blank uses env default.' },
  { key: 'authenticatorIssuer', label: 'Authenticator Issuer', type: 'text', hint: 'Issuer name shown in authenticator apps (e.g. OET Dr. Hesham). Blank uses env default.' },
];

const WEB_PUSH_FIELDS: FieldDef<WebPushSettings>[] = [
  { key: 'enabled', label: 'Enable Browser Web Push', type: 'checkbox', hint: 'Allow learners to receive browser push notifications (requires VAPID keys in the Push section).' },
];

const SUPPORT_FIELDS: FieldDef<SupportSettings>[] = [
  { key: 'whatsAppNumber', label: 'Support WhatsApp Number (dialable)', type: 'text', hint: 'The number learners message to send payment proof — shown next to every package. Digits only, country code first, no "+", spaces, or dashes (e.g. 447961725989); the server strips those and rejects anything else, because the value goes straight into a wa.me/<number> link. NOT the "WhatsApp Phone Number ID" in the Messaging section — that is the Meta Cloud API sender id and cannot be dialled. Blank falls back to the built-in default number.' },
  { key: 'whatsAppProofTemplate', label: 'Payment-Proof Message Template', type: 'text', hint: 'Message pre-filled for the learner when they tap "Send proof on WhatsApp". Blank uses the built-in wording. wa.me can only pre-fill text — the learner still attaches the screenshot themselves.' },
];

const SECTION_META: { id: SectionId; title: string; description: string }[] = [
  { id: 'email', title: 'Email (Brevo + SMTP)', description: 'Transactional email delivery via Brevo with SMTP fallback.' },
  { id: 'billing', title: 'Billing (Stripe)', description: 'Stripe Checkout, Customer Portal, and webhook signing.' },
  { id: 'paypal', title: 'Payments: PayPal', description: 'PayPal Expanded (embedded) checkout + redirect fallback. Client ID is public (used by the browser SDK); Secret and Webhook ID stay server-side. Enable embedded card fields if your account has Advanced Cards eligibility.' },
  { id: 'sentry', title: 'Sentry', description: 'Error reporting and performance monitoring.' },
  { id: 'backup', title: 'Backup S3', description: 'Off-site database and media backup destination.' },
  { id: 'oauth', title: 'OAuth (Google + Apple + Facebook)', description: 'Social sign-in providers.' },
  { id: 'push', title: 'Push (Browser + APNs + FCM)', description: 'Browser VAPID and native mobile push notifications via Apple and Firebase.' },
  { id: 'uploadScanner', title: 'Upload Scanner (ClamAV)', description: 'Antivirus scanning for learner/admin uploads.' },
  { id: 'zoom', title: 'Zoom Live Classes', description: 'Server-to-server OAuth, Meeting SDK, and webhook verification.' },
  { id: 'speakingWhisper', title: 'Speaking: Whisper Transcription', description: 'Legacy fallback. The whisper-asr row in Admin → AI Providers takes precedence when it has an API key — configure Whisper there to cover Speaking, Pronunciation, and Conversation transcription with one key.' },
  { id: 'speakingLiveKit', title: 'Speaking: LiveKit (Live Rooms)', description: 'LiveKit Cloud WebRTC for live tutor rooms and egress recording.' },
  { id: 'speakingAi', title: 'Speaking: AI Providers', description: 'Anthropic (Claude scoring + patient turns) and ElevenLabs (AI patient TTS voice).' },
  { id: 'speakingStorage', title: 'Speaking: Recording Storage (AWS S3)', description: 'AWS S3 bucket for speaking session recording archive.' },
  { id: 'speakingCompliance', title: 'Speaking: Compliance & Retention', description: 'Consent versioning, recording retention windows, and audit log retention.' },
  { id: 'speakingFeatures', title: 'Speaking: Feature Flags', description: 'Controls the Speaking v2 module rollout to learners.' },
  { id: 'checkoutCom', title: 'Payments: Checkout.com', description: 'Premium MENA + global cards (3DS2, Apple/Google Pay, mada). Keys override env config; webhook verification uses the same key.' },
  { id: 'bunnyStream', title: 'Video Library: Bunny Stream', description: 'Bunny Stream hosting for the Video Library — library credentials, CDN token-auth key, webhook secret, and the app-only playback attestation keys. Test does a non-destructive Bunny API probe. Videos play only in the desktop/mobile apps; leave disabled to keep the whole feature dormant.' },
  { id: 'paymob', title: 'Payments: Paymob', description: 'Egypt — cards, Meeza, Fawry, wallets. Keys override env config; webhook verification uses the same HMAC secret.' },
  { id: 'easyKash', title: 'Payments: EasyKash', description: 'Egypt hosted Direct-Pay — cards, wallets, Fawry, instalments. Appears at checkout (under Pay globally, below PayPal) only once BOTH the API key and HMAC secret are set. Set the EasyKash dashboard Callback URL to /v1/payment/webhooks/easykash. Currency mode toggles GBP passthrough vs FX-convert to EGP.' },
  { id: 'payTabs', title: 'Payments: PayTabs', description: 'Gulf + Egypt — cards, mada, KNET, Apple Pay. Keys override env config; webhook verification uses the same key.' },
  { id: 'soketi', title: 'Realtime: Soketi', description: 'Server-side websocket push. The browser client key comes from NEXT_PUBLIC_* env — changes here affect server dispatch only.' },
  { id: 'dataRetention', title: 'Data Retention', description: 'Retention windows for high-volume event tables (analytics, audit, payment webhooks, notification attempts) and the sweeper cadence.' },
  { id: 'expertAutoAssignment', title: 'Expert Auto-Assignment', description: 'Writing-review auto-assignment loop: enablement, SLA windows, per-expert load cap, and batch sizes.' },
  { id: 'passwordPolicy', title: 'Password Policy', description: 'Complexity requirements and the HaveIBeenPwned breach check for new/changed passwords.' },
  { id: 'aiAssistant', title: 'AI Assistant', description: 'Codebase AI assistant orchestration: ReAct loop limits, circuit breaker, command/backup limits, and embedding/indexing knobs. Provider API keys live in AI Providers.' },
  { id: 'aiGateway', title: 'AI Gateway & Tooling', description: 'Grounded gateway defaults (provider, model, temperature, max tokens) and AI tool knobs (tool-call cap, grant cache, external-network allowlist/budget). API key managed in AI Providers.' },
  { id: 'writing', title: 'Writing Module', description: 'Writing V2 feature flags and tunables: cron kill switch, coach hint limits/cost cap, OCR pipeline + GCV key, appeals, tutor-review queue gates, plan regen, and grade idempotency.' },
  { id: 'platform', title: 'Platform', description: 'Public-facing API/Web base URLs (external auth callbacks, CSRF origin validation) and the fallback email domain for synthesized learner emails.' },
  { id: 'messaging', title: 'Messaging (SMS / WhatsApp)', description: 'Billing-notification channels: Twilio SMS and Meta WhatsApp Business Cloud. Auth Token and Access Token are stored encrypted.' },
  { id: 'fx', title: 'FX / Currency', description: 'Currency conversion provider for dynamic pricing. The API key is stored encrypted; empty key falls back to offline seed rates.' },
  { id: 'billingCore', title: 'Billing Core (non-gateway)', description: 'Core billing knobs: default currency/region, wallet currency + top-up tiers, and webhook replay window/attempts. Gateway credentials live in the Billing/Stripe/PayPal/Checkout.com/Paymob/PayTabs sections; the PayPal live/sandbox switch is in the Payments: PayPal section.' },
  { id: 'storage', title: 'Storage (S3 / Object Store)', description: 'S3-compatible object storage (AWS S3, DigitalOcean Spaces, Cloudflare R2) and content-upload limits. Access keys are stored encrypted. Filesystem paths stay env-only; provider switch needs a restart.' },
  { id: 'pdfExtraction', title: 'PDF Extraction', description: 'PDF text-extraction provider and Azure Document Intelligence OCR fallback. The Azure key is stored encrypted.' },
  { id: 'pronunciation', title: 'Pronunciation', description: 'Pronunciation ASR provider selection, region/locale, Whisper/Gemini base-urls + models, audio limits, retention, and free-tier gating. API keys live in Admin → AI Providers.' },
  { id: 'authTokens', title: 'Auth Tokens', description: 'Access/refresh/OTP token lifetimes and the authenticator issuer label. Signing keys, issuer, and audience stay env-only (trust anchors).' },
  { id: 'webPush', title: 'Web Push', description: 'Browser web-push master toggle. VAPID keys are configured in the Push section.' },
  { id: 'support', title: 'Support WhatsApp', description: 'The public WhatsApp number learners message to send payment proof, plus the pre-filled message. Shown next to every package and on every checkout/billing surface. Stored in plaintext — it is a public number, not a secret. Separate from the Messaging section, which holds the Meta Cloud API sender credentials for automated notifications.' },
];

/* ───────────────────────── Helpers ───────────────────────── */

function emptyResponse(): RuntimeSettingsResponse {
  return {
    email: {
      brevoApiKey: '',
      brevoEmailVerificationTemplateId: null,
      brevoPasswordResetTemplateId: null,
      smtpHost: '',
      smtpPort: null,
      smtpUsername: '',
      smtpPassword: '',
      smtpFromAddress: '',
      smtpFromName: '',
      brevoWelcomeTemplateId: null,
      brevoPasswordChangedTemplateId: null,
      brevoMfaEnabledTemplateId: null,
      brevoAdminInviteTemplateId: null,
      brevoSecurityAlertTemplateId: null,
      brevoReviewCompletedTemplateId: null,
      brevoWebhookSecret: '',
      brevoEnabled: null,
      smtpEnabled: null,
      smtpEnableSsl: null,
    },
    billing: {
      stripeSecretKey: '',
      stripePublishableKey: '',
      stripeWebhookSecret: '',
      stripeSuccessUrl: '',
      stripeCancelUrl: '',
      publicAppBaseUrl: '',
      paypalClientId: '',
      paypalClientSecret: '',
      paypalWebhookId: '',
      paypalSuccessUrl: '',
      paypalCancelUrl: '',
      paypalAdvancedCardsEnabled: null,
    },
    sentry: { dsn: '', environment: '', sampleRate: null },
    backup: {
      s3Url: '',
      awsAccessKeyId: '',
      awsSecretAccessKey: '',
      gpgPassphrase: '',
      alertWebhook: '',
    },
    oauth: {
      googleClientId: '',
      googleClientSecret: '',
      appleClientId: '',
      appleTeamId: '',
      appleKeyId: '',
      applePrivateKey: '',
      facebookAppId: '',
      facebookAppSecret: '',
      linkedInClientId: '',
      linkedInClientSecret: '',
      linkedInEnabled: null,
      googleAuthEnabled: null,
      facebookAuthEnabled: null,
    },
    push: {
      apnsKeyId: '',
      apnsTeamId: '',
      apnsBundleId: '',
      apnsAuthKey: '',
      fcmServerKey: '',
      fcmProjectId: '',
      vapidSubject: '',
      vapidPublicKey: '',
      vapidPrivateKey: '',
    },
    uploadScanner: {
      provider: '',
      host: '',
      port: null,
      timeoutSeconds: null,
      failClosedOnError: null,
    },
    zoom: {
      enabled: null,
      accountId: '',
      clientId: '',
      clientSecret: '',
      apiBaseUrl: '',
      tokenUrl: '',
      hostUserId: '',
      meetingSdkKey: '',
      meetingSdkSecret: '',
      webhookSecretToken: '',
      webhookRetryToleranceSeconds: null,
      allowSandboxFallback: null,
    },
    speakingWhisper: {
      apiKey: '',
      baseUrl: '',
      model: '',
      isConfigured: false,
    },
    speakingLiveKit: {
      provider: '',
      apiKey: '',
      apiSecret: '',
      wssUrl: '',
      webhookSigningSecret: '',
      egressBucket: '',
      defaultMaxDurationSeconds: null,
      egressEnabled: null,
      isEnabled: false,
    },
    speakingAi: {
      anthropicApiKey: '',
      elevenLabsApiKey: '',
      isAnthropicConfigured: false,
      isElevenLabsConfigured: false,
    },
    speakingStorage: {
      awsAccessKeyId: '',
      awsSecretAccessKey: '',
      region: '',
      bucket: '',
      isConfigured: false,
    },
    speakingCompliance: {
      currentConsentVersion: '',
      currentLiveVideoConsentVersion: '',
      retentionDaysDefault: null,
      retentionDaysWhenTutorReviewed: null,
      auditLogRetentionDays: null,
    },
    speakingFeatures: {
      speakingV2Enabled: null,
    },
    checkoutCom: {
      apiBaseUrl: '',
      secretKey: '',
      publicKey: '',
      processingChannelId: '',
      webhookSecret: '',
      successUrl: '',
      cancelUrl: '',
      isConfigured: false,
    },
    bunnyStream: {
      enabled: null,
      libraryId: '',
      apiKey: '',
      cdnHostname: '',
      tokenAuthKey: '',
      webhookSecret: '',
      collectionId: '',
      playbackTokenTtlSeconds: null,
      videoAttestationKeysJson: '',
      videoAttestationKeys: '',
      videoAttestationKeyIds: [],
      isConfigured: false,
    },
    paymob: {
      apiBaseUrl: '',
      apiKey: '',
      merchantId: '',
      hmacSecret: '',
      integrationIdsJson: '',
      iframeId: null,
      successUrl: '',
      cancelUrl: '',
      isConfigured: false,
    },
    easyKash: {
      apiBaseUrl: '',
      apiKey: '',
      hmacSecret: '',
      paymentOptionsCsv: '',
      currencyMode: 'passthrough',
      successUrl: '',
      cancelUrl: '',
      isConfigured: false,
    },
    payTabs: {
      apiBaseUrl: '',
      serverKey: '',
      profileId: '',
      webhookSecret: '',
      successUrl: '',
      cancelUrl: '',
      isConfigured: false,
    },
    soketi: {
      host: '',
      port: null,
      appId: '',
      appKey: '',
      appSecret: '',
      useTls: null,
      enabled: null,
    },
    dataRetention: {
      analyticsEventsDays: null,
      auditEventsDays: null,
      paymentWebhookEventsDays: null,
      paymentWebhookPiiNullOutAgeDays: null,
      notificationDeliveryAttemptsDays: null,
      sweepIntervalHours: null,
      batchSize: null,
    },
    expertAutoAssignment: {
      enabled: null,
      pollingIntervalSeconds: null,
      slaEscalationIntervalSeconds: null,
      slaHoursStandard: null,
      slaHoursExpress: null,
      maxActiveAssignmentsPerExpert: null,
      lookbackHoursForLoad: null,
      batchSize: null,
    },
    passwordPolicy: {
      minimumLength: null,
      requireMixedCase: null,
      requireDigit: null,
      requireSymbol: null,
      breachCheckEnabled: null,
      breachApiBaseUrl: '',
      breachApiTimeoutSeconds: null,
    },
    aiAssistant: {
      globalEnabled: null,
      requireApprovalAlways: null,
      maxIterations: null,
      maxContextMessages: null,
      backupRetentionDays: null,
      maxWriteFileSizeBytes: null,
      commandTimeoutSeconds: null,
      circuitBreakerMaxFailures: null,
      circuitBreakerFailureWindowSeconds: null,
      circuitBreakerMaxWrites: null,
      circuitBreakerWriteWindowSeconds: null,
      embeddingModel: '',
      maxChunkTokens: null,
    },
    aiGateway: {
      aiProviderProviderId: '',
      aiProviderBaseUrl: '',
      aiProviderDefaultModel: '',
      aiProviderReasoningEffort: '',
      aiProviderDefaultMaxTokens: null,
      aiProviderDefaultTemperature: null,
      aiToolMaxToolCallsPerCompletion: null,
      aiToolFeatureGrantCacheSeconds: null,
      aiToolAllowedExternalHostsCsv: '',
      aiToolExternalNetworkPerUserDailyCalls: null,
      aiToolExternalNetworkTimeoutMilliseconds: null,
      aiToolExternalNetworkMaxResponseBytes: null,
    },
    writing: {
      cronsEnabled: null,
      coachEnabled: null,
      coachDailyCostCapPerLearnerUsd: null,
      coachMaxHintsPerSession: null,
      coachMinSecondsBetweenHints: null,
      gcvApiKey: '',
      ocrEnabled: null,
      appealsEnabled: null,
      tutorReviewQueueMaxDepth: null,
      tutorReviewMaxWaitHours: null,
      maxDailyPlanRegenerationsPerDay: null,
      gradeIdempotencyTtlHours: null,
    },
    platform: {
      publicApiBaseUrl: '',
      publicWebBaseUrl: '',
      fallbackEmailDomain: '',
    },
    messaging: {
      twilioEnabled: null,
      twilioApiBaseUrl: '',
      twilioAccountSid: '',
      twilioAuthToken: '',
      twilioFromNumber: '',
      twilioMessagingServiceSid: '',
      whatsAppEnabled: null,
      whatsAppApiBaseUrl: '',
      whatsAppAccessToken: '',
      whatsAppPhoneNumberId: '',
      whatsAppFallbackTemplateName: '',
    },
    fx: {
      baseCurrency: '',
      apiKey: '',
      apiBaseUrl: '',
      dynamicPricingEnabled: null,
    },
    billingCore: {
      checkoutBaseUrl: '',
      webhookMaxAgeSeconds: null,
      webhookMaxAttempts: null,
      defaultCurrency: '',
      defaultRegion: '',
      walletCurrency: '',
      walletTopUpTiersJson: '',
      paypalUseSandbox: null,
      paypalApiBaseUrl: '',
    },
    storage: {
      provider: '',
      bucketName: '',
      endpointUrl: '',
      accessKeyId: '',
      secretAccessKey: '',
      awsRegion: '',
      signedReadTtlSeconds: null,
      maxAudioBytes: null,
      maxPdfBytes: null,
      maxImageBytes: null,
      maxZipBytes: null,
      maxZipEntries: null,
      maxZipEntryBytes: null,
      maxZipUncompressedBytes: null,
      maxZipCompressionRatio: null,
      chunkSizeBytes: null,
      stagingTtlHours: null,
    },
    pdfExtraction: {
      provider: '',
      azureEndpoint: '',
      azureApiKey: '',
      minTextLengthForSuccess: null,
    },
    pronunciation: {
      provider: '',
      azureSpeechRegion: '',
      azureLocale: '',
      whisperBaseUrl: '',
      whisperModel: '',
      geminiBaseUrl: '',
      geminiModel: '',
      maxAudioBytes: null,
      audioRetentionDays: null,
      freeTierWeeklyAttemptLimit: null,
      freeTierWindowDays: null,
    },
    authTokens: {
      accessTokenLifetimeSeconds: null,
      refreshTokenLifetimeSeconds: null,
      otpLifetimeSeconds: null,
      authenticatorIssuer: '',
    },
    webPush: {
      enabled: null,
    },
    support: {
      whatsAppNumber: '',
      whatsAppProofTemplate: '',
      isWhatsAppConfigured: false,
    },
    updatedBy: null,
    updatedByUserId: null,
    updatedAt: null,
  };
}

function normalizeResponse(data: Partial<RuntimeSettingsResponse>): RuntimeSettingsResponse {
  const empty = emptyResponse();
  return sanitizeSecretFields({
    ...empty,
    ...data,
    email: { ...empty.email, ...data.email },
    billing: { ...empty.billing, ...data.billing },
    sentry: { ...empty.sentry, ...data.sentry },
    backup: { ...empty.backup, ...data.backup },
    oauth: { ...empty.oauth, ...data.oauth },
    push: { ...empty.push, ...data.push },
    uploadScanner: { ...empty.uploadScanner, ...data.uploadScanner },
    zoom: { ...empty.zoom, ...data.zoom },
    speakingWhisper: { ...empty.speakingWhisper, ...data.speakingWhisper },
    speakingLiveKit: { ...empty.speakingLiveKit, ...data.speakingLiveKit },
    speakingAi: { ...empty.speakingAi, ...data.speakingAi },
    speakingStorage: { ...empty.speakingStorage, ...data.speakingStorage },
    speakingCompliance: { ...empty.speakingCompliance, ...data.speakingCompliance },
    speakingFeatures: { ...empty.speakingFeatures, ...data.speakingFeatures },
    checkoutCom: { ...empty.checkoutCom, ...data.checkoutCom },
    bunnyStream: {
      ...empty.bunnyStream,
      ...data.bunnyStream,
      // GET returns the masked "videoAttestationKeys" indicator; surface it as the
      // draft's write field so the SecretField shows "Set" when configured.
      videoAttestationKeysJson: data.bunnyStream?.videoAttestationKeys ?? '',
    },
    paymob: { ...empty.paymob, ...data.paymob },
    payTabs: { ...empty.payTabs, ...data.payTabs },
    easyKash: { ...empty.easyKash, ...data.easyKash },
    soketi: { ...empty.soketi, ...data.soketi },
    dataRetention: { ...empty.dataRetention, ...data.dataRetention },
    expertAutoAssignment: { ...empty.expertAutoAssignment, ...data.expertAutoAssignment },
    passwordPolicy: { ...empty.passwordPolicy, ...data.passwordPolicy },
    aiAssistant: { ...empty.aiAssistant, ...data.aiAssistant },
    aiGateway: { ...empty.aiGateway, ...data.aiGateway },
    writing: { ...empty.writing, ...data.writing },
    platform: { ...empty.platform, ...data.platform },
    messaging: { ...empty.messaging, ...data.messaging },
    fx: { ...empty.fx, ...data.fx },
    billingCore: { ...empty.billingCore, ...data.billingCore },
    storage: { ...empty.storage, ...data.storage },
    pdfExtraction: { ...empty.pdfExtraction, ...data.pdfExtraction },
    pronunciation: { ...empty.pronunciation, ...data.pronunciation },
    authTokens: { ...empty.authTokens, ...data.authTokens },
    webPush: { ...empty.webPush, ...data.webPush },
    support: { ...empty.support, ...data.support },
  });
}

function sanitizeSecretFields(data: RuntimeSettingsResponse): RuntimeSettingsResponse {
  return {
    ...data,
    email: {
      ...data.email,
      brevoApiKey: maskUnexpectedSecret(data.email.brevoApiKey),
      smtpPassword: maskUnexpectedSecret(data.email.smtpPassword),
      brevoWebhookSecret: maskUnexpectedSecret(data.email.brevoWebhookSecret),
    },
    billing: {
      ...data.billing,
      stripeSecretKey: maskUnexpectedSecret(data.billing.stripeSecretKey),
      stripeWebhookSecret: maskUnexpectedSecret(data.billing.stripeWebhookSecret),
      paypalClientSecret: maskUnexpectedSecret(data.billing.paypalClientSecret),
      paypalWebhookId: maskUnexpectedSecret(data.billing.paypalWebhookId),
    },
    backup: {
      ...data.backup,
      awsSecretAccessKey: maskUnexpectedSecret(data.backup.awsSecretAccessKey),
      gpgPassphrase: maskUnexpectedSecret(data.backup.gpgPassphrase),
    },
    oauth: {
      ...data.oauth,
      googleClientSecret: maskUnexpectedSecret(data.oauth.googleClientSecret),
      applePrivateKey: maskUnexpectedSecret(data.oauth.applePrivateKey),
      facebookAppSecret: maskUnexpectedSecret(data.oauth.facebookAppSecret),
      linkedInClientId: maskUnexpectedSecret(data.oauth.linkedInClientId),
      linkedInClientSecret: maskUnexpectedSecret(data.oauth.linkedInClientSecret),
    },
    push: {
      ...data.push,
      apnsAuthKey: maskUnexpectedSecret(data.push.apnsAuthKey),
      fcmServerKey: maskUnexpectedSecret(data.push.fcmServerKey),
      vapidPrivateKey: maskUnexpectedSecret(data.push.vapidPrivateKey),
    },
    zoom: {
      ...data.zoom,
      clientSecret: maskUnexpectedSecret(data.zoom.clientSecret),
      meetingSdkSecret: maskUnexpectedSecret(data.zoom.meetingSdkSecret),
      webhookSecretToken: maskUnexpectedSecret(data.zoom.webhookSecretToken),
    },
    speakingLiveKit: {
      ...data.speakingLiveKit,
      apiKey: maskUnexpectedSecret(data.speakingLiveKit.apiKey),
      apiSecret: maskUnexpectedSecret(data.speakingLiveKit.apiSecret),
      webhookSigningSecret: maskUnexpectedSecret(data.speakingLiveKit.webhookSigningSecret),
    },
    speakingAi: {
      ...data.speakingAi,
      anthropicApiKey: maskUnexpectedSecret(data.speakingAi.anthropicApiKey),
      elevenLabsApiKey: maskUnexpectedSecret(data.speakingAi.elevenLabsApiKey),
    },
    speakingStorage: {
      ...data.speakingStorage,
      awsSecretAccessKey: maskUnexpectedSecret(data.speakingStorage.awsSecretAccessKey),
    },
    checkoutCom: {
      ...data.checkoutCom,
      secretKey: maskUnexpectedSecret(data.checkoutCom.secretKey),
      webhookSecret: maskUnexpectedSecret(data.checkoutCom.webhookSecret),
    },
    bunnyStream: {
      ...data.bunnyStream,
      apiKey: maskUnexpectedSecret(data.bunnyStream.apiKey),
      tokenAuthKey: maskUnexpectedSecret(data.bunnyStream.tokenAuthKey),
      webhookSecret: maskUnexpectedSecret(data.bunnyStream.webhookSecret),
      videoAttestationKeysJson: maskUnexpectedSecret(data.bunnyStream.videoAttestationKeysJson),
    },
    paymob: {
      ...data.paymob,
      apiKey: maskUnexpectedSecret(data.paymob.apiKey),
      hmacSecret: maskUnexpectedSecret(data.paymob.hmacSecret),
    },
    easyKash: {
      ...data.easyKash,
      apiKey: maskUnexpectedSecret(data.easyKash.apiKey),
      hmacSecret: maskUnexpectedSecret(data.easyKash.hmacSecret),
    },
    payTabs: {
      ...data.payTabs,
      serverKey: maskUnexpectedSecret(data.payTabs.serverKey),
      webhookSecret: maskUnexpectedSecret(data.payTabs.webhookSecret),
    },
    soketi: {
      ...data.soketi,
      appSecret: maskUnexpectedSecret(data.soketi.appSecret),
    },
    writing: {
      ...data.writing,
      gcvApiKey: maskUnexpectedSecret(data.writing.gcvApiKey),
    },
    messaging: {
      ...data.messaging,
      twilioAuthToken: maskUnexpectedSecret(data.messaging.twilioAuthToken),
      whatsAppAccessToken: maskUnexpectedSecret(data.messaging.whatsAppAccessToken),
    },
    fx: {
      ...data.fx,
      apiKey: maskUnexpectedSecret(data.fx.apiKey),
    },
    storage: {
      ...data.storage,
      accessKeyId: maskUnexpectedSecret(data.storage.accessKeyId),
      secretAccessKey: maskUnexpectedSecret(data.storage.secretAccessKey),
    },
    pdfExtraction: {
      ...data.pdfExtraction,
      azureApiKey: maskUnexpectedSecret(data.pdfExtraction.azureApiKey),
    },
  };
}

function maskUnexpectedSecret(value: string): string {
  return value && value !== MASKED ? MASKED : value;
}

function parseNullableNumberInput(value: string): number | null {
  if (value.trim() === '') return null;
  return Number(value);
}

function getApiErrorMessage(err: unknown, fallback: string): string {
  if (err instanceof Error && err.message) return redactPotentialSecrets(err.message);
  if (typeof err === 'string') return redactPotentialSecrets(err);
  return fallback;
}

function redactPotentialSecrets(value: string): string {
  return value.replace(
    /(?:github_pat_[A-Za-z0-9_]{20,}|ghp_[A-Za-z0-9]{20,}|sk-[A-Za-z0-9_-]{20,}|sk_live_[A-Za-z0-9_]{12,}|whsec_[A-Za-z0-9_]{12,}|AIza[0-9A-Za-z_-]{20,})/gi,
    '***REDACTED***',
  );
}

function formatTimestamp(value: string | null): string {
  if (!value) return 'unknown time';
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) return value;
  return date.toLocaleString();
}

/* ───────────────────────── SecretField ───────────────────────── */

interface SecretFieldProps {
  label: string;
  hint?: string;
  /** Original server value: '' = unset, '********' = set, anything else = literal. */
  serverValue: string;
  /** Working draft value (same conventions as serverValue). */
  draftValue: string;
  onChange: (next: string) => void;
}

function SecretField({ label, hint, serverValue, draftValue, onChange }: SecretFieldProps) {
  const reactId = useId();
  const inputId = `secret-${reactId}`;
  const [reveal, setReveal] = useState(false);

  const isSetOnServer = serverValue === MASKED;
  const isUntouched = draftValue === serverValue;
  // Display empty when untouched + server-set so user sees the placeholder, not the literal '********'.
  const displayValue = isUntouched && isSetOnServer ? '' : draftValue;

  return (
    <div className="flex flex-col gap-1.5">
      <div className="flex items-center justify-between gap-2">
        <label htmlFor={inputId} className="text-sm font-semibold tracking-tight text-admin-fg-strong">
          {label}
        </label>
        {isSetOnServer ? (
          <Badge variant="success">Set</Badge>
        ) : (
          <Badge variant="default">Not set</Badge>
        )}
      </div>
      <div className="flex items-stretch gap-2">
        <div className="relative flex-1">
          <input
            id={inputId}
            type={reveal ? 'text' : 'password'}
            value={displayValue}
            placeholder={isSetOnServer ? MASKED : 'Enter value'}
            onChange={(event) => onChange(event.target.value)}
            autoComplete="off"
            spellCheck={false}
            className="w-full rounded-admin border border-admin-border bg-admin-bg-surface px-4 py-3 pr-12 text-sm text-admin-fg-default shadow-sm transition-[border-color,box-shadow] focus:border-[var(--admin-primary)] focus:outline-none focus:ring-2 focus:ring-[var(--admin-primary)]"
          />
          <button
            type="button"
            onClick={() => setReveal((v) => !v)}
            className="absolute inset-y-0 right-2 my-auto flex h-9 w-9 items-center justify-center rounded-admin text-admin-fg-muted hover:bg-[var(--admin-state-hover)] focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[var(--admin-primary)]"
            aria-label={reveal ? `Hide ${label}` : `Show ${label}`}
          >
            {reveal ? <EyeOff className="h-4 w-4" aria-hidden="true" /> : <Eye className="h-4 w-4" aria-hidden="true" />}
          </button>
        </div>
        {isSetOnServer && (
          <Button
            type="button"
            variant="outline"
            size="sm"
            onClick={() => onChange('')}
            aria-label={`Clear ${label}`}
          >
            Clear
          </Button>
        )}
      </div>
      {hint ? <p className="text-xs leading-5 text-admin-fg-muted">{hint}</p> : null}
    </div>
  );
}

/* ───────────────────────── PlainField ───────────────────────── */

interface PlainFieldProps {
  label: string;
  hint?: string;
  type?: 'text' | 'number' | 'url' | 'checkbox' | 'select';
  options?: { value: string; label: string }[];
  value: string | number | boolean | null | undefined;
  onChange: (next: string | boolean) => void;
}

function PlainField({ label, hint, type = 'text', options, value, onChange }: PlainFieldProps) {
  const reactId = useId();
  const inputId = `plain-${reactId}`;
  if (type === 'select') {
    return (
      <div className="flex flex-col gap-1.5">
        <label htmlFor={inputId} className="text-sm font-semibold tracking-tight text-admin-fg-strong">
          {label}
        </label>
        <select
          id={inputId}
          value={value === null || value === undefined ? '' : String(value)}
          onChange={(event) => onChange(event.target.value)}
          className="w-full rounded-admin border border-admin-border bg-admin-bg-surface px-4 py-3 text-sm text-admin-fg-default shadow-sm transition-[border-color,box-shadow] focus:border-[var(--admin-primary)] focus:outline-none focus:ring-2 focus:ring-[var(--admin-primary)]"
        >
          {(options ?? []).map((opt) => (
            <option key={opt.value} value={opt.value}>
              {opt.label}
            </option>
          ))}
        </select>
        {hint ? <p className="text-xs leading-5 text-admin-fg-muted">{hint}</p> : null}
      </div>
    );
  }
  if (type === 'checkbox') {
    return (
      <div className="flex flex-col gap-1.5">
        <label htmlFor={inputId} className="flex items-center gap-3 text-sm font-semibold tracking-tight text-admin-fg-strong">
          <input
            id={inputId}
            type="checkbox"
            checked={Boolean(value)}
            onChange={(event) => onChange(event.target.checked)}
            className="h-4 w-4 rounded border-admin-border text-[var(--admin-primary)] focus:ring-[var(--admin-primary)]"
          />
          {label}
        </label>
        {hint ? <p className="text-xs leading-5 text-admin-fg-muted">{hint}</p> : null}
      </div>
    );
  }

  return (
    <div className="flex flex-col gap-1.5">
      <label htmlFor={inputId} className="text-sm font-semibold tracking-tight text-admin-fg-strong">
        {label}
      </label>
      <input
        id={inputId}
        type={type}
        value={value === null || value === undefined ? '' : String(value)}
        onChange={(event) => onChange(event.target.value)}
        spellCheck={false}
        className="w-full rounded-admin border border-admin-border bg-admin-bg-surface px-4 py-3 text-sm text-admin-fg-default shadow-sm transition-[border-color,box-shadow] focus:border-[var(--admin-primary)] focus:outline-none focus:ring-2 focus:ring-[var(--admin-primary)]"
      />
      {hint ? <p className="text-xs leading-5 text-admin-fg-muted">{hint}</p> : null}
    </div>
  );
}

/* ───────────────────────── Section ───────────────────────── */

interface SectionProps {
  id: SectionId;
  title: string;
  description: string;
  open: boolean;
  onToggle: () => void;
  testing: boolean;
  testStatus?: RuntimeSettingsIntegrationTestResponse;
  onTest: () => void;
  children: ReactNode;
}

function Section({ id, title, description, open, onToggle, testing, testStatus, onTest, children }: SectionProps) {
  const headingId = `runtime-settings-${id}-heading`;
  return (
    <Card
      role="region"
      aria-labelledby={headingId}
      id={`runtime-settings-${id}`}
      className="scroll-mt-24"
    >
      <button
        type="button"
        onClick={onToggle}
        aria-expanded={open}
        aria-controls={`runtime-settings-${id}-body`}
        className="flex w-full items-center justify-between gap-4 px-5 py-4 text-left rounded-admin-lg focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[var(--admin-primary)]"
      >
        <span>
          <span id={headingId} className="block text-base font-semibold text-admin-fg-strong">
            {title}
          </span>
          <span className="mt-0.5 block text-xs text-admin-fg-muted">{description}</span>
        </span>
        {open ? (
          <ChevronDown className="h-5 w-5 text-admin-fg-muted" aria-hidden="true" />
        ) : (
          <ChevronRight className="h-5 w-5 text-admin-fg-muted" aria-hidden="true" />
        )}
      </button>
      {open && (
        <div id={`runtime-settings-${id}-body`} className="border-t border-admin-border px-5 py-5">
          <div className="mb-5 flex flex-col gap-3 rounded-admin border border-admin-border bg-admin-bg-subtle p-4 sm:flex-row sm:items-center sm:justify-between">
            <div>
              <div className="flex items-center gap-2">
                <span className="text-sm font-semibold text-admin-fg-strong">Connection check</span>
                {testStatus ? (
                  <Badge variant={testStatus.status === 'ok' ? 'success' : 'danger'}>
                    {testStatus.status === 'ok' ? 'Green' : 'Failed'}
                  </Badge>
                ) : (
                  <Badge variant="default">Not tested</Badge>
                )}
              </div>
              <p className="mt-1 text-xs leading-5 text-admin-fg-muted">
                {testStatus?.message ?? 'Runs a non-mutating sandbox/format check; no live learner data is sent.'}
              </p>
            </div>
            <Button
              type="button"
              variant="outline"
              size="sm"
              onClick={onTest}
              disabled={testing}
              loading={testing}
              loadingText="Testing..."
              aria-label={`Test ${title}`}
            >
              Test
            </Button>
          </div>
          <div className="grid grid-cols-1 gap-5 md:grid-cols-2">{children}</div>
        </div>
      )}
    </Card>
  );
}

/* ───────────────────────── Loading skeleton ───────────────────────── */

function LoadingState() {
  return (
    <div className="space-y-4" aria-busy="true" aria-live="polite">
      {SECTION_META.map((s) => (
        <Card key={s.id}>
          <CardContent className="p-5 pt-5">
            <Skeleton className="h-5 w-1/3" />
            <Skeleton className="mt-3 h-4 w-2/3" />
            <div className="mt-4 grid grid-cols-1 gap-3 md:grid-cols-2">
              <Skeleton className="h-12 w-full" />
              <Skeleton className="h-12 w-full" />
            </div>
          </CardContent>
        </Card>
      ))}
    </div>
  );
}

/* ───────────────────────── Locked state ───────────────────────── */

function LockedState() {
  return (
    <Card surface="tinted-warning">
      <CardContent className="p-8 pt-8">
        <EmptyState
          variant="error"
          size="md"
          illustration={<Lock aria-hidden="true" />}
          title="System administrator access required"
          description="Runtime Settings exposes production secrets and is gated behind the system_admin permission. Ask a super-admin to grant access if you need to change Brevo, Stripe, Sentry, OAuth, push, Zoom, or backup credentials from this UI."
        />
      </CardContent>
    </Card>
  );
}

/* ───────────────────────── Main client ───────────────────────── */

export function RuntimeSettingsClient() {
  const { user, loading: authLoading } = useAuth();
  const isSystemAdmin = hasPermission(user?.adminPermissions ?? null, AdminPermission.SystemAdmin);

  const [server, setServer] = useState<RuntimeSettingsResponse | null>(null);
  const [draft, setDraft] = useState<RuntimeSettingsResponse | null>(null);
  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  const [testingSections, setTestingSections] = useState<Partial<Record<SectionId, boolean>>>({});
  const [testStatuses, setTestStatuses] = useState<TestStatusState>({});
  const [toast, setToast] = useState<ToastState>(null);
  const [openSections, setOpenSections] = useState<Record<SectionId, boolean>>({
    email: true,
    billing: false,
    paypal: false,
    sentry: false,
    backup: false,
    oauth: false,
    push: false,
    uploadScanner: false,
    zoom: false,
    speakingWhisper: false,
    speakingLiveKit: false,
    speakingAi: false,
    speakingStorage: false,
    speakingCompliance: false,
    speakingFeatures: false,
    checkoutCom: false,
    bunnyStream: false,
    paymob: false,
    payTabs: false,
    easyKash: false,
    soketi: false,
    dataRetention: false,
    expertAutoAssignment: false,
    passwordPolicy: false,
    aiAssistant: false,
    aiGateway: false,
    writing: false,
    platform: false,
    messaging: false,
    fx: false,
    billingCore: false,
    storage: false,
    pdfExtraction: false,
    pronunciation: false,
    authTokens: false,
    webPush: false,
    support: false,
  });

  const load = useCallback(async () => {
    setLoading(true);
    try {
      const data = await apiClient.get<RuntimeSettingsResponse>('/v1/admin/runtime-settings');
      // Defensive merge so missing keys never blow up the UI.
      const merged = normalizeResponse(data);
      setServer(merged);
      setDraft(merged);
    } catch (err) {
      setToast({ variant: 'error', message: getApiErrorMessage(err, 'Failed to load runtime settings.') });
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    if (!isSystemAdmin) return;
    void load();
  }, [isSystemAdmin, load]);

  const toggleSection = useCallback((id: SectionId) => {
    setOpenSections((prev) => ({ ...prev, [id]: !prev[id] }));
  }, []);

  const updateField = useCallback(
    // Indexes the data payload, so the section param is a key of RuntimeSettingsResponse
    // (NOT a UI SectionId — the "paypal" UI section writes into the "billing" payload).
    <S extends DataSectionId, K extends keyof RuntimeSettingsResponse[S] & string>(
      section: S,
      key: K,
      value: RuntimeSettingsResponse[S][K],
    ) => {
      setDraft((prev) => {
        if (!prev) return prev;
        return {
          ...prev,
          [section]: {
            ...prev[section],
            [key]: value,
          },
        } as RuntimeSettingsResponse;
      });
    },
    [],
  );

  const handleSave = useCallback(async () => {
    if (!draft) return;
    setSaving(true);
    try {
      await apiClient.put('/v1/admin/runtime-settings', draft);
      await load();
      setToast({ variant: 'success', message: 'Runtime settings saved. Changes apply within ~30 seconds.' });
    } catch (err) {
      setToast({ variant: 'error', message: getApiErrorMessage(err, 'Failed to save runtime settings.') });
    } finally {
      setSaving(false);
    }
  }, [draft, load]);

  const handleTest = useCallback(async (section: SectionId) => {
    setTestingSections((prev) => ({ ...prev, [section]: true }));
    try {
      const result = await apiClient.post<RuntimeSettingsIntegrationTestResponse>(
        `/v1/admin/runtime-settings/test/${section}`,
      );
      setTestStatuses((prev) => ({ ...prev, [section]: result }));
      setToast({
        variant: result.status === 'ok' ? 'success' : 'error',
        message: result.message,
      });
    } catch (err) {
      const message = getApiErrorMessage(err, 'Failed to test integration.');
      setTestStatuses((prev) => ({
        ...prev,
        [section]: {
          section,
          status: 'failed',
          message,
          testedAt: new Date().toISOString(),
        },
      }));
      setToast({ variant: 'error', message });
    } finally {
      setTestingSections((prev) => ({ ...prev, [section]: false }));
    }
  }, []);

  const updatedLine = useMemo(() => {
    if (!server) return null;
    if (!server.updatedBy && !server.updatedAt) return null;
    return `Last updated by ${server.updatedBy ?? 'unknown'} at ${formatTimestamp(server.updatedAt)}`;
  }, [server]);

  if (authLoading) {
    return (
      <AdminSettingsLayout
        title="Runtime Settings"
        description="Configure production secrets without editing the server's .env file. Changes apply within ~30 seconds."
        breadcrumbs={[{ label: 'Admin', href: '/admin' }, { label: 'Runtime Settings' }]}
      >
        <LoadingState />
      </AdminSettingsLayout>
    );
  }

  if (!isSystemAdmin) {
    return (
      <AdminSettingsLayout
        title="Runtime Settings"
        description="Configure production secrets without editing the server's .env file. Changes apply within ~30 seconds."
        breadcrumbs={[{ label: 'Admin', href: '/admin' }, { label: 'Runtime Settings' }]}
      >
        <LockedState />
      </AdminSettingsLayout>
    );
  }

  const navItems = SECTION_META.map((s) => ({
    label: s.title,
    href: `#runtime-settings-${s.id}`,
  }));

  return (
    <AdminSettingsLayout
      title="Runtime Settings"
      description="Configure production secrets without editing the server's .env file. Changes apply within ~30 seconds."
      breadcrumbs={[{ label: 'Admin', href: '/admin' }, { label: 'Runtime Settings' }]}
      sidebar={<SettingsNav items={navItems} title="Sections" />}
      mobileNavLabel="Sections"
      actions={
        <Button
          variant="primary"
          onClick={handleSave}
          disabled={saving || !draft}
          loading={saving}
          loadingText="Saving…"
          startIcon={<Save className="h-4 w-4" aria-hidden="true" />}
          aria-label="Save all runtime settings"
        >
          Save All
        </Button>
      }
    >
      {loading || !draft || !server ? (
        <LoadingState />
      ) : (
        <div className="space-y-4">
          {SECTION_META.map((section) => (
            <Section
              key={section.id}
              id={section.id}
              title={section.title}
              description={section.description}
              open={openSections[section.id]}
              onToggle={() => toggleSection(section.id)}
              testing={Boolean(testingSections[section.id])}
              testStatus={testStatuses[section.id]}
              onTest={() => handleTest(section.id)}
            >
              {section.id === 'email' &&
                EMAIL_FIELDS.map((field) =>
                  field.secret ? (
                    <SecretField
                      key={field.key}
                      label={field.label}
                      hint={field.hint}
                      serverValue={String(server.email[field.key] ?? '')}
                      draftValue={String(draft.email[field.key] ?? '')}
                      onChange={(next) => updateField('email', field.key, next as never)}
                    />
                  ) : (
                    <PlainField
                      key={field.key}
                      label={field.label}
                      hint={field.hint}
                      type={field.type}
                      value={draft.email[field.key] as string | number | null}
                      onChange={(next) =>
                        updateField(
                          'email',
                          field.key,
                          (field.type === 'number' && typeof next === 'string' ? parseNullableNumberInput(next) : next) as never,
                        )
                      }
                    />
                  ),
                )}

              {section.id === 'billing' &&
                BILLING_FIELDS.map((field) =>
                  field.secret ? (
                    <SecretField
                      key={field.key}
                      label={field.label}
                      hint={field.hint}
                      serverValue={String(server.billing[field.key] ?? '')}
                      draftValue={String(draft.billing[field.key] ?? '')}
                      onChange={(next) => updateField('billing', field.key, next as never)}
                    />
                  ) : (
                    <PlainField
                      key={field.key}
                      label={field.label}
                      hint={field.hint}
                      type={field.type}
                      value={draft.billing[field.key] as string}
                      onChange={(next) => updateField('billing', field.key, next as never)}
                    />
                  ),
                )}

              {section.id === 'paypal' &&
                PAYPAL_FIELDS.map((field) =>
                  field.secret ? (
                    <SecretField
                      key={field.key}
                      label={field.label}
                      hint={field.hint}
                      serverValue={String(server.billing[field.key] ?? '')}
                      draftValue={String(draft.billing[field.key] ?? '')}
                      onChange={(next) => updateField('billing', field.key, next as never)}
                    />
                  ) : (
                    <PlainField
                      key={field.key}
                      label={field.label}
                      hint={field.hint}
                      type={field.type}
                      value={draft.billing[field.key] as string | boolean | null}
                      onChange={(next) =>
                        updateField('billing', field.key, (field.type === 'checkbox' ? Boolean(next) : next) as never)
                      }
                    />
                  ),
                )}

              {/* Live/sandbox switch — lives with the PayPal credentials it applies to,
                  but persists into the billingCore payload object on save. */}
              {section.id === 'paypal' && (
                <>
                  <PlainField
                    label="Use PayPal Sandbox"
                    hint="UNCHECK this for live student payments. When ON, PayPal calls the sandbox (testing) API even if the credentials above are LIVE — real payments will 401. The credentials and this toggle MUST match (live keys → unchecked; sandbox keys → checked)."
                    type="checkbox"
                    value={draft.billingCore.paypalUseSandbox as boolean | null}
                    onChange={(next) => updateField('billingCore', 'paypalUseSandbox', Boolean(next) as never)}
                  />
                  <PlainField
                    label="PayPal API Base URL (advanced — leave blank)"
                    hint="Optional host override. Blank uses the host implied by the toggle above (live → https://api-m.paypal.com). Only set this for a custom/proxy endpoint."
                    type="url"
                    value={draft.billingCore.paypalApiBaseUrl as string | null}
                    onChange={(next) => updateField('billingCore', 'paypalApiBaseUrl', String(next) as never)}
                  />
                </>
              )}

              {section.id === 'sentry' &&
                SENTRY_FIELDS.map((field) =>
                  field.secret ? (
                    <SecretField
                      key={field.key}
                      label={field.label}
                      hint={field.hint}
                      serverValue={String(server.sentry[field.key] ?? '')}
                      draftValue={String(draft.sentry[field.key] ?? '')}
                      onChange={(next) => updateField('sentry', field.key, next as never)}
                    />
                  ) : (
                    <PlainField
                      key={field.key}
                      label={field.label}
                      hint={field.hint}
                      type={field.type}
                      value={draft.sentry[field.key] as string | number | null}
                      onChange={(next) =>
                        updateField(
                          'sentry',
                          field.key,
                          (field.type === 'number' && typeof next === 'string' ? parseNullableNumberInput(next) : next) as never,
                        )
                      }
                    />
                  ),
                )}

              {section.id === 'backup' &&
                BACKUP_FIELDS.map((field) =>
                  field.secret ? (
                    <SecretField
                      key={field.key}
                      label={field.label}
                      hint={field.hint}
                      serverValue={String(server.backup[field.key] ?? '')}
                      draftValue={String(draft.backup[field.key] ?? '')}
                      onChange={(next) => updateField('backup', field.key, next as never)}
                    />
                  ) : (
                    <PlainField
                      key={field.key}
                      label={field.label}
                      hint={field.hint}
                      type={field.type}
                      value={draft.backup[field.key] as string}
                      onChange={(next) => updateField('backup', field.key, next as never)}
                    />
                  ),
                )}

              {section.id === 'oauth' &&
                OAUTH_FIELDS.map((field) =>
                  field.secret ? (
                    <SecretField
                      key={field.key}
                      label={field.label}
                      hint={field.hint}
                      serverValue={String(server.oauth[field.key] ?? '')}
                      draftValue={String(draft.oauth[field.key] ?? '')}
                      onChange={(next) => updateField('oauth', field.key, next as never)}
                    />
                  ) : (
                    <PlainField
                      key={field.key}
                      label={field.label}
                      hint={field.hint}
                      type={field.type}
                      value={draft.oauth[field.key] as string}
                      onChange={(next) => updateField('oauth', field.key, next as never)}
                    />
                  ),
                )}

              {section.id === 'push' &&
                PUSH_FIELDS.map((field) =>
                  field.secret ? (
                    <SecretField
                      key={field.key}
                      label={field.label}
                      hint={field.hint}
                      serverValue={String(server.push[field.key] ?? '')}
                      draftValue={String(draft.push[field.key] ?? '')}
                      onChange={(next) => updateField('push', field.key, next as never)}
                    />
                  ) : (
                    <PlainField
                      key={field.key}
                      label={field.label}
                      hint={field.hint}
                      type={field.type}
                      value={draft.push[field.key] as string}
                      onChange={(next) => updateField('push', field.key, next as never)}
                    />
                  ),
                )}

              {section.id === 'uploadScanner' &&
                UPLOAD_SCANNER_FIELDS.map((field) => (
                  <PlainField
                    key={field.key}
                    label={field.label}
                    hint={field.hint}
                    type={field.type}
                    value={draft.uploadScanner[field.key] as string | number | boolean | null}
                    onChange={(next) =>
                      updateField(
                        'uploadScanner',
                        field.key,
                        (field.type === 'number'
                          ? parseNullableNumberInput(String(next))
                          : field.type === 'checkbox'
                            ? Boolean(next)
                            : String(next)) as never,
                      )
                    }
                  />
                ))}

              {section.id === 'zoom' &&
                ZOOM_FIELDS.map((field) =>
                  field.secret ? (
                    <SecretField
                      key={field.key}
                      label={field.label}
                      hint={field.hint}
                      serverValue={String(server.zoom[field.key] ?? '')}
                      draftValue={String(draft.zoom[field.key] ?? '')}
                      onChange={(next) => updateField('zoom', field.key, next as never)}
                    />
                  ) : (
                    <PlainField
                      key={field.key}
                      label={field.label}
                      hint={field.hint}
                      type={field.type}
                      value={draft.zoom[field.key] as string | number | boolean | null}
                      onChange={(next) =>
                        updateField(
                          'zoom',
                          field.key,
                          (field.type === 'number'
                            ? parseNullableNumberInput(String(next))
                            : field.type === 'checkbox'
                              ? Boolean(next)
                              : String(next)) as never,
                        )
                      }
                    />
                  ),
                )}

              {section.id === 'speakingWhisper' &&
                SPEAKING_WHISPER_FIELDS.map((field) =>
                  field.secret ? (
                    <SecretField
                      key={field.key}
                      label={field.label}
                      hint={field.hint}
                      serverValue={String(server.speakingWhisper[field.key] ?? '')}
                      draftValue={String(draft.speakingWhisper[field.key] ?? '')}
                      onChange={(next) => updateField('speakingWhisper', field.key, next as never)}
                    />
                  ) : (
                    <PlainField
                      key={field.key}
                      label={field.label}
                      hint={field.hint}
                      type={field.type}
                      value={draft.speakingWhisper[field.key] as string | number | boolean | null}
                      onChange={(next) =>
                        updateField(
                          'speakingWhisper',
                          field.key,
                          (field.type === 'number'
                            ? parseNullableNumberInput(String(next))
                            : field.type === 'checkbox'
                              ? Boolean(next)
                              : String(next)) as never,
                        )
                      }
                    />
                  ),
                )}

              {section.id === 'speakingLiveKit' &&
                SPEAKING_LIVEKIT_FIELDS.map((field) =>
                  field.secret ? (
                    <SecretField
                      key={field.key}
                      label={field.label}
                      hint={field.hint}
                      serverValue={String(server.speakingLiveKit[field.key] ?? '')}
                      draftValue={String(draft.speakingLiveKit[field.key] ?? '')}
                      onChange={(next) => updateField('speakingLiveKit', field.key, next as never)}
                    />
                  ) : (
                    <PlainField
                      key={field.key}
                      label={field.label}
                      hint={field.hint}
                      type={field.type}
                      value={draft.speakingLiveKit[field.key] as string | number | boolean | null}
                      onChange={(next) =>
                        updateField(
                          'speakingLiveKit',
                          field.key,
                          (field.type === 'number'
                            ? parseNullableNumberInput(String(next))
                            : field.type === 'checkbox'
                              ? Boolean(next)
                              : String(next)) as never,
                        )
                      }
                    />
                  ),
                )}

              {section.id === 'speakingAi' &&
                SPEAKING_AI_FIELDS.map((field) =>
                  field.secret ? (
                    <SecretField
                      key={field.key}
                      label={field.label}
                      hint={field.hint}
                      serverValue={String(server.speakingAi[field.key] ?? '')}
                      draftValue={String(draft.speakingAi[field.key] ?? '')}
                      onChange={(next) => updateField('speakingAi', field.key, next as never)}
                    />
                  ) : (
                    <PlainField
                      key={field.key}
                      label={field.label}
                      hint={field.hint}
                      type={field.type}
                      value={draft.speakingAi[field.key] as string | number | boolean | null}
                      onChange={(next) => updateField('speakingAi', field.key, String(next) as never)}
                    />
                  ),
                )}

              {section.id === 'speakingStorage' &&
                SPEAKING_STORAGE_FIELDS.map((field) =>
                  field.secret ? (
                    <SecretField
                      key={field.key}
                      label={field.label}
                      hint={field.hint}
                      serverValue={String(server.speakingStorage[field.key] ?? '')}
                      draftValue={String(draft.speakingStorage[field.key] ?? '')}
                      onChange={(next) => updateField('speakingStorage', field.key, next as never)}
                    />
                  ) : (
                    <PlainField
                      key={field.key}
                      label={field.label}
                      hint={field.hint}
                      type={field.type}
                      value={draft.speakingStorage[field.key] as string | number | boolean | null}
                      onChange={(next) => updateField('speakingStorage', field.key, String(next) as never)}
                    />
                  ),
                )}

              {section.id === 'speakingCompliance' &&
                SPEAKING_COMPLIANCE_FIELDS.map((field) => (
                  <PlainField
                    key={field.key}
                    label={field.label}
                    hint={field.hint}
                    type={field.type}
                    value={draft.speakingCompliance[field.key] as string | number | null}
                    onChange={(next) =>
                      updateField(
                        'speakingCompliance',
                        field.key,
                        (field.type === 'number' && typeof next === 'string' ? parseNullableNumberInput(next) : next) as never,
                      )
                    }
                  />
                ))}

              {section.id === 'speakingFeatures' &&
                SPEAKING_FEATURES_FIELDS.map((field) => (
                  <PlainField
                    key={field.key}
                    label={field.label}
                    hint={field.hint}
                    type={field.type}
                    value={draft.speakingFeatures[field.key] as boolean | null}
                    onChange={(next) => updateField('speakingFeatures', field.key, Boolean(next) as never)}
                  />
                ))}

              {section.id === 'checkoutCom' &&
                CHECKOUT_COM_FIELDS.map((field) =>
                  field.secret ? (
                    <SecretField
                      key={field.key}
                      label={field.label}
                      hint={field.hint}
                      serverValue={String(server.checkoutCom[field.key] ?? '')}
                      draftValue={String(draft.checkoutCom[field.key] ?? '')}
                      onChange={(next) => updateField('checkoutCom', field.key, next as never)}
                    />
                  ) : (
                    <PlainField
                      key={field.key}
                      label={field.label}
                      hint={field.hint}
                      type={field.type}
                      value={draft.checkoutCom[field.key] as string | number | boolean | null}
                      onChange={(next) =>
                        updateField(
                          'checkoutCom',
                          field.key,
                          (field.type === 'number'
                            ? parseNullableNumberInput(String(next))
                            : field.type === 'checkbox'
                              ? Boolean(next)
                              : String(next)) as never,
                        )
                      }
                    />
                  ),
                )}

              {section.id === 'bunnyStream' &&
                BUNNY_STREAM_FIELDS.map((field) =>
                  field.secret ? (
                    <SecretField
                      key={field.key}
                      label={field.label}
                      hint={field.hint}
                      serverValue={String(server.bunnyStream[field.key] ?? '')}
                      draftValue={String(draft.bunnyStream[field.key] ?? '')}
                      onChange={(next) => updateField('bunnyStream', field.key, next as never)}
                    />
                  ) : (
                    <PlainField
                      key={field.key}
                      label={field.label}
                      hint={field.hint}
                      type={field.type}
                      value={draft.bunnyStream[field.key] as string | number | boolean | null}
                      onChange={(next) =>
                        updateField(
                          'bunnyStream',
                          field.key,
                          (field.type === 'number'
                            ? parseNullableNumberInput(String(next))
                            : field.type === 'checkbox'
                              ? Boolean(next)
                              : String(next)) as never,
                        )
                      }
                    />
                  ),
                )}

              {section.id === 'bunnyStream' && (
                <p className="text-xs text-muted">
                  {server.bunnyStream.isConfigured
                    ? 'Bunny Stream credentials are configured.'
                    : 'Not fully configured yet — set the library id, API key, CDN hostname and token-auth key, then turn Enabled on.'}
                  {server.bunnyStream.videoAttestationKeyIds && server.bunnyStream.videoAttestationKeyIds.length > 0
                    ? ` App attestation keys set: ${server.bunnyStream.videoAttestationKeyIds.join(', ')}.`
                    : ' No app attestation keys set — playback stays disabled until they are added.'}
                </p>
              )}

              {section.id === 'paymob' &&
                PAYMOB_FIELDS.map((field) =>
                  field.secret ? (
                    <SecretField
                      key={field.key}
                      label={field.label}
                      hint={field.hint}
                      serverValue={String(server.paymob[field.key] ?? '')}
                      draftValue={String(draft.paymob[field.key] ?? '')}
                      onChange={(next) => updateField('paymob', field.key, next as never)}
                    />
                  ) : (
                    <PlainField
                      key={field.key}
                      label={field.label}
                      hint={field.hint}
                      type={field.type}
                      value={draft.paymob[field.key] as string | number | boolean | null}
                      onChange={(next) =>
                        updateField(
                          'paymob',
                          field.key,
                          (field.type === 'number'
                            ? parseNullableNumberInput(String(next))
                            : field.type === 'checkbox'
                              ? Boolean(next)
                              : String(next)) as never,
                        )
                      }
                    />
                  ),
                )}

              {section.id === 'easyKash' &&
                EASYKASH_FIELDS.map((field) =>
                  field.secret ? (
                    <SecretField
                      key={field.key}
                      label={field.label}
                      hint={field.hint}
                      serverValue={String(server.easyKash[field.key] ?? '')}
                      draftValue={String(draft.easyKash[field.key] ?? '')}
                      onChange={(next) => updateField('easyKash', field.key, next as never)}
                    />
                  ) : (
                    <PlainField
                      key={field.key}
                      label={field.label}
                      hint={field.hint}
                      type={field.type}
                      options={field.options}
                      value={draft.easyKash[field.key] as string | number | boolean | null}
                      onChange={(next) =>
                        updateField(
                          'easyKash',
                          field.key,
                          (field.type === 'number'
                            ? parseNullableNumberInput(String(next))
                            : field.type === 'checkbox'
                              ? Boolean(next)
                              : String(next)) as never,
                        )
                      }
                    />
                  ),
                )}

              {section.id === 'payTabs' &&
                PAYTABS_FIELDS.map((field) =>
                  field.secret ? (
                    <SecretField
                      key={field.key}
                      label={field.label}
                      hint={field.hint}
                      serverValue={String(server.payTabs[field.key] ?? '')}
                      draftValue={String(draft.payTabs[field.key] ?? '')}
                      onChange={(next) => updateField('payTabs', field.key, next as never)}
                    />
                  ) : (
                    <PlainField
                      key={field.key}
                      label={field.label}
                      hint={field.hint}
                      type={field.type}
                      value={draft.payTabs[field.key] as string | number | boolean | null}
                      onChange={(next) =>
                        updateField(
                          'payTabs',
                          field.key,
                          (field.type === 'number'
                            ? parseNullableNumberInput(String(next))
                            : field.type === 'checkbox'
                              ? Boolean(next)
                              : String(next)) as never,
                        )
                      }
                    />
                  ),
                )}

              {section.id === 'soketi' &&
                SOKETI_FIELDS.map((field) =>
                  field.secret ? (
                    <SecretField
                      key={field.key}
                      label={field.label}
                      hint={field.hint}
                      serverValue={String(server.soketi[field.key] ?? '')}
                      draftValue={String(draft.soketi[field.key] ?? '')}
                      onChange={(next) => updateField('soketi', field.key, next as never)}
                    />
                  ) : (
                    <PlainField
                      key={field.key}
                      label={field.label}
                      hint={field.hint}
                      type={field.type}
                      value={draft.soketi[field.key] as string | number | boolean | null}
                      onChange={(next) =>
                        updateField(
                          'soketi',
                          field.key,
                          (field.type === 'number'
                            ? parseNullableNumberInput(String(next))
                            : field.type === 'checkbox'
                              ? Boolean(next)
                              : String(next)) as never,
                        )
                      }
                    />
                  ),
                )}

              {section.id === 'dataRetention' &&
                DATA_RETENTION_FIELDS.map((field) => (
                  <PlainField
                    key={field.key}
                    label={field.label}
                    hint={field.hint}
                    type={field.type}
                    value={draft.dataRetention[field.key] as string | number | boolean | null}
                    onChange={(next) =>
                      updateField(
                        'dataRetention',
                        field.key,
                        (field.type === 'number'
                          ? parseNullableNumberInput(String(next))
                          : field.type === 'checkbox'
                            ? Boolean(next)
                            : String(next)) as never,
                      )
                    }
                  />
                ))}

              {section.id === 'expertAutoAssignment' &&
                EXPERT_AUTO_ASSIGNMENT_FIELDS.map((field) => (
                  <PlainField
                    key={field.key}
                    label={field.label}
                    hint={field.hint}
                    type={field.type}
                    value={draft.expertAutoAssignment[field.key] as string | number | boolean | null}
                    onChange={(next) =>
                      updateField(
                        'expertAutoAssignment',
                        field.key,
                        (field.type === 'number'
                          ? parseNullableNumberInput(String(next))
                          : field.type === 'checkbox'
                            ? Boolean(next)
                            : String(next)) as never,
                      )
                    }
                  />
                ))}

              {section.id === 'passwordPolicy' &&
                PASSWORD_POLICY_FIELDS.map((field) => (
                  <PlainField
                    key={field.key}
                    label={field.label}
                    hint={field.hint}
                    type={field.type}
                    value={draft.passwordPolicy[field.key] as string | number | boolean | null}
                    onChange={(next) =>
                      updateField(
                        'passwordPolicy',
                        field.key,
                        (field.type === 'number'
                          ? parseNullableNumberInput(String(next))
                          : field.type === 'checkbox'
                            ? Boolean(next)
                            : String(next)) as never,
                      )
                    }
                  />
                ))}

              {section.id === 'aiAssistant' &&
                AI_ASSISTANT_FIELDS.map((field) => (
                  <PlainField
                    key={field.key}
                    label={field.label}
                    hint={field.hint}
                    type={field.type}
                    value={draft.aiAssistant[field.key] as string | number | boolean | null}
                    onChange={(next) =>
                      updateField(
                        'aiAssistant',
                        field.key,
                        (field.type === 'number'
                          ? parseNullableNumberInput(String(next))
                          : field.type === 'checkbox'
                            ? Boolean(next)
                            : String(next)) as never,
                      )
                    }
                  />
                ))}

              {section.id === 'aiGateway' &&
                AI_GATEWAY_FIELDS.map((field) => (
                  <PlainField
                    key={field.key}
                    label={field.label}
                    hint={field.hint}
                    type={field.type}
                    value={draft.aiGateway[field.key] as string | number | boolean | null}
                    onChange={(next) =>
                      updateField(
                        'aiGateway',
                        field.key,
                        (field.type === 'number'
                          ? parseNullableNumberInput(String(next))
                          : field.type === 'checkbox'
                            ? Boolean(next)
                            : String(next)) as never,
                      )
                    }
                  />
                ))}

              {section.id === 'writing' &&
                WRITING_FIELDS.map((field) =>
                  field.secret ? (
                    <SecretField
                      key={field.key}
                      label={field.label}
                      hint={field.hint}
                      serverValue={String(server.writing[field.key] ?? '')}
                      draftValue={String(draft.writing[field.key] ?? '')}
                      onChange={(next) => updateField('writing', field.key, next as never)}
                    />
                  ) : (
                    <PlainField
                      key={field.key}
                      label={field.label}
                      hint={field.hint}
                      type={field.type}
                      value={draft.writing[field.key] as string | number | boolean | null}
                      onChange={(next) =>
                        updateField(
                          'writing',
                          field.key,
                          (field.type === 'number'
                            ? parseNullableNumberInput(String(next))
                            : field.type === 'checkbox'
                              ? Boolean(next)
                              : String(next)) as never,
                        )
                      }
                    />
                  ),
                )}

              {section.id === 'platform' &&
                PLATFORM_FIELDS.map((field) => (
                  <PlainField
                    key={field.key}
                    label={field.label}
                    hint={field.hint}
                    type={field.type}
                    value={draft.platform[field.key] as string | number | boolean | null}
                    onChange={(next) =>
                      updateField(
                        'platform',
                        field.key,
                        (field.type === 'number'
                          ? parseNullableNumberInput(String(next))
                          : field.type === 'checkbox'
                            ? Boolean(next)
                            : String(next)) as never,
                      )
                    }
                  />
                ))}

              {section.id === 'messaging' &&
                MESSAGING_FIELDS.map((field) =>
                  field.secret ? (
                    <SecretField
                      key={field.key}
                      label={field.label}
                      hint={field.hint}
                      serverValue={String(server.messaging[field.key] ?? '')}
                      draftValue={String(draft.messaging[field.key] ?? '')}
                      onChange={(next) => updateField('messaging', field.key, next as never)}
                    />
                  ) : (
                    <PlainField
                      key={field.key}
                      label={field.label}
                      hint={field.hint}
                      type={field.type}
                      value={draft.messaging[field.key] as string | number | boolean | null}
                      onChange={(next) =>
                        updateField(
                          'messaging',
                          field.key,
                          (field.type === 'number'
                            ? parseNullableNumberInput(String(next))
                            : field.type === 'checkbox'
                              ? Boolean(next)
                              : String(next)) as never,
                        )
                      }
                    />
                  ),
                )}

              {section.id === 'fx' &&
                FX_FIELDS.map((field) =>
                  field.secret ? (
                    <SecretField
                      key={field.key}
                      label={field.label}
                      hint={field.hint}
                      serverValue={String(server.fx[field.key] ?? '')}
                      draftValue={String(draft.fx[field.key] ?? '')}
                      onChange={(next) => updateField('fx', field.key, next as never)}
                    />
                  ) : (
                    <PlainField
                      key={field.key}
                      label={field.label}
                      hint={field.hint}
                      type={field.type}
                      value={draft.fx[field.key] as string | number | boolean | null}
                      onChange={(next) =>
                        updateField(
                          'fx',
                          field.key,
                          (field.type === 'number'
                            ? parseNullableNumberInput(String(next))
                            : field.type === 'checkbox'
                              ? Boolean(next)
                              : String(next)) as never,
                        )
                      }
                    />
                  ),
                )}

              {section.id === 'billingCore' &&
                BILLING_CORE_FIELDS.map((field) => (
                  <PlainField
                    key={field.key}
                    label={field.label}
                    hint={field.hint}
                    type={field.type}
                    value={draft.billingCore[field.key] as string | number | boolean | null}
                    onChange={(next) =>
                      updateField(
                        'billingCore',
                        field.key,
                        (field.type === 'number'
                          ? parseNullableNumberInput(String(next))
                          : field.type === 'checkbox'
                            ? Boolean(next)
                            : String(next)) as never,
                      )
                    }
                  />
                ))}

              {section.id === 'storage' &&
                STORAGE_FIELDS.map((field) =>
                  field.secret ? (
                    <SecretField
                      key={field.key}
                      label={field.label}
                      hint={field.hint}
                      serverValue={String(server.storage[field.key] ?? '')}
                      draftValue={String(draft.storage[field.key] ?? '')}
                      onChange={(next) => updateField('storage', field.key, next as never)}
                    />
                  ) : (
                    <PlainField
                      key={field.key}
                      label={field.label}
                      hint={field.hint}
                      type={field.type}
                      value={draft.storage[field.key] as string | number | boolean | null}
                      onChange={(next) =>
                        updateField(
                          'storage',
                          field.key,
                          (field.type === 'number'
                            ? parseNullableNumberInput(String(next))
                            : field.type === 'checkbox'
                              ? Boolean(next)
                              : String(next)) as never,
                        )
                      }
                    />
                  ),
                )}

              {section.id === 'pdfExtraction' &&
                PDF_EXTRACTION_FIELDS.map((field) =>
                  field.secret ? (
                    <SecretField
                      key={field.key}
                      label={field.label}
                      hint={field.hint}
                      serverValue={String(server.pdfExtraction[field.key] ?? '')}
                      draftValue={String(draft.pdfExtraction[field.key] ?? '')}
                      onChange={(next) => updateField('pdfExtraction', field.key, next as never)}
                    />
                  ) : (
                    <PlainField
                      key={field.key}
                      label={field.label}
                      hint={field.hint}
                      type={field.type}
                      value={draft.pdfExtraction[field.key] as string | number | boolean | null}
                      onChange={(next) =>
                        updateField(
                          'pdfExtraction',
                          field.key,
                          (field.type === 'number'
                            ? parseNullableNumberInput(String(next))
                            : field.type === 'checkbox'
                              ? Boolean(next)
                              : String(next)) as never,
                        )
                      }
                    />
                  ),
                )}

              {section.id === 'pronunciation' &&
                PRONUNCIATION_FIELDS.map((field) => (
                  <PlainField
                    key={field.key}
                    label={field.label}
                    hint={field.hint}
                    type={field.type}
                    value={draft.pronunciation[field.key] as string | number | boolean | null}
                    onChange={(next) =>
                      updateField(
                        'pronunciation',
                        field.key,
                        (field.type === 'number'
                          ? parseNullableNumberInput(String(next))
                          : field.type === 'checkbox'
                            ? Boolean(next)
                            : String(next)) as never,
                      )
                    }
                  />
                ))}

              {section.id === 'authTokens' &&
                AUTH_TOKENS_FIELDS.map((field) => (
                  <PlainField
                    key={field.key}
                    label={field.label}
                    hint={field.hint}
                    type={field.type}
                    value={draft.authTokens[field.key] as string | number | boolean | null}
                    onChange={(next) =>
                      updateField(
                        'authTokens',
                        field.key,
                        (field.type === 'number'
                          ? parseNullableNumberInput(String(next))
                          : field.type === 'checkbox'
                            ? Boolean(next)
                            : String(next)) as never,
                      )
                    }
                  />
                ))}

              {section.id === 'webPush' &&
                WEB_PUSH_FIELDS.map((field) => (
                  <PlainField
                    key={field.key}
                    label={field.label}
                    hint={field.hint}
                    type={field.type}
                    value={draft.webPush[field.key] as string | number | boolean | null}
                    onChange={(next) =>
                      updateField(
                        'webPush',
                        field.key,
                        (field.type === 'number'
                          ? parseNullableNumberInput(String(next))
                          : field.type === 'checkbox'
                            ? Boolean(next)
                            : String(next)) as never,
                      )
                    }
                  />
                ))}

              {section.id === 'support' &&
                SUPPORT_FIELDS.map((field) => (
                  <PlainField
                    key={field.key}
                    label={field.label}
                    hint={field.hint}
                    type={field.type}
                    value={draft.support[field.key] as string | number | boolean | null}
                    onChange={(next) =>
                      updateField(
                        'support',
                        field.key,
                        (field.type === 'number'
                          ? parseNullableNumberInput(String(next))
                          : field.type === 'checkbox'
                            ? Boolean(next)
                            : String(next)) as never,
                      )
                    }
                  />
                ))}
            </Section>
          ))}

          <div className="flex flex-col items-stretch gap-3 pt-2 sm:flex-row sm:items-center sm:justify-end">
            <Button
              variant="primary"
              onClick={handleSave}
              disabled={saving}
              loading={saving}
              loadingText="Saving…"
              startIcon={<Save className="h-4 w-4" aria-hidden="true" />}
              aria-label="Save all runtime settings"
            >
              Save All
            </Button>
          </div>

          {updatedLine && (
            <p className="text-right text-xs text-admin-fg-muted" data-testid="runtime-settings-updated-line">
              {updatedLine}
            </p>
          )}
        </div>
      )}

      {toast && <Toast variant={toast.variant} message={toast.message} onClose={() => setToast(null)} />}
    </AdminSettingsLayout>
  );
}

export default RuntimeSettingsClient;
