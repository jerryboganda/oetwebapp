/**
 * Vitest spec for the AI Providers admin page focused on the new
 * `github-copilot` preset and the `Copilot` dialect option.
 *
 * Mirrors the project pattern in
 * `app/admin/billing/wallet-tiers/page.test.tsx` — `vi.hoisted` for shared
 * mocks, mock `lib/ai-management-api`, mock `useAdminAuth`. Asserts:
 *   1. The Copilot preset button renders in the create modal.
 *   2. Picking it sets dialect=Copilot, code=copilot, baseUrl=GitHub Models.
 *   3. Submit calls createAiProvider with the right shape.
 *   4. Existing Copilot rows render in the table without leaking the API key.
 */
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

const { mockFetch, mockCreate, mockUpdate, mockDeactivate, authState } = vi.hoisted(() => ({
  mockFetch: vi.fn(),
  mockCreate: vi.fn(),
  mockUpdate: vi.fn(),
  mockDeactivate: vi.fn(),
  authState: {
    isAuthenticated: true as boolean,
    role: 'admin' as 'admin' | 'learner' | null,
  },
}));

vi.mock('@/lib/ai-management-api', async () => {
  const actual = await vi.importActual<typeof import('@/lib/ai-management-api')>('@/lib/ai-management-api');
  return {
    ...actual,
    fetchAiProviders: mockFetch,
    createAiProvider: mockCreate,
    updateAiProvider: mockUpdate,
    deactivateAiProvider: mockDeactivate,
  };
});

vi.mock('@/lib/hooks/use-admin-auth', () => ({
  useAdminAuth: () => ({
    isAuthenticated: authState.isAuthenticated,
    role: authState.role,
  }),
}));

import AiProvidersPage from './page';

describe('AiProvidersPage — GitHub Copilot integration', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    authState.isAuthenticated = true;
    authState.role = 'admin';
  });

  it('renders an existing Copilot row without leaking the API key', async () => {
    mockFetch.mockResolvedValue([
      {
        id: 'copilot-1',
        code: 'copilot',
        name: 'GitHub Copilot / Models',
        dialect: 'Copilot',
        baseUrl: 'https://models.github.ai/inference',
        apiKeyHint: '…wxyz',
        defaultModel: 'openai/gpt-4o-mini',
        allowedModelsCsv: '',
        pricePer1kPromptTokens: 0.00015,
        pricePer1kCompletionTokens: 0.0006,
        retryCount: 2,
        circuitBreakerThreshold: 5,
        circuitBreakerWindowSeconds: 30,
        failoverPriority: 120,
        isActive: true,
        createdAt: '',
        updatedAt: '',
      },
    ]);

    render(<AiProvidersPage />);

    // DataTable renders both desktop and mobile views, so each text node
    // appears multiple times. Use getAllByText and assert ≥ 1.
    await waitFor(() => {
      expect(screen.getAllByText('GitHub Copilot / Models').length).toBeGreaterThan(0);
    });
    expect(screen.getAllByText('Copilot').length).toBeGreaterThan(0);
    expect(screen.getAllByText('…wxyz').length).toBeGreaterThan(0);
    const html = document.body.innerHTML;
    expect(html).not.toContain('github_pat_');
  });

  it('exposes a "GitHub Copilot / Models" preset that fills Copilot dialect', async () => {
    // mockResolvedValue (not Once): the page calls fetchAiProviders again
    // after a successful save, and the DataTable crashes on undefined data.
    mockFetch.mockResolvedValue([]);
    mockCreate.mockResolvedValue({
      id: 'new-id',
      code: 'copilot',
      apiKeyHint: '…abcd',
    });

    render(<AiProvidersPage />);
    await waitFor(() => expect(mockFetch).toHaveBeenCalled());

    await userEvent.click(screen.getByRole('button', { name: /Register provider/i }));
    // Preset button labelled by preset.name (see PRESETS map in page.tsx).
    const presetButton = await screen.findByRole('button', { name: 'GitHub Copilot / Models' });
    await userEvent.click(presetButton);

    // fireEvent.change is more reliable than userEvent.type for password
    // inputs under React 19 + jsdom (which can swallow per-keystroke
    // re-renders and only register the first character).
    const apiKeyInput = screen.getByLabelText(/API key/i) as HTMLInputElement;
    fireEvent.change(apiKeyInput, { target: { value: 'github_pat_TESTKEYabcdefgh1234' } });

    await userEvent.click(screen.getByRole('button', { name: /^Save$/ }));

    await waitFor(() => expect(mockCreate).toHaveBeenCalledTimes(1));
    const payload = mockCreate.mock.calls[0][0] as Record<string, unknown>;
    expect(payload.code).toBe('copilot');
    expect(payload.dialect).toBe('Copilot');
    expect(payload.baseUrl).toBe('https://models.github.ai/inference');
    expect(payload.defaultModel).toBe('openai/gpt-4o-mini');
    expect(payload.apiKey).toBe('github_pat_TESTKEYabcdefgh1234');
  });

  it('exposes an ElevenLabs realtime STT preset with ASR category', async () => {
    mockFetch.mockResolvedValue([]);
    mockCreate.mockResolvedValue({
      id: 'elevenlabs-stt-1',
      code: 'elevenlabs-stt',
      apiKeyHint: '…1234',
    });

    render(<AiProvidersPage />);
    await waitFor(() => expect(mockFetch).toHaveBeenCalled());

    await userEvent.click(screen.getByRole('button', { name: /Register provider/i }));
    await userEvent.click(await screen.findByRole('button', { name: 'ElevenLabs Scribe Realtime STT' }));
    fireEvent.change(screen.getByLabelText(/API key/i), { target: { value: 'elevenlabs_secret_key_1234' } });
    await userEvent.click(screen.getByRole('button', { name: /^Save$/ }));

    await waitFor(() => expect(mockCreate).toHaveBeenCalledTimes(1));
    const payload = mockCreate.mock.calls[0][0] as Record<string, unknown>;
    expect(payload.code).toBe('elevenlabs-stt');
    expect(payload.dialect).toBe('ElevenLabsStt');
    expect(payload.category).toBe('Asr');
    expect(payload.baseUrl).toBe('https://api.elevenlabs.io/v1');
    expect(payload.defaultModel).toBe('scribe_v2_realtime');
  });

  it('blocks non-admin viewers', () => {
    authState.role = 'learner';
    render(<AiProvidersPage />);
    expect(screen.getByText(/Admin access required/i)).toBeInTheDocument();
    expect(mockFetch).not.toHaveBeenCalled();
  });
});
