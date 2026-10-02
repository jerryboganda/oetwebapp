import { render, screen } from '@testing-library/react';
import type { WritingAiProviderStatus } from '@/lib/ai-management-api';

const { mockFetchProvider, mockUpdateProvider } = vi.hoisted(() => ({
  mockFetchProvider: vi.fn(),
  mockUpdateProvider: vi.fn(),
}));

vi.mock('@/lib/ai-management-api', async () => {
  const actual = await vi.importActual<typeof import('@/lib/ai-management-api')>('@/lib/ai-management-api');
  return {
    ...actual,
    fetchWritingAiProvider: mockFetchProvider,
    updateWritingAiProvider: mockUpdateProvider,
  };
});

vi.mock('@/lib/hooks/use-admin-auth', () => ({
  useAdminAuth: () => ({ isAuthenticated: true, role: 'admin' }),
}));

import WritingAiProviderPage from './page';

function providerStatus(overrides: Partial<WritingAiProviderStatus> = {}): WritingAiProviderStatus {
  return {
    mode: 'auto',
    warnPct: 80,
    failoverPct: 90,
    quota: {
      utilizationPct: 96,
      resetsAt: null,
      source: 'reported',
      weeklyTokensUsed: 960_000,
      weeklyTokenCap: 1_000_000,
      sampledAt: '2026-10-02T09:00:00Z',
    },
    quotaExceededUntil: null,
    failoverActive: false,
    currentPrimary: { provider: 'writing-claude-sub', model: 'claude-opus-5-5' },
    gradedToday: 4,
    gradedWeek: 31,
    fallbackCountWeek: 0,
    claude: { callsWeek: 31, tokensWeek: 960_000 },
    claudeApi: { callsWeek: 0, tokensWeek: 0, costWeekUsd: 0 },
    codex: { callsWeek: 0, tokensWeek: 0, recordedCostWeekUsd: 0 },
    ...overrides,
  };
}

describe('WritingAiProviderPage', () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it('labels the provider modes with the current chain', async () => {
    mockFetchProvider.mockResolvedValue(providerStatus());
    render(<WritingAiProviderPage />);

    expect(await screen.findByRole('option', { name: 'Automatic — Claude Max primary, Codex fallback' })).toBeInTheDocument();
    expect(screen.getByRole('option', { name: 'GPT-6.1 Sol (Codex) — force fallback' })).toBeInTheDocument();
  });

  it('treats a high weekly usage estimate as information only', async () => {
    mockFetchProvider.mockResolvedValue(providerStatus());
    render(<WritingAiProviderPage />);

    expect(await screen.findByText('High weekly usage estimate.')).toBeInTheDocument();
    expect(screen.queryByText('Failover active.')).not.toBeInTheDocument();
    expect(document.body.textContent).not.toMatch(/fails? ?over at/i);
  });

  it('shows the failover banner only for a recorded quota refusal', async () => {
    mockFetchProvider.mockResolvedValue(providerStatus({
      failoverActive: true,
      quotaExceededUntil: '2026-10-05T00:00:00Z',
      currentPrimary: { provider: 'anthropic', model: 'claude-opus-5-5' },
    }));
    render(<WritingAiProviderPage />);

    expect(await screen.findByText('Failover active.')).toBeInTheDocument();
    expect(screen.getByText(/Claude Max reported a quota or rate-limit refusal/)).toBeInTheDocument();
    expect(screen.queryByText('High weekly usage estimate.')).not.toBeInTheDocument();
  });
});
