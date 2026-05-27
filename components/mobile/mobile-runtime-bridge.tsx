'use client';

import { useCallback, useEffect, useRef } from 'react';
import { useRouter } from 'next/navigation';
import { Capacitor } from '@capacitor/core';
import { toast } from 'sonner';
import { useAuth } from '@/contexts/auth-context';
import { initializeMobileRuntime } from '@/lib/mobile/runtime';
import { triggerResumeMotion } from '@/lib/mobile/lifecycle-motion';
import { registerPushNotifications } from '@/lib/mobile/push-notifications';
import { initializeDeepLinkHandler } from '@/lib/mobile/deep-link-handler';
import { removeAllDeliveredNotifications } from '@/lib/mobile/push-notifications';
import { registerNativePushToken } from '@/lib/notifications-api';
import type { NativePushPlatform } from '@/lib/types/notifications';

function currentPushPlatform(): NativePushPlatform {
  const platform = Capacitor.getPlatform();
  return platform === 'android' || platform === 'ios' ? platform : 'web';
}

/* ── Billing push event routing (Wave B3) ─────────────────────────
 *
 * Push payloads from the backend ship `data.kind === 'billing.event'`
 * plus a `data.event` discriminator. We surface a toast and, where the
 * event implies user follow-through, navigate to the matching surface.
 */

type BillingEvent =
  | 'payment.success'
  | 'payment.failed'
  | 'subscription.renewing'
  | 'credits.low';

interface BillingRouting {
  message: string;
  variant: 'success' | 'error' | 'info';
  navigateTo: string | null;
}

const BILLING_ROUTING: Record<BillingEvent, BillingRouting> = {
  'payment.success': {
    message: 'Payment received — your credits are ready.',
    variant: 'success',
    navigateTo: '/account/billing?paid=1',
  },
  'payment.failed': {
    message: "We couldn't process your payment. Update your payment method to keep your access.",
    variant: 'error',
    navigateTo: '/account/billing/payment-methods',
  },
  'subscription.renewing': {
    message: 'Heads up — your subscription renews soon.',
    variant: 'info',
    navigateTo: null,
  },
  'credits.low': {
    message: "You're running low on credits. Top up to keep practising.",
    variant: 'info',
    navigateTo: '/pricing',
  },
};

function isBillingEventKind(value: string | undefined): value is BillingEvent {
  return (
    value === 'payment.success' ||
    value === 'payment.failed' ||
    value === 'subscription.renewing' ||
    value === 'credits.low'
  );
}

/* ── Writing module push routing (WS7 — additive) ──────────────────
 *
 * Writing pushes ship `data.kind === 'writing.event'` plus a
 * `data.event` discriminator. Foreground deliveries surface a toast
 * (no auto-navigation); user-action deliveries (notification tap)
 * navigate to the right surface.
 *
 * Deep links: writing://submissions/{id}/results, writing://today,
 * writing://mocks are normalised to /writing/* paths before navigation.
 *
 * Supported events:
 *   - writing.daily-plan-ready    → /writing/today
 *   - writing.grade-ready         → /writing/submissions/{submissionId}/results
 *   - writing.coach-hint          → toast (no nav; ephemeral)
 *   - writing.mock-reminder       → /writing/mocks
 */

type WritingEvent =
  | 'writing.daily-plan-ready'
  | 'writing.grade-ready'
  | 'writing.coach-hint'
  | 'writing.mock-reminder';

interface WritingPushPayload {
  kind?: string;
  event?: string;
  message?: string;
  submissionId?: string;
  sessionId?: string;
}

function isWritingEventKind(value: string | undefined): value is WritingEvent {
  return (
    value === 'writing.daily-plan-ready' ||
    value === 'writing.grade-ready' ||
    value === 'writing.coach-hint' ||
    value === 'writing.mock-reminder'
  );
}

function writingDeepLinkToRoute(value: unknown): string | null {
  if (typeof value !== 'string') return null;
  if (!value.startsWith('writing://')) return null;
  const rest = value.slice('writing://'.length);
  if (!rest || rest.startsWith('/')) return null;
  return `/writing/${rest}`;
}

function handleWritingPush(
  data: Record<string, string>,
  navigate: (path: string) => void,
): boolean {
  const payload = data as WritingPushPayload;
  if (payload.kind !== 'writing.event') return false;
  if (!isWritingEventKind(payload.event)) return false;

  switch (payload.event) {
    case 'writing.daily-plan-ready':
      toast(payload.message?.trim() ? payload.message : "Today's Writing plan is ready.");
      navigate('/writing/today');
      return true;
    case 'writing.grade-ready': {
      const id = payload.submissionId?.trim();
      toast.success(payload.message?.trim() ? payload.message : 'Your letter has been graded.');
      if (id) navigate(`/writing/submissions/${encodeURIComponent(id)}/results`);
      else navigate('/writing');
      return true;
    }
    case 'writing.coach-hint':
      // Coach hints are ephemeral — show a toast, do not navigate.
      toast(payload.message?.trim() ? payload.message : 'Coach has a new hint for you.');
      return true;
    case 'writing.mock-reminder':
      toast(payload.message?.trim() ? payload.message : 'Time to take your scheduled mock.');
      navigate('/writing/mocks');
      return true;
    default:
      return false;
  }
}

interface BillingPushPayload {
  kind?: string;
  event?: string;
  message?: string;
}

function handleBillingPush(
  data: Record<string, string>,
  navigate: (path: string) => void,
): boolean {
  const payload = data as BillingPushPayload;
  if (payload.kind !== 'billing.event') return false;
  if (!isBillingEventKind(payload.event)) return false;

  const routing = BILLING_ROUTING[payload.event];
  const message = payload.message?.trim().length ? payload.message : routing.message;

  switch (routing.variant) {
    case 'success':
      toast.success(message);
      break;
    case 'error':
      toast.error(message);
      break;
    default:
      toast(message);
      break;
  }

  if (routing.navigateTo) {
    navigate(routing.navigateTo);
  }
  return true;
}

function toInternalRoute(value: unknown): string | null {
  if (typeof value !== 'string') return null;
  const trimmed = value.trim();
  if (!trimmed || trimmed.startsWith('//') || /[\u0000-\u001f\u007f]/.test(trimmed)) return null;
  if (trimmed.startsWith('/') && !trimmed.startsWith('/\\')) return trimmed;

  try {
    const url = new URL(trimmed);
    if (url.protocol !== 'https:') return null;
    if (url.hostname !== 'app.oetwithdrhesham.co.uk') return null;
    const route = `${url.pathname}${url.search}${url.hash}`;
    if (!route || route.startsWith('//') || route.startsWith('/\\') || /[\u0000-\u001f\u007f]/.test(route)) return null;
    return route;
  } catch {
    return null;
  }
}

export function MobileRuntimeBridge() {
  const { refreshSession, isAuthenticated, loading } = useAuth();
  const router = useRouter();
  const authStateRef = useRef({ isAuthenticated, loading, refreshSession });
  const lastNativePushTokenRef = useRef<string | null>(null);

  useEffect(() => {
    authStateRef.current = { isAuthenticated, loading, refreshSession };
  }, [isAuthenticated, loading, refreshSession]);

  const sendNativePushToken = useCallback(async () => {
    const token = lastNativePushTokenRef.current;
    const currentState = authStateRef.current;
    if (!token || currentState.loading || !currentState.isAuthenticated) {
      return;
    }

    try {
      await registerNativePushToken({
        token,
        platform: currentPushPlatform(),
      });
    } catch (error) {
      console.warn('[push] Failed to register push token with backend', error);
    }
  }, []);

  useEffect(() => {
    void sendNativePushToken();
  }, [isAuthenticated, loading, sendNativePushToken]);

  useEffect(() => {
    let cancelled = false;
    const cleanupFns: Array<() => void> = [];

    void (async () => {
      // Core runtime initialization
      const runtimeCleanup = await initializeMobileRuntime({
        onResume: () => {
          triggerResumeMotion();
          const currentState = authStateRef.current;
          if (!currentState.loading && currentState.isAuthenticated) {
            void currentState.refreshSession();
          }
          // Clear badge notifications when app is resumed
          void removeAllDeliveredNotifications();
        },
      });
      cleanupFns.push(runtimeCleanup);

      if (cancelled) {
        runtimeCleanup();
        return;
      }

      // Push notification registration
      const pushCleanup = await registerPushNotifications({
        onRegistration: async (token) => {
          console.debug('[push] Registered with token:', token.value.slice(0, 8) + '...');
          lastNativePushTokenRef.current = token.value;
          await sendNativePushToken();
        },
        onNotificationReceived: (notification) => {
          // Foreground delivery — surface billing toasts but do NOT auto-navigate
          // since the user hasn't acknowledged the notification yet.
          if (notification.data.kind === 'billing.event') {
            handleBillingPush(notification.data, () => {
              /* foreground: skip navigation */
            });
            return;
          }
          if (notification.data.kind === 'writing.event') {
            handleWritingPush(notification.data, () => {
              /* foreground: skip navigation */
            });
          }
        },
        onNotificationActionPerformed: (notification) => {
          // First check the billing routing matrix (Wave B3); falls through
          // to the legacy route handler if not a billing event.
          if (handleBillingPush(notification.data, (path) => router.push(path))) {
            return;
          }

          // Writing module routing (WS7).
          if (handleWritingPush(notification.data, (path) => router.push(path))) {
            return;
          }

          // Navigate to a route if the push payload includes one — accept
          // writing:// deep links by normalising to /writing/* paths.
          const writingDeep = writingDeepLinkToRoute(notification.data.route ?? notification.data.actionUrl);
          if (writingDeep) {
            router.push(writingDeep);
            return;
          }
          const route = toInternalRoute(notification.data.route ?? notification.data.actionUrl);
          if (route) {
            router.push(route);
          }
        },
      });
      cleanupFns.push(pushCleanup);

      if (cancelled) {
        cleanupFns.forEach((fn) => fn());
        return;
      }

      // Deep link handler — accept https://app.* deep links plus writing://
      // app-scheme deep links (writing://submissions/{id}/results, etc.).
      const deepLinkCleanup = await initializeDeepLinkHandler({
        onDeepLink: (event) => {
          // If the path looks like a writing:// fragment passed verbatim
          // (no leading slash, e.g. "submissions/abc/results"), prefix
          // /writing/. Otherwise honour the path as-is.
          const path = event.path.startsWith('/') ? event.path : `/writing/${event.path}`;
          router.push(path + (Object.keys(event.queryParams).length > 0
            ? '?' + new URLSearchParams(event.queryParams).toString()
            : ''));
        },
      });
      cleanupFns.push(deepLinkCleanup);

      if (cancelled) {
        cleanupFns.forEach((fn) => fn());
      }
    })();

    return () => {
      cancelled = true;
      cleanupFns.forEach((fn) => fn());
    };
  }, [router, sendNativePushToken]);

  return null;
}
