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

const { mockGet, mockPut } = vi.hoisted(() => ({
  mockGet: vi.fn(),
  mockPut: vi.fn(),
}));

vi.mock('@/lib/api', () => ({
  apiClient: {
    get: mockGet,
    put: mockPut,
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
      brevoEmailVerificationTemplateId: '7',
      brevoPasswordResetTemplateId: '9',
      smtpHost: 'smtp.example.com',
      smtpPort: 587,
      smtpUsername: 'mailer',
      smtpPassword: '********',
      smtpFromAddress: 'noreply@example.com',
      smtpFromName: 'Example',
    },
    billing: {
      stripeSecretKey: '',
      stripePublishableKey: 'pk_test_123',
      stripeWebhookSecret: '',
      stripeSuccessUrl: 'https://example.com/success',
      stripeCancelUrl: 'https://example.com/cancel',
    },
    sentry: { sentryDsn: 'https://abc@sentry.io/1', sentryEnvironment: 'production', sentrySampleRate: 0.1 },
    backup: {
      backupS3Url: 's3://b/p',
      backupAwsAccessKeyId: 'AKIA',
      backupAwsSecretAccessKey: '********',
      backupGpgPassphrase: '********',
      backupAlertWebhook: 'https://example.com/hook',
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
    },
    push: {
      apnsKeyId: 'kid',
      apnsTeamId: 'tid',
      apnsBundleId: 'com.example',
      apnsAuthKey: '********',
      fcmServerKey: '',
      fcmProjectId: '',
    },
    updatedBy: 'admin@example.com',
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

describe('RuntimeSettingsClient', () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it('renders all 6 sections after the initial fetch resolves', async () => {
    mockGet.mockResolvedValue(makeResponse());

    render(<RuntimeSettingsClient />);

    expect(await screen.findByRole('region', { name: 'Email (Brevo + SMTP)' })).toBeInTheDocument();
    expect(screen.getByRole('region', { name: 'Billing (Stripe)' })).toBeInTheDocument();
    expect(screen.getByRole('region', { name: 'Sentry' })).toBeInTheDocument();
    expect(screen.getByRole('region', { name: 'Backup S3' })).toBeInTheDocument();
    expect(screen.getByRole('region', { name: 'OAuth (Google + Apple + Facebook)' })).toBeInTheDocument();
    expect(screen.getByRole('region', { name: 'Push (APNs + FCM)' })).toBeInTheDocument();
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

    await user.click(screen.getByRole('button', { name: 'Save all runtime settings' }));

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
    await user.click(screen.getByRole('button', { name: 'Save all runtime settings' }));

    await waitFor(() => expect(mockPut).toHaveBeenCalledTimes(1));
    const payload = mockPut.mock.calls[0][1] as RuntimeSettingsResponse;
    // Secrets that the user never touched stay as '********'.
    expect(payload.email.brevoApiKey).toBe('********');
    expect(payload.email.smtpPassword).toBe('********');
    expect(payload.backup.backupAwsSecretAccessKey).toBe('********');
  });

  it('sends an empty string when the user clicks Clear on a set secret', async () => {
    mockGet.mockResolvedValue(makeResponse());
    mockPut.mockResolvedValue(makeResponse());
    const user = userEvent.setup();

    render(<RuntimeSettingsClient />);

    await screen.findByRole('region', { name: 'Email (Brevo + SMTP)' });
    await user.click(screen.getByRole('button', { name: 'Clear Brevo API Key' }));
    await user.click(screen.getByRole('button', { name: 'Save all runtime settings' }));

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
    await user.click(screen.getByRole('button', { name: 'Save all runtime settings' }));

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
    await user.click(screen.getByRole('button', { name: 'Save all runtime settings' }));

    expect(await screen.findByText('Stripe key is invalid')).toBeInTheDocument();
  });
});
