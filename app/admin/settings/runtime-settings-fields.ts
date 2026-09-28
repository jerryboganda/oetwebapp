import type { EmailSettings, BillingSettings, SentrySettings, BackupSettings, OAuthSettings, PushSettings, UploadScannerSettings, SecuritySettings, VideoProtectionSettings, ZoomSettings, SpeakingWhisperSettings, SpeakingLiveKitSettings, SpeakingAiSettings, SpeakingStorageSettings, SpeakingComplianceSettingsData, SpeakingFeaturesSettings, CheckoutComSettings, BunnyStreamSettings, PaymobSettings, EasyKashSettings, PayTabsSettings, SoketiSettings, DataRetentionSettings, ExpertAutoAssignmentSettings, PasswordPolicySettings, AiAssistantSettings, AiGatewaySettings, WritingSettings, PlatformSettings, MessagingSettings, FxSettings, BillingCoreSettings, StorageSettings, PdfExtractionSettings, PronunciationSettings, AuthTokensSettings, WebPushSettings, SupportSettings, FirebaseOtpSettings, RuntimeSettingsResponse, RuntimeSettingsIntegrationTestResponse } from './runtime-settings-types';

export type SectionId = 'email' | 'billing' | 'paypal' | 'sentry' | 'backup' | 'oauth' | 'push' | 'uploadScanner' | 'zoom' | 'speakingWhisper' | 'speakingLiveKit' | 'speakingAi' | 'speakingStorage' | 'speakingCompliance' | 'speakingFeatures' | 'checkoutCom' | 'bunnyStream' | 'paymob' | 'payTabs' | 'easyKash' | 'soketi' | 'dataRetention' | 'expertAutoAssignment' | 'passwordPolicy' | 'aiAssistant' | 'aiGateway' | 'writing' | 'platform' | 'messaging' | 'fx' | 'billingCore' | 'storage' | 'pdfExtraction' | 'pronunciation' | 'authTokens' | 'webPush' | 'support' | 'firebaseOtp' | 'security' | 'videoProtection';

// The object-valued payload keys (every UI section maps to one of these; the "paypal"
// UI section writes into "billing"). Excludes the scalar audit fields so updateField can
// safely spread the prior section object.
export type DataSectionId = Exclude<keyof RuntimeSettingsResponse, 'updatedBy' | 'updatedByUserId' | 'updatedAt'>;

export type ToastState = { variant: 'success' | 'error'; message: string } | null;
export type TestStatusState = Partial<Record<SectionId, RuntimeSettingsIntegrationTestResponse>>;

/* ───────────────────────── Field metadata ───────────────────────── */

export interface FieldDef<TSection> {
  key: keyof TSection & string;
  label: string;
  hint?: string;
  secret?: boolean;
  type?: 'text' | 'number' | 'url' | 'checkbox' | 'select';
  /** Options for a 'select' field. */
  options?: { value: string; label: string }[];
  placeholder?: string;
}

export const EMAIL_FIELDS: FieldDef<EmailSettings>[] = [
  { key: 'brevoApiKey', label: 'Brevo API Key', secret: true, hint: 'Server-side Brevo (Sendinblue) API key for transactional email.' },
  { key: 'brevoEmailVerificationTemplateId', label: 'Brevo Email Verification Template ID', hint: 'Numeric template id used for OTP verification emails.' },
  { key: 'brevoPasswordResetTemplateId', label: 'Brevo Password Reset Template ID', hint: 'Numeric template id used for password-reset emails.' },
  { key: 'smtpHost', label: 'SMTP Host', hint: 'Fallback SMTP server hostname.' },
  { key: 'smtpPort', label: 'SMTP Port', type: 'number', hint: 'Typically 587 (STARTTLS) or 465 (TLS).' },
  { key: 'smtpUsername', label: 'SMTP Username' },
  { key: 'smtpPassword', label: 'SMTP Password', secret: true },
  { key: 'smtpFromAddress', label: 'Fallback From Address', hint: 'Used only when a lane-specific From address is empty.' },
  { key: 'smtpFromName', label: 'Fallback From Name' },
  { key: 'authFromAddress', label: 'Auth From Address', hint: 'OTP, verification, password reset, and security mail. Default: auth@oetwithdrhesham.co.uk' },
  { key: 'authFromName', label: 'Auth From Name' },
  { key: 'marketingFromAddress', label: 'Marketing From Address', hint: 'Promotions and credits-low digests. Default: updates@oetwithdrhesham.co.uk' },
  { key: 'marketingFromName', label: 'Marketing From Name' },
  { key: 'productFromAddress', label: 'Product From Address', hint: 'Transactional product notifications. Default: no-reply@oetwithdrhesham.co.uk' },
  { key: 'productFromName', label: 'Product From Name' },
  { key: 'supportFromAddress', label: 'Support From Address', hint: 'Ops and admin alerts. Default: support@oetwithdrhesham.co.uk' },
  { key: 'supportFromName', label: 'Support From Name' },
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

export const BILLING_FIELDS: FieldDef<BillingSettings>[] = [
  { key: 'stripeSecretKey', label: 'Stripe Secret Key', secret: true, hint: 'Server-side Stripe key (starts with sk_live_ or sk_test_).' },
  { key: 'stripePublishableKey', label: 'Stripe Publishable Key', hint: 'Client-safe Stripe key (starts with pk_).' },
  { key: 'stripeWebhookSecret', label: 'Stripe Webhook Signing Secret', secret: true, hint: 'Used to verify Stripe webhook signatures (starts with whsec_).' },
  { key: 'stripeSuccessUrl', label: 'Checkout Success URL', type: 'url' },
  { key: 'stripeCancelUrl', label: 'Checkout Cancel URL', type: 'url' },
  { key: 'publicAppBaseUrl', label: 'Public App Base URL', type: 'url', hint: 'e.g. https://app.oetwithdrhesham.co.uk — builds absolute checkout return URLs without an env var. Leave blank to use the server APP_URL.' },
];

export const PAYPAL_FIELDS: FieldDef<BillingSettings>[] = [
  { key: 'paypalClientId', label: 'PayPal Client ID', hint: 'REST app client id (Apps & Credentials). Public — also used by the embedded Expanded checkout SDK in the browser.' },
  { key: 'paypalClientSecret', label: 'PayPal Client Secret', secret: true, hint: 'REST app secret. Server-side only — never sent to the browser.' },
  { key: 'paypalWebhookId', label: 'PayPal Webhook ID', secret: true, hint: 'Webhook id used to verify PayPal webhook signatures (Dashboard → Webhooks → your webhook).' },
  { key: 'paypalAdvancedCardsEnabled', label: 'Enable embedded card fields (Advanced Cards)', type: 'checkbox', hint: 'When on, the embedded checkout shows on-page card fields (requires PayPal "Advanced Credit and Debit Card Payments" eligibility). Turn off to show PayPal/Venmo/Pay Later buttons only. Default: on.' },
  { key: 'paypalSuccessUrl', label: 'PayPal Success URL', type: 'url', hint: 'Return URL for the redirect fallback flow (e.g. https://app.oetwithdrhesham.co.uk/billing/payment-return).' },
  { key: 'paypalCancelUrl', label: 'PayPal Cancel URL', type: 'url' },
];

export const SENTRY_FIELDS: FieldDef<SentrySettings>[] = [
  { key: 'dsn', label: 'Sentry DSN', hint: 'Project DSN for error reporting.' },
  { key: 'environment', label: 'Sentry Environment', hint: 'e.g. production, staging.' },
  { key: 'sampleRate', label: 'Sentry Sample Rate', type: 'number', hint: 'Number between 0 and 1 (e.g. 0.1 = 10%).' },
];

export const BACKUP_FIELDS: FieldDef<BackupSettings>[] = [
  { key: 's3Url', label: 'Backup S3 URL', type: 'url', hint: 's3://bucket/prefix or https-style endpoint.' },
  { key: 'awsAccessKeyId', label: 'AWS Access Key ID' },
  { key: 'awsSecretAccessKey', label: 'AWS Secret Access Key', secret: true },
  { key: 'gpgPassphrase', label: 'GPG Passphrase', secret: true, hint: 'Used to encrypt backup archives.' },
  { key: 'alertWebhook', label: 'Backup Alert Webhook', type: 'url', hint: 'POSTed when a backup fails.' },
];

export const OAUTH_FIELDS: FieldDef<OAuthSettings>[] = [
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

export const PUSH_FIELDS: FieldDef<PushSettings>[] = [
  { key: 'vapidSubject', label: 'Browser Push VAPID Subject', hint: 'Contact URI, usually mailto:support@example.com.' },
  { key: 'vapidPublicKey', label: 'Browser Push VAPID Public Key' },
  { key: 'vapidPrivateKey', label: 'Browser Push VAPID Private Key', secret: true },
  { key: 'apnsKeyId', label: 'APNs Key ID' },
  { key: 'apnsTeamId', label: 'APNs Team ID' },
  { key: 'apnsBundleId', label: 'APNs Bundle ID', hint: 'iOS app bundle identifier.' },
  { key: 'apnsAuthKey', label: 'APNs Auth Key (.p8 contents)', secret: true },
  { key: 'fcmServiceAccountJson', label: 'FCM Service Account JSON', secret: true, hint: 'Full JSON key from Firebase Console → Project Settings → Service Accounts → Generate new private key. FCM’s legacy server-key API is retired; sending now requires this.' },
  { key: 'fcmProjectId', label: 'FCM Project ID' },
];

export const UPLOAD_SCANNER_FIELDS: FieldDef<UploadScannerSettings>[] = [
  { key: 'provider', label: 'Scanner Provider', hint: 'Use "clamav" for production; "noop" is development-only.' },
  { key: 'host', label: 'ClamAV Host', hint: 'ClamAV daemon host, for example clamav or 127.0.0.1.' },
  { key: 'port', label: 'ClamAV Port', type: 'number', hint: 'Default clamd port is 3310.' },
  { key: 'timeoutSeconds', label: 'Scan Timeout (seconds)', type: 'number', hint: 'Fail fast for slow scanners; valid range is 1-120.' },
  { key: 'failClosedOnError', label: 'Fail closed on scanner errors', type: 'checkbox', hint: 'Reject uploads when ClamAV is unavailable or times out.' },
];

export const SECURITY_FIELDS: FieldDef<SecuritySettings>[] = [
  { key: 'singleActiveSessionEnabled', label: 'Enforce single active session', type: 'checkbox', hint: 'Security spec §3.1 (P0). Signing in anywhere revokes every other session for the account.' },
  {
    key: 'riskMode',
    label: 'Sign-in Risk Mode',
    type: 'select',
    options: [
      { value: 'off', label: 'Off — no risk evaluation' },
      { value: 'log_only', label: 'Log only — record risk signals, never block' },
      { value: 'enforce', label: 'Enforce — step-up/block on risk signals' },
    ],
    hint: 'Security spec §3.3. Review a week of log_only data before switching to enforce.',
  },
  { key: 'trustedDeviceRequired', label: 'Require device verification', type: 'checkbox', hint: 'Security spec §3.2 (P1). A sign-in from an unrecognized device requires an email-OTP challenge. Do not enable until the device-verification sign-in UI (app/(auth)/device/verify) has been verified end-to-end.' },
  { key: 'deviceChangeWindowDays', label: 'Device Change Window (days)', type: 'number', hint: 'Rolling window for the device-change cooldown, 1-365.' },
  { key: 'deviceChangeMaxPerWindow', label: 'Max Device Changes per Window', type: 'number', hint: 'Device changes allowed within the window before further changes are blocked, 1-100.' },
  { key: 'inactiveSessionTimeoutDays', label: 'Inactive Session Timeout (days)', type: 'number', hint: 'Security spec §4.2. Sessions idle this long are revoked by the auth data-retention sweep, 1-365.' },
  { key: 'requireVerifiedEmailForLearners', label: 'Require verified email for learners', type: 'checkbox', hint: 'Security spec §4.2 hard gate. When ON, unverified learners get 403 email_verification_required on every learner endpoint and are routed to the verify screen. Leave OFF until the banner has drained the unverified backlog.' },
  { key: 'countryAllowList', label: 'Country Allow-List', type: 'text', placeholder: 'e.g. EG,SA,AE', hint: 'Security spec §3.3. Comma-separated 2-letter ISO codes a sign-in country must be in. Empty = no restriction. Sign-ins with unknown country always pass.' },
  {
    key: 'countryAllowListMode',
    label: 'Country Allow-List Mode',
    type: 'select',
    options: [
      { value: 'off', label: 'Off — list is ignored' },
      { value: 'step_up', label: 'Step up — email-OTP challenge outside the list' },
      { value: 'block', label: 'Block — reject sign-ins outside the list' },
    ],
    hint: 'What happens to a sign-in from outside the allow-list.',
  },
  {
    key: 'ipIntelligenceProvider',
    label: 'IP Intelligence Provider',
    type: 'select',
    options: [
      { value: 'off', label: 'Off — account-history risk signals only' },
      { value: 'ipinfo', label: 'IPinfo — VPN, proxy, Tor and hosting detection' },
    ],
    hint: 'IPinfo Core or better is required. Provider errors fail open so sign-in remains available.',
  },
  {
    key: 'ipinfoToken',
    label: 'IPinfo API Token',
    secret: true,
    hint: 'Stored encrypted. Use an IPinfo Core, Plus or Max token; the free Lite tier lacks privacy flags.',
  },
];

export const VIDEO_PROTECTION_FIELDS: FieldDef<VideoProtectionSettings>[] = [
  { key: 'revokeOnCaptureDetected', label: 'Revoke playback on capture detected', type: 'checkbox', hint: 'Security spec §2.4. Immediately kill a playback session when the client reports screen capture/recording.' },
  { key: 'blockRootedDevices', label: 'Block rooted/jailbroken devices', type: 'checkbox', hint: 'Security spec §3 (mobile hardening). Reject a new playback session when the client reports root/jailbreak integrity signals. A client that sends no integrity signal (old shell, desktop) always fails open.' },
  { key: 'blockEmulators', label: 'Block emulators', type: 'checkbox', hint: 'Same as above, for emulator signals.' },
];

export const ZOOM_FIELDS: FieldDef<ZoomSettings>[] = [
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

export const SPEAKING_WHISPER_FIELDS: FieldDef<SpeakingWhisperSettings>[] = [
  { key: 'apiKey', label: 'OpenAI Whisper API Key', secret: true, hint: 'Used by the Speaking RULE_40 tone pipeline. Starts with sk-…' },
  { key: 'baseUrl', label: 'Whisper API Base URL', type: 'url', hint: 'Default: https://api.openai.com/v1. Change only for self-hosted gateways.' },
  { key: 'model', label: 'Whisper Model', hint: 'Default: whisper-1.' },
];

export const SPEAKING_LIVEKIT_FIELDS: FieldDef<SpeakingLiveKitSettings>[] = [
  { key: 'provider', label: 'LiveKit Provider', hint: '"livekit_cloud" to enable, "disabled" to stub out.' },
  { key: 'apiKey', label: 'LiveKit API Key', secret: true, hint: 'Server-side LiveKit Cloud API key.' },
  { key: 'apiSecret', label: 'LiveKit API Secret', secret: true, hint: 'Server-side LiveKit Cloud API secret.' },
  { key: 'wssUrl', label: 'LiveKit WebSocket URL', type: 'url', hint: 'e.g. wss://your-project.livekit.cloud' },
  { key: 'webhookSigningSecret', label: 'Webhook Signing Secret', secret: true, hint: 'HMAC secret for verifying LiveKit webhook payloads.' },
  { key: 'egressBucket', label: 'Egress S3 Bucket', hint: 'S3 bucket name for LiveKit egress recordings.' },
  { key: 'defaultMaxDurationSeconds', label: 'Max Session Duration (seconds)', type: 'number', hint: '60–7200 seconds (default: 1800 = 30 min).' },
  { key: 'egressEnabled', label: 'Enable Egress Recording', type: 'checkbox', hint: 'When enabled, completed sessions are recorded to S3.' },
];

export const SPEAKING_AI_FIELDS: FieldDef<SpeakingAiSettings>[] = [
  { key: 'anthropicApiKey', label: 'Anthropic API Key', secret: true, hint: 'Claude Sonnet 4.6 for AI grading + patient turns. Starts with sk-ant-…' },
  { key: 'elevenLabsApiKey', label: 'ElevenLabs API Key', secret: true, hint: 'AI patient TTS voice synthesis.' },
];

export const SPEAKING_STORAGE_FIELDS: FieldDef<SpeakingStorageSettings>[] = [
  { key: 'awsAccessKeyId', label: 'AWS Access Key ID', hint: 'IAM user with S3 PutObject/GetObject permissions.' },
  { key: 'awsSecretAccessKey', label: 'AWS Secret Access Key', secret: true },
  { key: 'region', label: 'AWS Region', hint: 'e.g. eu-west-2 (London).' },
  { key: 'bucket', label: 'S3 Bucket Name', hint: 'Bucket for speaking session recordings.' },
];

export const SPEAKING_COMPLIANCE_FIELDS: FieldDef<SpeakingComplianceSettingsData>[] = [
  { key: 'currentConsentVersion', label: 'Recording Consent Version', hint: 'Version string shown to learners (e.g. recording.v1).' },
  { key: 'currentLiveVideoConsentVersion', label: 'Live Video Consent Version', hint: 'Version string for live tutor video sessions (e.g. live_video_with_tutor.v1).' },
  { key: 'retentionDaysDefault', label: 'Default Retention (days)', type: 'number', hint: 'Days to retain recordings before deletion (default: 90).' },
  { key: 'retentionDaysWhenTutorReviewed', label: 'Tutor-Reviewed Retention (days)', type: 'number', hint: 'Extended retention when a tutor has reviewed (default: 365).' },
  { key: 'auditLogRetentionDays', label: 'Audit Log Retention (days)', type: 'number', hint: 'Days to retain speaking audit logs (default: 2555 ≈ 7 years).' },
];

export const SPEAKING_FEATURES_FIELDS: FieldDef<SpeakingFeaturesSettings>[] = [
  { key: 'speakingV2Enabled', label: 'Enable Speaking V2 Module', type: 'checkbox', hint: 'Feature flag that gates the full Speaking v2 module rollout to learners.' },
];

export const CHECKOUT_COM_FIELDS: FieldDef<CheckoutComSettings>[] = [
  { key: 'apiBaseUrl', label: 'API Base URL', type: 'url', hint: 'Default: https://api.checkout.com (use https://api.sandbox.checkout.com for testing).' },
  { key: 'secretKey', label: 'Secret Key', secret: true, hint: 'Server-side Checkout.com key (starts with sk_).' },
  { key: 'publicKey', label: 'Public Key', hint: 'Client-safe key (starts with pk_).' },
  { key: 'processingChannelId', label: 'Processing Channel ID', hint: 'pc_… channel id for hosted payments.' },
  { key: 'webhookSecret', label: 'Webhook Signing Secret', secret: true, hint: 'Verifies Cko-Signature webhook headers.' },
  { key: 'successUrl', label: 'Success URL', type: 'url' },
  { key: 'cancelUrl', label: 'Cancel URL', type: 'url' },
];

export const BUNNY_STREAM_FIELDS: FieldDef<BunnyStreamSettings>[] = [
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

export const PAYMOB_FIELDS: FieldDef<PaymobSettings>[] = [
  { key: 'apiBaseUrl', label: 'API Base URL', type: 'url', hint: 'Default: https://accept.paymob.com.' },
  { key: 'apiKey', label: 'API Key', secret: true, hint: 'Paymob secret API key used for auth tokens.' },
  { key: 'merchantId', label: 'Merchant ID' },
  { key: 'hmacSecret', label: 'HMAC Secret', secret: true, hint: 'Verifies the SHA-512 webhook signature.' },
  { key: 'integrationIdsJson', label: 'Integration IDs (JSON)', hint: 'Method→id map, e.g. {"card":123,"fawry":456}.' },
  { key: 'iframeId', label: 'Iframe ID', type: 'number', hint: 'Hosted iframe id for the redirect URL.' },
  { key: 'successUrl', label: 'Success URL', type: 'url' },
  { key: 'cancelUrl', label: 'Cancel URL', type: 'url' },
];

export const EASYKASH_FIELDS: FieldDef<EasyKashSettings>[] = [
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

export const PAYTABS_FIELDS: FieldDef<PayTabsSettings>[] = [
  { key: 'apiBaseUrl', label: 'API Base URL', type: 'url', hint: 'Region-specific, e.g. https://secure.paytabs.com or https://secure-egypt.paytabs.com.' },
  { key: 'serverKey', label: 'Server Key', secret: true, hint: 'Authorization header value for PayTabs API.' },
  { key: 'profileId', label: 'Profile ID', hint: 'Merchant profile id (identifies account + region).' },
  { key: 'webhookSecret', label: 'Webhook Secret', secret: true, hint: 'Verifies the HMAC-SHA256 Signature header.' },
  { key: 'successUrl', label: 'Success URL', type: 'url' },
  { key: 'cancelUrl', label: 'Cancel URL', type: 'url' },
];

export const SOKETI_FIELDS: FieldDef<SoketiSettings>[] = [
  { key: 'enabled', label: 'Enable Soketi push', type: 'checkbox', hint: 'Server-side realtime websocket dispatch.' },
  { key: 'host', label: 'Host', hint: 'Soketi server host (e.g. soketi or 127.0.0.1).' },
  { key: 'port', label: 'Port', type: 'number', hint: 'Default 6001.' },
  { key: 'appId', label: 'App ID' },
  { key: 'appKey', label: 'App Key', hint: 'Note: the browser client reads its key from NEXT_PUBLIC_* env — this affects server-side dispatch only.' },
  { key: 'appSecret', label: 'App Secret', secret: true, hint: 'HMAC secret for signing Pusher-protocol requests.' },
  { key: 'useTls', label: 'Use TLS (wss/https)', type: 'checkbox' },
];

export const DATA_RETENTION_FIELDS: FieldDef<DataRetentionSettings>[] = [
  { key: 'auditEventsDays', label: 'Audit Events Retention (days)', type: 'number', hint: 'How long admin/billing audit rows are kept. 0 disables the sweep (default 730).' },
  { key: 'analyticsEventsDays', label: 'Analytics Events Retention (days)', type: 'number', hint: 'High-volume product analytics retention. 0 disables (default 365).' },
  { key: 'paymentWebhookEventsDays', label: 'Payment Webhook Retention (days)', type: 'number', hint: 'How long processed gateway webhook rows are kept (default 180).' },
  { key: 'paymentWebhookPiiNullOutAgeDays', label: 'Webhook Payload PII Null-out (days)', type: 'number', hint: 'Age after which webhook payload bodies are nulled while metadata is kept (default 90).' },
  { key: 'notificationDeliveryAttemptsDays', label: 'Notification Attempts Retention (days)', type: 'number', hint: 'How long delivery-attempt rows are kept (default 90).' },
  { key: 'securityEventsDays', label: 'Security Events Retention (days)', type: 'number', hint: 'How long auth/session/device/playback security telemetry rows are kept (default 180).' },
  { key: 'sweepIntervalHours', label: 'Sweep Interval (hours)', type: 'number', hint: 'How often the retention sweeper runs (default 24).' },
  { key: 'batchSize', label: 'Batch Size (rows per table per sweep)', type: 'number', hint: 'Caps rows deleted per table per sweep to avoid long locks (default 5000).' },
];

export const EXPERT_AUTO_ASSIGNMENT_FIELDS: FieldDef<ExpertAutoAssignmentSettings>[] = [
  { key: 'enabled', label: 'Enable auto-assignment', type: 'checkbox', hint: 'Auto-assign Writing review requests to the lowest-loaded eligible expert.' },
  { key: 'slaHoursStandard', label: 'Standard SLA (hours)', type: 'number', hint: 'Turnaround target for standard reviews (default 48).' },
  { key: 'slaHoursExpress', label: 'Express SLA (hours)', type: 'number', hint: 'Turnaround target for express reviews (default 12).' },
  { key: 'maxActiveAssignmentsPerExpert', label: 'Max Active Assignments / Expert', type: 'number', hint: 'Load cap per expert before they stop receiving new work (default 8).' },
  { key: 'lookbackHoursForLoad', label: 'Load Lookback (hours)', type: 'number', hint: 'Window used to tally recent completions when balancing load (default 24).' },
  { key: 'batchSize', label: 'Batch Size (per poll)', type: 'number', hint: 'Max pending requests processed per poll cycle (default 50).' },
  { key: 'pollingIntervalSeconds', label: 'Polling Interval (seconds)', type: 'number', hint: 'Cadence of the assignment poll (default 30).' },
  { key: 'slaEscalationIntervalSeconds', label: 'SLA Escalation Interval (seconds)', type: 'number', hint: 'Cadence of the SLA escalation poll (default 60).' },
];

export const PASSWORD_POLICY_FIELDS: FieldDef<PasswordPolicySettings>[] = [
  { key: 'minimumLength', label: 'Minimum Length', type: 'number', hint: 'Minimum password length (NIST recommends 8+, default 10).' },
  { key: 'requireMixedCase', label: 'Require mixed case', type: 'checkbox', hint: 'Require both uppercase and lowercase letters.' },
  { key: 'requireDigit', label: 'Require a digit', type: 'checkbox' },
  { key: 'requireSymbol', label: 'Require a symbol', type: 'checkbox' },
  { key: 'breachCheckEnabled', label: 'Enable HIBP breach check', type: 'checkbox', hint: 'k-anonymity check against HaveIBeenPwned. The password never leaves the server. Disable for air-gapped deployments.' },
  { key: 'breachApiBaseUrl', label: 'Breach API Base URL', type: 'url', hint: 'HIBP range API base. Override only for a self-hosted mirror (default https://api.pwnedpasswords.com/).' },
  { key: 'breachApiTimeoutSeconds', label: 'Breach API Timeout (seconds)', type: 'number', hint: 'Fail-open timeout for the breach check (1–60, default 3).' },
];

export const AI_ASSISTANT_FIELDS: FieldDef<AiAssistantSettings>[] = [
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

export const AI_GATEWAY_FIELDS: FieldDef<AiGatewaySettings>[] = [
  { key: 'aiProviderProviderId', label: 'AI Provider ID', type: 'text', hint: 'Stable code (digitalocean-serverless, openai-platform, anthropic, etc). Fallback: env AI:ProviderId.' },
  { key: 'aiProviderBaseUrl', label: 'AI Provider Base URL', type: 'url', hint: 'OpenAI-compatible endpoint (e.g. https://inference.do-ai.run/v1). Validated for safety. Fallback: env AI:BaseUrl.' },
  { key: 'aiProviderDefaultModel', label: 'Default Model', type: 'text', hint: 'Model ID when none specified (e.g. glm-5). Fallback: env AI:DefaultModel.' },
  { key: 'aiProviderReasoningEffort', label: 'Reasoning Effort (for o-series models)', type: 'text', hint: 'low, medium, or high. Leave blank for non-reasoning models (glm-5). Fallback: env AI:ReasoningEffort.' },
  { key: 'aiProviderDefaultMaxTokens', label: 'Default Max Tokens', type: 'number', hint: 'Completion token limit (e.g. 4096). Fallback: env AI:DefaultMaxTokens.' },
  { key: 'aiProviderDefaultTemperature', label: 'Default Temperature', type: 'number', hint: 'Sampler temperature 0.0–1.0 (e.g. 0.2). Fallback: env AI:DefaultTemperature.' },
  { key: 'aiToolMaxToolCallsPerCompletion', label: 'Max Tool Calls per Completion', type: 'number', hint: 'Agentic loop breaker. When reached, gateway returns without trying more tool calls. Fallback: env AiTool:MaxToolCallsPerCompletion (default 4).' },
  { key: 'aiToolFeatureGrantCacheSeconds', label: 'Feature Grant Cache TTL (seconds)', type: 'number', hint: 'In-memory cache lifetime for per-feature tool grants. Fallback: env AiTool:FeatureGrantCacheSeconds (default 30).' },
  { key: 'aiToolAllowedExternalHostsCsv', label: 'Allowed External Hosts (CSV)', type: 'text', hint: 'Comma-separated hostnames for ExternalNetwork tools (no scheme/path; exact match). Default: api.dictionaryapi.dev. Fallback: env AiTool:AllowedExternalHosts.' },
  { key: 'aiToolExternalNetworkPerUserDailyCalls', label: 'External Network Daily Call Budget per User', type: 'number', hint: 'Max calls/user/day for ExternalNetwork tools (0 = disabled). Admin-class features (admin/expert assistant, admin drafts) are exempt. Fallback: env AiTool:ExternalNetworkPerUserDailyCalls (default 200).' },
  { key: 'aiToolExternalNetworkTimeoutMilliseconds', label: 'External Network HTTP Timeout (ms)', type: 'number', hint: 'Request timeout in milliseconds for external-network tool calls. Fallback: env AiTool:ExternalNetworkTimeoutMilliseconds (default 4000).' },
  { key: 'aiToolExternalNetworkMaxResponseBytes', label: 'External Network Max Response Size (bytes)', type: 'number', hint: 'Max response body size (defends unbounded downloads). Fallback: env AiTool:ExternalNetworkMaxResponseBytes (default 65536 = 64 KB).' },
];

export const WRITING_FIELDS: FieldDef<WritingSettings>[] = [
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

export const PLATFORM_FIELDS: FieldDef<PlatformSettings>[] = [
  { key: 'publicApiBaseUrl', label: 'Public API Base URL', type: 'url', hint: 'e.g. https://api.oetwithdrhesham.co.uk. Builds absolute callback URLs for external auth. Leave blank to use the env var Platform:PublicApiBaseUrl.' },
  { key: 'publicWebBaseUrl', label: 'Public Web Base URL', type: 'url', hint: 'e.g. https://app.oetwithdrhesham.co.uk. Used for external auth redirects and cookie CSRF origin validation. Required in production when external auth is enabled.' },
  { key: 'fallbackEmailDomain', label: 'Fallback Email Domain', type: 'text', hint: 'Domain for synthesized learner emails when no external provider email exists (default: example.invalid). Omit the @ symbol.' },
];

export const MESSAGING_FIELDS: FieldDef<MessagingSettings>[] = [
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

export const FX_FIELDS: FieldDef<FxSettings>[] = [
  { key: 'baseCurrency', label: 'Base Currency', type: 'text', hint: 'ISO 4217 code (e.g. USD, GBP, EUR). Reference currency for all FX pairs. Default USD.' },
  { key: 'apiKey', label: 'FX Provider API Key', secret: true, hint: 'openexchangerates.org app_id or compatible. Empty → offline seed rates. Stored encrypted.' },
  { key: 'apiBaseUrl', label: 'FX Provider Base URL', type: 'url', hint: 'e.g. https://openexchangerates.org/api. Ignored when no API key is set.' },
  { key: 'dynamicPricingEnabled', label: 'Enable Dynamic Pricing', type: 'checkbox', hint: 'When on, checkout amounts are FX-converted to the buyer display currency.' },
];

export const BILLING_CORE_FIELDS: FieldDef<BillingCoreSettings>[] = [
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

export const STORAGE_FIELDS: FieldDef<StorageSettings>[] = [
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

export const PDF_EXTRACTION_FIELDS: FieldDef<PdfExtractionSettings>[] = [
  { key: 'provider', label: 'PDF Extraction Provider', type: 'text', hint: 'noop | pdfpig | azure | auto (default: auto = pdfpig with azure fallback).' },
  { key: 'azureEndpoint', label: 'Azure Document Intelligence Endpoint', type: 'url', hint: 'e.g. https://{name}.cognitiveservices.azure.com/. Empty disables OCR.' },
  { key: 'azureApiKey', label: 'Azure Document Intelligence API Key', secret: true, hint: 'Stored encrypted; masked in UI.' },
  { key: 'minTextLengthForSuccess', label: 'Min Text Length for Success (chars)', type: 'number', hint: 'Below this, retry with Azure OCR if configured. Default 50.' },
];

export const PRONUNCIATION_FIELDS: FieldDef<PronunciationSettings>[] = [
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

export const AUTH_TOKENS_FIELDS: FieldDef<AuthTokensSettings>[] = [
  { key: 'accessTokenLifetimeSeconds', label: 'Access Token Lifetime (seconds)', type: 'number', hint: 'How long access tokens remain valid (e.g. 3600 = 1 hour). Blank uses env default.' },
  { key: 'refreshTokenLifetimeSeconds', label: 'Refresh Token Lifetime (seconds)', type: 'number', hint: 'How long refresh tokens remain valid (e.g. 2592000 = 30 days). Blank uses env default.' },
  { key: 'otpLifetimeSeconds', label: 'OTP / MFA Lifetime (seconds)', type: 'number', hint: 'Validity window for one-time passwords (e.g. 300 = 5 min). Blank uses env default.' },
  { key: 'authenticatorIssuer', label: 'Authenticator Issuer', type: 'text', hint: 'Issuer name shown in authenticator apps (e.g. OET Dr. Hesham). Blank uses env default.' },
];

export const WEB_PUSH_FIELDS: FieldDef<WebPushSettings>[] = [
  { key: 'enabled', label: 'Enable Browser Web Push', type: 'checkbox', hint: 'Allow learners to receive browser push notifications (requires VAPID keys in the Push section).' },
];

export const SUPPORT_FIELDS: FieldDef<SupportSettings>[] = [
  { key: 'whatsAppNumber', label: 'Support WhatsApp Number (dialable)', type: 'text', hint: 'The number learners message to send payment proof — shown next to every package. Digits only, country code first, no "+", spaces, or dashes (e.g. 447961725989); the server strips those and rejects anything else, because the value goes straight into a wa.me/<number> link. NOT the "WhatsApp Phone Number ID" in the Messaging section — that is the Meta Cloud API sender id and cannot be dialled. Blank falls back to the built-in default number.' },
  { key: 'whatsAppProofTemplate', label: 'Payment-Proof Message Template', type: 'text', hint: 'Message pre-filled for the learner when they tap "Send proof on WhatsApp". Blank uses the built-in wording. wa.me can only pre-fill text — the learner still attaches the screenshot themselves.' },
];

export const FIREBASE_OTP_FIELDS: FieldDef<FirebaseOtpSettings>[] = [
  { key: 'enabled', label: 'Enable Firebase SMS OTP', type: 'checkbox', hint: 'Master switch. Leave off until Phone Auth, SMS regions, and the authorized domain are ready in Firebase Console. Email verification always stays on Brevo.' },
  { key: 'smsEnabled', label: 'Allow SMS OTP', type: 'checkbox', hint: 'When on, password-reset and new-device codes prefer Firebase SMS if a mobile number and reCAPTCHA token are present.' },
  { key: 'fallbackToBrevo', label: 'Fall back to Brevo email', type: 'checkbox', hint: 'Required safety net. If Firebase SMS fails or is unavailable, send the existing 6-digit Brevo email instead.' },
  { key: 'projectId', label: 'Firebase Project ID', type: 'text', hint: 'Usually oet-prep-learner. Not the FCM service-account JSON from the Push section.' },
  { key: 'authDomain', label: 'Auth Domain', type: 'text', hint: 'Usually {projectId}.firebaseapp.com. Must match an authorized domain in Firebase Console.' },
  { key: 'webApiKey', label: 'Web API Key', secret: true, hint: 'Identity Toolkit browser key from Firebase Console → Project settings. Stored encrypted. The public runtime config publishes it as webKey, never apiKey.' },
];
