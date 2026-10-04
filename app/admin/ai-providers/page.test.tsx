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

  it('exposes a TypeSafe Jev preset with the Judgment category, inactive until the key is tested', async () => {
    mockFetch.mockResolvedValue([]);
    mockCreate.mockResolvedValue({
      id: 'typesafe-jev-1',
      code: 'typesafe-jev',
      apiKeyHint: '…0000',
    });

    render(<AiProvidersPage />);
    await waitFor(() => expect(mockFetch).toHaveBeenCalled());

    await userEvent.click(screen.getByRole('button', { name: /Register provider/i }));
    await userEvent.click(await screen.findByRole('button', { name: 'TypeSafe Jev (typed judgments)' }));
    // Judgment rows get the "key is entered here, never in files" hint.
    expect(screen.getByText(/never put it in files/i)).toBeInTheDocument();
    fireEvent.change(screen.getByLabelText(/API key/i), { target: { value: 'typesafe_test_key_000000' } });
    await userEvent.click(screen.getByRole('button', { name: /^Save$/ }));

    await waitFor(() => expect(mockCreate).toHaveBeenCalledTimes(1));
    const payload = mockCreate.mock.calls[0][0] as Record<string, unknown>;
    expect(payload.code).toBe('typesafe-jev');
    expect(payload.dialect).toBe('TypeSafeJev');
    expect(payload.category).toBe('Judgment');
    expect(payload.baseUrl).toBe('https://api.typesafe.ai');
    expect(payload.defaultModel).toBe('jev-1.13.0');
    expect(payload.isActive).toBe(false);
  });

  it.each([
    ['OpenCode (inference gateway) - Zen', 'https://opencode.ai/zen/v1'],
    ['OpenCode (inference gateway) - Go', 'https://opencode.ai/zen/go/v1'],
  ])('exposes the "%s" preset: shared code, own base URL, priority 900, inactive', async (presetName, baseUrl) => {
    mockFetch.mockResolvedValue([]);
    mockCreate.mockResolvedValue({
      id: 'opencode-1',
      code: 'opencode',
      apiKeyHint: '',
    });

    render(<AiProvidersPage />);
    await waitFor(() => expect(mockFetch).toHaveBeenCalled());

    await userEvent.click(screen.getByRole('button', { name: /Register provider/i }));
    await userEvent.click(await screen.findByRole('button', { name: presetName }));
    // The key hint appears once the preset sets code=opencode.
    expect(screen.getByText(/TV-029/)).toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: /^Save$/ }));

    await waitFor(() => expect(mockCreate).toHaveBeenCalledTimes(1));
    const payload = mockCreate.mock.calls[0][0] as Record<string, unknown>;
    expect(payload.code).toBe('opencode');
    expect(payload.name).toBe(presetName);
    expect(payload.dialect).toBe('OpenAiCompatible');
    expect(payload.category).toBe('TextChat');
    expect(payload.baseUrl).toBe(baseUrl);
    expect(payload.defaultModel).toBe('glm-5.3-flash');
    expect(payload.pricePer1kPromptTokens).toBe(0.00015);
    expect(payload.pricePer1kCompletionTokens).toBe(0.0005);
    expect(payload.failoverPriority).toBe(900);
    expect(payload.isActive).toBe(false);
  });

  it('shows the OpenCode key hint when editing the seeded opencode row', async () => {
    mockFetch.mockResolvedValue([
      {
        id: 'opencode-1',
        code: 'opencode',
        name: 'OpenCode (inference gateway)',
        dialect: 'OpenAiCompatible',
        category: 'TextChat',
        baseUrl: 'https://opencode.ai/zen/v1',
        apiKeyHint: '',
        defaultModel: 'glm-5.3-flash',
        allowedModelsCsv: 'glm-5.3-flash,glm-5.3',
        pricePer1kPromptTokens: 0.00015,
        pricePer1kCompletionTokens: 0.0005,
        retryCount: 2,
        circuitBreakerThreshold: 5,
        circuitBreakerWindowSeconds: 30,
        failoverPriority: 900,
        isActive: false,
        createdAt: '',
        updatedAt: '',
      },
    ]);

    render(<AiProvidersPage />);
    await waitFor(() => {
      expect(screen.getAllByText('OpenCode (inference gateway)').length).toBeGreaterThan(0);
    });

    // DataTable renders desktop and mobile views, so Edit appears twice.
    await userEvent.click(screen.getAllByRole('button', { name: 'Edit' })[0]);
    expect(screen.getByText(/TV-029/)).toBeInTheDocument();
  });

  it('lists a Judgment row only under the Judgment category filter', async () => {
    const base = {
      baseUrl: 'https://example.com',
      apiKeyHint: '',
      defaultModel: 'm',
      allowedModelsCsv: '',
      pricePer1kPromptTokens: 0,
      pricePer1kCompletionTokens: 0,
      retryCount: 2,
      circuitBreakerThreshold: 5,
      circuitBreakerWindowSeconds: 30,
      failoverPriority: 28,
      isActive: false,
      createdAt: '',
      updatedAt: '',
    };
    mockFetch.mockResolvedValue([
      { ...base, id: 'jev-1', code: 'typesafe-jev', name: 'TypeSafe Jev (typed judgments)', dialect: 'TypeSafeJev', category: 'Judgment' },
      { ...base, id: 'chat-1', code: 'plain-chat', name: 'Plain chat provider', dialect: 'OpenAiCompatible', category: 'TextChat' },
    ]);

    render(<AiProvidersPage />);
    await waitFor(() => {
      expect(screen.getAllByText('TypeSafe Jev (typed judgments)').length).toBeGreaterThan(0);
    });

    await userEvent.click(screen.getByRole('button', { name: 'TextChat' }));
    expect(screen.queryAllByText('TypeSafe Jev (typed judgments)')).toHaveLength(0);

    await userEvent.click(screen.getByRole('button', { name: 'Judgment' }));
    expect(screen.getAllByText('TypeSafe Jev (typed judgments)').length).toBeGreaterThan(0);
    expect(screen.queryAllByText('Plain chat provider')).toHaveLength(0);
  });

  it('blocks non-admin viewers', () => {
    authState.role = 'learner';
    render(<AiProvidersPage />);
    expect(screen.getByText(/Admin access required/i)).toBeInTheDocument();
    expect(mockFetch).not.toHaveBeenCalled();
  });
});
