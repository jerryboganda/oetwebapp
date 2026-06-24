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
  fetchAiPackages: vi.fn().mockResolvedValue({ currency: 'AUD', full: [], separate: { listening: [], reading: [], writing: [], speaking: [] }, mock: [] }),
  fetchBillingContent: vi.fn().mockResolvedValue({}),
  fetchAvailablePaymentGateways: vi.fn().mockResolvedValue({ gateways: ['stripe', 'paypal'] }),
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
    invoices: [
      {
        id: 'in_1NXyHk2eZvKYlo2CABCDEFGH',
        date: '2026-04-01',
        amount: '$49.00',
        status: 'Paid' as const,
      },
    ],
  };
}

describe('Billing page invoice ID masking', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mockFetchBilling.mockResolvedValue(defaultBillingData());
    mockFetchFreezeStatus.mockResolvedValue(null);
    mockFetchWalletTransactions.mockResolvedValue({ balance: 0, transactions: [] });
    mockFetchWalletTopUpTiers.mockResolvedValue({
      currency: 'AUD',
      tiers: [],
    });
  });

  it('masks raw provider invoice IDs in the recent invoices list', async () => {
    renderWithRouter(<BillingPage />);

    // Wait for the page to settle.
    expect(await screen.findByText('Your billing center')).toBeInTheDocument();

    // The raw token must NEVER appear in full anywhere on the page.
    expect(
      screen.queryByText(/in_1NXyHk2eZvKYlo2CABCDEFGH/),
    ).not.toBeInTheDocument();

    // The masked rendering for `in_1NXyHk2eZvKYlo2CABCDEFGH` is `in_***FGH` (last 4
    // chars after the underscore prefix).
    const masked = screen.getAllByText(/in_\*\*\*[A-Za-z0-9]{4}/);
    expect(masked.length).toBeGreaterThan(0);
  });
});
