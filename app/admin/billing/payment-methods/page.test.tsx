import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

const {
  mockListAdminPaymentMethods,
  mockUpsertPaymentMethod,
  mockDeletePaymentMethod,
  mockUploadPaymentMethodQr,
  mockToast,
} = vi.hoisted(() => ({
  mockListAdminPaymentMethods: vi.fn(),
  mockUpsertPaymentMethod: vi.fn(),
  mockDeletePaymentMethod: vi.fn(),
  mockUploadPaymentMethodQr: vi.fn(),
  mockToast: { success: vi.fn(), error: vi.fn() },
}));

vi.mock('@/lib/api', () => ({
  listAdminPaymentMethods: mockListAdminPaymentMethods,
  upsertPaymentMethod: mockUpsertPaymentMethod,
  deletePaymentMethod: mockDeletePaymentMethod,
  uploadPaymentMethodQr: mockUploadPaymentMethodQr,
}));

vi.mock('@/components/admin/ui/toaster', () => ({
  toast: mockToast,
  Toaster: () => null,
}));

import AdminPaymentMethodsPage from './page';
import { renderWithRouter } from '@/tests/test-utils';

function methodRow(overrides: Record<string, any> = {}) {
  return {
    id: 'pmc-instapay',
    key: 'instapay_qr_link',
    label: 'InstaPay QR / link',
    category: 'inside_egypt',
    detail: 'Handle: drahmedhesham_work@instapay',
    meta: 'https://ipn.eg/example',
    instructions: 'Scan and pay.',
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

describe('Admin payment methods page', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mockListAdminPaymentMethods.mockResolvedValue([methodRow()]);
    mockUpsertPaymentMethod.mockResolvedValue(methodRow());
    mockDeletePaymentMethod.mockResolvedValue(undefined);
    mockUploadPaymentMethodQr.mockResolvedValue(methodRow({ hasQrImage: true }));
  });

  it('renders the loaded payment methods in the table', async () => {
    renderWithRouter(<AdminPaymentMethodsPage />);
    expect(await screen.findByText('instapay_qr_link')).toBeInTheDocument();
    expect(screen.getByText('InstaPay QR / link')).toBeInTheDocument();
  });

  it('opens the dialog when New method is clicked', async () => {
    const user = userEvent.setup();
    renderWithRouter(<AdminPaymentMethodsPage />);
    await screen.findByText('instapay_qr_link');

    await user.click(screen.getByRole('button', { name: /new method/i }));

    expect(await screen.findByText('Edit payment method')).toBeInTheDocument();
  });

  it('saves a new method via upsertPaymentMethod', async () => {
    const user = userEvent.setup();
    renderWithRouter(<AdminPaymentMethodsPage />);
    await screen.findByText('instapay_qr_link');

    await user.click(screen.getByRole('button', { name: /new method/i }));
    const dialog = await screen.findByRole('dialog');

    await user.type(within(dialog).getByLabelText('Key (slug)'), 'new_wallet');
    await user.type(within(dialog).getByLabelText('Label'), 'New Wallet');
    await user.click(within(dialog).getByRole('button', { name: /^save$/i }));

    await waitFor(() => expect(mockUpsertPaymentMethod).toHaveBeenCalledTimes(1));
    expect(mockUpsertPaymentMethod).toHaveBeenCalledWith(
      expect.objectContaining({ key: 'new_wallet', label: 'New Wallet' }),
    );
    expect(mockToast.success).toHaveBeenCalled();
  });

  it('surfaces an error when saving fails', async () => {
    mockUpsertPaymentMethod.mockRejectedValue(new Error('save boom'));
    const user = userEvent.setup();
    renderWithRouter(<AdminPaymentMethodsPage />);
    await screen.findByText('instapay_qr_link');

    await user.click(screen.getByRole('button', { name: /new method/i }));
    const dialog = await screen.findByRole('dialog');
    await user.type(within(dialog).getByLabelText('Key (slug)'), 'x');
    await user.type(within(dialog).getByLabelText('Label'), 'X');
    await user.click(within(dialog).getByRole('button', { name: /^save$/i }));

    expect(await within(dialog).findByText('save boom')).toBeInTheDocument();
  });

  it('deletes a method after confirmation', async () => {
    const confirmSpy = vi.spyOn(window, 'confirm').mockReturnValue(true);
    const user = userEvent.setup();
    renderWithRouter(<AdminPaymentMethodsPage />);
    await screen.findByText('instapay_qr_link');

    await user.click(screen.getByRole('button', { name: /delete/i }));

    await waitFor(() => expect(mockDeletePaymentMethod).toHaveBeenCalledWith('pmc-instapay'));
    confirmSpy.mockRestore();
  });

  it('shows the QR upload input only when Show QR is on', async () => {
    const user = userEvent.setup();
    renderWithRouter(<AdminPaymentMethodsPage />);
    await screen.findByText('instapay_qr_link');

    // Edit the existing row which has showQr=true → QR file input present.
    await user.click(screen.getByRole('button', { name: /edit/i }));
    const dialog = await screen.findByRole('dialog');
    expect(within(dialog).getByLabelText(/QR image/i)).toBeInTheDocument();
  });
});
