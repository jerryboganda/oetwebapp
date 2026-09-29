'use client';

import Link from 'next/link';
import { useEffect } from 'react';
import { InlineAlert } from '@/components/ui/alert';
import { Button } from '@/components/ui/button';
import { analytics } from '@/lib/analytics';
import type { AnalyticsEvent } from '@/lib/analytics';
import { BackToBillingLink } from '@/components/domain/billing';

export default function Error({
  error,
  reset,
}: {
  error: Error & { digest?: string };
  reset: () => void;
}) {
  useEffect(() => {
    console.error('[Billing Referral Error]', error);
    analytics.track('error_view' as AnalyticsEvent, {
      page: 'billing-referral',
      message: error.message,
      digest: error.digest,
    });
  }, [error]);

  return (
    <>
      <div className="space-y-6">
        <BackToBillingLink />
        <InlineAlert variant="error" title="We couldn't load your referral program">
          {error.message ||
            'An unexpected error occurred while loading your referral details. Please try again in a moment.'}
        </InlineAlert>
        <div className="flex flex-wrap items-center gap-3">
          <Button onClick={reset} variant="primary" aria-label="Retry loading referral program">
            Try again
          </Button>
          <Button asChild variant="outline">
            <Link href="/billing">Back to billing</Link>
          </Button>
        </div>
      </div>
    </>
  );
}
