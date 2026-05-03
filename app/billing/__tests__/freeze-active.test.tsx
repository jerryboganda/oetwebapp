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
    plans: [
      {
        id: 'pro',
        code: 'pro',
        badge: 'Current',
        tier: 'Pro',
        label: 'Pro',
        description: 'Current learner plan.',
        price: '$49',
        interval: 'month',
        reviewCredits: 3,
        canChangeTo: false,
        changeDirection: 'current',
        status: 'active',
        durationMonths: 1,
        isVisible: true,
        isRenewable: true,
        trialDays: 0,
        displayOrder: 1,
        includedSubtests: ['Writing', 'Speaking'],
        entitlements: {
          productiveSkillReviewsEnabled: true,
          supportedReviewSubtests: ['Writing', 'Speaking'],
          invoiceDownloadsAvailable: true,
        },
      },
      {
        id: 'plus',
        code: 'plus',
        badge: 'Upgrade',
        tier: 'Plus',
        label: 'Plus',
        description: 'Upgrade plan.',
        price: '$79',
        interval: 'month',
        reviewCredits: 5,
        canChangeTo: true,
        changeDirection: 'upgrade',
        status: 'active',
        durationMonths: 1,
        isVisible: true,
        isRenewable: true,
        trialDays: 0,
        displayOrder: 2,
        includedSubtests: ['Writing', 'Speaking'],
        entitlements: {
          productiveSkillReviewsEnabled: true,
          supportedReviewSubtests: ['Writing', 'Speaking'],
          invoiceDownloadsAvailable: true,
        },
      },
    ],
    addOns: [
      {
        id: 'credits-5',
        code: 'credits-5',
        name: 'Review credits pack',
        productType: 'review_credits',
        quantity: 5,
        price: '$29',
        currency: 'AUD',
        interval: 'one-time',
        status: 'active',
        description: 'Add more review credits.',
        grantCredits: 5,
        durationDays: 30,
        isRecurring: false,
        appliesToAllPlans: true,
        quantityStep: 1,
        maxQuantity: null,
        compatiblePlanCodes: ['pro'],
      },
    ],
    coupons: [],
    quote: null,
    invoices: [],
  };
}

describe('Billing page with active freeze', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mockFetchBilling.mockResolvedValue(defaultBillingData());
    mockFetchWalletTransactions.mockResolvedValue({ balance: 0, transactions: [] });
    mockFetchWalletTopUpTiers.mockResolvedValue({
      currency: 'AUD',
      tiers: [
        { amount: 10, credits: 10, bonus: 0, totalCredits: 10, label: 'Starter', isPopular: false },
        { amount: 25, credits: 28, bonus: 3, totalCredits: 31, label: 'Standard', isPopular: true },
      ],
    });
    mockFetchFreezeStatus.mockResolvedValue({
      policy: {},
      currentFreeze: {
        id: 'freeze-active',
        userId: 'learner-1',
        status: 'Active',
        requestedAt: '2026-01-01T00:00:00Z',
        scheduledStartAt: '2026-01-01T00:00:00Z',
        startedAt: '2026-01-02T00:00:00Z',
        endedAt: null,
      },
      eligibility: { eligible: false },
      history: [],
    });
  });

  it('renders the freeze banner and disables all paid action buttons', async () => {
    const user = userEvent.setup();
    renderWithRouter(<BillingPage />);

    expect(await screen.findByText('Your billing center')).toBeInTheDocument();

    // Freeze banner renders
    expect(
      screen.getByText(
        /Your account is frozen, so checkout, plan changes, and top-ups are paused\./i,
      ),
    ).toBeInTheDocument();

    // Plans tab — preview/upgrade button disabled
    await user.click(screen.getByRole('tab', { name: /^plans$/i }));
    const previewButton = await screen.findByRole('button', { name: /preview upgrade/i });
    expect(previewButton).toBeDisabled();

    // Credits tab — top-up tier buttons + purchase add-on disabled
    await user.click(screen.getByRole('tab', { name: /credits & add-ons/i }));

    const topUpStarter = (await screen.findByText('Starter')).closest('button');
    const topUpStandard = screen.getByText('Standard').closest('button');
    expect(topUpStarter).toBeDisabled();
    expect(topUpStandard).toBeDisabled();

    expect(screen.getByRole('button', { name: /purchase credits/i })).toBeDisabled();
  });
});
