/**
 * Vitest spec for the Runtime Settings admin client.
 *
 * Mocks `@/lib/api` (so no real network calls happen) and `@/contexts/auth-context`
 * to grant `system_admin` permission. Mirrors the project's existing admin test
 * pattern (see `app/admin/ai-providers/page.test.tsx`).
 */
import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import type { RuntimeSettingsResponse } from './RuntimeSettingsClient';

const { mockGet, mockPut, mockPost } = vi.hoisted(() => ({
  mockGet: vi.fn(),
  mockPut: vi.fn(),
  mockPost: vi.fn(),
}));

vi.mock('@/lib/api', () => ({
  apiClient: {
    get: mockGet,
    put: mockPut,
    post: mockPost,
  },
}));

vi.mock('@/contexts/auth-context', () => ({
  useAuth: () => ({
    user: {
      userId: 'u-1',
      email: 'admin@example.com',
      role: 'admin',
      adminPermissions: ['system_admin'],
    },
    loading: false,
  }),
}));

import { RuntimeSettingsClient } from './RuntimeSettingsClient';

function makeResponse(overrides: Partial<RuntimeSettingsResponse> = {}): RuntimeSettingsResponse {
  return {
    email: {
      brevoApiKey: '********',
      brevoEmailVerificationTemplateId: 7,
      brevoPasswordResetTemplateId: 9,
      smtpHost: 'smtp.example.com',
      smtpPort: 587,
      smtpUsername: 'mailer',
      smtpPassword: '********',
      smtpFromAddress: 'noreply@example.com',
      smtpFromName: 'Example',
      brevoWelcomeTemplateId: 11,
      brevoPasswordChangedTemplateId: 12,
      brevoMfaEnabledTemplateId: 13,
      brevoAdminInviteTemplateId: 14,
      brevoSecurityAlertTemplateId: 15,
      brevoReviewCompletedTemplateId: 16,
      brevoWebhookSecret: '********',
      brevoEnabled: true,
      smtpEnabled: false,
      smtpEnableSsl: true,
    },
    billing: {
      stripeSecretKey: '',
      stripePublishableKey: 'pk_test_123',
      stripeWebhookSecret: '',
      stripeSuccessUrl: 'https://example.com/success',
      stripeCancelUrl: 'https://example.com/cancel',
      publicAppBaseUrl: 'https://example.com',
      paypalClientId: 'paypal-client',
      paypalClientSecret: '********',
      paypalWebhookId: '********',
      paypalSuccessUrl: 'https://example.com/paypal/success',
      paypalCancelUrl: 'https://example.com/paypal/cancel',
      paypalAdvancedCardsEnabled: true,
    },
    sentry: { dsn: 'https://abc@sentry.io/1', environment: 'production', sampleRate: 0.1 },
    backup: {
      s3Url: 's3://b/p',
      awsAccessKeyId: 'AKIA',
      awsSecretAccessKey: '********',
      gpgPassphrase: '********',
      alertWebhook: 'https://example.com/hook',
    },
    oauth: {
      googleClientId: 'g-id',
      googleClientSecret: '********',
      appleClientId: '',
      appleTeamId: '',
      appleKeyId: '',
      applePrivateKey: '',
      facebookAppId: '',
      facebookAppSecret: '',
      linkedInClientId: '********',
      linkedInClientSecret: '********',
      linkedInEnabled: true,
      googleAuthEnabled: true,
      facebookAuthEnabled: false,
    },
    push: {
      apnsKeyId: 'kid',
      apnsTeamId: 'tid',
      apnsBundleId: 'com.example',
      apnsAuthKey: '********',
      fcmServerKey: '',
      fcmProjectId: '',
      vapidSubject: 'mailto:support@example.com',
      vapidPublicKey: 'vapid-public',
      vapidPrivateKey: '********',
    },
    uploadScanner: {
      provider: 'clamav',
      host: 'clamav',
      port: 3310,
      timeoutSeconds: 10,
      failClosedOnError: true,
    },
    zoom: {
      enabled: true,
      accountId: 'zoom-account',
      clientId: 'zoom-client',
      clientSecret: '********',
      apiBaseUrl: 'https://api.zoom.us/v2',
      tokenUrl: 'https://zoom.us/oauth/token',
      hostUserId: 'host@example.com',
      meetingSdkKey: 'zoom-sdk-key',
      meetingSdkSecret: '********',
      webhookSecretToken: '********',
      webhookRetryToleranceSeconds: 300,
      allowSandboxFallback: false,
    },
    speakingWhisper: {
      apiKey: '********',
      baseUrl: 'https://api.openai.com/v1',
      model: 'whisper-1',
      isConfigured: true,
    },
    speakingLiveKit: {
      provider: 'livekit',
      apiKey: 'lk-key',
      apiSecret: '********',
      wssUrl: 'wss://example.livekit.cloud',
      webhookSigningSecret: '********',
      egressBucket: 'speaking-egress',
      defaultMaxDurationSeconds: 1800,
      egressEnabled: true,
      isEnabled: true,
    },
    speakingAi: {
      anthropicApiKey: '********',
      elevenLabsApiKey: '********',
      isAnthropicConfigured: true,
      isElevenLabsConfigured: true,
    },
    speakingStorage: {
      awsAccessKeyId: 'AKIA',
      awsSecretAccessKey: '********',
      region: 'eu-west-1',
      bucket: 'speaking-recordings',
      isConfigured: true,
    },
    speakingCompliance: {
      currentConsentVersion: 'v1',
      currentLiveVideoConsentVersion: 'v1',
      retentionDaysDefault: 30,
      retentionDaysWhenTutorReviewed: 90,
      auditLogRetentionDays: 365,
    },
    speakingFeatures: {
      speakingV2Enabled: true,
    },
    checkoutCom: {
      apiBaseUrl: 'https://api.checkout.com',
      secretKey: '********',
      publicKey: 'pk_test_cko',
      processingChannelId: 'pc_123',
      webhookSecret: '********',
      successUrl: 'https://example.com/cko/success',
      cancelUrl: 'https://example.com/cko/cancel',
      isConfigured: true,
    },
    bunnyStream: {
      enabled: false,
      libraryId: '',
      apiKey: '',
      cdnHostname: '',
      tokenAuthKey: '',
      webhookSecret: '',
      collectionId: '',
      playbackTokenTtlSeconds: 14400,
      videoAttestationKeysJson: '',
      videoAttestationKeys: '',
      videoAttestationKeyIds: [],
      isConfigured: false,
    },
    paymob: {
      apiBaseUrl: 'https://accept.paymob.com',
      apiKey: '********',
      merchantId: 'm-1',
      hmacSecret: '********',
      integrationIdsJson: '{"card":123}',
      iframeId: 456,
      successUrl: 'https://example.com/paymob/success',
      cancelUrl: 'https://example.com/paymob/cancel',
      isConfigured: true,
    },
    payTabs: {
      apiBaseUrl: 'https://secure.paytabs.com',
      serverKey: '********',
      profileId: 'prof-1',
      webhookSecret: '********',
      successUrl: 'https://example.com/paytabs/success',
      cancelUrl: 'https://example.com/paytabs/cancel',
      isConfigured: true,
    },
    soketi: {
      host: 'soketi',
      port: 6001,
      appId: 'oet-app',
      appKey: 'oet-key',
      appSecret: '********',
      useTls: false,
      enabled: true,
    },
    dataRetention: {
      analyticsEventsDays: 365,
      auditEventsDays: 730,
      paymentWebhookEventsDays: 180,
      paymentWebhookPiiNullOutAgeDays: 90,
      notificationDeliveryAttemptsDays: 90,
      sweepIntervalHours: 24,
      batchSize: 5000,
    },
    expertAutoAssignment: {
      enabled: true,
      pollingIntervalSeconds: 30,
      slaEscalationIntervalSeconds: 60,
      slaHoursStandard: 48,
      slaHoursExpress: 12,
      maxActiveAssignmentsPerExpert: 8,
      lookbackHoursForLoad: 24,
      batchSize: 50,
    },
    passwordPolicy: {
      minimumLength: 10,
      requireMixedCase: true,
      requireDigit: true,
      requireSymbol: true,
      breachCheckEnabled: true,
      breachApiBaseUrl: 'https://api.pwnedpasswords.com/',
      breachApiTimeoutSeconds: 3,
    },
    aiAssistant: {
      globalEnabled: false,
      requireApprovalAlways: true,
      maxIterations: 10,
      maxContextMessages: 50,
      backupRetentionDays: 30,
      maxWriteFileSizeBytes: 1048576,
      commandTimeoutSeconds: 300,
      circuitBreakerMaxFailures: 3,
      circuitBreakerFailureWindowSeconds: 60,
      circuitBreakerMaxWrites: 10,
      circuitBreakerWriteWindowSeconds: 300,
      embeddingModel: 'text-embedding-3-small',
      maxChunkTokens: 512,
    },
    aiGateway: {
      aiProviderProviderId: 'digitalocean-serverless',
      aiProviderBaseUrl: 'https://inference.do-ai.run/v1',
      aiProviderDefaultModel: 'glm-5',
      aiProviderReasoningEffort: '',
      aiProviderDefaultMaxTokens: 4096,
      aiProviderDefaultTemperature: 0.2,
      aiToolMaxToolCallsPerCompletion: 4,
      aiToolFeatureGrantCacheSeconds: 30,
      aiToolAllowedExternalHostsCsv: 'api.dictionaryapi.dev',
      aiToolExternalNetworkPerUserDailyCalls: 200,
      aiToolExternalNetworkTimeoutMilliseconds: 4000,
      aiToolExternalNetworkMaxResponseBytes: 65536,
    },
    writing: {
      cronsEnabled: true,
      coachEnabled: true,
      coachDailyCostCapPerLearnerUsd: 0.5,
      coachMaxHintsPerSession: 80,
      coachMinSecondsBetweenHints: 30,
      gcvApiKey: '',
      ocrEnabled: false,
      appealsEnabled: true,
      tutorReviewQueueMaxDepth: 50,
      tutorReviewMaxWaitHours: 36,
      maxDailyPlanRegenerationsPerDay: 1,
      gradeIdempotencyTtlHours: 24,
    },
    platform: {
      publicApiBaseUrl: 'https://api.example.com',
      publicWebBaseUrl: 'https://app.example.com',
      fallbackEmailDomain: 'example.invalid',
    },
    messaging: {
      twilioEnabled: true,
      twilioApiBaseUrl: 'https://api.twilio.com',
      twilioAccountSid: 'AC123',
      twilioAuthToken: '********',
      twilioFromNumber: '+15551234567',
      twilioMessagingServiceSid: 'MG123',
      whatsAppEnabled: false,
      whatsAppApiBaseUrl: 'https://graph.facebook.com/v20.0',
      whatsAppAccessToken: '********',
      whatsAppPhoneNumberId: 'PN123',
      whatsAppFallbackTemplateName: 'billing_reminder',
    },
    fx: {
      baseCurrency: 'USD',
      apiKey: '********',
      apiBaseUrl: 'https://openexchangerates.org/api',
      dynamicPricingEnabled: false,
    },
    billingCore: {
      checkoutBaseUrl: 'https://checkout.example.com',
      webhookMaxAgeSeconds: 300,
      webhookMaxAttempts: 5,
      defaultCurrency: 'GBP',
      defaultRegion: 'ROW',
      walletCurrency: 'AUD',
      walletTopUpTiersJson: '[{"Amount":10,"Credits":3,"Bonus":0,"Label":"Starter","IsPopular":false}]',
      paypalUseSandbox: true,
      paypalApiBaseUrl: 'https://api-m.paypal.com',
    },
    storage: {
      provider: 's3',
      bucketName: 'oet-media',
      endpointUrl: 'https://ams3.digitaloceanspaces.com',
      accessKeyId: '********',
      secretAccessKey: '********',
      awsRegion: 'eu-west-2',
      signedReadTtlSeconds: 3600,
      maxAudioBytes: 157286400,
      maxPdfBytes: 26214400,
      maxImageBytes: 5242880,
      maxZipBytes: 524288000,
      maxZipEntries: 5000,
      maxZipEntryBytes: 157286400,
      maxZipUncompressedBytes: 2147483648,
      maxZipCompressionRatio: 100,
      chunkSizeBytes: 8388608,
      stagingTtlHours: 24,
      isConfigured: true,
    },
    pdfExtraction: {
      provider: 'auto',
      azureEndpoint: 'https://docintel.cognitiveservices.azure.com/',
      azureApiKey: '********',
      minTextLengthForSuccess: 50,
    },
    pronunciation: {
      provider: 'auto',
      azureSpeechRegion: 'uksouth',
      azureLocale: 'en-GB',
      whisperBaseUrl: 'https://api.openai.com/v1',
      whisperModel: 'whisper-1',
      geminiBaseUrl: 'https://generativelanguage.googleapis.com/v1beta',
      geminiModel: 'gemini-3.5-flash',
      maxAudioBytes: 15728640,
      audioRetentionDays: 45,
      freeTierWeeklyAttemptLimit: 20,
      freeTierWindowDays: 7,
    },
    authTokens: {
      accessTokenLifetimeSeconds: 900,
      refreshTokenLifetimeSeconds: 2592000,
      otpLifetimeSeconds: 600,
      authenticatorIssuer: 'OET Learner',
    },
    webPush: {
      enabled: true,
    },
    updatedBy: 'admin@example.com',
    updatedByUserId: 'u-1',
    updatedAt: '2026-05-16T10:00:00Z',
    ...overrides,
  };
}

async function expandSection(name: string) {
  const user = userEvent.setup();
  const trigger = screen.getByRole('button', { name: new RegExp(`^${name}`) });
  if (trigger.getAttribute('aria-expanded') === 'false') {
    await user.click(trigger);
  }
}

function getPrimarySaveButton() {
  return screen.getAllByRole('button', { name: 'Save all runtime settings' })[0];
}

describe('RuntimeSettingsClient', () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it('renders all runtime integration sections after the initial fetch resolves', async () => {
    mockGet.mockResolvedValue(makeResponse());

    render(<RuntimeSettingsClient />);

    expect(await screen.findByRole('region', { name: 'Email (Brevo + SMTP)' })).toBeInTheDocument();
    expect(screen.getByRole('region', { name: 'Billing (Stripe)' })).toBeInTheDocument();
    expect(screen.getByRole('region', { name: 'Sentry' })).toBeInTheDocument();
    expect(screen.getByRole('region', { name: 'Backup S3' })).toBeInTheDocument();
    expect(screen.getByRole('region', { name: 'OAuth (Google + Apple + Facebook)' })).toBeInTheDocument();
    expect(screen.getByRole('region', { name: 'Push (Browser + APNs + FCM)' })).toBeInTheDocument();
    expect(screen.getByRole('region', { name: 'Upload Scanner (ClamAV)' })).toBeInTheDocument();
    expect(screen.getByRole('region', { name: 'Zoom Live Classes' })).toBeInTheDocument();
  });

  it('shows the "Set" badge when the API returns the masked sentinel', async () => {
    mockGet.mockResolvedValue(makeResponse());

    render(<RuntimeSettingsClient />);

    const region = await screen.findByRole('region', { name: 'Email (Brevo + SMTP)' });
    // Brevo API key is '********' in the fixture → labelled "Set".
    const brevoLabel = within(region).getByText('Brevo API Key');
    const row = brevoLabel.closest('div')!.parentElement!;
    expect(within(row).getByText('Set')).toBeInTheDocument();
  });

  it('shows the "Not set" badge when the API returns an empty string', async () => {
    mockGet.mockResolvedValue(makeResponse());

    render(<RuntimeSettingsClient />);

    await screen.findByRole('region', { name: 'Email (Brevo + SMTP)' });
    await expandSection('Billing \\(Stripe\\)');

    const region = screen.getByRole('region', { name: 'Billing (Stripe)' });
    const label = within(region).getByText('Stripe Secret Key');
    const row = label.closest('div')!.parentElement!;
    expect(within(row).getByText('Not set')).toBeInTheDocument();
  });

  it('sends the typed value when the user edits a secret field and saves', async () => {
    mockGet.mockResolvedValue(makeResponse());
    mockPut.mockResolvedValue(makeResponse({ updatedBy: 'admin@example.com', updatedAt: '2026-05-16T11:00:00Z' }));
    const user = userEvent.setup();

    render(<RuntimeSettingsClient />);

    const brevoLabel = await screen.findByLabelText('Brevo API Key');
    await user.type(brevoLabel, 'new-brevo-key');

    await user.click(getPrimarySaveButton());

    await waitFor(() => expect(mockPut).toHaveBeenCalledTimes(1));
    const [path, body] = mockPut.mock.calls[0];
    expect(path).toBe('/v1/admin/runtime-settings');
    const payload = body as RuntimeSettingsResponse;
    expect(payload.email.brevoApiKey).toBe('new-brevo-key');
  });

  it('preserves untouched secrets by sending the masked sentinel back unchanged', async () => {
    mockGet.mockResolvedValue(makeResponse());
    mockPut.mockResolvedValue(makeResponse());
    const user = userEvent.setup();

    render(<RuntimeSettingsClient />);

    await screen.findByRole('region', { name: 'Email (Brevo + SMTP)' });
    await user.click(getPrimarySaveButton());

    await waitFor(() => expect(mockPut).toHaveBeenCalledTimes(1));
    const payload = mockPut.mock.calls[0][1] as RuntimeSettingsResponse;
    // Secrets that the user never touched stay as '********'.
    expect(payload.email.brevoApiKey).toBe('********');
    expect(payload.email.smtpPassword).toBe('********');
    expect(payload.backup.awsSecretAccessKey).toBe('********');
    expect(payload.zoom.clientSecret).toBe('********');
    expect(payload.zoom.meetingSdkSecret).toBe('********');
  });

  it('sends an empty string when the user clicks Clear on a set secret', async () => {
    mockGet.mockResolvedValue(makeResponse());
    mockPut.mockResolvedValue(makeResponse());
    const user = userEvent.setup();

    render(<RuntimeSettingsClient />);

    await screen.findByRole('region', { name: 'Email (Brevo + SMTP)' });
    await user.click(screen.getByRole('button', { name: 'Clear Brevo API Key' }));
    await user.click(getPrimarySaveButton());

    await waitFor(() => expect(mockPut).toHaveBeenCalledTimes(1));
    const payload = mockPut.mock.calls[0][1] as RuntimeSettingsResponse;
    expect(payload.email.brevoApiKey).toBe('');
  });

  it('shows a success toast after a successful save', async () => {
    mockGet.mockResolvedValue(makeResponse());
    mockPut.mockResolvedValue(makeResponse());
    const user = userEvent.setup();

    render(<RuntimeSettingsClient />);

    await screen.findByRole('region', { name: 'Email (Brevo + SMTP)' });
    await user.click(getPrimarySaveButton());

    expect(
      await screen.findByText('Runtime settings saved. Changes apply within ~30 seconds.'),
    ).toBeInTheDocument();
  });

  it('shows an error toast with the API error message when saving fails', async () => {
    mockGet.mockResolvedValue(makeResponse());
    mockPut.mockRejectedValue(new Error('Stripe key is invalid'));
    const user = userEvent.setup();

    render(<RuntimeSettingsClient />);

    await screen.findByRole('region', { name: 'Email (Brevo + SMTP)' });
    await user.click(getPrimarySaveButton());

    expect(await screen.findByText('Stripe key is invalid')).toBeInTheDocument();
  });

  it('tests an integration section without saving settings', async () => {
    mockGet.mockResolvedValue(makeResponse());
    mockPost.mockResolvedValue({
      section: 'uploadscanner',
      status: 'ok',
      message: 'ClamAV TCP endpoint accepted a connection.',
      testedAt: '2026-05-16T11:00:00Z',
    });
    const user = userEvent.setup();

    render(<RuntimeSettingsClient />);

    await screen.findByRole('region', { name: 'Email (Brevo + SMTP)' });
    await expandSection('Upload Scanner \\(ClamAV\\)');
    await user.click(screen.getByRole('button', { name: 'Test Upload Scanner (ClamAV)' }));

    await waitFor(() => expect(mockPost).toHaveBeenCalledTimes(1));
    expect(mockPost).toHaveBeenCalledWith('/v1/admin/runtime-settings/test/uploadScanner');
    expect(mockPut).not.toHaveBeenCalled();
    await waitFor(() =>
      expect(screen.getAllByText('ClamAV TCP endpoint accepted a connection.').length).toBeGreaterThan(0),
    );
  });

  it('masks unexpected raw secret values returned by the API before rendering', async () => {
    mockGet.mockResolvedValue(makeResponse({
      email: {
        ...makeResponse().email,
        brevoApiKey: 'raw-brevo-secret',
      },
    }));

    render(<RuntimeSettingsClient />);

    const input = await screen.findByLabelText('Brevo API Key');
    expect(input).toHaveValue('');
    expect(screen.queryByDisplayValue('raw-brevo-secret')).not.toBeInTheDocument();
  });
});
