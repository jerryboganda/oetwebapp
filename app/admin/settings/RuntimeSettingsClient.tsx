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
}

export interface BillingSettings {
  stripeSecretKey: string;
  stripePublishableKey: string;
  stripeWebhookSecret: string;
  stripeSuccessUrl: string;
  stripeCancelUrl: string;
  paypalClientId: string;
  paypalClientSecret: string;
  paypalWebhookId: string;
  paypalSuccessUrl: string;
  paypalCancelUrl: string;
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

type SectionId = 'email' | 'billing' | 'sentry' | 'backup' | 'oauth' | 'push' | 'uploadScanner' | 'zoom' | 'speakingWhisper' | 'speakingLiveKit' | 'speakingAi' | 'speakingStorage' | 'speakingCompliance' | 'speakingFeatures';

type ToastState = { variant: 'success' | 'error'; message: string } | null;
type TestStatusState = Partial<Record<SectionId, RuntimeSettingsIntegrationTestResponse>>;

/* ───────────────────────── Field metadata ───────────────────────── */

interface FieldDef<TSection> {
  key: keyof TSection & string;
  label: string;
  hint?: string;
  secret?: boolean;
  type?: 'text' | 'number' | 'url' | 'checkbox';
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
];

const BILLING_FIELDS: FieldDef<BillingSettings>[] = [
  { key: 'stripeSecretKey', label: 'Stripe Secret Key', secret: true, hint: 'Server-side Stripe key (starts with sk_live_ or sk_test_).' },
  { key: 'stripePublishableKey', label: 'Stripe Publishable Key', hint: 'Client-safe Stripe key (starts with pk_).' },
  { key: 'stripeWebhookSecret', label: 'Stripe Webhook Signing Secret', secret: true, hint: 'Used to verify Stripe webhook signatures (starts with whsec_).' },
  { key: 'stripeSuccessUrl', label: 'Checkout Success URL', type: 'url' },
  { key: 'stripeCancelUrl', label: 'Checkout Cancel URL', type: 'url' },
  { key: 'paypalClientId', label: 'PayPal Client ID', hint: 'REST app client id for PayPal checkout and refunds.' },
  { key: 'paypalClientSecret', label: 'PayPal Client Secret', secret: true },
  { key: 'paypalWebhookId', label: 'PayPal Webhook ID', secret: true, hint: 'Used to verify PayPal webhook signatures.' },
  { key: 'paypalSuccessUrl', label: 'PayPal Success URL', type: 'url' },
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
  { key: 'appleClientId', label: 'Apple Client ID', hint: 'The service identifier (e.g. com.example.web).' },
  { key: 'appleTeamId', label: 'Apple Team ID' },
  { key: 'appleKeyId', label: 'Apple Key ID' },
  { key: 'applePrivateKey', label: 'Apple Private Key (.p8 contents)', secret: true },
  { key: 'facebookAppId', label: 'Facebook App ID' },
  { key: 'facebookAppSecret', label: 'Facebook App Secret', secret: true },
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
  { key: 'baseUrl', label: 'Whisper API Base URL', type: 'url', hint: 'Default: https://api.openai.com/v1 — change only for self-hosted gateways.' },
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

const SECTION_META: { id: SectionId; title: string; description: string }[] = [
  { id: 'email', title: 'Email (Brevo + SMTP)', description: 'Transactional email delivery via Brevo with SMTP fallback.' },
  { id: 'billing', title: 'Billing (Stripe)', description: 'Stripe Checkout, Customer Portal, and webhook signing.' },
  { id: 'sentry', title: 'Sentry', description: 'Error reporting and performance monitoring.' },
  { id: 'backup', title: 'Backup S3', description: 'Off-site database and media backup destination.' },
  { id: 'oauth', title: 'OAuth (Google + Apple + Facebook)', description: 'Social sign-in providers.' },
  { id: 'push', title: 'Push (Browser + APNs + FCM)', description: 'Browser VAPID and native mobile push notifications via Apple and Firebase.' },
  { id: 'uploadScanner', title: 'Upload Scanner (ClamAV)', description: 'Antivirus scanning for learner/admin uploads.' },
  { id: 'zoom', title: 'Zoom Live Classes', description: 'Server-to-server OAuth, Meeting SDK, and webhook verification.' },
  { id: 'speakingWhisper', title: 'Speaking — Whisper Transcription', description: 'OpenAI Whisper API for speaking session transcription and RULE_40 tone pipeline.' },
  { id: 'speakingLiveKit', title: 'Speaking — LiveKit (Live Rooms)', description: 'LiveKit Cloud WebRTC for live tutor rooms and egress recording.' },
  { id: 'speakingAi', title: 'Speaking — AI Providers', description: 'Anthropic (Claude scoring + patient turns) and ElevenLabs (AI patient TTS voice).' },
  { id: 'speakingStorage', title: 'Speaking — Recording Storage (AWS S3)', description: 'AWS S3 bucket for speaking session recording archive.' },
  { id: 'speakingCompliance', title: 'Speaking — Compliance & Retention', description: 'Consent versioning, recording retention windows, and audit log retention.' },
  { id: 'speakingFeatures', title: 'Speaking — Feature Flags', description: 'Controls the Speaking v2 module rollout to learners.' },
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
    },
    billing: {
      stripeSecretKey: '',
      stripePublishableKey: '',
      stripeWebhookSecret: '',
      stripeSuccessUrl: '',
      stripeCancelUrl: '',
      paypalClientId: '',
      paypalClientSecret: '',
      paypalWebhookId: '',
      paypalSuccessUrl: '',
      paypalCancelUrl: '',
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
  });
}

function sanitizeSecretFields(data: RuntimeSettingsResponse): RuntimeSettingsResponse {
  return {
    ...data,
    email: {
      ...data.email,
      brevoApiKey: maskUnexpectedSecret(data.email.brevoApiKey),
      smtpPassword: maskUnexpectedSecret(data.email.smtpPassword),
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
  type?: 'text' | 'number' | 'url' | 'checkbox';
  value: string | number | boolean | null | undefined;
  onChange: (next: string | boolean) => void;
}

function PlainField({ label, hint, type = 'text', value, onChange }: PlainFieldProps) {
  const reactId = useId();
  const inputId = `plain-${reactId}`;
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
    <S extends SectionId, K extends keyof RuntimeSettingsResponse[S] & string>(
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
