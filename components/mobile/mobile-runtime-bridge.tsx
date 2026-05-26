'use client';

import { useCallback, useEffect, useRef } from 'react';
import { useRouter } from 'next/navigation';
import { Capacitor } from '@capacitor/core';
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
        onNotificationActionPerformed: (notification) => {
          // Navigate to a route if the push payload includes one
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

      // Deep link handler
      const deepLinkCleanup = await initializeDeepLinkHandler({
        onDeepLink: (event) => {
          router.push(event.path + (Object.keys(event.queryParams).length > 0
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
