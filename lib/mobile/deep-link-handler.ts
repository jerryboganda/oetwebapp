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
  /**
   * H13 device pairing: invoked when a `/pair?code=XXXXXX` deep link arrives.
   * The callback receives the 6-char pairing code, which the mobile client
   * should POST to `/v1/device-pairing/redeem` to complete the handoff.
   * Only fired for codes that look well-formed (6–8 alphanumeric chars).
   */
  onPairing?: (code: string) => void;
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
        dispatchDeepLink(deepLink, handlers);
      }
    });
    cleanup.push(() => appUrlOpenListener.remove());

    // Check if app was opened via a deep link (cold start)
    try {
      const launchUrl = await App.getLaunchUrl();
      if (launchUrl?.url) {
        const deepLink = parseDeepLinkUrl(launchUrl.url);
        if (deepLink) {
          dispatchDeepLink(deepLink, handlers);
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

// ── Dispatch ──────────────────────────────────────────────────

const PAIRING_CODE_REGEX = /^[A-Za-z0-9]{6,8}$/;

/**
 * Routes a parsed deep link to the correct handler. The `/pair` path is
 * specialised for H13 device-pairing; all other paths flow through the
 * generic `onDeepLink`. When `/pair` arrives WITHOUT a valid `code`, the
 * event still flows through `onDeepLink` so the UI can render an error.
 */
function dispatchDeepLink(event: DeepLinkEvent, handlers: DeepLinkHandlers): void {
  if (event.path === '/pair') {
    const code = event.queryParams.code;
    if (code && PAIRING_CODE_REGEX.test(code)) {
      handlers.onPairing?.(code.toUpperCase());
      return;
    }
  }
  handlers.onDeepLink?.(event);
}

