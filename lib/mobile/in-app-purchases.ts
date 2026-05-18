import { Capacitor } from '@capacitor/core';
import type { PublicAppReleaseSettings } from '@/lib/api';

export type MobileBillingPolicy = PublicAppReleaseSettings['billingPolicy'];

export interface NativePurchaseOptions {
  releaseSettings: PublicAppReleaseSettings;
  appUserId?: string | null;
  productId?: string | null;
}

let configuredRevenueCatKey: string | null = null;

export async function purchaseConfiguredNativeProduct({
  releaseSettings,
  appUserId,
  productId,
}: NativePurchaseOptions): Promise<string> {
  if (!Capacitor.isNativePlatform()) {
    throw new Error('Native in-app purchases are only available in the mobile app.');
  }

  const revenueCatApiKey = releaseSettings.revenueCatApiKey?.trim();
  const configuredProductId = (productId ?? releaseSettings.iapProductId)?.trim();
  if (!revenueCatApiKey || !configuredProductId) {
    throw new Error('Native in-app purchase is not configured for this platform. Ask an admin to set the RevenueCat key and product id in Launch Readiness.');
  }

  const { Purchases } = await import('@revenuecat/purchases-capacitor');
  if (configuredRevenueCatKey !== revenueCatApiKey) {
    try {
      await Purchases.configure({
        apiKey: revenueCatApiKey,
        appUserID: appUserId?.trim() || undefined,
      });
    } catch (error) {
      throw new Error(`Native purchase provider could not be initialized: ${error instanceof Error ? error.message : String(error)}`);
    }
    configuredRevenueCatKey = revenueCatApiKey;
  }

  const { products } = await Purchases.getProducts({ productIdentifiers: [configuredProductId] });
  const product = products[0];
  if (!product) {
    throw new Error(`Native in-app purchase product '${configuredProductId}' is not available from the store.`);
  }

  const result = await Purchases.purchaseStoreProduct({ product });
  return result.productIdentifier;
}
