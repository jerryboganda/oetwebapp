import { act, cleanup, renderHook, waitFor } from '@testing-library/react';

const mocks = vi.hoisted(() => ({
  authUser: null as { role?: string } | null,
  fetchAdminAlerts: vi.fn(),
}));

vi.mock('@/contexts/auth-context', () => ({
  useAuth: () => ({ user: mocks.authUser }),
}));

vi.mock('@/lib/api', () => ({
  fetchAdminAlerts: mocks.fetchAdminAlerts,
}));

import {
  ADMIN_FULFILMENT_ALERT_TYPES,
  normalizeAdminAlerts,
  parseCountFromDescription,
  useAdminAlerts,
} from '../use-admin-alerts';

function validAlert(overrides: Record<string, unknown> = {}) {
  return {
    alertType: 'pending_fulfilment',
    severity: 'warning',
    title: 'Pending Fulfilment',
    description: '3 paid order(s) waiting for fulfilment',
    actionRoute: '/admin/billing/manual-payments?tab=fulfilment',
    detectedAt: '2026-01-01T00:00:00Z',
    ...overrides,
  };
}

function adminPayload(alerts: unknown[]) {
  return { alerts, criticalCount: 0, warningCount: 1, infoCount: 1, generatedAt: '2026-01-01T00:00:00Z' };
}

describe('useAdminAlerts helpers', () => {
  it('parses the numeric count embedded in backend descriptions', () => {
    expect(parseCountFromDescription('3 paid order(s) waiting for fulfilment')).toBe(3);
    expect(parseCountFromDescription('12 payment proof(s) pending review')).toBe(12);
    expect(parseCountFromDescription('No numbers here')).toBe(0);
  });

  it('normalizes the envelope defensively and drops malformed rows', () => {
    const payload = adminPayload([
      validAlert(),
      { alertType: 'broken' }, // missing severity + actionRoute → dropped
      'not-an-object', // wrong shape → dropped
      validAlert({ alertType: 'system_maintenance' }), // valid row, filtered downstream by type
    ]);

    const result = normalizeAdminAlerts(payload);

    expect(result).toHaveLength(2);
    expect(result[0]).toMatchObject({ alertType: 'pending_fulfilment', severity: 'warning' });
    expect(normalizeAdminAlerts(null)).toEqual([]);
    expect(normalizeAdminAlerts({ alerts: 'nope' })).toEqual([]);
  });
});

describe('useAdminAlerts store', () => {
  beforeEach(() => {
    // mockReset (not clearAllMocks): a previous test's mockRejectedValue /
    // mockResolvedValue MUST NOT leak into the next one via the singleton.
    mocks.fetchAdminAlerts.mockReset();
    mocks.authUser = null;
    cleanup();
  });

  afterEach(() => {
    cleanup();
  });

  it('never fetches and stays idle for non-admin users (endpoint would 403)', async () => {
    mocks.authUser = { role: 'student' };

    const { result } = renderHook(() => useAdminAlerts());

    expect(result.current.status).toBe('idle');
    expect(result.current.totalAlertCount).toBe(0);

    await act(async () => {
      await Promise.resolve();
    });

    expect(mocks.fetchAdminAlerts).not.toHaveBeenCalled();
  });

  it('fetches, filters to fulfilment types and sums description counts for admins', async () => {
    mocks.authUser = { role: 'admin' };
    mocks.fetchAdminAlerts.mockResolvedValue(adminPayload([
      validAlert(),
      validAlert({
        alertType: 'pending_payment_proofs',
        severity: 'info',
        title: 'Payment Proofs Pending',
        description: '2 payment proof(s) pending review',
        actionRoute: '/admin/billing/manual-payments?tab=proofs',
      }),
      validAlert({ alertType: 'system_maintenance', description: '9 systems affected' }), // filtered out
    ]));

    const { result } = renderHook(() => useAdminAlerts());

    await waitFor(() => expect(result.current.status).toBe('ready'));

    expect(mocks.fetchAdminAlerts).toHaveBeenCalledTimes(1);
    expect(result.current.alerts.map((alert) => alert.alertType)).toEqual([...ADMIN_FULFILMENT_ALERT_TYPES]);
    expect(result.current.totalAlertCount).toBe(5); // 3 orders + 2 proofs
  });

  it('degrades silently to the error state when the endpoint rejects (never throws)', async () => {
    const consoleError = vi.spyOn(console, 'error').mockImplementation(() => {});
    mocks.authUser = { role: 'admin' };
    mocks.fetchAdminAlerts.mockRejectedValue(new Error('403 Forbidden'));

    const { result } = renderHook(() => useAdminAlerts());

    await waitFor(() => expect(result.current.status).toBe('error'));

    expect(result.current.alerts).toHaveLength(0);
    expect(result.current.totalAlertCount).toBe(0);
    consoleError.mockRestore();
  });

  it('shares ONE 30s interval across simultaneous subscribers and stops it fully on unsubscribe', async () => {
    vi.useFakeTimers();
    try {
      mocks.authUser = { role: 'admin' };
      mocks.fetchAdminAlerts.mockResolvedValue(adminPayload([validAlert()]));

      // Layout badge + bell button mount simultaneously → exactly one store.
      const layout = renderHook(() => useAdminAlerts());
      const bell = renderHook(() => useAdminAlerts());

      const flush = async () => {
        await act(async () => {
          await Promise.resolve();
          await Promise.resolve();
          await Promise.resolve();
          await Promise.resolve();
        });
      };

      await flush();
      expect(mocks.fetchAdminAlerts).toHaveBeenCalledTimes(1);

      await act(async () => {
        vi.advanceTimersByTime(30_000);
      });
      await flush();
      expect(mocks.fetchAdminAlerts).toHaveBeenCalledTimes(2);

      // Both consumers unmount → refcount 0 → interval cleared.
      layout.unmount();
      bell.unmount();

      await act(async () => {
        vi.advanceTimersByTime(120_000);
      });
      expect(mocks.fetchAdminAlerts).toHaveBeenCalledTimes(2);
    } finally {
      vi.useRealTimers();
    }
  });
});
