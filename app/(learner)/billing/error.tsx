'use client';

import Link from 'next/link';
import { useEffect } from 'react';
import { InlineAlert } from '@/components/ui/alert';
import { Button } from '@/components/ui/button';
import { analytics } from '@/lib/analytics';
import type { AnalyticsEvent } from '@/lib/analytics';

export default function Error({
  error,
  reset,
}: {
  error: Error & { digest?: string };
  reset: () => void;
}) {
  useEffect(() => {
    console.error('[Billing Error]', error);
    // 'error_view' is a generic error-surface event used across page-level
    // error boundaries; cast keeps the call site honest while the central
    // analytics event union is updated separately.
    analytics.track('error_view' as AnalyticsEvent, {
      page: 'billing',
      message: error.message,
      digest: error.digest,
    });
  }, [error]);

  return (
    <>
      <InlineAlert
        variant="error"
        title="We couldn't load your billing details"
      >
        {error.message || 'An unexpected error occurred while loading billing. Please try again in a moment.'}
      </InlineAlert>

      <div className="flex flex-wrap items-center gap-3">
        <Button onClick={reset} variant="primary">
          Try again
        </Button>
        <Button asChild variant="outline">
          <Link href="/">Back to dashboard</Link>
        </Button>
      </div>
    </>
  );
}