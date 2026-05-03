import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

const { mockFetch, mockReplace, api } = vi.hoisted(() => ({
  mockFetch: vi.fn(),
  mockReplace: vi.fn(),
  api: {} as Record<string, unknown>,
}));

vi.mock('@/lib/api', () => {
  api.fetchAdminWalletTiers = mockFetch;
  api.replaceAdminWalletTiers = mockReplace;
  return api;
});

vi.mock('@/lib/analytics', () => ({
  analytics: { track: vi.fn() },
}));

import AdminWalletTiersPage from './page';

const dbResponse = {
  source: 'database' as const,
  currency: 'AUD',
  tiers: [
    {
      id: '11111111-1111-1111-1111-111111111111',
      amount: 25,
      credits: 28,
      bonus: 3,
      totalCredits: 31,
      label: 'Standard',
      isPopular: false,
      displayOrder: 1,
      isActive: true,
      currency: 'AUD',
    },
  ],
};

const fallbackResponse = {
  source: 'appsettings' as const,
  currency: 'AUD',
  tiers: [
    {
      id: null,
      amount: 10,
      credits: 10,
      bonus: 0,
      totalCredits: 10,
      label: 'Starter',
      isPopular: false,
      displayOrder: 0,
      isActive: true,
      currency: 'AUD',
    },
  ],
};

describe('AdminWalletTiersPage', () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it('renders DB-backed tiers and saves an update', async () => {
    mockFetch.mockResolvedValueOnce(dbResponse);
    mockReplace.mockResolvedValueOnce({
      ...dbResponse,
      tiers: [{ ...dbResponse.tiers[0], amount: 30, credits: 32, totalCredits: 35 }],
    });

    render(<AdminWalletTiersPage />);

    await waitFor(() => {
      expect(screen.getByTestId('wallet-tiers-editor')).toBeInTheDocument();
    });

    const amountInput = screen.getByDisplayValue('25');
    await userEvent.clear(amountInput);
    await userEvent.type(amountInput, '30');

    const saveButton = screen.getByTestId('wallet-tiers-save');
    await userEvent.click(saveButton);

    await waitFor(() => {
      expect(mockReplace).toHaveBeenCalledTimes(1);
    });

    const payload = mockReplace.mock.calls[0][0];
    expect(payload).toHaveLength(1);
    expect(payload[0]).toMatchObject({
      id: '11111111-1111-1111-1111-111111111111',
      amount: 30,
      credits: 28,
      currency: 'AUD',
    });

    await waitFor(() => {
      expect(screen.getByText('Wallet top-up tiers saved.')).toBeInTheDocument();
    });
  });

  it('shows the appsettings fallback notice when no DB rows exist', async () => {
    mockFetch.mockResolvedValueOnce(fallbackResponse);

    render(<AdminWalletTiersPage />);

    await waitFor(() => {
      expect(screen.getByTestId('wallet-tiers-editor')).toBeInTheDocument();
    });
    expect(screen.getByText(/Showing appsettings fallback/i)).toBeInTheDocument();
  });

  it('blocks save when an amount is invalid', async () => {
    mockFetch.mockResolvedValueOnce(dbResponse);

    render(<AdminWalletTiersPage />);

    await waitFor(() => {
      expect(screen.getByTestId('wallet-tiers-editor')).toBeInTheDocument();
    });

    const amountInput = screen.getByDisplayValue('25');
    await userEvent.clear(amountInput);

    const saveButton = screen.getByTestId('wallet-tiers-save');
    expect(saveButton).toBeDisabled();
    expect(mockReplace).not.toHaveBeenCalled();
  });

  it('surfaces an error when the API call rejects', async () => {
    mockFetch.mockResolvedValueOnce(dbResponse);
    mockReplace.mockRejectedValueOnce(new Error('boom'));

    render(<AdminWalletTiersPage />);

    await waitFor(() => {
      expect(screen.getByTestId('wallet-tiers-editor')).toBeInTheDocument();
    });

    await userEvent.click(screen.getByTestId('wallet-tiers-save'));

    await waitFor(() => {
      expect(screen.getByText('boom')).toBeInTheDocument();
    });
  });
});
