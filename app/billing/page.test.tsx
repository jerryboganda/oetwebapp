import { render, screen } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';

const {
  mockFetchBilling,
  mockFetchBillingChangePreview,
  mockCreateBillingCheckoutSession,
  mockDownloadInvoice,
  mockFetchBillingQuote,
  mockTrack,
} = vi.hoisted(() => ({
  mockFetchBilling: vi.fn(),
  mockFetchBillingChangePreview: vi.fn(),
  mockCreateBillingCheckoutSession: vi.fn(),
  mockDownloadInvoice: vi.fn(),
  mockFetchBillingQuote: vi.fn(),
  mockTrack: vi.fn(),
}));

vi.mock('motion/react', () => ({
  motion: {
    div: ({ children, initial: _initial, animate: _animate, transition: _transition, whileHover: _whileHover, whileTap: _whileTap, ...props }: any) => (
      <div {...props}>{children}</div>
    ),
    button: ({ children, initial: _initial, animate: _animate, transition: _transition, whileHover: _whileHover, whileTap: _whileTap, ...props }: any) => (
      <button {...props}>{children}</button>
    ),
  },
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
  createBillingCheckoutSession: mockCreateBillingCheckoutSession,
  downloadInvoice: mockDownloadInvoice,
  fetchBillingQuote: mockFetchBillingQuote,
}));

import BillingPage from './page';

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
        },
      ],
      addOns: [
        {
          id: 'credits-5',
          code: 'credits-5',
          name: 'Review credits pack',
          productType: 'addon_purchase',
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
    mockCreateBillingCheckoutSession.mockResolvedValue({ checkoutUrl: 'https://example.com/checkout' });
    mockDownloadInvoice.mockResolvedValue('blob:invoice');
  });

  it('renders inside the shared learner dashboard shell', async () => {
    render(<BillingPage />);

    expect(await screen.findByText('See what changes before you spend or switch plans')).toBeInTheDocument();
    expect(screen.getByTestId('learner-dashboard-shell')).toBeInTheDocument();
  });
});
