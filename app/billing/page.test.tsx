import { screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
const {
  mockFetchBilling,
  mockFetchBillingChangePreview,
  mockFetchFreezeStatus,
  mockCreateBillingCheckoutSession,
  mockCreateWalletTopUp,
  mockDownloadInvoice,
  mockFetchBillingQuote,
  mockTrack,
} = vi.hoisted(() => ({
  mockFetchBilling: vi.fn(),
  mockFetchBillingChangePreview: vi.fn(),
  mockFetchFreezeStatus: vi.fn(),
  mockCreateBillingCheckoutSession: vi.fn(),
  mockCreateWalletTopUp: vi.fn(),
  mockDownloadInvoice: vi.fn(),
  mockFetchBillingQuote: vi.fn(),
  mockTrack: vi.fn(),
}));


vi.mock('@/components/layout', () => ({
  LearnerDashboardShell: ({ children, workspaceClassName }: { children: React.ReactNode; workspaceClassName?: string }) => (
    <div data-testid="learner-dashboard-shell" data-workspace-class={workspaceClassName}>{children}</div>
  ),
}));

vi.mock('@/components/layout/app-shell', () => ({
  AppShell: ({ children }: { children: React.ReactNode }) => <div data-testid="app-shell">{children}</div>,
}));

vi.mock('@/lib/analytics', () => ({
  analytics: {
    track: mockTrack,
  },
}));

vi.mock('@/lib/api', () => ({
  fetchBilling: mockFetchBilling,
  fetchBillingChangePreview: mockFetchBillingChangePreview,
  fetchFreezeStatus: mockFetchFreezeStatus,
  createBillingCheckoutSession: mockCreateBillingCheckoutSession,
  createWalletTopUp: mockCreateWalletTopUp,
  downloadInvoice: mockDownloadInvoice,
  fetchBillingQuote: mockFetchBillingQuote,
}));

import BillingPage from './page';
import { renderWithRouter } from '@/tests/test-utils';

describe('Billing page', () => {
  beforeEach(() => {
    vi.clearAllMocks();

    mockFetchBilling.mockResolvedValue({
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
    });
    mockFetchBillingChangePreview.mockResolvedValue({
      currentPlanId: 'plan-pro',
      targetPlanId: 'plan-plus',
      direction: 'upgrade',
      summary: 'Preview',
      proratedAmount: '$0',
      effectiveAt: '2026-06-27',
      currentCreditsIncluded: 3,
      targetCreditsIncluded: 5,
    });
    mockFetchBillingQuote.mockResolvedValue({
      quoteId: 'quote-1',
      status: 'ready',
      currency: 'AUD',
      subtotalAmount: 29,
      discountAmount: 0,
      totalAmount: 29,
      planCode: null,
      couponCode: null,
      addOnCodes: ['credits-5'],
      items: [],
      expiresAt: '2026-06-27T00:00:00Z',
      summary: 'Quote ready',
      validation: {},
    });
    mockFetchFreezeStatus.mockResolvedValue(null);
    mockCreateBillingCheckoutSession.mockResolvedValue({ checkoutUrl: 'https://example.com/checkout' });
    mockCreateWalletTopUp.mockResolvedValue({ checkoutUrl: 'https://example.com/top-up', totalCredits: 10 });
    mockDownloadInvoice.mockResolvedValue('blob:invoice');
  });

  it('renders inside the shared learner dashboard shell', async () => {
    renderWithRouter(<BillingPage />);

    expect(await screen.findByText('Manage subscriptions without billing surprises')).toBeInTheDocument();
    expect(screen.getByTestId('learner-dashboard-shell')).toBeInTheDocument();
  });

  it('passes the selected gateway into review credit checkout sessions', async () => {
    const user = userEvent.setup();
    const openSpy = vi.spyOn(window, 'open').mockReturnValue(null);

    renderWithRouter(<BillingPage />);

    expect(await screen.findByText('Manage subscriptions without billing surprises')).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: /^paypal$/i }));
    await user.click(screen.getByRole('button', { name: /purchase credits/i }));

    await waitFor(() => {
      expect(mockCreateBillingCheckoutSession).toHaveBeenCalledWith(expect.objectContaining({
        productType: 'review_credits',
        quantity: 5,
        priceId: 'credits-5',
        gateway: 'paypal',
      }));
    });

    openSpy.mockRestore();
  });

  it('fails closed for paid actions when freeze status cannot be verified', async () => {
    mockFetchFreezeStatus.mockRejectedValueOnce(new Error('freeze unavailable'));

    renderWithRouter(<BillingPage />);

    expect(await screen.findByText('Manage subscriptions without billing surprises')).toBeInTheDocument();
    expect(screen.getByText(/freeze status could not be verified/i)).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /purchase credits/i })).toBeDisabled();
  });

  it('allows checkout when a freeze is scheduled for the future', async () => {
    const user = userEvent.setup();
    const openSpy = vi.spyOn(window, 'open').mockReturnValue(null);
    mockFetchFreezeStatus.mockResolvedValueOnce({
      currentFreeze: {
        id: 'freeze-1',
        userId: 'learner-1',
        status: 'Scheduled',
        requestedAt: '2026-01-01T00:00:00Z',
        scheduledStartAt: '2999-01-01T00:00:00Z',
        startedAt: null,
        endedAt: '2999-01-08T00:00:00Z',
      },
      policy: {},
      eligibility: { eligible: false },
      history: [],
    });

    renderWithRouter(<BillingPage />);

    expect(await screen.findByText('Manage subscriptions without billing surprises')).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: /purchase credits/i }));

    await waitFor(() => expect(mockCreateBillingCheckoutSession).toHaveBeenCalled());
    openSpy.mockRestore();
  });
});
