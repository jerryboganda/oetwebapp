'use client';

import dynamic from 'next/dynamic';
import { Skeleton } from '@/components/ui/skeleton';
import type { PayPalExpandedCheckoutProps } from './paypal-expanded-checkout';

/**
 * PayPal's expanded checkout, loaded only when it is about to be shown. `@paypal/react-paypal-js`
 * used to be in the first-load bundle of every page that can take a PayPal payment (checkout,
 * private-speaking booking) for everyone, including learners who never pick PayPal.
 * Client-only: the PayPal SDK needs the browser.
 */
export const LazyPayPalExpandedCheckout = dynamic<PayPalExpandedCheckoutProps>(
  () => import('./paypal-expanded-checkout').then((module) => module.PayPalExpandedCheckout),
  {
    ssr: false,
    loading: () => <Skeleton aria-hidden className="h-24 w-full rounded-xl" />,
  },
);
