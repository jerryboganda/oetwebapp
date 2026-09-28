import type { RuntimeSettingsResponse } from './runtime-settings-types';
import { emptyResponse } from './runtime-settings-defaults';

export const MASKED = '********' as const;

/* ───────────────────────── Helpers ───────────────────────── */

export function normalizeResponse(data: Partial<RuntimeSettingsResponse>): RuntimeSettingsResponse {
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
    firebaseOtp: { ...empty.firebaseOtp, ...data.firebaseOtp },
    security: { ...empty.security, ...data.security },
    videoProtection: { ...empty.videoProtection, ...data.videoProtection },
  });
}

export function sanitizeSecretFields(data: RuntimeSettingsResponse): RuntimeSettingsResponse {
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
      fcmServiceAccountJson: maskUnexpectedSecret(data.push.fcmServiceAccountJson),
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
    security: {
      ...data.security,
      ipinfoToken: maskUnexpectedSecret(data.security.ipinfoToken),
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
    firebaseOtp: {
      ...data.firebaseOtp,
      webApiKey: maskUnexpectedSecret(data.firebaseOtp.webApiKey),
    },
  };
}

export function maskUnexpectedSecret(value: string): string {
  return value && value !== MASKED ? MASKED : value;
}

export function parseNullableNumberInput(value: string): number | null {
  if (value.trim() === '') return null;
  return Number(value);
}

export function getApiErrorMessage(err: unknown, fallback: string): string {
  if (err instanceof Error && err.message) return redactPotentialSecrets(err.message);
  if (typeof err === 'string') return redactPotentialSecrets(err);
  return fallback;
}

export function redactPotentialSecrets(value: string): string {
  return value.replace(
    /(?:github_pat_[A-Za-z0-9_]{20,}|ghp_[A-Za-z0-9]{20,}|sk-[A-Za-z0-9_-]{20,}|sk_live_[A-Za-z0-9_]{12,}|whsec_[A-Za-z0-9_]{12,}|AIza[0-9A-Za-z_-]{20,})/gi,
    '***REDACTED***',
  );
}

export function formatTimestamp(value: string | null): string {
  if (!value) return 'unknown time';
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) return value;
  return date.toLocaleString();
}
