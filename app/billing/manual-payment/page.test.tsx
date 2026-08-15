import { screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

const {
  mockListOwnManualPayments,
  mockSubmitManualPayment,
  mockListPublicPaymentMethods,
  mockFetchAvailablePaymentGateways,
  mockFetchPaymentMethodQrBlob,
} = vi.hoisted(() => ({
  mockListOwnManualPayments: vi.fn(),
  mockSubmitManualPayment: vi.fn(),
  mockListPublicPaymentMethods: vi.fn(),
  mockFetchAvailablePaymentGateways: vi.fn(),
  mockFetchPaymentMethodQrBlob: vi.fn(),
}));

vi.mock('@/lib/api', () => ({
  listOwnManualPayments: mockListOwnManualPayments,
  submitManualPayment: mockSubmitManualPayment,
  listPublicPaymentMethods: mockListPublicPaymentMethods,
  fetchAvailablePaymentGateways: mockFetchAvailablePaymentGateways,
  fetchPaymentMethodQrBlob: mockFetchPaymentMethodQrBlob,
}));

vi.mock('@/lib/billing/whatsapp', () => ({
  fetchSupportWhatsApp: vi.fn().mockResolvedValue({ whatsAppNumber: '447961725989', whatsAppProofTemplate: null }),
  buildManualPaymentWhatsAppLink: vi.fn(() => 'https://wa.me/447961725989?text=TXN-123'),
  displayWhatsAppNumber: vi.fn((num) => `+${num || '447961725989'}`),
  normalizeWhatsAppNumber: vi.fn((num) => num || '447961725989'),
  PLATFORM_WHATSAPP: '447961725989',
}));

vi.mock('@/contexts/auth-context', () => ({
  useAuth: () => ({ user: { userId: 'u1', email: 'learner@example.com', displayName: 'Jane Learner', role: 'learner' } }),
}));

vi.mock('@/components/layout', () => ({
  LearnerDashboardShell: ({ children }: { children?: any }) => <div data-testid="shell">{children}</div>,
}));

vi.mock('@/components/domain', () => ({
  LearnerPageHero: (props: any) => <div data-testid="hero">{props.title}</div>,
}));

import ManualPaymentPage from './page';
import { renderWithRouter } from '@/tests/test-utils';

const linkedParams = new URLSearchParams('quoteId=q1&course=Nursing Complete&amount=199&currency=GBP');

describe('Manual payment page', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mockListOwnManualPayments.mockResolvedValue([]);
    mockSubmitManualPayment.mockResolvedValue({ id: 'mp1', status: 'pending' });
    mockListPublicPaymentMethods.mockResolvedValue([]); // use bundled fallback
    mockFetchAvailablePaymentGateways.mockResolvedValue({ gateways: ['stripe'] });
    mockFetchPaymentMethodQrBlob.mockResolvedValue(new Blob(['qr'], { type: 'image/png' }));
    (URL as any).createObjectURL = vi.fn(() => 'blob:qr-url');
    (URL as any).revokeObjectURL = vi.fn();
    (window as any).open = vi.fn();
  });

  it('shows the configured Egyptian and international payment methods', async () => {
    renderWithRouter(<ManualPaymentPage />, { searchParams: linkedParams });

    expect((await screen.findAllByText('InstaPay QR / link')).length).toBeGreaterThan(0);
    expect(screen.getAllByText('QNB Egypt bank transfer').length).toBeGreaterThan(0);
    expect(screen.getByText('International payment')).toBeInTheDocument();
    expect(screen.getByText('PayPal Business')).toBeInTheDocument();
  });

  it('submits an auto-derived payload and opens the WhatsApp confirmation', async () => {
    const user = userEvent.setup();
    const { container } = renderWithRouter(<ManualPaymentPage />, { searchParams: linkedParams });

    const email = (await screen.findByLabelText('Your registered email')) as HTMLInputElement;
    expect(email.value).toBe('learner@example.com');
    expect(email.readOnly).toBe(true);

    await user.type(screen.getByLabelText('Transaction ID'), 'TXN-123');
    await user.type(screen.getByLabelText('Your WhatsApp number'), '+201001234567');
    const fileInput = container.querySelector('input[type="file"]') as HTMLInputElement;
    await user.upload(fileInput, new File(['proof-bytes'], 'proof.png', { type: 'image/png' }));

    await user.click(screen.getByRole('button', { name: 'Submit' }));

    await waitFor(() => expect(mockSubmitManualPayment).toHaveBeenCalledTimes(1));
    expect(mockSubmitManualPayment).toHaveBeenCalledWith(
      expect.objectContaining({
        quoteId: 'q1',
        amountAmount: 199,
        currency: 'GBP',
        courseName: 'Nursing Complete',
        candidateEmail: 'learner@example.com',
        candidateFullName: 'Jane Learner',
        candidateWhatsApp: '+201001234567',
        paymentCategory: 'inside_egypt',
        reference: 'TXN-123',
      }),
    );

    expect(await screen.findByRole('dialog')).toBeInTheDocument();
    expect(await screen.findByText(/payment submitted successfully/i)).toBeInTheDocument();
    const wa = screen.getByRole('link', { name: /notify us on whatsapp/i });
    expect(wa.getAttribute('href')).toContain('wa.me/447961725989');
    expect(decodeURIComponent(wa.getAttribute('href') ?? '')).toContain('TXN-123');
  });

  it('prompts the learner to pick a package when there is no quote', async () => {
    renderWithRouter(<ManualPaymentPage />, { searchParams: new URLSearchParams() });

    expect(await screen.findByText(/pick a package first/i)).toBeInTheDocument();
    const link = screen.getByRole('link', { name: /subscriptions & packages/i });
    expect(link.getAttribute('href')).toBe('/subscriptions');
    expect(screen.queryByRole('button', { name: 'Submit' })).not.toBeInTheDocument();
    expect(screen.queryByLabelText('Your registered email')).not.toBeInTheDocument();
  });
});
