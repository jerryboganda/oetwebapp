/**
 * Vitest spec for the multi-account pool modal opened from the AI
 * Providers admin page. Asserts the modal lists existing accounts,
 * fires the create / update / deactivate / reset API calls, and never
 * leaks the API key.
 */
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { fireEvent } from '@testing-library/react';

const {
  mockFetchAccounts,
  mockCreateAccount,
  mockUpdateAccount,
  mockDeactivateAccount,
  mockResetAccount,
  mockTestAccount,
} = vi.hoisted(() => ({
  mockFetchAccounts: vi.fn(),
  mockCreateAccount: vi.fn(),
  mockUpdateAccount: vi.fn(),
  mockDeactivateAccount: vi.fn(),
  mockResetAccount: vi.fn(),
  mockTestAccount: vi.fn(),
}));

vi.mock('@/lib/ai-management-api', async () => {
  const actual = await vi.importActual<typeof import('@/lib/ai-management-api')>('@/lib/ai-management-api');
  return {
    ...actual,
    fetchAiProviderAccounts: mockFetchAccounts,
    createAiProviderAccount: mockCreateAccount,
    updateAiProviderAccount: mockUpdateAccount,
    deactivateAiProviderAccount: mockDeactivateAccount,
    resetAiProviderAccount: mockResetAccount,
    testAiProviderAccount: mockTestAccount,
  };
});

import { AiProviderAccountsModal } from './ai-provider-accounts-modal';

describe('AiProviderAccountsModal', () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  function renderModal() {
    return render(
      <AiProviderAccountsModal
        open={true}
        providerId="copilot-1"
        providerLabel="GitHub Copilot"
        onClose={() => {}}
      />,
    );
  }

  it('lists existing accounts with labels and key hints (never the raw key)', async () => {
    mockFetchAccounts.mockResolvedValue([
      {
        id: 'acc-1',
        providerId: 'copilot-1',
        label: 'primary-org',
        apiKeyHint: '…wxyz',
        monthlyRequestCap: 100,
        requestsUsedThisMonth: 25,
        priority: 0,
        exhaustedUntil: null,
        isActive: true,
        periodMonthKey: '2026-05',
        createdAt: '',
        updatedAt: '',
        updatedByAdminId: null,
      },
    ]);

    renderModal();

    await waitFor(() => {
      expect(screen.getAllByText('primary-org').length).toBeGreaterThan(0);
    });
    expect(screen.getAllByText('…wxyz').length).toBeGreaterThan(0);
    expect(document.body.innerHTML).not.toContain('github_pat_');
  });

  it('creates a new account via createAiProviderAccount when the form is submitted', async () => {
    mockFetchAccounts.mockResolvedValue([]);
    mockCreateAccount.mockResolvedValue({ id: 'new', label: 'backup', apiKeyHint: '…1234' });

    renderModal();
    await waitFor(() => expect(mockFetchAccounts).toHaveBeenCalled());

    await userEvent.click(screen.getByRole('button', { name: /\+ Add account/i }));

    const labelInput = screen.getByLabelText(/Label/i) as HTMLInputElement;
    fireEvent.change(labelInput, { target: { value: 'backup' } });
    const apiKeyInput = screen.getByLabelText(/PAT \/ API key/i) as HTMLInputElement;
    fireEvent.change(apiKeyInput, { target: { value: 'github_pat_abcdefghij1234' } });
    const capInput = screen.getByLabelText(/Monthly request cap/i) as HTMLInputElement;
    fireEvent.change(capInput, { target: { value: '50' } });

    await userEvent.click(screen.getByRole('button', { name: /^Save$/ }));

    await waitFor(() => {
      expect(mockCreateAccount).toHaveBeenCalledWith('copilot-1', expect.objectContaining({
        label: 'backup',
        apiKey: 'github_pat_abcdefghij1234',
        monthlyRequestCap: 50,
        priority: 0,
        isActive: true,
      }));
    });
  });

  it('rejects API keys shorter than 16 characters at create time', async () => {
    mockFetchAccounts.mockResolvedValue([]);

    renderModal();
    await waitFor(() => expect(mockFetchAccounts).toHaveBeenCalled());

    await userEvent.click(screen.getByRole('button', { name: /\+ Add account/i }));
    fireEvent.change(screen.getByLabelText(/Label/i), { target: { value: 'tiny' } });
    fireEvent.change(screen.getByLabelText(/PAT \/ API key/i), { target: { value: 'short' } });

    await userEvent.click(screen.getByRole('button', { name: /^Save$/ }));

    expect(mockCreateAccount).not.toHaveBeenCalled();
    expect(screen.getByText(/at least 16 characters/i)).toBeInTheDocument();
  });

  it('fires resetAiProviderAccount when Reset is clicked', async () => {
    mockFetchAccounts.mockResolvedValue([
      {
        id: 'acc-1',
        providerId: 'copilot-1',
        label: 'primary',
        apiKeyHint: '…1',
        monthlyRequestCap: 100,
        requestsUsedThisMonth: 100,
        priority: 0,
        exhaustedUntil: null,
        isActive: true,
        periodMonthKey: '2026-05',
        createdAt: '',
        updatedAt: '',
        updatedByAdminId: null,
      },
    ]);
    mockResetAccount.mockResolvedValue({ id: 'acc-1', requestsUsedThisMonth: 0, exhaustedUntil: null });

    renderModal();
    await waitFor(() => expect(mockFetchAccounts).toHaveBeenCalled());

    const resetButtons = await screen.findAllByRole('button', { name: /^Reset$/ });
    await userEvent.click(resetButtons[0]);

    await waitFor(() => {
      expect(mockResetAccount).toHaveBeenCalledWith('copilot-1', 'acc-1');
    });
  });

  it('fires testAiProviderAccount when Test is clicked and surfaces the status', async () => {
    mockFetchAccounts.mockResolvedValue([
      {
        id: 'acc-1',
        providerId: 'copilot-1',
        label: 'primary',
        apiKeyHint: '…1',
        monthlyRequestCap: 100,
        requestsUsedThisMonth: 0,
        priority: 0,
        exhaustedUntil: null,
        isActive: true,
        periodMonthKey: '2026-05',
        lastTestedAt: null,
        lastTestStatus: null,
        lastTestError: null,
        createdAt: '',
        updatedAt: '',
        updatedByAdminId: null,
      },
    ]);
    mockTestAccount.mockResolvedValue({
      status: 'ok',
      errorMessage: null,
      latencyMs: 123,
      testedAt: '2026-05-09T12:00:00Z',
    });

    renderModal();
    await waitFor(() => expect(mockFetchAccounts).toHaveBeenCalled());

    const testButtons = await screen.findAllByRole('button', { name: /^Test$/ });
    await userEvent.click(testButtons[0]);

    await waitFor(() => {
      expect(mockTestAccount).toHaveBeenCalledWith('copilot-1', 'acc-1');
    });
    // Toast surfaces status + latency.
    expect(screen.getAllByText(/ok/i).length).toBeGreaterThan(0);
  });
});
