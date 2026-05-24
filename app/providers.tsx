'use client';

import '@/lib/zod-jitless';
import { useEffect, type ReactNode } from 'react';
import { ThemeProvider } from '@/components/theme-provider';
import { AuthProvider } from '@/contexts/auth-context';
import { MobileRuntimeBridge } from '@/components/mobile/mobile-runtime-bridge';
import { RuntimeLifecycleBridge } from '@/components/runtime/runtime-lifecycle-bridge';
import { QueryProvider } from '@/components/providers/query-provider';
import { Toaster } from '@/components/admin/ui/toaster';
import { TooltipProvider } from '@/components/admin/ui/tooltip';

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

export function AppProviders({ children, nonce }: { children: ReactNode; nonce?: string }) {
  useServiceWorkerRegistration();

  return (
    <ThemeProvider nonce={nonce}>
      {/*
        TooltipProvider must wrap every admin (and learner) consumer of the
        Tooltip primitive so portals share a single delay context. 800ms hover
        delay matches the Material spec; focus opens are instant by default.
      */}
      <TooltipProvider delayDuration={800}>
        <QueryProvider>
          <AuthProvider>
            <RuntimeLifecycleBridge />
            <MobileRuntimeBridge />
            {children}
            {/*
              Global sonner toaster — rendered once at the root so any
              `toast()` call anywhere in the tree surfaces in the same anchor.
              Theme is read from next-themes inside the component.
            */}
            <Toaster />
          </AuthProvider>
        </QueryProvider>
      </TooltipProvider>
    </ThemeProvider>
  );
}
