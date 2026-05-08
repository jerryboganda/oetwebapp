/**
 * Vitest spec for the Phase 7 per-feature routing panel. Asserts that
 * the bulk-route action is disabled when the Copilot provider is not
 * registered or not active — and enabled once it is.
 */
import { render, screen, waitFor, cleanup } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

const {
  mockFetchRoutes,
  mockFetchProviders,
  mockBulkRoute,
  mockUpsertRoute,
  mockDeleteRoute,
} = vi.hoisted(() => ({
  mockFetchRoutes: vi.fn(),
  mockFetchProviders: vi.fn(),
  mockBulkRoute: vi.fn(),
  mockUpsertRoute: vi.fn(),
  mockDeleteRoute: vi.fn(),
}));

vi.mock('@/lib/ai-management-api', async () => {
  const actual = await vi.importActual<typeof import('@/lib/ai-management-api')>('@/lib/ai-management-api');
  return {
    ...actual,
    fetchAiFeatureRoutes: mockFetchRoutes,
    fetchAiProviders: mockFetchProviders,
    bulkRouteFeaturesToCopilot: mockBulkRoute,
    upsertAiFeatureRoute: mockUpsertRoute,
    deleteAiFeatureRoute: mockDeleteRoute,
  };
});

import { AiFeatureRoutesPanel } from './ai-feature-routes-panel';

const ROUTES_RESPONSE = {
  rows: [],
  knownFeatureCodes: ['vocabulary.gloss', 'recalls.mistake_explain', 'conversation.opening'],
  copilotBulkRouteTargets: [
    'vocabulary.gloss',
    'recalls.mistake_explain',
    'conversation.opening',
  ],
};

function provider(overrides: Partial<{ code: string; isActive: boolean }>) {
  return {
    id: 'p-' + (overrides.code ?? 'x'),
    code: overrides.code ?? 'x',
    name: overrides.code ?? 'x',
    dialect: 'OpenAiCompatible' as const,
    baseUrl: 'https://x',
    apiKeyHint: '',
    apiKey: '',
    defaultModel: '',
    failoverPriority: 100,
    isActive: overrides.isActive ?? true,
    lastTestedAt: null,
    lastTestStatus: null,
    lastTestError: null,
    createdAt: '',
    updatedAt: '',
  };
}

describe('AiFeatureRoutesPanel', () => {
  beforeEach(() => {
    vi.resetAllMocks();
  });
  afterEach(() => {
    cleanup();
  });

  it('disables "Route to Copilot" when the Copilot provider is not active', async () => {
    mockFetchRoutes.mockResolvedValue(ROUTES_RESPONSE);
    mockFetchProviders.mockResolvedValue([
      provider({ code: 'openai', isActive: true }),
      // Copilot row exists but is inactive.
      provider({ code: 'copilot', isActive: false }),
    ]);

    render(<AiFeatureRoutesPanel />);

    const button = await screen.findByRole('button', {
      name: /Route bulk-route feature set to Copilot/i,
    });
    await waitFor(() => expect(button).toBeDisabled());
    expect(
      screen.getByText(/Copilot provider is not registered or not active/i),
    ).toBeInTheDocument();
  });

  it('enables and fires bulkRouteFeaturesToCopilot when Copilot is active', async () => {
    mockFetchRoutes.mockResolvedValue(ROUTES_RESPONSE);
    mockFetchProviders.mockResolvedValue([
      provider({ code: 'copilot', isActive: true }),
    ]);
    mockBulkRoute.mockResolvedValue({
      changed: ['vocabulary.gloss', 'recalls.mistake_explain', 'conversation.opening'],
    });

    render(<AiFeatureRoutesPanel />);

    // Wait for providers to load by waiting for the warning to disappear
    // (the warning only renders when copilotActive=false).
    await waitFor(() =>
      expect(
        screen.queryByText(/Copilot provider is not registered or not active/i),
      ).not.toBeInTheDocument(),
    );

    const button = await screen.findByRole('button', {
      name: /Route bulk-route feature set to Copilot/i,
    });
    await waitFor(() => expect(button).toBeEnabled());

    await userEvent.click(button);

    await waitFor(() => expect(mockBulkRoute).toHaveBeenCalledTimes(1));
    expect(
      await screen.findByText(/Routed to Copilot:.*vocabulary\.gloss/i),
    ).toBeInTheDocument();
  });

  it('disables "Route to Copilot" when no Copilot row exists at all', async () => {
    mockFetchRoutes.mockResolvedValue(ROUTES_RESPONSE);
    mockFetchProviders.mockResolvedValue([provider({ code: 'openai', isActive: true })]);

    render(<AiFeatureRoutesPanel />);

    const button = await screen.findByRole('button', {
      name: /Route bulk-route feature set to Copilot/i,
    });
    await waitFor(() => expect(button).toBeDisabled());
    expect(mockBulkRoute).not.toHaveBeenCalled();
  });
});
