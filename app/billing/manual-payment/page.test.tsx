import { screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

const {
  mockFetchPublicPlans,
  mockListOwnManualPayments,
  mockSubmitManualPayment,
  mockListPublicPaymentMethods,
  mockFetchAvailablePaymentGateways,
  mockFetchPaymentMethodQrBlob,
} = vi.hoisted(() => ({
  mockFetchPublicPlans: vi.fn(),
  mockListOwnManualPayments: vi.fn(),
  mockSubmitManualPayment: vi.fn(),
  mockListPublicPaymentMethods: vi.fn(),
  mockFetchAvailablePaymentGateways: vi.fn(),
  mockFetchPaymentMethodQrBlob: vi.fn(),
}));

vi.mock('@/lib/api', () => ({
  fetchPublicPlans: mockFetchPublicPlans,
  listOwnManualPayments: mockListOwnManualPayments,
  submitManualPayment: mockSubmitManualPayment,
  listPublicPaymentMethods: mockListPublicPaymentMethods,
  fetchAvailablePaymentGateways: mockFetchAvailablePaymentGateways,
  fetchPaymentMethodQrBlob: mockFetchPaymentMethodQrBlob,
}));

function methodFixture(overrides: Record<string, any> = {}) {
  return {
    id: 'm1',
    key: 'instapay_qr_link',
    label: 'InstaPay QR / link',
    category: 'inside_egypt',
    detail: 'Handle: drahmedhesham_work@instapay',
    meta: 'https://ipn.eg/example',
    instructions: 'Scan the QR and send proof.',
    note: null,
    referenceRule: false,
    showQr: true,
    hasQrImage: false,
    iconName: 'QrCode',
    isActive: true,
    displayOrder: 1,
    createdAt: '',
    updatedAt: '',
    ...overrides,
  };
}

vi.mock('@/components/layout', () => ({
  LearnerDashboardShell: ({ children }: { children?: any }) => <div data-testid="shell">{children}</div>,
}));

vi.mock('@/components/domain', () => ({
  LearnerPageHero: (props: any) => <div data-testid="hero">{props.title}</div>,
}));

import ManualPaymentPage from './page';
import { renderWithRouter } from '@/tests/test-utils';

function planFixture() {
  return {
    items: [
      {
        planId: 'p1',
        code: 'nursing-complete',
        label: 'Nursing Complete',
        tier: 'complete',
        description: 'Full course access',
        price: { amount: 199, currency: 'GBP', interval: 'one_time' },
        reviewCredits: 0,
        mockReportsIncluded: true,
        includedSubtests: [],
        trialDays: 0,
        isRenewable: false,
        changeDirection: 'none',
      },
    ],
  };
}

const linkedParams = new URLSearchParams('quoteId=q1&course=Nursing Complete&amount=199&currency=GBP');

describe('Manual payment page', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mockFetchPublicPlans.mockResolvedValue(planFixture());
    mockListOwnManualPayments.mockResolvedValue([]);
    mockSubmitManualPayment.mockResolvedValue({ id: 'mp1', status: 'pending' });
    // Default: API returns no custom methods → page uses its bundled fallback list.
    mockListPublicPaymentMethods.mockResolvedValue([]);
    mockFetchAvailablePaymentGateways.mockResolvedValue({ gateways: ['stripe'] });
    mockFetchPaymentMethodQrBlob.mockResolvedValue(new Blob(['qr'], { type: 'image/png' }));
    (URL as any).createObjectURL = vi.fn(() => 'blob:qr-url');
    (URL as any).revokeObjectURL = vi.fn();
  });

  it('populates the course dropdown from public plans', async () => {
    renderWithRouter(<ManualPaymentPage />, { searchParams: new URLSearchParams() });

    expect(await screen.findByRole('option', { name: 'Nursing Complete' })).toBeInTheDocument();
    // The static spec content renders too. The support email appears both in the
    // PayPal method details and the footer, so assert at least one occurrence.
    expect(screen.getByText(/Required proof:/i)).toBeInTheDocument();
    expect(screen.getAllByText('support@oetwithdrhesham.co.uk').length).toBeGreaterThan(0);
  });

  it('pre-fills the plan, amount and currency from the checkout link', async () => {
    renderWithRouter(<ManualPaymentPage />, { searchParams: linkedParams });

    await screen.findByRole('option', { name: 'Nursing Complete' });

    await waitFor(() => {
      expect((screen.getByLabelText('Selected course / plan') as HTMLSelectElement).value).toBe('nursing-complete');
    });
    expect((screen.getByLabelText('Paid amount') as HTMLInputElement).value).toBe('199');
    expect((screen.getByLabelText('Currency') as HTMLInputElement).value).toBe('GBP');
  });

  it('submits the proof with the quoteId and resolved plan code', async () => {
    const user = userEvent.setup();
    const { container } = renderWithRouter(<ManualPaymentPage />, { searchParams: linkedParams });

    await screen.findByRole('option', { name: 'Nursing Complete' });

    await user.type(screen.getByLabelText('Full name'), 'Jane Doe');
    await user.type(screen.getByLabelText('Email'), 'jane@example.com');
    await user.type(screen.getByLabelText('WhatsApp number'), '+201000000000');
    await user.type(screen.getByLabelText('Transaction reference'), 'TXN-123');

    const fileInput = container.querySelector('input[type="file"]') as HTMLInputElement;
    await user.upload(fileInput, new File(['proof-bytes'], 'proof.png', { type: 'image/png' }));

    await user.click(screen.getByRole('button', { name: /submit for review/i }));

    await waitFor(() => expect(mockSubmitManualPayment).toHaveBeenCalledTimes(1));
    expect(mockSubmitManualPayment).toHaveBeenCalledWith(
      expect.objectContaining({
        quoteId: 'q1',
        courseId: 'nursing-complete',
        courseName: 'Nursing Complete',
        currency: 'GBP',
        amountAmount: 199,
        candidateFullName: 'Jane Doe',
        candidateEmail: 'jane@example.com',
        candidateWhatsApp: '+201000000000',
        reference: 'TXN-123',
      }),
    );
  });

  it('loads payment methods from the API and renders them in the method dropdown', async () => {
    mockListPublicPaymentMethods.mockResolvedValue([
      methodFixture({ key: 'custom_wallet', label: 'Custom Wallet', showQr: false }),
    ]);

    renderWithRouter(<ManualPaymentPage />, { searchParams: new URLSearchParams() });

    expect(await screen.findByRole('option', { name: 'Custom Wallet' })).toBeInTheDocument();
  });

  it('falls back to the bundled methods when the API fails', async () => {
    mockListPublicPaymentMethods.mockRejectedValue(new Error('offline'));

    renderWithRouter(<ManualPaymentPage />, { searchParams: new URLSearchParams() });

    // The bundled fallback still renders the canonical methods (each label appears
    // in both the method card and the method <select>, so match on >= 1).
    expect((await screen.findAllByText('InstaPay QR / link')).length).toBeGreaterThan(0);
    expect(screen.getAllByText('QNB Egypt bank transfer').length).toBeGreaterThan(0);
  });

  it('renders the QR image from the API blob when hasQrImage is true', async () => {
    mockListPublicPaymentMethods.mockResolvedValue([methodFixture({ hasQrImage: true })]);

    const { container } = renderWithRouter(<ManualPaymentPage />, { searchParams: new URLSearchParams() });

    await waitFor(() => expect(mockFetchPaymentMethodQrBlob).toHaveBeenCalledWith('instapay_qr_link'));
    await waitFor(() => {
      const img = container.querySelector('img[alt="InstaPay QR / link QR"]') as HTMLImageElement | null;
      expect(img?.getAttribute('src')).toBe('blob:qr-url');
    });
  });

  it('falls back to the static QR asset when hasQrImage is false', async () => {
    mockListPublicPaymentMethods.mockResolvedValue([methodFixture({ hasQrImage: false })]);

    const { container } = renderWithRouter(<ManualPaymentPage />, { searchParams: new URLSearchParams() });

    await waitFor(() => {
      const img = container.querySelector('img[alt="InstaPay QR / link QR"]') as HTMLImageElement | null;
      expect(img?.getAttribute('src')).toBe('/payment/instapay-qr.jpg');
    });
    expect(mockFetchPaymentMethodQrBlob).not.toHaveBeenCalled();
  });

  it('shows the Pay with PayPal button when the gateway is available', async () => {
    mockFetchAvailablePaymentGateways.mockResolvedValue({ gateways: ['stripe', 'paypal'] });

    renderWithRouter(<ManualPaymentPage />, { searchParams: new URLSearchParams() });

    expect(await screen.findByRole('button', { name: /pay with paypal/i })).toBeInTheDocument();
  });

  it('hides the Pay with PayPal button when the gateway is unavailable', async () => {
    mockFetchAvailablePaymentGateways.mockResolvedValue({ gateways: ['stripe'] });

    renderWithRouter(<ManualPaymentPage />, { searchParams: new URLSearchParams() });

    // Ensure the page has settled (PayPal method present from fallback) before asserting absence.
    expect((await screen.findAllByText('PayPal Business')).length).toBeGreaterThan(0);
    expect(screen.queryByRole('button', { name: /pay with paypal/i })).not.toBeInTheDocument();
  });
});
