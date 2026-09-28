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
  authFromAddress: string;
  authFromName: string;
  marketingFromAddress: string;
  marketingFromName: string;
  productFromAddress: string;
  productFromName: string;
  supportFromAddress: string;
  supportFromName: string;
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
  fcmServiceAccountJson: string;
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

/** Course Platform Security Requirements §3.2/§3.3/§4.4 — account-security policy toggles. */
export interface SecuritySettings {
  singleActiveSessionEnabled: boolean | null;
  riskMode: string;
  trustedDeviceRequired: boolean | null;
  deviceChangeWindowDays: number | null;
  deviceChangeMaxPerWindow: number | null;
  inactiveSessionTimeoutDays: number | null;
  requireVerifiedEmailForLearners: boolean | null;
  countryAllowList: string | null;
  countryAllowListMode: string;
  deviceVerificationExemptEmails: string | null;
  ipIntelligenceProvider: string;
  ipinfoToken: string;
  ipIntelligenceConfigured?: boolean | null;
}

/** Course Platform Security Requirements §2.4 — video capture-protection response. */
export interface VideoProtectionSettings {
  revokeOnCaptureDetected: boolean | null;
  blockRootedDevices: boolean | null;
  blockEmulators: boolean | null;
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
  securityEventsDays: number | null;
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

export interface FirebaseOtpSettings {
  enabled: boolean | null;
  smsEnabled: boolean | null;
  emailLinksEnabled: boolean | null;
  fallbackToBrevo: boolean | null;
  projectId: string;
  authDomain: string;
  webApiKey: string;
  isSmsConfigured?: boolean | null;
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
  firebaseOtp: FirebaseOtpSettings;
  security: SecuritySettings;
  videoProtection: VideoProtectionSettings;
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
