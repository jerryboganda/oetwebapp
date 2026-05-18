import { Capacitor } from '@capacitor/core';
import { fetchPublicAppReleaseSettings } from '@/lib/api';
import { purchaseConfiguredNativeProduct, type MobileBillingPolicy } from '@/lib/mobile/in-app-purchases';

export type OpenCheckoutResult = 'native-iap' | 'capacitor-browser' | 'window-open' | 'window-assign' | 'noop';

export interface OpenCheckoutOptions {
  appUserId?: string | null;
  nativeProductId?: string | null;
  billingPolicyOverride?: MobileBillingPolicy | null;
}

function normalizeBillingPolicy(value: string | null | undefined): MobileBillingPolicy {
  return value === 'native-iap' || value === 'web-checkout' || value === 'hybrid' ? value : 'hybrid';
}

/**
 * Opens a checkout / billing-portal URL in the most appropriate context for
 * the current runtime.
 *
 * - On Capacitor native builds: the admin-configured Launch Readiness policy
 *   decides whether to use RevenueCat-backed native IAP or web checkout in the
 *   system browser.
 * - On the web: opens a new tab; falls back to a same-tab redirect when the
 *   popup blocker prevents it.
 */
export async function openCheckoutUrl(url: string, options: OpenCheckoutOptions = {}): Promise<OpenCheckoutResult> {
  if (typeof url !== 'string' || url.length === 0) return 'noop';

  // Capacitor native shell — route through the system browser plugin.
  if (typeof window !== 'undefined' && Capacitor.isNativePlatform()) {
    const platform = Capacitor.getPlatform();
    const releaseSettings = await fetchPublicAppReleaseSettings(platform).catch(() => null);
    const policy = options.billingPolicyOverride ?? normalizeBillingPolicy(releaseSettings?.billingPolicy);
    if (policy === 'native-iap') {
      if (!releaseSettings) {
        throw new Error('Native in-app purchase policy could not be loaded. Try again when release settings are reachable.');
      }
      await purchaseConfiguredNativeProduct({
        releaseSettings,
        appUserId: options.appUserId,
        productId: options.nativeProductId,
      });
      return 'native-iap';
    }

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
