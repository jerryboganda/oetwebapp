'use client';

import dynamic from 'next/dynamic';
import { Skeleton } from '@/components/ui/skeleton';
import { CheckoutLoadBoundary } from './checkout-load-boundary';
import type { WhopEmbeddedCheckoutProps } from './whop-embedded-checkout';

/**
 * Whop's embedded checkout, loaded only when it is about to be shown. `@whop/elements` and
 * `@whop/elements-react` used to be in the checkout page's first-load bundle for every visitor,
 * including everyone who pays with another gateway. Client-only: the element needs the browser.
 */
const DynamicWhopEmbeddedCheckout = dynamic<WhopEmbeddedCheckoutProps>(
  () => import('./whop-embedded-checkout').then((module) => module.WhopEmbeddedCheckout),
  {
    ssr: false,
    loading: () => <Skeleton aria-hidden className="mt-4 h-72 w-full rounded-xl" />,
  },
);

/**
 * If the checkout's code cannot be downloaded the component never mounts, so its own fallback to the
 * hosted checkout cannot run; the boundary calls the same `onUnavailable` instead of showing an error page.
 */
export function LazyWhopEmbeddedCheckout(props: WhopEmbeddedCheckoutProps) {
  return (
    <CheckoutLoadBoundary onFailed={props.onUnavailable}>
      <DynamicWhopEmbeddedCheckout {...props} />
    </CheckoutLoadBoundary>
  );
}
