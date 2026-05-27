'use client';

import { CartPageView } from '@/components/cart';

/**
 * Full-page cart. The heavy lifting (line items, promo codes, totals,
 * checkout CTA, recommendations) lives in `<CartPageView />` so the
 * route can be reused inside other shells if we ever want to mount the
 * cart inside the learner dashboard.
 */
export default function CartPage() {
  return (
    <div className="min-h-screen bg-background text-navy">
      <CartPageView />
    </div>
  );
}
