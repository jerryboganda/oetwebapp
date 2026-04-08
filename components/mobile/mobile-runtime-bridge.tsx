'use client';

import { useEffect, useRef } from 'react';
import { useAuth } from '@/contexts/auth-context';
import { initializeMobileRuntime } from '@/lib/mobile/runtime';
import { triggerResumeMotion } from '@/lib/mobile/lifecycle-motion';

export function MobileRuntimeBridge() {
  const { refreshSession, isAuthenticated, loading } = useAuth();
  const authStateRef = useRef({ isAuthenticated, loading, refreshSession });

  useEffect(() => {
    authStateRef.current = { isAuthenticated, loading, refreshSession };
  }, [isAuthenticated, loading, refreshSession]);

  useEffect(() => {
    let cancelled = false;
    let cleanup: (() => void) | undefined;

    void (async () => {
      cleanup = await initializeMobileRuntime({
        onResume: () => {
          triggerResumeMotion();
          const currentState = authStateRef.current;
          if (!currentState.loading && currentState.isAuthenticated) {
            void currentState.refreshSession();
          }
        },
      });

      if (cancelled) {
        cleanup?.();
      }
    })();

    return () => {
      cancelled = true;
      cleanup?.();
    };
  }, []);

  return null;
}