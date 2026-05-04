'use client';

import Link from 'next/link';
import { useEffect } from 'react';
import { LearnerDashboardShell } from '@/components/layout/learner-dashboard-shell';
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
    <LearnerDashboardShell pageTitle="Referral program" backHref="/billing">
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
          <Link
            href="/billing"
            className="inline-flex min-h-11 items-center justify-center gap-2 rounded-2xl border border-border bg-surface px-4 py-2 text-sm font-bold text-navy transition-colors hover:bg-background-light focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary"
          >
            Back to billing
          </Link>
        </div>
      </div>
    </LearnerDashboardShell>
  );
}
