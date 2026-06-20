import { screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

const { mockFetchBillingQuote, mockCreateBillingCheckoutSession, mockOpenCheckoutUrl } = vi.hoisted(() => ({
  mockFetchBillingQuote: vi.fn(),
  mockCreateBillingCheckoutSession: vi.fn(),
  mockOpenCheckoutUrl: vi.fn(),
}));

vi.mock('@/lib/api', () => ({
  fetchBillingQuote: mockFetchBillingQuote,
  createBillingCheckoutSession: mockCreateBillingCheckoutSession,
}));

vi.mock('@/lib/mobile/web-checkout', () => ({
  openCheckoutUrl: mockOpenCheckoutUrl,
}));

vi.mock('@/contexts/auth-context', () => ({
  useAuth: () => ({ isAuthenticated: true, loading: false }),
}));

vi.mock('@/lib/api/billing-region', () => ({
  detectBillingRegion: vi.fn().mockResolvedValue({ region: 'ROW', country: 'GB', currency: 'GBP', source: 'default' }),
}));

import CheckoutReviewPage from './page';
import { renderWithRouter } from '@/tests/test-utils';

function quoteFixture(expiresInMs = 15 * 60_000) {
  return {
    quoteId: 'quote-1',
    status: 'pending',
    currency: 'AUD',
    subtotalAmount: 199,
    discountAmount: 0,
    totalAmount: 199,
    planCode: 'nursing-complete',
    couponCode: null,
    addOnCodes: [],
    items: [
      {
        kind: 'plan',
        code: 'nursing-complete',
        name: 'Nursing Complete',
        description: 'Full course access',
        quantity: 1,
        amount: 199,
        currency: 'AUD',
      },
    ],
    expiresAt: new Date(Date.now() + expiresInMs).toISOString(),
    summary: 'Nursing Complete plan',
    validation: {},
  };
}

const searchParams = new URLSearchParams('productType=plan_purchase&priceId=nursing-complete&quantity=1');

describe('Checkout review page', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mockFetchBillingQuote.mockResolvedValue(quoteFixture());
    mockCreateBillingCheckoutSession.mockResolvedValue({
      checkoutUrl: 'https://pay.example.test/cs_test_123',
      checkoutSessionId: 'cs_test_123',
      quoteId: 'quote-1',
    });
  });

  it('renders the quote with a validity countdown', async () => {
    renderWithRouter(<CheckoutReviewPage />, { searchParams });

    expect(await screen.findByText('Nursing Complete')).toBeInTheDocument();
    expect(await screen.findByText(/quoted price valid for/i)).toBeInTheDocument();
  });

  it('turns this tab into the payment-status poller when checkout opens in a new window', async () => {
    const replace = vi.fn();
    mockOpenCheckoutUrl.mockResolvedValue('window-open');
    const user = userEvent.setup();
    renderWithRouter(<CheckoutReviewPage />, { searchParams, router: { replace } });

    await user.click(await screen.findByRole('button', { name: /continue to secure payment/i }));

    expect(mockOpenCheckoutUrl).toHaveBeenCalledWith('https://pay.example.test/cs_test_123');
    expect(replace).toHaveBeenCalledWith('/billing/payment-return?quote=quote-1&session=cs_test_123');
  });

  it('shows an actionable error and re-enables the button when the payment window cannot open', async () => {
    const replace = vi.fn();
    mockOpenCheckoutUrl.mockResolvedValue('noop');
    const user = userEvent.setup();
    renderWithRouter(<CheckoutReviewPage />, { searchParams, router: { replace } });

    await user.click(await screen.findByRole('button', { name: /continue to secure payment/i }));

    expect(await screen.findByText(/could not open the secure payment window/i)).toBeInTheDocument();
    expect(replace).not.toHaveBeenCalledWith(expect.stringContaining('/billing/payment-return'));
    expect(screen.getByRole('button', { name: /continue to secure payment/i })).toBeEnabled();
  });

  it('lets the learner choose between paying inside Egypt and paying globally', async () => {
    const user = userEvent.setup();
    renderWithRouter(<CheckoutReviewPage />, { searchParams });

    // Global is the default region, so the Stripe payment button is shown.
    expect(await screen.findByRole('button', { name: /continue to secure payment/i })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /pay globally/i })).toBeInTheDocument();

    // Choosing Egypt reveals the manual-payment CTA, focused on the Egypt section.
    await user.click(screen.getByRole('button', { name: /pay inside egypt/i }));
    const cta = await screen.findByRole('link', { name: /continue to egyptian payment/i });
    expect(cta.getAttribute('href')).toContain('/billing/manual-payment');
    expect(cta.getAttribute('href')).toContain('region=egypt');
  });
});
