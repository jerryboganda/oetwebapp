'use client';

import '@/lib/zod-jitless';
import { useEffect, type ReactNode } from 'react';
import { ThemeProvider } from '@/components/theme-provider';
import { AuthProvider } from '@/contexts/auth-context';
import { MobileRuntimeBridge } from '@/components/mobile/mobile-runtime-bridge';
import { RuntimeLifecycleBridge } from '@/components/runtime/runtime-lifecycle-bridge';
import { QueryProvider } from '@/components/providers/query-provider';

function useServiceWorkerRegistration() {
  useEffect(() => {
    if (typeof window === 'undefined' || !('serviceWorker' in navigator)) return;
    if (navigator.webdriver) return;
    // Don't register in Electron or Capacitor native shells
    if ((window as unknown as Record<string, unknown>).desktopBridge || (window as unknown as Record<string, unknown>).__CAPACITOR_NATIVE__) return;

    navigator.serviceWorker.register('/sw.js').catch(() => {
      // Service worker registration failed — non-critical
    });
  }, []);
}

export function AppProviders({ children }: { children: ReactNode }) {
  useServiceWorkerRegistration();

  return (
    <ThemeProvider>
      <QueryProvider>
        <AuthProvider>
          <RuntimeLifecycleBridge />
          <MobileRuntimeBridge />
          {children}
        </AuthProvider>
      </QueryProvider>
    </ThemeProvider>
  );
}
