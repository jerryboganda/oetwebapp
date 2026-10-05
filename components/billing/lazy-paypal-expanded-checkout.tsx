'use client';

import dynamic from 'next/dynamic';
import { Skeleton } from '@/components/ui/skeleton';
import { CheckoutLoadBoundary } from '@/components/checkout/checkout-load-boundary';
import type { PayPalExpandedCheckoutProps } from './paypal-expanded-checkout';

/**
 * PayPal's expanded checkout, loaded only when it is about to be shown. `@paypal/react-paypal-js`
 * used to be in the first-load bundle of every page that can take a PayPal payment (checkout,
 * private-speaking booking) for everyone, including learners who never pick PayPal.
 * Client-only: the PayPal SDK needs the browser.
 */
const DynamicPayPalExpandedCheckout = dynamic<PayPalExpandedCheckoutProps>(
  () => import('./paypal-expanded-checkout').then((module) => module.PayPalExpandedCheckout),
  {
    ssr: false,
    loading: () => <Skeleton aria-hidden className="h-24 w-full rounded-xl" />,
  },
);

/**
 * If the checkout's code cannot be downloaded the component never mounts, so its own `onUnavailable`
 * (the parent's switch to the redirect flow) cannot run; the boundary calls it instead of showing an error page.
 */
export function LazyPayPalExpandedCheckout(props: PayPalExpandedCheckoutProps) {
  return (
    <CheckoutLoadBoundary onFailed={props.onUnavailable}>
      <DynamicPayPalExpandedCheckout {...props} />
    </CheckoutLoadBoundary>
  );
}
