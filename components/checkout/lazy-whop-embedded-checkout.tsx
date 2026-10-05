'use client';

import dynamic from 'next/dynamic';
import { Skeleton } from '@/components/ui/skeleton';
import type { WhopEmbeddedCheckoutProps } from './whop-embedded-checkout';

/**
 * Whop's embedded checkout, loaded only when it is about to be shown. `@whop/elements` and
 * `@whop/elements-react` used to be in the checkout page's first-load bundle for every visitor,
 * including everyone who pays with another gateway. Client-only: the element needs the browser.
 */
export const LazyWhopEmbeddedCheckout = dynamic<WhopEmbeddedCheckoutProps>(
  () => import('./whop-embedded-checkout').then((module) => module.WhopEmbeddedCheckout),
  {
    ssr: false,
    loading: () => <Skeleton aria-hidden className="mt-4 h-72 w-full rounded-xl" />,
  },
);
