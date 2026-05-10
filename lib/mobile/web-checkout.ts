// Mobile / Capacitor-aware checkout opener.
//
// Decision recorded 2026-05-10 against `RW-014` in
// `docs/STATUS/remaining-work.yaml`: payment processing on Capacitor builds
// is performed entirely on the web via Stripe Checkout. We do NOT use Apple
// or Google in-app purchases. To keep store-policy compliance simple the
// checkout URL is launched in the **system browser** rather than the
// in-app webview.
//
// On the web this falls back to `window.open(...)` so the behaviour for
// existing learners is unchanged.

import { Capacitor } from '@capacitor/core';

export type OpenCheckoutResult = 'capacitor-browser' | 'window-open' | 'window-assign' | 'noop';

/**
 * Opens a checkout / billing-portal URL in the most appropriate context for
 * the current runtime.
 *
 * - On Capacitor native builds: uses `@capacitor/browser` so Stripe runs in
 *   the OS browser (Safari View Controller / Chrome Custom Tabs). This is
 *   the path required by App Store + Play Store policy because we are
 *   processing payment on the web, not via in-app purchase.
 * - On the web: opens a new tab; falls back to a same-tab redirect when the
 *   popup blocker prevents it.
 */
export async function openCheckoutUrl(url: string): Promise<OpenCheckoutResult> {
  if (typeof url !== 'string' || url.length === 0) return 'noop';

  // Capacitor native shell — route through the system browser plugin.
  if (typeof window !== 'undefined' && Capacitor.isNativePlatform()) {
    try {
      const { Browser } = await import('@capacitor/browser');
      await Browser.open({ url, presentationStyle: 'fullscreen' });
      return 'capacitor-browser';
    } catch {
      // Fall through to web behaviour if the plugin is unavailable for any reason.
    }
  }

  if (typeof window === 'undefined') return 'noop';

  const popup = window.open(url, '_blank', 'noopener,noreferrer');
  if (!popup) {
    window.location.assign(url);
    return 'window-assign';
  }
  return 'window-open';
}
