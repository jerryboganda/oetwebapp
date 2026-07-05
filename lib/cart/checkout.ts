import type { CartItem } from './cart-store';

/**
 * Builds the `/checkout/review` URL for the current cart.
 *
 * The cart hands its selected items to the shared checkout review page, which
 * owns the payment-route UI (Pay globally / Egypt, Stripe / PayPal / bank
 * transfer), the live quote + countdown, and the actual checkout-session
 * creation. This keeps ONE professional checkout surface for both the cart and
 * the single-item "buy now" deep links, instead of the cart silently
 * redirecting to a hosted portal.
 *
 * Mapping onto the express checkout contract: a plan (if present) is the primary
 * `priceId` with every add-on as a repeated `addOnCodes` param; otherwise the
 * first add-on is the primary and the rest are `addOnCodes`. The backend quote
 * builder composes them into one multi-item quote.
 */
export function buildCheckoutReviewHref(
  items: CartItem[],
  couponCode?: string | null,
): string {
  const params = new URLSearchParams();
  params.set('quantity', '1');

  const plan = items.find((item) => item.kind === 'plan');
  if (plan) {
    params.set('productType', 'plan_purchase');
    params.set('priceId', plan.code);
    for (const item of items) {
      if (item.kind === 'addon') params.append('addOnCodes', item.code);
    }
  } else {
    params.set('productType', 'addon_purchase');
    if (items[0]) params.set('priceId', items[0].code);
    for (const item of items.slice(1)) {
      params.append('addOnCodes', item.code);
    }
  }

  const coupon = couponCode?.trim();
  if (coupon) params.set('couponCode', coupon);

  return `/checkout/review?${params.toString()}`;
}
