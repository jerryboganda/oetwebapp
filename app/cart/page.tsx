'use client';

import { CartPageView } from '@/components/cart';
import { IosPurchaseGate } from '@/components/compliance/ios-purchase-gate';

/**
 * Full-page cart. The heavy lifting (line items, promo codes, totals,
 * checkout CTA, recommendations) lives in `<CartPageView />` so the
 * route can be reused inside other shells if we ever want to mount the
 * cart inside the learner dashboard.
 */
export default function CartPage() {
  // App Store compliance: iOS renders an enrol-on-website notice instead of
  // any purchase UI (docs/IOS-PURCHASE-COMPLIANCE.md).
  return (
    <IosPurchaseGate>
      <div className="min-h-screen bg-background-light text-navy">
        <CartPageView />
      </div>
    </IosPurchaseGate>
  );
}
