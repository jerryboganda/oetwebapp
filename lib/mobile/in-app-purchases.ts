/**
 * Mobile In-App Purchase (IAP) integration via RevenueCat.
 *
 * Wraps @revenuecat/purchases-capacitor for App Store / Play Store
 * subscription management. Lazy-loads the plugin to avoid import
 * errors on web.
 *
 * @see https://docs.revenuecat.com/docs/capacitor
 */

import { Capacitor } from '@capacitor/core';

/** Shape of the lazy-loaded RevenueCat Purchases plugin. */
interface PurchasesPlugin {
  configure(opts: { apiKey: string; appUserID: string }): Promise<void>;
  getOfferings(): Promise<{ current?: { availablePackages?: Array<{ identifier: string; product: { title: string; priceString: string } }> }; all?: Record<string, { availablePackages?: Array<{ identifier: string; product: { title: string; priceString: string } }> }> }>;
  purchasePackage(opts: { aPackage: { identifier: string } }): Promise<{ customerInfo: { entitlements: { active: Record<string, unknown> } } }>;
  restorePurchases(): Promise<{ customerInfo: { entitlements: { active: Record<string, unknown> } } }>;
  getCustomerInfo(): Promise<{ customerInfo: { entitlements: { active: Record<string, unknown> } } }>;
  logOut(): Promise<void>;
}

// Lazy-loaded RevenueCat plugin — only imported when running inside Capacitor.
let Purchases: PurchasesPlugin | null = null;

async function loadPlugin() {
  if (!Capacitor.isNativePlatform()) return null;
  if (Purchases) return Purchases;
  const mod = await import('@revenuecat/purchases-capacitor');
  Purchases = mod.Purchases as unknown as PurchasesPlugin;
  return Purchases;
}

/**
 * Initialize RevenueCat with the platform-specific API key.
 * Call this once at app startup (after auth completes).
 */
export async function initializePurchases(userId: string): Promise<boolean> {
  const plugin = await loadPlugin();
  if (!plugin) return false;

  const apiKey =
    typeof globalThis !== 'undefined' && 'Capacitor' in globalThis
      ? (globalThis as Record<string, unknown>).__OET_RC_API_KEY as string | undefined
      : undefined;

  // API keys should be provided via environment config.
  // Android: REVENUECAT_ANDROID_KEY, iOS: REVENUECAT_IOS_KEY
  const platformKey =
    apiKey ||
    process.env.NEXT_PUBLIC_REVENUECAT_API_KEY ||
    '';

  if (!platformKey) {
    console.warn('[iap] RevenueCat API key not configured — purchases disabled');
    return false;
  }

  try {
    await plugin.configure({ apiKey: platformKey, appUserID: userId });
    return true;
  } catch (err) {
    console.error('[iap] Failed to initialize RevenueCat:', err);
    return false;
  }
}

/**
 * Fetch all available subscription offerings.
 */
export async function getOfferings() {
  const plugin = await loadPlugin();
  if (!plugin) return null;

  try {
    const result = await plugin.getOfferings();
    return result.current ?? null;
  } catch (err) {
    console.error('[iap] Failed to fetch offerings:', err);
    return null;
  }
}

/**
 * Purchase a specific package from an offering.
 */
export async function purchasePackage(packageToPurchase: { identifier: string; offeringIdentifier: string }) {
  const plugin = await loadPlugin();
  if (!plugin) throw new Error('IAP not available on this platform');

  const offerings = await plugin.getOfferings();
  const offering = offerings.all?.[packageToPurchase.offeringIdentifier];
  const pkg = offering?.availablePackages?.find(
    (p: { identifier: string }) => p.identifier === packageToPurchase.identifier,
  );
  if (!pkg) throw new Error(`Package ${packageToPurchase.identifier} not found`);

  return plugin.purchasePackage({ aPackage: pkg });
}

/**
 * Restore purchases from the App Store / Play Store.
 * Useful when users reinstall the app or switch devices.
 */
export async function restorePurchases() {
  const plugin = await loadPlugin();
  if (!plugin) return null;

  try {
    return await plugin.restorePurchases();
  } catch (err) {
    console.error('[iap] Failed to restore purchases:', err);
    return null;
  }
}

/**
 * Get the current customer subscription info.
 */
export async function getCustomerInfo() {
  const plugin = await loadPlugin();
  if (!plugin) return null;

  try {
    return await plugin.getCustomerInfo();
  } catch (err) {
    console.error('[iap] Failed to get customer info:', err);
    return null;
  }
}

/**
 * Check if the user has an active subscription entitlement.
 */
export async function hasActiveSubscription(entitlementId = 'premium'): Promise<boolean> {
  const info = await getCustomerInfo();
  if (!info?.customerInfo?.entitlements?.active) return false;
  return entitlementId in info.customerInfo.entitlements.active;
}

/**
 * Log the user out of RevenueCat (on sign-out).
 */
export async function logOutPurchases(): Promise<void> {
  const plugin = await loadPlugin();
  if (!plugin) return;

  try {
    await plugin.logOut();
  } catch {
    // Ignore — user may not have been logged in
  }
}
