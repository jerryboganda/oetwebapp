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
    console.error('[Billing Score Guarantee Error]', error);
    analytics.track('error_view' as AnalyticsEvent, {
      page: 'billing-score-guarantee',
      message: error.message,
      digest: error.digest,
    });
  }, [error]);

  return (
    <>
      <div className="space-y-6">
        <BackToBillingLink />
        <InlineAlert variant="error" title="We couldn't load your score guarantee">
          {error.message ||
            'An unexpected error occurred while loading your score guarantee. Please try again in a moment.'}
        </InlineAlert>
        <div className="flex flex-wrap items-center gap-3">
          <Button onClick={reset} variant="primary" aria-label="Retry loading score guarantee">
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
