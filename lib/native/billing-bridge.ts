/**
 * Mobile billing bridge — Wave B3 Option A (web-only purchases).
 *
 * Strategic decision recorded in
 * `C:/Users/Administrator/Downloads/OET_BILLING_SUBSCRIPTION_PLAN.md` §8:
 * we do NOT mirror SKUs into Apple IAP or Google Play Billing. Purchases
 * happen exclusively on the web via Stripe Checkout. Mobile shells expose a
 * platform-aware browse + manage surface and route the purchase action out
 * to the system browser whenever the platform's store policy allows it.
 *
 * Routing matrix:
 *   - web                       → 'native_iap'        (placeholder label;
 *                                                       opens checkout
 *                                                       in-place, same tab)
 *   - ios + US                  → 'external_browser'  (post-Epic external
 *                                                       link allowance)
 *   - ios + anywhere else       → 'web_only_cta'      (Apple reader-app
 *                                                       exception — show
 *                                                       "Manage on website")
 *   - android                   → 'external_browser'  (User Choice Billing
 *                                                       pilot — more
 *                                                       flexible than iOS)
 *
 * Country detection heuristic (in priority order):
 *   1. `useAuth().user.country`-equivalent from the billing profile endpoint
 *      (`/v1/billing/profile` already exposes a normalised country code).
 *   2. `Intl.DateTimeFormat().resolvedOptions().timeZone` → US time-zone
 *      family lookup. Conservative — only resolves US iOS users to the
 *      external-link experience; everything else falls back to the safer
 *      "web only CTA" copy.
 *   3. `null` — caller must default to the strictest copy.
 */

import { Capacitor } from '@capacitor/core';
import { apiClient } from '@/lib/api';

// ── Types ───────────────────────────────────────────────────────────

export type MobilePurchaseRoute = 'native_iap' | 'external_browser' | 'web_only_cta';

export type MobilePlatform = 'ios' | 'android' | 'web';

export interface MobileBillingCopy {
  ctaLabel: string;
  messageTitle: string;
  messageBody: string;
}

export interface MobileBillingContext {
  platform: MobilePlatform;
  country: string | null;
  /**
   * When true the bridge will call `Browser.open` directly on a tap; the
   * caller can still optionally show a confirmation, but no policy reason
   * prevents the redirect. When false the caller MUST surface the CTA
   * dialog and have the user explicitly confirm before opening the OS
   * browser, satisfying the Apple "no in-app purchase pushed off platform"
   * reviewer guideline.
   */
  allowExternalLink: boolean;
  /** Route the caller should drive UX from. */
  route: MobilePurchaseRoute;
  copy: MobileBillingCopy;
}

/* ── Country detection ─────────────────────────────────────────────
 *
 * The platform exposes a learner billing profile endpoint that already
 * normalises country (`/v1/billing/profile`). Re-fetching here avoids the
 * coupling to React context shape — the bridge is pure TS and is used by
 * tests + push handlers that do not have an `AuthContext`.
 *
 * Falls back to a hardened time-zone heuristic when the API call fails
 * (e.g. offline, signed-out user, anonymous browse on the App Store
 * "Manage Subscription" view).
 *
 * The fallback intentionally only resolves US time zones. The downside of
 * misclassifying a non-US user as US is showing them an external-link
 * button that opens Stripe Checkout — which is acceptable behaviour;
 * Stripe will still serve them. The downside of misclassifying a US user
 * as non-US is just slightly more friction (one extra "Open in browser"
 * tap), so the heuristic biases towards the safer reading.
 */

const US_TIMEZONE_PREFIXES = [
  'America/New_York',
  'America/Detroit',
  'America/Kentucky/',
  'America/Indiana/',
  'America/Indianapolis',
  'America/Chicago',
  'America/Menominee',
  'America/North_Dakota/',
  'America/Denver',
  'America/Boise',
  'America/Phoenix',
  'America/Los_Angeles',
  'America/Anchorage',
  'America/Juneau',
  'America/Sitka',
  'America/Metlakatla',
  'America/Yakutat',
  'America/Nome',
  'America/Adak',
  'Pacific/Honolulu',
  'Pacific/Pago_Pago',
  'Pacific/Guam',
  'Pacific/Saipan',
  'America/Puerto_Rico',
  'America/St_Thomas',
];

function countryFromTimeZone(): string | null {
  try {
    if (typeof Intl === 'undefined') return null;
    const tz = Intl.DateTimeFormat().resolvedOptions().timeZone;
    if (!tz) return null;
    if (US_TIMEZONE_PREFIXES.some((prefix) => tz === prefix || tz.startsWith(prefix))) {
      return 'US';
    }
    return null;
  } catch {
    return null;
  }
}

function normaliseCountry(raw: unknown): string | null {
  if (typeof raw !== 'string') return null;
  const trimmed = raw.trim().toUpperCase();
  if (!/^[A-Z]{2}$/.test(trimmed)) return null;
  return trimmed;
}

async function fetchProfileCountry(): Promise<string | null> {
  try {
    const profile = await apiClient.get<{ country?: string | null; detectedCountry?: string | null }>(
      '/v1/billing/profile',
    );
    return normaliseCountry(profile.country ?? profile.detectedCountry);
  } catch {
    return null;
  }
}

async function resolveCountry(): Promise<string | null> {
  const fromProfile = await fetchProfileCountry();
  if (fromProfile) return fromProfile;
  return countryFromTimeZone();
}

function resolvePlatform(): MobilePlatform {
  const platform = Capacitor.getPlatform();
  if (platform === 'ios' || platform === 'android') return platform;
  return 'web';
}

/* ── Copy + routing matrix ──────────────────────────────────────── */

const WEB_COPY: MobileBillingCopy = {
  ctaLabel: 'Buy now',
  messageTitle: 'Continue to checkout',
  messageBody: 'You will be taken to our secure Stripe Checkout to complete your purchase.',
};

const ANDROID_COPY: MobileBillingCopy = {
  ctaLabel: 'Buy on the web',
  messageTitle: 'Continue in your browser',
  messageBody:
    'We process payments on our website so 100% of your purchase supports your prep — no app-store fees. We will open your browser to continue.',
};

const IOS_US_COPY: MobileBillingCopy = {
  ctaLabel: 'Buy on our website',
  messageTitle: 'Continue on our website',
  messageBody:
    'Tap the button below to leave the app and complete your purchase securely on our website. Other payment methods may be available.',
};

const IOS_GLOBAL_COPY: MobileBillingCopy = {
  ctaLabel: 'Manage on website',
  messageTitle: 'Manage your subscription on the web',
  messageBody:
    'Purchases and subscription changes for OET with Dr Ahmed Hesham are handled on our website. Visit oetwithdrhesham.co.uk on any device to continue.',
};

export function buildBillingContext(
  platform: MobilePlatform,
  country: string | null,
): MobileBillingContext {
  if (platform === 'web') {
    return {
      platform,
      country,
      allowExternalLink: true,
      route: 'native_iap',
      copy: WEB_COPY,
    };
  }

  if (platform === 'android') {
    return {
      platform,
      country,
      allowExternalLink: true,
      route: 'external_browser',
      copy: ANDROID_COPY,
    };
  }

  // iOS branch
  if (country === 'US') {
    return {
      platform,
      country,
      allowExternalLink: true,
      route: 'external_browser',
      copy: IOS_US_COPY,
    };
  }

  return {
    platform,
    country,
    allowExternalLink: false,
    route: 'web_only_cta',
    copy: IOS_GLOBAL_COPY,
  };
}

export async function resolveMobileBillingContext(): Promise<MobileBillingContext> {
  const platform = resolvePlatform();
  const country = platform === 'web' ? null : await resolveCountry();
  return buildBillingContext(platform, country);
}

/* ── External browser navigation ────────────────────────────────── */

interface CheckoutResponse {
  url: string | null;
  checkoutUrl?: string | null;
  sessionId?: string | null;
  checkoutSessionId?: string | null;
}

function extractUrl(response: CheckoutResponse): string {
  const url = response.url ?? response.checkoutUrl ?? null;
  if (typeof url !== 'string' || url.length === 0) {
    throw new Error('Checkout response is missing a redirect URL.');
  }
  return url;
}

async function openInPlatformBrowser(url: string): Promise<void> {
  // Native shells route through the system browser plugin so the App
  // Store/Play Store reviewer never sees Stripe loaded inside the in-app
  // webview. Web fallback opens a new tab.
  if (Capacitor.isNativePlatform()) {
    const { Browser } = await import('@capacitor/browser');
    await Browser.open({ url, presentationStyle: 'fullscreen' });
    return;
  }

  if (typeof window !== 'undefined') {
    const popup = window.open(url, '_blank', 'noopener,noreferrer');
    if (!popup) {
      window.location.assign(url);
    }
  }
}

export async function openExternalCheckout(productCode: string): Promise<void> {
  if (typeof productCode !== 'string' || productCode.length === 0) {
    throw new Error('A product code is required to open external checkout.');
  }

  const response = await apiClient.post<CheckoutResponse>('/v1/checkout/sessions', {
    productCode,
  });
  await openInPlatformBrowser(extractUrl(response));
}

export async function openCustomerPortal(): Promise<void> {
  const response = await apiClient.post<CheckoutResponse>('/v1/subscriptions/me/portal-session', {
    returnUrl:
      typeof window !== 'undefined' && window.location?.origin
        ? `${window.location.origin}/account`
        : 'https://app.oetwithdrhesham.co.uk/account',
  });
  await openInPlatformBrowser(extractUrl(response));
}
