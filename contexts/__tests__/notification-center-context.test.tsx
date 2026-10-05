import { act, renderHook } from '@testing-library/react';
import type { ReactNode } from 'react';

const mocks = vi.hoisted(() => {
  const hub = {
    builds: 0,
    // A hub that never finishes connecting keeps its state at 'Connecting'.
    start: (): Promise<void> => new Promise<void>(() => {}),
  };

  class FakeHubBuilder {
    withUrl() {
      return this;
    }
    configureLogging() {
      return this;
    }
    withAutomaticReconnect() {
      return this;
    }
    build() {
      hub.builds += 1;
      return {
        state: 'Connecting',
        on: vi.fn(),
        onreconnecting: vi.fn(),
        onreconnected: vi.fn(),
        onclose: vi.fn(),
        start: () => hub.start(),
        stop: vi.fn().mockResolvedValue(undefined),
      };
    }
  }

  return {
    hub,
    FakeHubBuilder,
    auth: { isAuthenticated: true, loading: false },
    fetchNotifications: vi.fn(),
    fetchNotificationPreferences: vi.fn(),
    fetchPushConfiguration: vi.fn(),
  };
});

vi.mock('@microsoft/signalr', () => ({
  HubConnectionBuilder: mocks.FakeHubBuilder,
  HttpTransportType: { WebSockets: 1, LongPolling: 4 },
  LogLevel: { None: 6 },
}));

vi.mock('@/contexts/auth-context', () => ({
  useAuth: () => mocks.auth,
}));

vi.mock('@/app/providers/RuntimeConfigProvider', () => ({
  useRuntimeConfig: () => ({ webPush: { vapidPublicKey: 'fallback-key' } }),
}));

vi.mock('@/lib/auth-client', () => ({
  ensureFreshAccessToken: vi.fn().mockResolvedValue('access-token'),
  forceSignOutAndRedirect: vi.fn(),
}));

vi.mock('@/lib/env', () => ({
  env: { apiBaseUrl: '/api/backend', webPushPublicKey: '' },
}));

vi.mock('@/lib/runtime-signals', () => ({
  getAppRuntimeKind: () => 'web',
}));

vi.mock('@/components/ui/alert', () => ({
  Toast: () => null,
}));

vi.mock('@/lib/notifications-api', () => ({
  fetchNotifications: (...args: unknown[]) => mocks.fetchNotifications(...args),
  fetchNotificationPreferences: (...args: unknown[]) => mocks.fetchNotificationPreferences(...args),
  fetchPushConfiguration: (...args: unknown[]) => mocks.fetchPushConfiguration(...args),
  createPushSubscription: vi.fn(),
  deletePushSubscription: vi.fn(),
  markAllNotificationsRead: vi.fn(),
  markNotificationRead: vi.fn(),
  updateNotificationPreferences: vi.fn(),
}));

import { NotificationCenterProvider, useNotificationCenter } from '../notification-center-context';

function wrapper({ children }: { children: ReactNode }) {
  return <NotificationCenterProvider>{children}</NotificationCenterProvider>;
}

let visibility: 'visible' | 'hidden' = 'visible';

function setNavigatorWebdriver(value: boolean) {
  Object.defineProperty(window.navigator, 'webdriver', { configurable: true, get: () => value });
}

describe('NotificationCenterProvider', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mocks.hub.builds = 0;
    mocks.hub.start = () => new Promise<void>(() => {});
    mocks.auth.isAuthenticated = true;
    mocks.auth.loading = false;
    mocks.fetchNotifications.mockResolvedValue({ items: [], unreadCount: 0, totalCount: 0, page: 1 });
    mocks.fetchNotificationPreferences.mockResolvedValue({
      timezone: 'UTC',
      eventPreferences: {},
      legacyLearnerSettings: {},
    });
    mocks.fetchPushConfiguration.mockResolvedValue({ enabled: true, publicKey: 'configured-key' });
    visibility = 'visible';
    Object.defineProperty(document, 'visibilityState', { configurable: true, get: () => visibility });
    setNavigatorWebdriver(false);
  });

  afterEach(() => {
    vi.useRealTimers();
  });

  describe('first-use settings', () => {
    it('loads only the feed at mount: no preferences and no push configuration', async () => {
      renderHook(() => useNotificationCenter(), { wrapper });
      await act(async () => {});

      expect(mocks.fetchNotifications).toHaveBeenCalledTimes(1);
      expect(mocks.fetchNotificationPreferences).not.toHaveBeenCalled();
      expect(mocks.fetchPushConfiguration).not.toHaveBeenCalled();
    });

    it('loads preferences and push configuration once, the first time a settings surface asks', async () => {
      const { result } = renderHook(() => useNotificationCenter(), { wrapper });
      await act(async () => {});

      await act(async () => {
        result.current.ensureSettingsLoaded();
        result.current.ensureSettingsLoaded();
      });
      // Let the effects it triggered (and their resolved fetches) settle.
      await act(async () => {});

      expect(mocks.fetchNotificationPreferences).toHaveBeenCalledTimes(1);
      expect(mocks.fetchPushConfiguration).toHaveBeenCalledTimes(1);
      expect(result.current.preferences).not.toBeNull();
      expect(result.current.isPreferencesLoading).toBe(false);

      await act(async () => {
        result.current.ensureSettingsLoaded();
      });
      expect(mocks.fetchNotificationPreferences).toHaveBeenCalledTimes(1);
    });

    it('marks preferences as loading in the same update that requests them (no empty flash)', async () => {
      let resolvePreferences: (value: unknown) => void = () => {};
      mocks.fetchNotificationPreferences.mockReturnValue(new Promise((resolve) => { resolvePreferences = resolve; }));
      const { result } = renderHook(() => useNotificationCenter(), { wrapper });
      await act(async () => {});

      await act(async () => {
        result.current.ensureSettingsLoaded();
      });
      expect(result.current.isPreferencesLoading).toBe(true);
      expect(result.current.preferences).toBeNull();

      await act(async () => {
        resolvePreferences({ timezone: 'UTC', eventPreferences: {}, legacyLearnerSettings: {} });
      });
      await act(async () => {});
      expect(result.current.isPreferencesLoading).toBe(false);
      expect(result.current.preferences).not.toBeNull();
    });
  });

  describe('hub connection re-entrancy', () => {
    it('does not open a second hub when focus and visibilitychange fire while the first is still connecting', async () => {
      renderHook(() => useNotificationCenter(), { wrapper });

      // Fired before the dynamic import has even resolved.
      window.dispatchEvent(new Event('focus'));
      document.dispatchEvent(new Event('visibilitychange'));
      await act(async () => {});

      // Fired again once the first hub exists and is still in state 'Connecting'.
      window.dispatchEvent(new Event('focus'));
      document.dispatchEvent(new Event('visibilitychange'));
      await act(async () => {});

      expect(mocks.hub.builds).toBe(1);
    });

    it('skips the reconnect attempt while the tab is hidden', async () => {
      mocks.hub.start = () => Promise.reject(new Error('offline'));
      renderHook(() => useNotificationCenter(), { wrapper });
      await act(async () => {});
      expect(mocks.hub.builds).toBe(1);

      visibility = 'hidden';
      window.dispatchEvent(new Event('focus'));
      document.dispatchEvent(new Event('visibilitychange'));
      await act(async () => {});

      expect(mocks.hub.builds).toBe(1);
    });
  });

  describe('fallback poll', () => {
    it('pauses while the document is hidden and catches up as soon as it is visible', async () => {
      // No hub in this mode (automation), so the 30 s fallback poll is what keeps the feed fresh.
      setNavigatorWebdriver(true);
      vi.useFakeTimers();
      renderHook(() => useNotificationCenter(), { wrapper });
      await act(async () => {});
      expect(mocks.fetchNotifications).toHaveBeenCalledTimes(1);

      visibility = 'hidden';
      await act(async () => {
        vi.advanceTimersByTime(90_000);
      });
      expect(mocks.fetchNotifications).toHaveBeenCalledTimes(1);

      visibility = 'visible';
      await act(async () => {
        document.dispatchEvent(new Event('visibilitychange'));
      });
      expect(mocks.fetchNotifications).toHaveBeenCalledTimes(2);

      await act(async () => {
        vi.advanceTimersByTime(30_000);
      });
      expect(mocks.fetchNotifications).toHaveBeenCalledTimes(3);
    });
  });
});
