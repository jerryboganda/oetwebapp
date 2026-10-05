'use client';

import '@/lib/zod-jitless';
import { useEffect, type ReactNode } from 'react';
import { NextIntlClientProvider, type AbstractIntlMessages } from 'next-intl';
import { ThemeProvider } from '@/components/theme-provider';
import { AuthProvider } from '@/contexts/auth-context';
import { AccessibilityProvider } from '@/contexts/accessibility-context';
import { MobileRuntimeGate } from '@/components/mobile/mobile-runtime-gate';
import { RuntimeLifecycleBridge } from '@/components/runtime/runtime-lifecycle-bridge';
import { QueryProvider } from '@/components/providers/query-provider';
import { AuthenticatedNotificationCenter } from '@/components/providers/authenticated-notification-center';
import { Toaster } from '@/components/ui/toaster';
import { RuntimeShellBridges } from '@/components/shell/runtime-shell-bridges';
import { AiAssistantProvider } from '@/contexts/ai-assistant-context';
import { CompanionMount } from '@/components/providers/companion-mount';
import { getAppRuntimeKind } from '@/lib/runtime-signals';
import { getMessageFallback, ignoreIntlError } from '@/lib/i18n/message-fallback';
import { RuntimeConfigProvider } from './providers/RuntimeConfigProvider';
import { AppVersionGateProvider } from './providers/AppVersionGateProvider';

function useServiceWorkerRegistration() {
  useEffect(() => {
    if (typeof window === 'undefined' || !('serviceWorker' in navigator)) return;
    if (navigator.webdriver) return;
    // Don't register in the desktop or Capacitor native shells: the native
    // WebView must always load the live remote origin, never a cached copy,
    // and a controlling worker's skipWaiting/claim churn across deploys
    // produces stale-build states the shell cannot clear. getAppRuntimeKind
    // reads the bootstrap-stamped dataset plus the live Capacitor bridge —
    // do NOT key this off a window global (a previous revision checked
    // `__CAPACITOR_NATIVE__`, which nothing ever set, so the worker silently
    // registered inside every SW-capable native WebView).
    if (getAppRuntimeKind() !== 'web') return;

    // updateViaCache 'none': always fetch sw.js from the network so a worker fix
    // (e.g. a CACHE_VERSION bump) reaches existing users on their next page load.
    // Re-check when a long-lived tab becomes visible again. No forced reload on
    // controllerchange: the worker never caches HTML, and a reload could
    // interrupt an in-progress exam attempt.
    let registration: ServiceWorkerRegistration | null = null;
    const checkForUpdate = () => {
      if (document.visibilityState === 'visible') registration?.update().catch(() => {});
    };
    navigator.serviceWorker
      .register('/sw.js', { updateViaCache: 'none' })
      .then((reg) => {
        registration = reg;
      })
      .catch(() => {
        // Service worker registration failed — non-critical
      });
    document.addEventListener('visibilitychange', checkForUpdate);
    return () => document.removeEventListener('visibilitychange', checkForUpdate);
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
  messages?: AbstractIntlMessages;
}) {
  useServiceWorkerRegistration();

  return (
    <NextIntlClientProvider
      locale={locale}
      messages={messages}
      // Pages that don't have a translation for a requested key (or that
      // don't use next-intl at all) keep their existing English strings —
      // we don't want missing keys to throw a runtime error inside legacy
      // pages while the rollout is partial. (Shared with the nested providers
      // that add a bundle, e.g. the learner layout's Writing messages.)
      onError={ignoreIntlError}
      getMessageFallback={getMessageFallback}
    >
      {/*
        RuntimeConfigProvider sits high in the tree so every client consumer
        (auth, notifications/web-push, realtime) can read DB-driven boot values
        via useRuntimeConfig(), with NEXT_PUBLIC_* fallbacks for first paint.
      */}
      <RuntimeConfigProvider>
      {/*
        AppVersionGateProvider wraps the whole tree (outside AuthProvider) so the
        forced-update gate can block even unauthenticated/expired sessions. It
        renders no UI itself — ShellControls + ForcedUpdateOverlay are mounted
        deeper (below) where Theme context is available, and consume the
        gate via useAppVersionGate().
      */}
      <AppVersionGateProvider>
      <ThemeProvider nonce={nonce}>
        <QueryProvider>
          <AuthProvider>
            <RuntimeLifecycleBridge />
            <MobileRuntimeGate />
            {/*
              AccessibilityProvider reads the learner's saved Settings →
              Accessibility preferences (large text, high contrast, reduce
              motion, keyboard hints) and applies them to <html> + the
              motion/react MotionConfig. It sits inside AuthProvider (it needs
              the user id to read the settings cache) and wraps the visible app
              + shell so the preferences affect everything the learner sees.
            */}
            <AccessibilityProvider>
              {/*
                AI Learning Companion (docs/ai-learning-companion/). Sits inside
                AuthProvider because it needs session.accessToken for the SignalR
                hub and the role for its permission check. The provider only
                opens a connection when the user actually has access AND a
                surface that shows the assistant (the panel, the companion page)
                has asked for it, and CompanionMount additionally gates on the server-owned
                `ai_learning_companion` flag — which ships disabled — so this
                renders nothing until an operator enables it in /admin/flags.
              */}
              <AiAssistantProvider>
                <AuthenticatedNotificationCenter>
                  {children}
                </AuthenticatedNotificationCenter>
                <CompanionMount />
              </AiAssistantProvider>
              {/*
                Shell-only update UI (returns null on the website): the
                top-center Reload + Check-for-updates cluster and the
                non-dismissible forced-update overlay. Mounted here so they
                inherit Theme context.
              */}
              <RuntimeShellBridges />
              {/*
                Global sonner toaster — rendered once at the root so any
                `toast()` call anywhere in the tree surfaces in the same anchor.
                Theme is read from next-themes inside the component.
              */}
              <Toaster />
            </AccessibilityProvider>
          </AuthProvider>
        </QueryProvider>
      </ThemeProvider>
      </AppVersionGateProvider>
      </RuntimeConfigProvider>
    </NextIntlClientProvider>
  );
}
