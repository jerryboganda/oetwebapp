import { screen, waitFor } from '@testing-library/react';

const { mockFetchBillingPaymentStatus } = vi.hoisted(() => ({
  mockFetchBillingPaymentStatus: vi.fn(),
}));

vi.mock('@/lib/api', () => ({
  fetchBillingPaymentStatus: mockFetchBillingPaymentStatus,
}));

import BillingPaymentReturnPage from './page';
import { renderWithRouter } from '@/tests/test-utils';

describe('Billing payment return page', () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it('polls the quote status and shows the completed order destination', async () => {
    mockFetchBillingPaymentStatus.mockResolvedValue({
      status: 'completed',
      quoteId: 'quote-123',
      checkoutSessionId: 'cs_test_123',
      productType: 'plan_purchase',
      targetPlanId: 'nursing-complete',
      addOnCodes: [],
      items: [
        {
          kind: 'plan',
          code: 'nursing-complete',
          name: 'Nursing Complete',
          amount: 199,
          currency: 'AUD',
          quantity: 1,
          description: 'Locked course quote',
        },
      ],
      totalAmount: 199,
      currency: 'AUD',
      invoiceId: 'inv-123',
      subscriptionId: 'sub-123',
      failureReason: null,
      fulfilledAt: '2026-06-09T12:00:00Z',
      expiresAt: '2026-06-09T12:30:00Z',
    });

    renderWithRouter(<BillingPaymentReturnPage />, {
      searchParams: new URLSearchParams('status=success&quote=quote-123&session=cs_test_123'),
    });

    expect(await screen.findByText('Payment confirmed')).toBeInTheDocument();
    expect(screen.getByText('Nursing Complete')).toBeInTheDocument();
    expect(screen.getAllByText('$199.00')).toHaveLength(2);

    await waitFor(() => {
      expect(mockFetchBillingPaymentStatus).toHaveBeenCalledWith({
        quoteId: 'quote-123',
        sessionId: 'cs_test_123',
      });
    });
    expect(screen.getByRole('link', { name: /continue/i })).toHaveAttribute('href', '/dashboard?purchase=success');
  });

  it('renders a cancellable retry state without polling when no reference is present', async () => {
    renderWithRouter(<BillingPaymentReturnPage />, {
      searchParams: new URLSearchParams('status=cancelled'),
    });

    expect(await screen.findByText('Checkout cancelled')).toBeInTheDocument();
    expect(screen.getByRole('link', { name: /try again/i })).toHaveAttribute('href', '/catalog');
    expect(mockFetchBillingPaymentStatus).not.toHaveBeenCalled();
  });
});
