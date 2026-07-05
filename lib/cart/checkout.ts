import { createBillingCheckoutSession, isApiError } from '@/lib/api';
import { openCheckoutUrl } from '@/lib/mobile/web-checkout';
import { toast } from '@/components/admin/ui/toaster';
import type { CartItem } from './cart-store';

/** Backend error codes with a cart-specific, friendlier message than the raw API text. */
const CART_ERROR_MESSAGES: Record<string, string> = {
  cart_one_subscription: 'You can only check out one subscription plan at a time.',
  addon_requires_parent: 'This add-on requires an active parent plan — add the plan to your cart first.',
  addon_incompatible: 'One of the add-ons in your cart is not compatible with the selected plan.',
};

export interface StartCartCheckoutOptions {
  couponCode?: string | null;
  gateway?: string;
}

/**
 * Maps the client cart onto the existing `/v1/billing/checkout-sessions`
 * express endpoint (one plan + multiple add-ons per checkout) and redirects
 * the learner to the returned hosted checkout URL.
 */
export async function startCartCheckout(
  items: CartItem[],
  options: StartCartCheckoutOptions = {},
): Promise<void> {
  if (items.length === 0) {
    toast.error('Your cart is empty.');
    return;
  }

  const plan = items.find((item) => item.kind === 'plan');

  try {
    const result = plan
      ? await createBillingCheckoutSession({
        productType: 'plan_purchase',
        quantity: 1,
        priceId: plan.code,
        addOnCodes: items.filter((item) => item.kind === 'addon').map((item) => item.code),
        couponCode: options.couponCode ?? null,
        gateway: options.gateway ?? 'stripe',
      })
      : await createBillingCheckoutSession({
        productType: 'addon_purchase',
        quantity: 1,
        priceId: items[0].code,
        addOnCodes: items.slice(1).map((item) => item.code),
        couponCode: options.couponCode ?? null,
        gateway: options.gateway ?? 'stripe',
      });

    if (!result.checkoutUrl) {
      throw new Error('Checkout session did not return a redirect URL.');
    }

    try {
      await openCheckoutUrl(result.checkoutUrl);
    } catch {
      window.location.href = result.checkoutUrl;
    }
  } catch (err) {
    if (isApiError(err) && CART_ERROR_MESSAGES[err.code]) {
      toast.error(CART_ERROR_MESSAGES[err.code]);
      return;
    }
    toast.error(err instanceof Error ? err.message : 'Could not start checkout.');
  }
}
