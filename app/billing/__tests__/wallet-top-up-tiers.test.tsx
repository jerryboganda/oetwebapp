import { screen, within } from '@testing-library/react';
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
    invoices: [],
  };
}

describe('Billing page wallet top-up tiers (custom currency)', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mockFetchBilling.mockResolvedValue(defaultBillingData());
    mockFetchFreezeStatus.mockResolvedValue(null);
    mockFetchWalletTransactions.mockResolvedValue({ balance: 0, transactions: [] });
    mockFetchWalletTopUpTiers.mockResolvedValue({
      currency: 'USD',
      tiers: [
        { amount: 5, credits: 5, bonus: 0, totalCredits: 5, label: 'Trial', isPopular: false },
        { amount: 20, credits: 22, bonus: 2, totalCredits: 24, label: 'Booster', isPopular: true },
        { amount: 75, credits: 90, bonus: 15, totalCredits: 105, label: 'Pro pack', isPopular: false },
      ],
    });
  });

  it('renders the custom 3-tier set with USD currency and a single popular badge', async () => {
    const user = userEvent.setup();
    renderWithRouter(<BillingPage />);

    expect(await screen.findByText('Your billing center')).toBeInTheDocument();
    await user.click(screen.getByRole('tab', { name: /credits & add-ons/i }));

    // 3 tile labels render
    expect(await screen.findByText('Trial')).toBeInTheDocument();
    expect(screen.getByText('Booster')).toBeInTheDocument();
    expect(screen.getByText('Pro pack')).toBeInTheDocument();

    // Currency formatter shows USD amounts (en-AU formats USD as "US$")
    const trialButton = screen.getByText('Trial').closest('button');
    const boosterButton = screen.getByText('Booster').closest('button');
    const proButton = screen.getByText('Pro pack').closest('button');
    expect(trialButton).not.toBeNull();
    expect(boosterButton).not.toBeNull();
    expect(proButton).not.toBeNull();

    // The Intl.NumberFormat output for USD differs by ICU build ("US$" vs
    // "USD "), so accept either the symbol or the ISO code prefix.
    const usdAmount = (n: string) => new RegExp(`(\\$|USD)\\s?${n}\\.00`);
    expect(within(trialButton as HTMLElement).getByText(usdAmount('5'))).toBeInTheDocument();
    expect(within(boosterButton as HTMLElement).getByText(usdAmount('20'))).toBeInTheDocument();
    expect(within(proButton as HTMLElement).getByText(usdAmount('75'))).toBeInTheDocument();

    // Popular badge shows on the popular tier only
    const popularBadges = screen.getAllByText(/^Popular$/);
    expect(popularBadges).toHaveLength(1);
    expect(boosterButton).toContainElement(popularBadges[0]);
  });
});
