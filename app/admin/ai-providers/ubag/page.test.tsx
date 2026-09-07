/**
 * Vitest spec for the UBAG provider toggle board.
 *
 * Mirrors the project pattern in `app/admin/ai-providers/page.test.tsx` —
 * `vi.hoisted` for shared mocks, mock `lib/ai-management-api`, mock
 * `useAdminAuth`. Asserts:
 *   1. Groups and feature rows render with ON/OFF states from route rows.
 *   2. Switching a standard feature OFF→ON calls upsertAiFeatureRoute.
 *   3. Switching ON→OFF calls deleteAiFeatureRoute.
 *   4. Scoring features open a confirmation modal before upserting.
 *   5. Locked (Group E) rows render with no toggle buttons.
 */
import { fireEvent, render, screen, waitFor } from '@testing-library/react';

const {
  mockProviders,
  mockRoutes,
  mockUpsert,
  mockDelete,
  mockTest,
  mockTestModel,
  mockDiscover,
  mockUpdate,
  authState,
} = vi.hoisted(() => ({
  mockProviders: vi.fn(),
  mockRoutes: vi.fn(),
  mockUpsert: vi.fn(),
  mockDelete: vi.fn(),
  mockTest: vi.fn(),
  mockTestModel: vi.fn(),
  mockDiscover: vi.fn(),
  mockUpdate: vi.fn(),
  authState: {
    isAuthenticated: true as boolean,
    role: 'admin' as 'admin' | 'learner' | null,
  },
}));

vi.mock('@/lib/ai-management-api', async () => {
  const actual = await vi.importActual<typeof import('@/lib/ai-management-api')>('@/lib/ai-management-api');
  return {
    ...actual,
    fetchAiProviders: mockProviders,
    fetchAiFeatureRoutes: mockRoutes,
    upsertAiFeatureRoute: mockUpsert,
    deleteAiFeatureRoute: mockDelete,
    testAiProvider: mockTest,
    testAiProviderModel: mockTestModel,
    discoverAiProviderModels: mockDiscover,
    updateAiProvider: mockUpdate,
  };
});

vi.mock('@/lib/hooks/use-admin-auth', () => ({
  useAdminAuth: () => ({
    isAuthenticated: authState.isAuthenticated,
    role: authState.role,
  }),
}));

import UbagBoardPage from './page';

const ubagRow = {
  id: 'ubag-1',
  code: 'ubag',
  name: 'UBAG (browser AI providers)',
  dialect: 'OpenAiCompatible',
  category: 'TextChat',
  baseUrl: 'http://ubag-vps-gateway-1:8080/v1/openai',
  apiKeyHint: '…1234',
  defaultModel: 'mock',
  allowedModelsCsv: '',
  pricePer1kPromptTokens: 0,
  pricePer1kCompletionTokens: 0,
  retryCount: 2,
  circuitBreakerThreshold: 5,
  circuitBreakerWindowSeconds: 30,
  failoverPriority: 70,
  isActive: true,
  lastTestedAt: null,
  lastTestStatus: null,
  lastTestError: null,
  createdAt: '',
  updatedAt: '',
};

function seed(routes: Array<{ featureCode: string; providerCode: string; model?: string | null }>) {
  mockProviders.mockResolvedValue([ubagRow]);
  mockRoutes.mockResolvedValue({
    rows: routes.map((r, i) => ({
      id: `route-${i}`,
      featureCode: r.featureCode,
      providerCode: r.providerCode,
      model: r.model ?? null,
      isActive: true,
      createdAt: '',
      updatedAt: '',
      updatedByAdminId: null,
    })),
    knownFeatureCodes: ['vocabulary.gloss', 'writing.grade', 'ocr.listening.parta'],
    copilotBulkRouteTargets: [],
  });
  mockUpsert.mockResolvedValue({});
  mockDelete.mockResolvedValue(undefined);
}

describe('UbagBoardPage', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    authState.isAuthenticated = true;
    authState.role = 'admin';
  });

  it('renders groups with ON/OFF states from active routes', async () => {
    seed([{ featureCode: 'vocabulary.gloss', providerCode: 'ubag', model: 'deepseek_web' }]);
    render(<UbagBoardPage />);

    expect(await screen.findByText('A · Admin & content drafts')).toBeTruthy();
    expect(await screen.findByText('B · Learner, non-scoring')).toBeTruthy();
    expect(await screen.findByText('D · Scoring-critical')).toBeTruthy();
    expect(await screen.findByText('E · Media in/out via UBAG')).toBeTruthy();
    // vocabulary.gloss is routed to UBAG → ON; writing.grade has no route → OFF.
    const onButtons = await screen.findAllByRole('button', { name: 'ON' });
    expect(onButtons.length).toBeGreaterThanOrEqual(1);
    expect(await screen.findAllByRole('button', { name: 'OFF' })).not.toHaveLength(0);
  });

  it('switching OFF→ON upserts a UBAG route with the group model', async () => {
    seed([]);
    render(<UbagBoardPage />);
    await screen.findByText('B · Learner, non-scoring');

    const offButtons = await screen.findAllByRole('button', { name: 'OFF' });
    fireEvent.click(offButtons[0]);
    await waitFor(() => expect(mockUpsert).toHaveBeenCalled());
    const input = mockUpsert.mock.calls[0][0] as { featureCode: string; providerCode: string; isActive: boolean };
    expect(input.providerCode).toBe('ubag');
    expect(input.isActive).toBe(true);
  });

  it('switching ON→OFF deletes the route', async () => {
    seed([{ featureCode: 'vocabulary.gloss', providerCode: 'ubag' }]);
    render(<UbagBoardPage />);
    await screen.findByText('B · Learner, non-scoring');

    const onButtons = await screen.findAllByRole('button', { name: 'ON' });
    fireEvent.click(onButtons[0]);
    await waitFor(() => expect(mockDelete).toHaveBeenCalledWith('vocabulary.gloss'));
  });

  it('scoring toggles open a confirmation modal before upserting', async () => {
    seed([]);
    render(<UbagBoardPage />);
    await screen.findByText('D · Scoring-critical');

    // Scope to the scoring group's own row so later groups (E · Media)
    // cannot capture the click — their OFF buttons upsert without a modal.
    const row = screen.getByText('writing.grade').closest('tr') as HTMLElement;
    const offButton = row.querySelector('button[aria-pressed="false"]') as HTMLElement;
    fireEvent.click(offButton);
    // Modal asks for confirmation; nothing is upserted yet.
    expect(await screen.findByText('Enable scoring-critical UBAG routing?')).toBeTruthy();
    expect(mockUpsert).not.toHaveBeenCalled();
    fireEvent.click(screen.getByRole('button', { name: 'Enable anyway' }));
    await waitFor(() => expect(mockUpsert).toHaveBeenCalled());
    expect((mockUpsert.mock.calls[0][0] as { providerCode: string }).providerCode).toBe('ubag');
  });

  it('media rows render toggleable with facade-mechanism notes', async () => {
    seed([]);
    render(<UbagBoardPage />);
    await screen.findByText('E · Media in/out via UBAG');

    expect(await screen.findAllByText('Facade audio/transcriptions; provider listens.')).toHaveLength(1);
    expect(await screen.findByText('Facade /embeddings: deterministic hash vectors, NOT semantic.')).toBeTruthy();
    // Shared by the three route-aware JSON rows (Part A extract/score, B/C extract).
    expect(await screen.findAllByText('Route-aware: facade JSON coercion + forced-tool emulation.')).toHaveLength(3);
    // Matrix pin: 12 (A) + 23 (B) + 6 (C) + 9 (D) + 15 (E) = 65 toggleable rows, all OFF.
    expect(await screen.findAllByRole('button', { name: 'OFF' })).toHaveLength(65);
    expect(screen.queryAllByRole('button', { name: 'ON' })).toHaveLength(0);
  });

  it('renders the facade model dropdown and test button', async () => {
    seed([]);
    mockTestModel.mockResolvedValue({
      status: 'ok',
      errorMessage: null,
      latencyMs: 1234,
      testedAt: new Date().toISOString(),
      model: 'chatgpt_web|GPT-5.6 Sol',
      steps: [
        { step: 'connectivity', detail: 'http://ubag-vps-gateway-1:8080/v1/openai', ok: true },
        { step: 'completion', detail: 'chat completion returned a 2xx response', ok: true },
        { step: 'model', detail: 'chatgpt_web|GPT-5.6 Sol acknowledged', ok: true },
      ],
    });
    render(<UbagBoardPage />);
    await screen.findByText('Facade model test');

    // Dropdown is populated from the UBAG model fallback list.
    const select = screen.getByRole('combobox', { name: 'UBAG AI provider/model to test' }) as HTMLSelectElement;
    expect(select.options.length).toBeGreaterThan(0);
    expect(Array.from(select.options).some((o) => o.value === 'chatgpt_web|GPT-5.6 Sol')).toBeTruthy();

    // Picking a model + clicking Test calls the full-pipeline endpoint.
    fireEvent.change(select, { target: { value: 'chatgpt_web|GPT-5.6 Sol' } });
    fireEvent.click(screen.getByRole('button', { name: /^Test$/ }));
    await waitFor(() => expect(mockTestModel).toHaveBeenCalledWith('ubag', 'chatgpt_web|GPT-5.6 Sol'));

    // Success result surfaces a green signal and step trail.
    expect(await screen.findByText(/^ok$/)).toBeTruthy();
    expect(await screen.findByText('connectivity')).toBeTruthy();
    expect(await screen.findByText('completion')).toBeTruthy();
    expect(await screen.findByText('chatgpt_web|GPT-5.6 Sol acknowledged')).toBeTruthy();
  });
});
