'use client';

import { useEffect, useState } from 'react';
import {
  buildBillingContext,
  resolveMobileBillingContext,
  type MobileBillingContext,
} from '@/lib/native/billing-bridge';

/**
 * React hook that resolves the platform + country routing context for the
 * mobile billing surfaces. Returns `null` while loading so callers can
 * render a skeleton; defaults to the strictest copy on failure.
 */
export function useMobileBillingContext(): MobileBillingContext | null {
  const [context, setContext] = useState<MobileBillingContext | null>(null);

  useEffect(() => {
    let cancelled = false;
    void resolveMobileBillingContext()
      .then((resolved) => {
        if (!cancelled) setContext(resolved);
      })
      .catch(() => {
        // Hard failure → fall back to the strictest possible iOS-global
        // experience so reviewers never see an external link without copy.
        if (!cancelled) setContext(buildBillingContext('ios', null));
      });
    return () => {
      cancelled = true;
    };
  }, []);

  return context;
}
