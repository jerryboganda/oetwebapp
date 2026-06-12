import { screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

const {
  mockFetchBilling,
  mockFetchBillingChangePreview,
  mockFetchFreezeStatus,
  mockCreateBillingCheckoutSession,
  mockCreateWalletTopUp,
  mockDownloadInvoice,
  mockFetchBillingQuote,
  mockFetchWalletTopUpTiers,
  mockFetchWalletTransactions,
  mockFetchAvailablePaymentGateways,
  mockTrack,
} = vi.hoisted(() => ({
  mockFetchBilling: vi.fn(),
  mockFetchBillingChangePreview: vi.fn(),
  mockFetchFreezeStatus: vi.fn(),
  mockCreateBillingCheckoutSession: vi.fn(),
  mockCreateWalletTopUp: vi.fn(),
  mockDownloadInvoice: vi.fn(),
  mockFetchBillingQuote: vi.fn(),
  mockFetchWalletTopUpTiers: vi.fn(),
  mockFetchWalletTransactions: vi.fn(),
  mockFetchAvailablePaymentGateways: vi.fn(),
  mockTrack: vi.fn(),
}));

vi.mock('@/components/layout', () => ({
  LearnerDashboardShell: ({ children }: { children: React.ReactNode }) => (
    <div data-testid="learner-dashboard-shell">{children}</div>
  ),
}));

vi.mock('@/components/layout/app-shell', () => ({
  AppShell: ({ children }: { children: React.ReactNode }) => <div data-testid="app-shell">{children}</div>,
}));

vi.mock('@/lib/analytics', () => ({
  analytics: { track: mockTrack },
}));

vi.mock('@/lib/api', () => ({
  fetchBilling: mockFetchBilling,
  fetchBillingChangePreview: mockFetchBillingChangePreview,
  fetchFreezeStatus: mockFetchFreezeStatus,
  createBillingCheckoutSession: mockCreateBillingCheckoutSession,
  createWalletTopUp: mockCreateWalletTopUp,
  downloadInvoice: mockDownloadInvoice,
  fetchBillingQuote: mockFetchBillingQuote,
  fetchWalletTopUpTiers: mockFetchWalletTopUpTiers,
  fetchWalletTransactions: mockFetchWalletTransactions,
  fetchAiPackages: vi.fn().mockResolvedValue({ currency: 'AUD', full: [], separate: { listening: [], reading: [], writing: [], speaking: [] }, mock: [] }),
  fetchBillingContent: vi.fn().mockResolvedValue({}),
  fetchAvailablePaymentGateways: mockFetchAvailablePaymentGateways,
}));

import BillingPage from '../page';
import { renderWithRouter } from '@/tests/test-utils';

function defaultBillingData() {
  return {
    currentPlan: 'Pro',
    currentPlanId: 'plan-pro',
    currentPlanCode: 'pro',
    planName: 'Pro',
    planDescription: 'Current learner plan.',
    reviewCredits: 3,
    nextRenewal: '2026-06-27',
    price: '$49',
    interval: 'month',
    status: 'Active',
    activeAddOns: [],
    entitlements: {
      productiveSkillReviewsEnabled: true,
      supportedReviewSubtests: ['Writing', 'Speaking'],
      invoiceDownloadsAvailable: true,
    },
    plans: [],
    addOns: [],
    coupons: [],
    quote: null,
    invoices: [],
  };
}

describe('Billing page payment gateway availability', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mockFetchBilling.mockResolvedValue(defaultBillingData());
    mockFetchFreezeStatus.mockResolvedValue(null);
    mockFetchWalletTransactions.mockResolvedValue({ balance: 0, transactions: [] });
    mockFetchWalletTopUpTiers.mockResolvedValue({
      currency: 'AUD',
      tiers: [{ amount: 20, credits: 20, bonus: 0, totalCredits: 20, label: 'Booster', isPopular: false }],
    });
  });

  it('hides the gateway toggle when only Stripe is configured', async () => {
    mockFetchAvailablePaymentGateways.mockResolvedValue({ gateways: ['stripe'] });
    const user = userEvent.setup();
    renderWithRouter(<BillingPage />);

    expect(await screen.findByText('Your billing center')).toBeInTheDocument();
    await user.click(screen.getByRole('tab', { name: /credits & add-ons/i }));

    expect(await screen.findByText('Booster')).toBeInTheDocument();
    expect(screen.queryByText('Pay with')).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'PayPal' })).not.toBeInTheDocument();
  });

  it('shows both gateways when PayPal is configured', async () => {
    mockFetchAvailablePaymentGateways.mockResolvedValue({ gateways: ['stripe', 'paypal'] });
    const user = userEvent.setup();
    renderWithRouter(<BillingPage />);

    expect(await screen.findByText('Your billing center')).toBeInTheDocument();
    await user.click(screen.getByRole('tab', { name: /credits & add-ons/i }));

    expect(await screen.findByText('Pay with')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Stripe' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'PayPal' })).toBeInTheDocument();
  });

  it('falls back to Stripe-only when the availability lookup fails', async () => {
    mockFetchAvailablePaymentGateways.mockRejectedValue(new Error('offline'));
    const user = userEvent.setup();
    renderWithRouter(<BillingPage />);

    expect(await screen.findByText('Your billing center')).toBeInTheDocument();
    await user.click(screen.getByRole('tab', { name: /credits & add-ons/i }));

    expect(await screen.findByText('Booster')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'PayPal' })).not.toBeInTheDocument();
  });

  it('offers a Try again button when the initial billing load fails, and retries', async () => {
    mockFetchAvailablePaymentGateways.mockResolvedValue({ gateways: ['stripe'] });
    mockFetchBilling
      .mockRejectedValueOnce(new Error('Network down'))
      .mockResolvedValueOnce(defaultBillingData());
    const user = userEvent.setup();
    renderWithRouter(<BillingPage />);

    expect(await screen.findByText('Network down')).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: /try again/i }));

    expect(await screen.findByText('Your billing center')).toBeInTheDocument();
    expect(mockFetchBilling).toHaveBeenCalledTimes(2);
  });
});
