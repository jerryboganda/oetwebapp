import { screen } from '@testing-library/react';

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

describe('Billing page payment status banner', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mockFetchBilling.mockResolvedValue(defaultBillingData());
    mockFetchFreezeStatus.mockResolvedValue(null);
    mockFetchWalletTransactions.mockResolvedValue({ balance: 0, transactions: [] });
    mockFetchWalletTopUpTiers.mockResolvedValue({
      currency: 'AUD',
      tiers: [
        { amount: 10, credits: 10, bonus: 0, totalCredits: 10, label: 'Starter', isPopular: false },
      ],
    });
  });

  it('renders a success banner for ?payment=success&gateway=stripe', async () => {
    renderWithRouter(<BillingPage />, {
      searchParams: new URLSearchParams('payment=success&gateway=stripe'),
    });

    expect(await screen.findByText('Your billing center')).toBeInTheDocument();
    expect(
      screen.getByText(/Your Stripe checkout completed\./i),
    ).toBeInTheDocument();
  });

  it('renders an error banner for ?payment=failed&gateway=paypal', async () => {
    renderWithRouter(<BillingPage />, {
      searchParams: new URLSearchParams('payment=failed&gateway=paypal'),
    });

    expect(await screen.findByText('Your billing center')).toBeInTheDocument();
    expect(
      screen.getByText(/The Paypal checkout failed before completion\./i),
    ).toBeInTheDocument();
  });

  it('renders a warning banner for ?payment=cancelled (no gateway)', async () => {
    renderWithRouter(<BillingPage />, {
      searchParams: new URLSearchParams('payment=cancelled'),
    });

    expect(await screen.findByText('Your billing center')).toBeInTheDocument();
    expect(
      screen.getByText(/Your payment checkout was cancelled before payment\./i),
    ).toBeInTheDocument();
  });
});
