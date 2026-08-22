import { screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

const { mockFetchBillingQuote, mockCreateBillingCheckoutSession, mockOpenCheckoutUrl } = vi.hoisted(() => ({
  mockFetchBillingQuote: vi.fn(),
  mockCreateBillingCheckoutSession: vi.fn(),
  mockOpenCheckoutUrl: vi.fn(),
}));

vi.mock('@/lib/api', () => ({
  apiClient: {
    get: vi.fn().mockResolvedValue({ whatsAppNumber: null, whatsAppProofTemplate: null }),
  },
  ApiError: class ApiError extends Error {
    status: number;
    code: string;
    retryable: boolean;
    userMessage: string;
    fieldErrors: Array<{ field: string; code: string; message: string }>;
    constructor(status: number, code: string, message: string, retryable = false) {
      super(message);
      this.name = 'ApiError';
      this.status = status;
      this.code = code;
      this.retryable = retryable;
      this.userMessage = message;
      this.fieldErrors = [];
    }
  },
  fetchBillingQuote: mockFetchBillingQuote,
  createBillingCheckoutSession: mockCreateBillingCheckoutSession,
  fetchAvailablePaymentGateways: vi.fn().mockResolvedValue({
    gateways: ['whop', 'fawaterak'],
    methods: [
      { name: 'whop', label: 'Pay with Whop', iconName: 'credit-card', mode: 'embedded', badge: 'MAIN', recommended: true, region: 'global' },
      { name: 'fawaterak', label: 'Pay with Fawaterak', iconName: 'credit-card', mode: 'iframe', region: 'global' },
    ],
  }),
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

import { ApiError } from '@/lib/api';
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

    // Global is the default region, so Whop (MAIN) then Fawaterak are shown.
    expect(await screen.findByRole('button', { name: /continue to secure payment/i })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /pay globally/i })).toBeInTheDocument();
    expect(screen.getByText('Whop')).toBeInTheDocument();
    expect(screen.getByText('MAIN')).toBeInTheDocument();
    expect(screen.getByText('Fawaterak')).toBeInTheDocument();

    // Choosing Egypt reveals the manual-payment CTA, focused on the Egypt section.
    await user.click(screen.getByRole('button', { name: /pay inside egypt/i }));
    const cta = await screen.findByRole('link', { name: /i['’]ve paid.*upload proof.*activate/i });
    expect(cta.getAttribute('href')).toContain('/billing/manual-payment');
    expect(cta.getAttribute('href')).toContain('region=egypt');
  });

  it('renders official Whop embed instead of a raw purchase-url iframe', async () => {
    mockCreateBillingCheckoutSession.mockResolvedValue({
      checkoutUrl: 'https://whop.com/embedded/checkout/ch_live1/',
      checkoutSessionId: 'chcfg_live1',
      quoteId: 'quote-1',
      clientSecret: 'plan_live1',
      gateway: 'whop',
    });
    const user = userEvent.setup();
    renderWithRouter(<CheckoutReviewPage />, { searchParams });

    await user.click(await screen.findByRole('button', { name: /continue to secure payment/i }));

    expect(await screen.findByTestId('whop-embedded-checkout')).toBeInTheDocument();
    expect(document.querySelector('iframe[title="Secure payment"]')).not.toBeInTheDocument();
    expect(mockOpenCheckoutUrl).not.toHaveBeenCalled();
  });

  it('embeds Fawaterak in an on-page iframe after a method switch', async () => {
    mockCreateBillingCheckoutSession.mockResolvedValue({
      checkoutUrl: 'https://app.fawaterk.com/pay/99',
      checkoutSessionId: '2726912869',
      quoteId: 'quote-1',
      clientSecret: '272691286929958',
      gateway: 'fawaterak',
    });
    const user = userEvent.setup();
    renderWithRouter(<CheckoutReviewPage />, { searchParams });

    await user.click(await screen.findByText('Fawaterak'));
    await user.click(await screen.findByRole('button', { name: /continue to secure payment/i }));

    const frame = await screen.findByTitle('Secure payment');
    expect(frame).toHaveAttribute('src', 'https://app.fawaterk.com/pay/99');
    expect(screen.queryByTestId('whop-embedded-checkout')).not.toBeInTheDocument();
  });

  it('refreshes the quote once and retries when the previous checkout session is still attached', async () => {
    mockCreateBillingCheckoutSession
      .mockRejectedValueOnce(new ApiError(409, 'billing_quote_already_applied', 'This billing quote is already attached to a checkout session. Refresh your cart before starting a new checkout.', false))
      .mockResolvedValueOnce({
        checkoutUrl: 'https://app.fawaterk.com/pay/100',
        checkoutSessionId: 'invoice-100',
        quoteId: 'quote-2',
        gateway: 'fawaterak',
      });
    mockFetchBillingQuote
      .mockResolvedValueOnce(quoteFixture())
      .mockResolvedValueOnce({ ...quoteFixture(), quoteId: 'quote-2' });
    const user = userEvent.setup();
    renderWithRouter(<CheckoutReviewPage />, { searchParams });

    await user.click(await screen.findByText('Fawaterak'));
    await user.click(await screen.findByRole('button', { name: /continue to secure payment/i }));

    expect(await screen.findByTitle('Secure payment')).toHaveAttribute('src', 'https://app.fawaterk.com/pay/100');
    expect(mockFetchBillingQuote).toHaveBeenCalledTimes(2);
    expect(mockCreateBillingCheckoutSession).toHaveBeenCalledTimes(2);
    expect(mockCreateBillingCheckoutSession.mock.calls[1][0].quoteId).toBe('quote-2');
  });

  it('excludes EasyCash from customer checkout while supporting other enabled payment methods', async () => {
    renderWithRouter(<CheckoutReviewPage />, { searchParams });

    expect(await screen.findByText('Nursing Complete')).toBeInTheDocument();
    expect(screen.queryByText(/easycash/i)).not.toBeInTheDocument();
    expect(screen.queryByText(/easykash/i)).not.toBeInTheDocument();
  });
});
