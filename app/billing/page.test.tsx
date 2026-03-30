import { render, screen } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';

const {
  mockFetchBilling,
  mockFetchBillingChangePreview,
  mockCreateBillingCheckoutSession,
  mockDownloadInvoice,
  mockTrack,
} = vi.hoisted(() => ({
  mockFetchBilling: vi.fn(),
  mockFetchBillingChangePreview: vi.fn(),
  mockCreateBillingCheckoutSession: vi.fn(),
  mockDownloadInvoice: vi.fn(),
  mockTrack: vi.fn(),
}));

vi.mock('motion/react', () => ({
  motion: {
    div: ({ children, initial: _initial, animate: _animate, transition: _transition, whileHover: _whileHover, whileTap: _whileTap, ...props }: any) => (
      <div {...props}>{children}</div>
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
}));

import BillingPage from './page';

describe('Billing page', () => {
  beforeEach(() => {
    vi.clearAllMocks();

    mockFetchBilling.mockResolvedValue({
      currentPlan: 'Pro',
      reviewCredits: 3,
      nextRenewal: '2026-06-27',
      price: '$49',
      interval: 'month',
      status: 'Active',
      entitlements: {
        supportedReviewSubtests: ['Writing', 'Speaking'],
        invoiceDownloadsAvailable: true,
      },
      plans: [
        {
          id: 'pro',
          badge: 'Current',
          tier: 'Pro',
          label: 'Pro',
          description: 'Current learner plan.',
          price: '$49',
          interval: 'month',
          reviewCredits: 3,
          changeDirection: 'current',
        },
      ],
      extras: [
        {
          id: 'credits-5',
          quantity: 5,
          price: '$29',
          description: 'Add more review credits.',
        },
      ],
      invoices: [],
    });
    mockFetchBillingChangePreview.mockResolvedValue({
      summary: 'Preview',
      proratedAmount: '$0',
      effectiveAt: '2026-06-27',
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
