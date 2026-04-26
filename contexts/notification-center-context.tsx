'use client';

import { HubConnection, HubConnectionBuilder, HttpTransportType, LogLevel } from '@microsoft/signalr';
import { Toast } from '@/components/ui/alert';
import { ensureFreshAccessToken } from '@/lib/auth-client';
import { env } from '@/lib/env';
import {
  createPushSubscription,
  deletePushSubscription,
  fetchNotificationPreferences,
  fetchNotifications,
  markAllNotificationsRead,
  markNotificationRead,
  updateNotificationPreferences,
} from '@/lib/notifications-api';
import type {
  NotificationFeedItem,
  NotificationPreferencePatchRequest,
  NotificationPreferencePayload,
  NotificationRealtimeEnvelope,
  PushSubscriptionPayload,
} from '@/lib/types/notifications';
import { createContext, useCallback, useContext, useEffect, useMemo, useRef, useState, type ReactNode } from 'react';
import { useAuth } from './auth-context';

type ToastVariant = 'info' | 'success' | 'warning' | 'error';
type NotificationConnectionStatus = 'connecting' | 'connected' | 'reconnecting' | 'disconnected';
type PushPermissionState = NotificationPermission | 'unsupported';

interface NotificationCenterContextValue {
  notifications: NotificationFeedItem[];
  unreadCount: number;
  totalCount: number;
  hasMore: boolean;
  isLoading: boolean;
  isRefreshing: boolean;
  error: string | null;
  connectionStatus: NotificationConnectionStatus;
  preferences: NotificationPreferencePayload | null;
  isPreferencesLoading: boolean;
  preferencesError: string | null;
  isUpdatingPreferences: boolean;
  pushSupported: boolean;
  pushPublicKeyConfigured: boolean;
  pushPermission: PushPermissionState;
  pushEnabled: boolean;
  isUpdatingPush: boolean;
  refreshFeed: (options?: { reset?: boolean; silent?: boolean }) => Promise<void>;
  loadMore: () => Promise<void>;
  markRead: (notificationId: string) => Promise<void>;
  markAllRead: () => Promise<void>;
  updatePreferences: (payload: NotificationPreferencePatchRequest) => Promise<NotificationPreferencePayload>;
  subscribeToPush: () => Promise<void>;
  unsubscribeFromPush: () => Promise<void>;
}

/**
 * Split contexts so consumers can subscribe to just state OR just actions.
 * Action identities are stable (wrapped in useCallback), so components that
 * only need actions never re-render on state updates.
 */
type NotificationActionsValue = Pick<
  NotificationCenterContextValue,
  'refreshFeed' | 'loadMore' | 'markRead' | 'markAllRead' | 'updatePreferences' | 'subscribeToPush' | 'unsubscribeFromPush'
>;
type NotificationStateValue = Omit<NotificationCenterContextValue, keyof NotificationActionsValue>;

const NotificationCenterContext = createContext<NotificationCenterContextValue | null>(null);
const NotificationStateContext = createContext<NotificationStateValue | null>(null);
const NotificationActionsContext = createContext<NotificationActionsValue | null>(null);

const PAGE_SIZE = 20;
const POLL_INTERVAL_MS = 30_000;
const PUSH_STORAGE_KEY = 'oet.notification.push-registration';
// Same-origin `/api/backend` is implemented via a Next route handler, which can proxy HTTP
// requests but not a SignalR WebSocket upgrade. Fall back to long polling in that topology.
const PROXY_SAFE_SIGNALR_TRANSPORT = env.apiBaseUrl.startsWith('/')
  ? HttpTransportType.LongPolling
  : HttpTransportType.WebSockets;

interface StoredPushRegistration {
  endpoint: string;
  subscriptionId: string;
}

function normalizeError(error: unknown, fallback: string): string {
  if (error instanceof Error && error.message.trim()) {
    return error.message;
  }

  return fallback;
}

function mergeFeedItems(existingItems: NotificationFeedItem[], incomingItems: NotificationFeedItem[]) {
  const itemMap = new Map<string, NotificationFeedItem>();
  for (const item of existingItems) {
    itemMap.set(item.id, item);
  }
  for (const item of incomingItems) {
    itemMap.set(item.id, item);
  }

  return Array.from(itemMap.values()).sort(
    (left, right) => Date.parse(right.createdAt) - Date.parse(left.createdAt),
  );
}

function clonePreferences(preferences: NotificationPreferencePayload): NotificationPreferencePayload {
  return {
    ...preferences,
    eventPreferences: Object.fromEntries(
      Object.entries(preferences.eventPreferences).map(([eventKey, eventPreference]) => [
        eventKey,
        { ...eventPreference },
      ]),
    ),
    legacyLearnerSettings: { ...preferences.legacyLearnerSettings },
  };
}

function getPushPermission(): PushPermissionState {
  if (typeof window === 'undefined' || typeof Notification === 'undefined') {
    return 'unsupported';
  }

  return Notification.permission;
}

function severityToToastVariant(severity: NotificationFeedItem['severity']): ToastVariant {
  switch (severity) {
    case 'success':
      return 'success';
    case 'warning':
      return 'warning';
    case 'critical':
      return 'error';
    default:
      return 'info';
  }
}

function readStoredPushRegistration(): StoredPushRegistration | null {
  if (typeof window === 'undefined') {
    return null;
  }

  const rawValue = window.localStorage.getItem(PUSH_STORAGE_KEY);
  if (!rawValue) {
    return null;
  }

  try {
    const parsedValue = JSON.parse(rawValue) as StoredPushRegistration;
    if (!parsedValue.endpoint || !parsedValue.subscriptionId) {
      return null;
    }
    return parsedValue;
  } catch {
    return null;
  }
}

function writeStoredPushRegistration(value: StoredPushRegistration | null) {
  if (typeof window === 'undefined') {
    return;
  }

  if (!value) {
    window.localStorage.removeItem(PUSH_STORAGE_KEY);
    return;
  }

  window.localStorage.setItem(PUSH_STORAGE_KEY, JSON.stringify(value));
}

function base64UrlToArrayBuffer(value: string): ArrayBuffer {
  const paddedValue = `${value}${'='.repeat((4 - (value.length % 4 || 4)) % 4)}`
    .replace(/-/g, '+')
    .replace(/_/g, '/');
  const rawData = window.atob(paddedValue);
  const bytes = Uint8Array.from(rawData, (character) => character.charCodeAt(0));
  return bytes.buffer.slice(0);
}

async function ensureNotificationWorkerRegistered(): Promise<ServiceWorkerRegistration | null> {
  if (typeof window === 'undefined' || !('serviceWorker' in navigator)) {
    return null;
  }

  try {
    return await navigator.serviceWorker.register('/notification-worker.js', { scope: '/' });
  } catch {
    return null;
  }
}

function toPushPayload(subscription: PushSubscription): PushSubscriptionPayload | null {
  const rawKey = subscription.getKey('p256dh');
  const authKey = subscription.getKey('auth');
  if (!rawKey || !authKey) {
    return null;
  }

  const toBase64 = (arrayBuffer: ArrayBuffer) => {
    const bytes = new Uint8Array(arrayBuffer);
    let binary = '';
    bytes.forEach((byte) => {
      binary += String.fromCharCode(byte);
    });
    return window.btoa(binary);
  };

  return {
    endpoint: subscription.endpoint,
    p256dh: toBase64(rawKey),
    auth: toBase64(authKey),
    expiresAt: subscription.expirationTime ? new Date(subscription.expirationTime).toISOString() : null,
    userAgent: navigator.userAgent,
  };
}

export function NotificationCenterProvider({ children }: { children: ReactNode }) {
  const { isAuthenticated, loading } = useAuth();
  const [notifications, setNotifications] = useState<NotificationFeedItem[]>([]);
  const [unreadCount, setUnreadCount] = useState(0);
  const [totalCount, setTotalCount] = useState(0);
  const [page, setPage] = useState(1);
  const [isLoading, setIsLoading] = useState(true);
  const [isRefreshing, setIsRefreshing] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [connectionStatus, setConnectionStatus] = useState<NotificationConnectionStatus>('disconnected');
  const [preferences, setPreferences] = useState<NotificationPreferencePayload | null>(null);
  const [isPreferencesLoading, setIsPreferencesLoading] = useState(true);
  const [preferencesError, setPreferencesError] = useState<string | null>(null);
  const [isUpdatingPreferences, setIsUpdatingPreferences] = useState(false);
  const [pushPermission, setPushPermission] = useState<PushPermissionState>(getPushPermission());
  const [pushEnabled, setPushEnabled] = useState(false);
  const [isUpdatingPush, setIsUpdatingPush] = useState(false);
  const [toastState, setToastState] = useState<{ message: string; variant: ToastVariant } | null>(null);
  const hubConnectionRef = useRef<HubConnection | null>(null);

  const pushSupported =
    typeof window !== 'undefined'
    && 'serviceWorker' in navigator
    && 'PushManager' in window
    && pushPermission !== 'unsupported';
  const pushPublicKeyConfigured = Boolean(env.webPushPublicKey);

  const refreshFeed = useCallback(async (options?: { reset?: boolean; silent?: boolean }) => {
    if (!isAuthenticated) {
      return;
    }

    if (!options?.silent) {
      setIsRefreshing(true);
      if (options?.reset) {
        setIsLoading(true);
      }
    }

    try {
      const response = await fetchNotifications({ page: 1, pageSize: PAGE_SIZE });
      setNotifications((current) => (options?.reset ? response.items : mergeFeedItems(current, response.items)));
      setUnreadCount(response.unreadCount);
      setTotalCount(response.totalCount);
      setPage(response.page);
      setError(null);
    } catch (refreshError) {
      if (!options?.silent) {
        setError(normalizeError(refreshError, 'Unable to load notifications.'));
      }
    } finally {
      if (!options?.silent) {
        setIsLoading(false);
        setIsRefreshing(false);
      }
    }
  }, [isAuthenticated]);

  const loadMore = useCallback(async () => {
    if (!isAuthenticated || notifications.length >= totalCount || isRefreshing) {
      return;
    }

    setIsRefreshing(true);
    try {
      const nextPage = page + 1;
      const response = await fetchNotifications({ page: nextPage, pageSize: PAGE_SIZE });
      setNotifications((current) => mergeFeedItems(current, response.items));
      setUnreadCount(response.unreadCount);
      setTotalCount(response.totalCount);
      setPage(nextPage);
    } catch (loadMoreError) {
      setError(normalizeError(loadMoreError, 'Unable to load more notifications.'));
    } finally {
      setIsRefreshing(false);
    }
  }, [isAuthenticated, isRefreshing, notifications.length, page, totalCount]);

  const loadPreferences = useCallback(async () => {
    if (!isAuthenticated) {
      return;
    }

    setIsPreferencesLoading(true);
    try {
      const response = await fetchNotificationPreferences();
      setPreferences(response);
      setPreferencesError(null);
    } catch (loadError) {
      setPreferencesError(normalizeError(loadError, 'Unable to load notification preferences.'));
    } finally {
      setIsPreferencesLoading(false);
    }
  }, [isAuthenticated]);

  const syncPushRegistration = useCallback(async () => {
    if (!isAuthenticated || !pushSupported || !pushPublicKeyConfigured || getPushPermission() !== 'granted') {
      setPushEnabled(false);
      return;
    }

    const registration = await ensureNotificationWorkerRegistered();
    const subscription = await registration?.pushManager.getSubscription();
    if (!subscription) {
      setPushEnabled(false);
      writeStoredPushRegistration(null);
      return;
    }

    const payload = toPushPayload(subscription);
    if (!payload) {
      setPushEnabled(false);
      return;
    }

    const response = await createPushSubscription(payload);
    writeStoredPushRegistration({
      endpoint: payload.endpoint,
      subscriptionId: response.subscriptionId,
    });
    setPushEnabled(true);
  }, [isAuthenticated, pushPublicKeyConfigured, pushSupported]);

  const subscribeToPush = useCallback(async () => {
    if (!pushSupported) {
      throw new Error('Browser push is not supported in this browser.');
    }
    if (!pushPublicKeyConfigured) {
      throw new Error('Browser push is not configured for this environment yet.');
    }

    setIsUpdatingPush(true);
    try {
      const permission = await Notification.requestPermission();
      setPushPermission(permission);
      if (permission !== 'granted') {
        setPushEnabled(false);
        return;
      }

      const registration = await ensureNotificationWorkerRegistered();
      if (!registration) {
        throw new Error('Unable to register the browser notification worker.');
      }

      const existingSubscription = await registration.pushManager.getSubscription();
      const subscription = existingSubscription
        ?? await registration.pushManager.subscribe({
          userVisibleOnly: true,
          applicationServerKey: base64UrlToArrayBuffer(env.webPushPublicKey),
        });

      const payload = toPushPayload(subscription);
      if (!payload) {
        throw new Error('The browser returned an invalid push subscription.');
      }

      const response = await createPushSubscription(payload);
      writeStoredPushRegistration({
        endpoint: payload.endpoint,
        subscriptionId: response.subscriptionId,
      });
      setPushEnabled(true);
      setToastState({ message: 'Browser push has been enabled.', variant: 'success' });
    } catch (subscriptionError) {
      setToastState({
        message: normalizeError(subscriptionError, 'Unable to enable browser push.'),
        variant: 'error',
      });
      throw subscriptionError;
    } finally {
      setIsUpdatingPush(false);
    }
  }, [pushPublicKeyConfigured, pushSupported]);

  const unsubscribeFromPush = useCallback(async () => {
    if (!pushSupported) {
      return;
    }

    setIsUpdatingPush(true);
    try {
      const registration = await ensureNotificationWorkerRegistered();
      const subscription = await registration?.pushManager.getSubscription();
      const storedRegistration = readStoredPushRegistration();

      if (storedRegistration?.subscriptionId) {
        await deletePushSubscription(storedRegistration.subscriptionId);
      }

      await subscription?.unsubscribe();
      writeStoredPushRegistration(null);
      setPushEnabled(false);
      setToastState({ message: 'Browser push has been disabled.', variant: 'info' });
    } catch (unsubscribeError) {
      setToastState({
        message: normalizeError(unsubscribeError, 'Unable to disable browser push.'),
        variant: 'error',
      });
      throw unsubscribeError;
    } finally {
      setIsUpdatingPush(false);
    }
  }, [pushSupported]);

  const markRead = useCallback(async (notificationId: string) => {
    await markNotificationRead(notificationId);
    setNotifications((current) => current.map((item) => (
      item.id === notificationId
        ? { ...item, isRead: true, readAt: item.readAt ?? new Date().toISOString() }
        : item
    )));
    setUnreadCount((current) => Math.max(0, current - 1));
  }, []);

  const markAllRead = useCallback(async () => {
    await markAllNotificationsRead();
    const now = new Date().toISOString();
    setNotifications((current) => current.map((item) => ({ ...item, isRead: true, readAt: item.readAt ?? now })));
    setUnreadCount(0);
  }, []);

  const handleRealtimeEnvelope = useCallback((envelope: NotificationRealtimeEnvelope) => {
    let isNewItem = false;
    setNotifications((current) => {
      isNewItem = !current.some((item) => item.id === envelope.notification.id);
      return mergeFeedItems(current, [envelope.notification]);
    });
    setUnreadCount(envelope.unreadCount);
    setTotalCount((current) => (isNewItem ? current + 1 : current));

    if (typeof document !== 'undefined' && document.visibilityState === 'visible' && isNewItem) {
      setToastState({
        message: envelope.notification.title,
        variant: severityToToastVariant(envelope.notification.severity),
      });
    }
  }, []);

  const updatePreferencesHandler = useCallback(async (payload: NotificationPreferencePatchRequest) => {
    setIsUpdatingPreferences(true);
    try {
      const response = await updateNotificationPreferences(payload);
      setPreferences(response);
      setPreferencesError(null);
      return response;
    } catch (updateError) {
      setPreferencesError(normalizeError(updateError, 'Unable to update notification preferences.'));
      throw updateError;
    } finally {
      setIsUpdatingPreferences(false);
    }
  }, []);

  useEffect(() => {
    if (loading) {
      return;
    }

    if (!isAuthenticated) {
      setNotifications([]);
      setUnreadCount(0);
      setTotalCount(0);
      setPage(1);
      setIsLoading(false);
      setError(null);
      setPreferences(null);
      setIsPreferencesLoading(false);
      setPreferencesError(null);
      setConnectionStatus('disconnected');
      setPushPermission(getPushPermission());
      setPushEnabled(false);
      writeStoredPushRegistration(null);
      return;
    }

    void Promise.all([
      refreshFeed({ reset: true }),
      loadPreferences(),
    ]);
  }, [isAuthenticated, loadPreferences, loading, refreshFeed]);

  useEffect(() => {
    if (!isAuthenticated) {
      return;
    }

    if (typeof navigator !== 'undefined' && navigator.webdriver) {
      setConnectionStatus('disconnected');
      return;
    }

    let disposed = false;
    const connection = new HubConnectionBuilder()
      .withUrl(`${env.apiBaseUrl}/v1/notifications/hub`, {
        accessTokenFactory: async () => (await ensureFreshAccessToken()) ?? '',
        transport: PROXY_SAFE_SIGNALR_TRANSPORT,
      })
      .configureLogging(LogLevel.None)
      .withAutomaticReconnect([0, 2_000, 5_000, 10_000])
      .build();

    hubConnectionRef.current = connection;
    setConnectionStatus('connecting');
    connection.on('notification', (envelope: NotificationRealtimeEnvelope) => {
      handleRealtimeEnvelope(envelope);
    });
    connection.onreconnecting(() => {
      setConnectionStatus('reconnecting');
    });
    connection.onreconnected(() => {
      setConnectionStatus('connected');
      void refreshFeed({ silent: true });
    });
    connection.onclose(() => {
      if (!disposed) {
        setConnectionStatus('disconnected');
      }
    });

    void connection.start()
      .then(() => {
        if (!disposed) {
          setConnectionStatus('connected');
        }
      })
      .catch(() => {
        if (!disposed) {
          setConnectionStatus('disconnected');
        }
      });

    return () => {
      disposed = true;
      hubConnectionRef.current = null;
      void connection.stop();
    };
  }, [handleRealtimeEnvelope, isAuthenticated, refreshFeed]);

  useEffect(() => {
    if (!isAuthenticated || connectionStatus === 'connected') {
      return;
    }

    const interval = window.setInterval(() => {
      if (typeof document !== 'undefined' && document.visibilityState !== 'visible') {
        return;
      }
      void refreshFeed({ silent: true });
    }, POLL_INTERVAL_MS);

    return () => {
      window.clearInterval(interval);
    };
  }, [connectionStatus, isAuthenticated, refreshFeed]);

  useEffect(() => {
    setPushPermission(getPushPermission());
    // Defer push registration sync past first paint — it touches localStorage,
    // service worker APIs and makes a network request, none of which are critical
    // for initial render. Use requestIdleCallback when available, fall back to setTimeout.
    const win = typeof window !== 'undefined' ? window : null;
    const idleCb = win && 'requestIdleCallback' in win
      ? (cb: () => void) => (win as unknown as { requestIdleCallback: (cb: () => void, opts?: { timeout: number }) => number }).requestIdleCallback(cb, { timeout: 2000 })
      : (cb: () => void) => window.setTimeout(cb, 0);
    const cancelIdle = win && 'cancelIdleCallback' in win
      ? (id: number) => (win as unknown as { cancelIdleCallback: (id: number) => void }).cancelIdleCallback(id)
      : (id: number) => window.clearTimeout(id);
    const handle = idleCb(() => {
      void syncPushRegistration().catch(() => {
        setPushEnabled(false);
      });
    });
    return () => cancelIdle(handle as number);
  }, [syncPushRegistration]);

  const contextValue = useMemo<NotificationCenterContextValue>(() => ({
    notifications,
    unreadCount,
    totalCount,
    hasMore: notifications.length < totalCount,
    isLoading,
    isRefreshing,
    error,
    connectionStatus,
    preferences,
    isPreferencesLoading,
    preferencesError,
    isUpdatingPreferences,
    pushSupported,
    pushPublicKeyConfigured,
    pushPermission,
    pushEnabled,
    isUpdatingPush,
    refreshFeed,
    loadMore,
    markRead,
    markAllRead,
    updatePreferences: updatePreferencesHandler,
    subscribeToPush,
    unsubscribeFromPush,
  }), [
    connectionStatus,
    error,
    isLoading,
    isPreferencesLoading,
    isRefreshing,
    isUpdatingPreferences,
    isUpdatingPush,
    loadMore,
    markAllRead,
    markRead,
    notifications,
    preferences,
    preferencesError,
    pushEnabled,
    pushPermission,
    pushPublicKeyConfigured,
    pushSupported,
    refreshFeed,
    subscribeToPush,
    totalCount,
    unreadCount,
    updatePreferencesHandler,
    unsubscribeFromPush,
  ]);

  // State-only value: excludes actions, so consumers that only read state
  // (e.g. the bell badge) don't re-render when action refs are reconstructed.
  const stateValue = useMemo<NotificationStateValue>(() => ({
    notifications,
    unreadCount,
    totalCount,
    hasMore: notifications.length < totalCount,
    isLoading,
    isRefreshing,
    error,
    connectionStatus,
    preferences,
    isPreferencesLoading,
    preferencesError,
    isUpdatingPreferences,
    pushSupported,
    pushPublicKeyConfigured,
    pushPermission,
    pushEnabled,
    isUpdatingPush,
  }), [
    connectionStatus,
    error,
    isLoading,
    isPreferencesLoading,
    isRefreshing,
    isUpdatingPreferences,
    isUpdatingPush,
    notifications,
    preferences,
    preferencesError,
    pushEnabled,
    pushPermission,
    pushPublicKeyConfigured,
    pushSupported,
    totalCount,
    unreadCount,
  ]);

  // Actions-only value: all fields are useCallback-stable, so this memo
  // effectively never changes identity during a session — components that
  // only fire actions will never re-render.
  const actionsValue = useMemo<NotificationActionsValue>(() => ({
    refreshFeed,
    loadMore,
    markRead,
    markAllRead,
    updatePreferences: updatePreferencesHandler,
    subscribeToPush,
    unsubscribeFromPush,
  }), [refreshFeed, loadMore, markRead, markAllRead, updatePreferencesHandler, subscribeToPush, unsubscribeFromPush]);

  return (
    <NotificationCenterContext.Provider value={contextValue}>
      <NotificationStateContext.Provider value={stateValue}>
        <NotificationActionsContext.Provider value={actionsValue}>
          {children}
          {toastState ? (
            <Toast
              variant={toastState.variant}
              message={toastState.message}
              onClose={() => setToastState(null)}
            />
          ) : null}
        </NotificationActionsContext.Provider>
      </NotificationStateContext.Provider>
    </NotificationCenterContext.Provider>
  );
}

export function useNotificationCenter() {
  const context = useContext(NotificationCenterContext);
  if (!context) {
    throw new Error('useNotificationCenter must be used within NotificationCenterProvider');
  }

  return context;
}

/**
 * Subscribe to notification state only (no actions). Prefer this in components
 * that do not call any mutation — the bell badge, small state chips, etc.
 */
export function useNotificationState() {
  const context = useContext(NotificationStateContext);
  if (!context) {
    throw new Error('useNotificationState must be used within NotificationCenterProvider');
  }
  return context;
}

export function cloneNotificationPreferences(preferences: NotificationPreferencePayload | null) {
  return preferences ? clonePreferences(preferences) : null;
}
