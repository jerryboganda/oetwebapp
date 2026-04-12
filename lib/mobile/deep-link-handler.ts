'use client';

import { Capacitor } from '@capacitor/core';

// ── Types ───────────────────────────────────────────────────────

export interface DeepLinkEvent {
  url: string;
  path: string;
  queryParams: Record<string, string>;
}

export interface DeepLinkHandlers {
  onDeepLink?: (event: DeepLinkEvent) => void;
}

// ── Configuration ───────────────────────────────────────────────

const ALLOWED_HOSTS = ['app.oetwithdrhesham.co.uk'] as const;

// ── URL Validation ──────────────────────────────────────────────

function isAllowedDeepLinkUrl(rawUrl: string): boolean {
  try {
    const parsed = new URL(rawUrl);
    return (
      (parsed.protocol === 'https:' || parsed.protocol === 'http:') &&
      (ALLOWED_HOSTS as readonly string[]).includes(parsed.hostname)
    );
  } catch {
    return false;
  }
}

function parseDeepLinkUrl(rawUrl: string): DeepLinkEvent | null {
  if (!isAllowedDeepLinkUrl(rawUrl)) {
    return null;
  }

  try {
    const parsed = new URL(rawUrl);
    const queryParams: Record<string, string> = {};
    parsed.searchParams.forEach((value, key) => {
      queryParams[key] = value;
    });

    return {
      url: rawUrl,
      path: parsed.pathname,
      queryParams,
    };
  } catch {
    return null;
  }
}

// ── Deep Link Listener ──────────────────────────────────────────

export async function initializeDeepLinkHandler(
  handlers: DeepLinkHandlers = {},
): Promise<() => void> {
  if (!Capacitor.isNativePlatform()) {
    return () => undefined;
  }

  const cleanup: Array<() => Promise<void> | void> = [];

  try {
    const { App } = await import('@capacitor/app');

    // Handle deep links received while app is running
    const appUrlOpenListener = await App.addListener('appUrlOpen', (event) => {
      const deepLink = parseDeepLinkUrl(event.url);
      if (deepLink) {
        handlers.onDeepLink?.(deepLink);
      }
    });
    cleanup.push(() => appUrlOpenListener.remove());

    // Check if app was opened via a deep link (cold start)
    try {
      const launchUrl = await App.getLaunchUrl();
      if (launchUrl?.url) {
        const deepLink = parseDeepLinkUrl(launchUrl.url);
        if (deepLink) {
          handlers.onDeepLink?.(deepLink);
        }
      }
    } catch {
      // getLaunchUrl may not be supported on all platforms.
    }
  } catch {
    // Deep link handling is non-critical — fail silently.
  }

  return () => {
    cleanup.forEach((teardown) => {
      try {
        void teardown();
      } catch {
        // Ignore teardown failures.
      }
    });
  };
}
