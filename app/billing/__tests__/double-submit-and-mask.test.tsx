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

describe('Billing page double-submit guard (wallet top-up)', () => {
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

  it('only fires the top-up call once even when the tile is double-clicked', async () => {
    let resolveTopUp: (v: unknown) => void = () => {};
    mockCreateWalletTopUp.mockImplementation(
      () => new Promise((resolve) => {
        resolveTopUp = resolve;
      }),
    );

    const user = userEvent.setup();
    renderWithRouter(<BillingPage />);

    expect(await screen.findByText('Your billing center')).toBeInTheDocument();
    await user.click(screen.getByRole('tab', { name: /credits & add-ons/i }));

    const tile = (await screen.findByText('Starter')).closest('button');
    expect(tile).not.toBeNull();

    // Two synchronous clicks while the request is in flight.
    await user.click(tile as HTMLElement);
    await user.click(tile as HTMLElement);

    expect(mockCreateWalletTopUp).toHaveBeenCalledTimes(1);
    expect(mockCreateWalletTopUp).toHaveBeenCalledWith(
      10,
      'stripe',
      // The 3rd argument is a freshly-generated idempotency key.
      expect.stringMatching(/.+/),
    );

    // Resolve the in-flight request so test cleanup doesn't dangle.
    resolveTopUp({ checkoutUrl: 'about:blank', totalCredits: 10 });
  });
});

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
