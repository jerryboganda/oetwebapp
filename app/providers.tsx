'use client';

import '@/lib/zod-jitless';
import { useEffect, type ReactNode } from 'react';
import { NextIntlClientProvider } from 'next-intl';
import { ThemeProvider } from '@/components/theme-provider';
import { AuthProvider } from '@/contexts/auth-context';
import { MobileRuntimeBridge } from '@/components/mobile/mobile-runtime-bridge';
import { RuntimeLifecycleBridge } from '@/components/runtime/runtime-lifecycle-bridge';
import { QueryProvider } from '@/components/providers/query-provider';
import { Toaster } from '@/components/admin/ui/toaster';
import { TooltipProvider } from '@/components/admin/ui/tooltip';
import { TourProvider } from '@/components/onboarding/tour-provider';
import { LearnerPasteGuard } from '@/components/system/LearnerPasteGuard';

function isWritingMessageKey(key: string) {
  return key === 'writing' || key.startsWith('writing.');
}

function getMessageFallback({ key }: { key: string }) {
  if (!isWritingMessageKey(key)) return key;

  console.error(new Error(`Missing required writing translation: ${key}`));
  return 'Writing copy unavailable';
}

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

export function AppProviders({
  children,
  nonce,
  locale = 'en',
  messages = {},
}: {
  children: ReactNode;
  nonce?: string;
  locale?: string;
  messages?: Record<string, string>;
}) {
  useServiceWorkerRegistration();

  return (
    <NextIntlClientProvider
      locale={locale}
      messages={messages}
      // Pages that don't have a translation for a requested key (or that
      // don't use next-intl at all) keep their existing English strings —
      // we don't want missing keys to throw a runtime error inside legacy
      // pages while the rollout is partial.
      onError={() => {
        /* Non-writing pages still use key fallbacks during the partial rollout. */
      }}
      getMessageFallback={getMessageFallback}
    >
      <ThemeProvider nonce={nonce}>
        {/*
          TooltipProvider must wrap every admin (and learner) consumer of the
          Tooltip primitive so portals share a single delay context. 800ms hover
          delay matches the Material spec; focus opens are instant by default.
          skipDelayDuration={500} makes moving between adjacent tooltips (e.g. a
          toolbar) open instantly — the whole row then feels fast after the first.
        */}
        <TooltipProvider delayDuration={800} skipDelayDuration={500}>
          <QueryProvider>
            <AuthProvider>
              {/*
                Disables copy/paste/cut/drag across the entire learner app.
                Self-disables on /admin + auth routes via an internal path
                check, so mounting once at the root is correct.
              */}
              <LearnerPasteGuard />
              <RuntimeLifecycleBridge />
              <MobileRuntimeBridge />
              <TourProvider>
                {children}
              </TourProvider>
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
    </NextIntlClientProvider>
  );
}
