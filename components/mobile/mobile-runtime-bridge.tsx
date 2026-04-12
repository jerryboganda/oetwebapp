'use client';

import { useEffect, useRef } from 'react';
import { useRouter } from 'next/navigation';
import { useAuth } from '@/contexts/auth-context';
import { initializeMobileRuntime } from '@/lib/mobile/runtime';
import { triggerResumeMotion } from '@/lib/mobile/lifecycle-motion';
import { registerPushNotifications } from '@/lib/mobile/push-notifications';
import { initializeDeepLinkHandler } from '@/lib/mobile/deep-link-handler';
import { removeAllDeliveredNotifications } from '@/lib/mobile/push-notifications';

export function MobileRuntimeBridge() {
  const { refreshSession, isAuthenticated, loading } = useAuth();
  const router = useRouter();
  const authStateRef = useRef({ isAuthenticated, loading, refreshSession });

  useEffect(() => {
    authStateRef.current = { isAuthenticated, loading, refreshSession };
  }, [isAuthenticated, loading, refreshSession]);

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
        onRegistration: (token) => {
          // TODO: Send token to backend for push targeting
          console.debug('[push] Registered with token:', token.value.slice(0, 8) + '...');
        },
        onNotificationActionPerformed: (notification) => {
          // Navigate to a route if the push payload includes one
          const route = notification.data.route;
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
  }, [router]);

  return null;
}