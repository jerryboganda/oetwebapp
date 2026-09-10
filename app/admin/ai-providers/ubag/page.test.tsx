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
 *   5. Media (Group E) rows render toggleable with facade-mechanism notes.
 *   6. Each part heading carries one UBAG model selector + one UBAG on/off
 *      that apply to everything under that heading; every selector (row,
 *      part, facade-test) can pick any UBAG model from the catalog.
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
  // The board re-fetches routes after every write, so the mock is backed by a
  // mutable store: upserts/deletes must be visible to that re-fetch, exactly
  // like the server would reflect them.
  const store = new Map<string, { featureCode: string; providerCode: string; model: string | null }>();
  for (const r of routes) {
    store.set(r.featureCode, { featureCode: r.featureCode, providerCode: r.providerCode, model: r.model ?? null });
  }

  mockProviders.mockResolvedValue([ubagRow]);
  mockRoutes.mockImplementation(async () => ({
    rows: Array.from(store.values()).map((r, i) => ({
      id: `route-${i}`,
      featureCode: r.featureCode,
      providerCode: r.providerCode,
      model: r.model,
      isActive: true,
      createdAt: '',
      updatedAt: '',
      updatedByAdminId: null,
    })),
    knownFeatureCodes: ['vocabulary.gloss', 'writing.grade', 'ocr.listening.parta'],
    copilotBulkRouteTargets: [],
  }));
  mockUpsert.mockImplementation(async (input: { featureCode: string; providerCode: string; model?: string | null }) => {
    store.set(input.featureCode, {
      featureCode: input.featureCode,
      providerCode: input.providerCode,
      model: input.model ?? null,
    });
    return {};
  });
  mockDelete.mockImplementation(async (featureCode: string) => {
    store.delete(featureCode);
  });
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
    // Row toggles keep exact ON/OFF labels; the five per-part UBAG switches
    // use their own UBAG ON/OFF labels so both are queryable independently.
    const rowOff = await screen.findAllByRole('button', { name: 'OFF' });
    expect(rowOff).toHaveLength(65);
    expect(screen.queryAllByRole('button', { name: 'ON' })).toHaveLength(0);
    expect(await screen.findAllByRole('button', { name: /UBAG for .* off/ })).toHaveLength(5);
  });

  it('each part heading has one model selector + one UBAG on/off for the whole part', async () => {
    seed([]);
    render(<UbagBoardPage />);
    await screen.findByText('B · Learner, non-scoring');

    // One per-part model selector per heading (A–E), each offering any UBAG model.
    const partSelects = await screen.findAllByRole('combobox', { name: /UBAG model for / });
    expect(partSelects).toHaveLength(5);
    for (const select of partSelects) {
      const options = Array.from((select as HTMLSelectElement).options).map((o) => o.value);
      expect(options).toContain('chatgpt_web|GPT-5.6 Sol + Medium');
      expect(options).toContain('duckai_web|GPT-5.6 Luna');
      expect(options).toContain('gemini_web|3.8 Flash');
      expect(options).toContain('whisper-1');
    }

    // One per-part UBAG switch per heading, all OFF with nothing routed.
    expect(await screen.findAllByRole('button', { name: /UBAG for .* off/ })).toHaveLength(5);

    // Flipping part B's switch routes everything under that heading with the
    // part model (deepseek_web default): 23 upserts, zero deletes.
    fireEvent.click(screen.getByRole('button', { name: /UBAG for B · Learner, non-scoring: off/ }));
    await waitFor(() => expect(mockUpsert.mock.calls.length).toBe(23));
    expect(mockUpsert.mock.calls[0][0]).toMatchObject({ featureCode: 'vocabulary.gloss', providerCode: 'ubag', isActive: true });
    // The part model wins for every row without its own draft — one model for
    // the whole part, exactly what the heading selector promises.
    expect(mockUpsert.mock.calls.every(
      (c) => ((c[0] as { model?: string }).model ?? null) === 'deepseek_web',
    )).toBe(true);
    expect(mockDelete).not.toHaveBeenCalled();
    expect(await screen.findByRole('button', { name: /UBAG for B · Learner, non-scoring: on/ })).toBeTruthy();

    // Picking a part model re-seeds every row draft under that heading, so the
    // next row enable uses the part model.
    fireEvent.change(screen.getByRole('combobox', { name: 'UBAG model for B · Learner, non-scoring' }), {
      target: { value: 'gemini_web|3.8 Flash' },
    });
    const glossRow = screen.getByText('vocabulary.gloss').closest('tr') as HTMLElement;
    const glossSelect = glossRow.querySelector('select') as HTMLSelectElement;
    expect(glossSelect.value).toBe('gemini_web|3.8 Flash');
  });

  it('per-part UBAG off un-routes everything under that heading', async () => {
    seed([
      { featureCode: 'vocabulary.gloss', providerCode: 'ubag', model: 'deepseek_web' },
      { featureCode: 'summarise.passage', providerCode: 'ubag', model: 'deepseek_web' },
    ]);
    render(<UbagBoardPage />);
    await screen.findByText('B · Learner, non-scoring');

    // Two of 23 routed → the part switch reports its mixed state. It still
    // reads OFF (not everything is on UBAG) and offers the whole-part ON.
    expect(await screen.findByRole('button', { name: 'UBAG for B · Learner, non-scoring: off (mixed)' })).toBeTruthy();

    fireEvent.click(screen.getByRole('button', { name: 'UBAG for B · Learner, non-scoring: off (mixed)' }));
    // Whole-part ON routes all 23 (the 2 already-routed rows are re-upserted
    // with the part model, like the per-part ON test proves), zero deletes.
    await waitFor(() => expect(mockUpsert.mock.calls.length).toBe(23));
    expect(mockUpsert.mock.calls.every(
      (c) => ((c[0] as { model?: string }).model ?? null) === 'deepseek_web',
    )).toBe(true);
    expect(mockDelete).not.toHaveBeenCalled();
  });

  it('per-part UBAG on then off un-routes everything under that heading', async () => {
    // Seed all 23 routed: the switch reads fully ON, one click OFF deletes
    // exactly those 23 — one delete per feature under the heading, the mirror
    // of the per-row OFF.
    seed([
      'vocabulary.gloss', 'summarise.passage', 'reading.explanation.v1', 'reading.passage_qna.v1',
      'reading.vocabulary.card', 'listening.explanation.v1', 'pronunciation.tip', 'pronunciation.feedback',
      'writing.coach.suggest', 'writing.coach.explain', 'writing.coach.v1', 'writing.rewrite.v1',
      'writing.scenario.generate.v1', 'writing.outline.v1', 'writing.paraphrase.v1', 'writing.ask.v1',
      'writing.canon.detect.v1', 'recalls.mistake_explain', 'recalls.revision_plan', 'mock.remediation_draft',
      'tutor.recommendation.v1', 'class.assistant.qna.v1', 'class.recording.translate.v1',
    ].map((featureCode) => ({ featureCode, providerCode: 'ubag', model: 'deepseek_web' })));
    render(<UbagBoardPage />);
    await screen.findByText('B · Learner, non-scoring');

    expect(await screen.findByRole('button', { name: 'UBAG for B · Learner, non-scoring: on' })).toBeTruthy();
    fireEvent.click(screen.getByRole('button', { name: 'UBAG for B · Learner, non-scoring: on' }));
    await waitFor(() => expect(mockDelete).toHaveBeenCalledTimes(23));
    expect(mockDelete).toHaveBeenCalledWith('vocabulary.gloss');
    expect(mockDelete).toHaveBeenCalledWith('class.recording.translate.v1');
    expect(mockUpsert).not.toHaveBeenCalled();
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

    // Dropdown is populated from the UBAG model fallback list (live facade
    // catalog mirror incl. duck.ai + transcription alias). ChatGPT shows ONE
    // curated entry (Sol + Medium bound as one pick) with a recommended
    // label; legacy single-setting IDs stay out of the board.
    const select = screen.getByRole('combobox', { name: 'UBAG AI provider/model to test' }) as HTMLSelectElement;
    expect(select.options.length).toBeGreaterThan(0);
    expect(Array.from(select.options).some((o) => o.value === 'chatgpt_web|GPT-5.6 Sol + Medium')).toBeTruthy();
    expect(Array.from(select.options).some((o) => o.text === 'chatgpt_web · GPT-5.6 Sol + Medium (recommended)')).toBeTruthy();
    expect(Array.from(select.options).some((o) => o.value === 'chatgpt_web|GPT-5.5')).toBeFalsy();
    expect(Array.from(select.options).some((o) => o.value === 'chatgpt_web|Medium')).toBeFalsy();
    expect(Array.from(select.options).some((o) => o.value === 'duckai_web|GPT-5.6 Luna')).toBeTruthy();
    expect(Array.from(select.options).some((o) => o.value === 'duckai_web|Reasoning')).toBeTruthy();
    expect(Array.from(select.options).some((o) => o.value === 'gemini_web|3.8 Flash')).toBeTruthy();
    expect(Array.from(select.options).some((o) => o.value === 'generic_form')).toBeFalsy();
    expect(Array.from(select.options).some((o) => o.value === 'whisper-1')).toBeFalsy();

    // Picking the curated entry + clicking Test calls the full-pipeline endpoint.
    fireEvent.change(select, { target: { value: 'chatgpt_web|GPT-5.6 Sol + Medium' } });
    fireEvent.click(screen.getByRole('button', { name: /^Test$/ }));
    await waitFor(() => expect(mockTestModel).toHaveBeenCalledWith('ubag', 'chatgpt_web|GPT-5.6 Sol + Medium'));

    // Success result surfaces a green signal and step trail.
    expect(await screen.findByText(/^ok$/)).toBeTruthy();
    expect(await screen.findByText('connectivity')).toBeTruthy();
    expect(await screen.findByText('completion')).toBeTruthy();
    expect(await screen.findByText('chatgpt_web|GPT-5.6 Sol acknowledged')).toBeTruthy();
  });
});
