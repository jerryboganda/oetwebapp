import { screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

const {
  mockListStripeAccounts,
  mockCreateStripeAccount,
  mockUpdateStripeAccount,
  mockSetDefaultStripeAccount,
  mockTestStripeAccountConnection,
  mockDeleteStripeAccount,
} = vi.hoisted(() => ({
  mockListStripeAccounts: vi.fn(),
  mockCreateStripeAccount: vi.fn(),
  mockUpdateStripeAccount: vi.fn(),
  mockSetDefaultStripeAccount: vi.fn(),
  mockTestStripeAccountConnection: vi.fn(),
  mockDeleteStripeAccount: vi.fn(),
}));

vi.mock('@/lib/api', () => ({
  listStripeAccounts: mockListStripeAccounts,
  createStripeAccount: mockCreateStripeAccount,
  updateStripeAccount: mockUpdateStripeAccount,
  setDefaultStripeAccount: mockSetDefaultStripeAccount,
  testStripeAccountConnection: mockTestStripeAccountConnection,
  deleteStripeAccount: mockDeleteStripeAccount,
}));

vi.mock('@/contexts/auth-context', () => ({
  useAuth: () => ({ user: { userId: 'admin-1', role: 'admin', permissions: ['BillingRead', 'BillingWrite'] } }),
}));

import AdminStripeAccountsPage from './page';
import { renderWithRouter } from '@/tests/test-utils';

const sampleAccounts = [
  {
    id: 'stripe-1',
    label: 'UK Main Stripe',
    mode: 'live',
    publishableKey: 'pk_live_••••••••9K2',
    secretKeyHint: 'sk_live_...9k2',
    hasSecretKey: true,
    hasWebhookSecret: true,
    stripeAccountId: 'acct_123456789',
    isActive: true,
    isDefault: true,
    lastTestedAt: '2026-08-15T12:00:00Z',
    lastTestResult: 'ok',
    routingCountriesCsv: 'GB,EU',
  },
  {
    id: 'stripe-2',
    label: 'Secondary Stripe (Gulf)',
    mode: 'live',
    publishableKey: 'pk_live_••••••••8J1',
    secretKeyHint: 'sk_live_...8j1',
    hasSecretKey: true,
    hasWebhookSecret: false,
    stripeAccountId: 'acct_987654321',
    isActive: true,
    isDefault: false,
    lastTestedAt: null,
    lastTestResult: null,
    routingCountriesCsv: 'AE,SA,KW',
  },
];

describe('Admin Stripe Accounts Page', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mockListStripeAccounts.mockResolvedValue(sampleAccounts);
    mockCreateStripeAccount.mockResolvedValue({ id: 'stripe-3', label: 'New Stripe' });
    mockUpdateStripeAccount.mockResolvedValue({ id: 'stripe-1', label: 'UK Main Stripe' });
    mockTestStripeAccountConnection.mockResolvedValue({ id: 'stripe-1', lastTestResult: 'ok', stripeAccountId: 'acct_123456789' });
  });

  it('renders existing Stripe accounts with status, mode, and masked keys', async () => {
    renderWithRouter(<AdminStripeAccountsPage />);

    expect(await screen.findByText('UK Main Stripe')).toBeInTheDocument();
    expect(screen.getByText('Secondary Stripe (Gulf)')).toBeInTheDocument();
    expect(screen.getByText('Default')).toBeInTheDocument();
    expect(screen.getByText('sk_live_...9k2')).toBeInTheDocument();
  });

  it('allows opening the editor and provides Save & Test Connection', async () => {
    const user = userEvent.setup();
    renderWithRouter(<AdminStripeAccountsPage />);

    await user.click(await screen.findByRole('button', { name: /new account/i }));

    expect(await screen.findByRole('dialog')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /save & test connection/i })).toBeInTheDocument();

    await user.type(screen.getByLabelText(/label/i), 'New Global Stripe');
    await user.type(screen.getByLabelText(/publishable key/i), 'pk_live_123');
    await user.type(screen.getByLabelText(/secret key/i), 'sk_live_123');

    await user.click(screen.getByRole('button', { name: /save & test connection/i }));

    await waitFor(() => expect(mockCreateStripeAccount).toHaveBeenCalledTimes(1));
    expect(mockCreateStripeAccount).toHaveBeenCalledWith(
      expect.objectContaining({
        label: 'New Global Stripe',
        publishableKey: 'pk_live_123',
        secretKey: 'sk_live_123',
        isActive: true,
      }),
    );
  });
});
