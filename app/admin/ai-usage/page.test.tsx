import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import type { AiGlobalPolicy } from '@/lib/ai-management-api';

const { mockFetchPolicy, mockUpdatePolicy, mockFetchSummary, mockFetchUsage, mockFetchTrend, mockFetchProviders } = vi.hoisted(() => ({
  mockFetchPolicy: vi.fn(),
  mockUpdatePolicy: vi.fn(),
  mockFetchSummary: vi.fn(),
  mockFetchUsage: vi.fn(),
  mockFetchTrend: vi.fn(),
  mockFetchProviders: vi.fn(),
}));

vi.mock('@/lib/ai-management-api', async () => {
  const actual = await vi.importActual<typeof import('@/lib/ai-management-api')>('@/lib/ai-management-api');
  return {
    ...actual,
    fetchAiGlobalPolicy: mockFetchPolicy,
    updateAiGlobalPolicy: mockUpdatePolicy,
    fetchAiUsageSummary: mockFetchSummary,
    fetchAiUsage: mockFetchUsage,
    fetchAiUsageTrend: mockFetchTrend,
    fetchAiProviders: mockFetchProviders,
  };
});

vi.mock('@/lib/hooks/use-admin-auth', () => ({
  useAdminAuth: () => ({ isAuthenticated: true, role: 'admin' }),
}));

import AiUsagePage from './page';

const policy: AiGlobalPolicy = {
  id: 'global',
  killSwitchEnabled: false,
  killSwitchScope: 'PlatformKeysOnly',
  killSwitchReason: null,
  disabledFeaturesCsv: '',
  monthlyBudgetUsd: 10,
  softWarnPct: 80,
  hardKillPct: 100,
  currentSpendUsd: 12.5,
  enforceSpendCaps: false,
  allowByokOnScoringFeatures: false,
  allowByokOnNonScoringFeatures: true,
  defaultPlatformProviderId: 'digitalocean-serverless',
  byokErrorCooldownHours: 24,
  byokTransientRetryCount: 2,
  anomalyDetectionEnabled: true,
  anomalyMultiplierX: 10,
  rowVersion: 3,
  updatedAt: '2026-10-02T09:00:00Z',
  updatedByAdminId: null,
};

async function openBudgetTab() {
  const user = userEvent.setup();
  render(<AiUsagePage />);
  await user.click(screen.getByRole('tab', { name: /Budget & Kill-switch/ }));
  const toggle = await screen.findByRole('checkbox', { name: 'Enforce platform spend caps' });
  return { user, toggle };
}

describe('AiUsagePage — platform spend-cap switch', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mockFetchPolicy.mockResolvedValue(policy);
    mockUpdatePolicy.mockImplementation(async (body: AiGlobalPolicy) => body);
    mockFetchSummary.mockResolvedValue({ periodMonthKey: '2026-10', groupBy: 'feature', rows: [] });
    mockFetchUsage.mockResolvedValue({ page: 1, pageSize: 25, total: 0, rows: [] });
    mockFetchTrend.mockResolvedValue({ fromMonth: '2026-10', toMonth: '2026-10', rows: [] });
    mockFetchProviders.mockResolvedValue([]);
  });

  it('shows the switch off by default and says what still applies', async () => {
    const { toggle } = await openBudgetTab();

    expect(toggle).not.toBeChecked();
    expect(screen.queryByText('Enforced')).not.toBeInTheDocument();
    expect(screen.getByText(/never block an AI call/)).toHaveTextContent(/spend is still recorded/);
    expect(screen.getByText(/never block an AI call/)).toHaveTextContent(/kill-switch/);
  });

  it('saves enforceSpendCaps when an admin turns the caps back on', async () => {
    const { user, toggle } = await openBudgetTab();

    await user.click(toggle);
    expect(toggle).toBeChecked();
    expect(screen.getByText('Enforced')).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Save changes' }));

    await waitFor(() => expect(mockUpdatePolicy).toHaveBeenCalledTimes(1));
    expect(mockUpdatePolicy).toHaveBeenCalledWith(expect.objectContaining({ enforceSpendCaps: true }));
  });
});
