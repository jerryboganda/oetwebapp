import { render, screen } from '@testing-library/react';
import type { WritingAiProviderStatus } from '@/lib/ai-management-api';

const { mockFetchProvider } = vi.hoisted(() => ({
  mockFetchProvider: vi.fn(),
}));

vi.mock('@/lib/ai-management-api', async () => {
  const actual = await vi.importActual<typeof import('@/lib/ai-management-api')>('@/lib/ai-management-api');
  return {
    ...actual,
    fetchWritingAiProvider: mockFetchProvider,
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

describe('WritingAiProviderPage — Claude Max always on', () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it('states the hard rule and offers no provider control', async () => {
    mockFetchProvider.mockResolvedValue(providerStatus());
    render(<WritingAiProviderPage />);

    expect(await screen.findByText(/Claude Max subscription — always tried first \(hard rule\)/)).toBeInTheDocument();
    expect(screen.queryByRole('combobox')).not.toBeInTheDocument();
    expect(screen.queryByRole('spinbutton')).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /apply|clear/i })).not.toBeInTheDocument();
    expect(screen.queryByText(/force fallback/i)).not.toBeInTheDocument();
  });

  it('treats a high weekly usage estimate as information only', async () => {
    mockFetchProvider.mockResolvedValue(providerStatus());
    render(<WritingAiProviderPage />);

    expect(await screen.findByText('High weekly usage estimate.')).toBeInTheDocument();
    expect(document.body.textContent).not.toMatch(/fails? ?over at|failover active/i);
  });

  it('never shows a failover banner, even if an older server reports one', async () => {
    mockFetchProvider.mockResolvedValue(providerStatus({
      failoverActive: true,
      quotaExceededUntil: '2026-10-05T00:00:00Z',
    }));
    render(<WritingAiProviderPage />);

    expect(await screen.findByText('Provider chain')).toBeInTheDocument();
    expect(screen.queryByText(/failover active/i)).not.toBeInTheDocument();
  });
});
