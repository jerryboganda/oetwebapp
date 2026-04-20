'use client';

import { useEffect } from 'react';
import Link from 'next/link';
import { AlertTriangle, RefreshCcw } from 'lucide-react';
import { LearnerDashboardShell } from '@/components/layout';
import { Card } from '@/components/ui/card';
import { Button } from '@/components/ui/button';

export default function GrammarError({
  error,
  reset,
}: {
  error: Error & { digest?: string };
  reset: () => void;
}) {
  useEffect(() => {
    console.error('[Grammar Error]', error);
  }, [error]);

  return (
    <LearnerDashboardShell>
      <div className="mx-auto max-w-xl">
        <Card className="text-center">
          <div className="flex flex-col items-center gap-4 py-4">
            <div className="flex h-14 w-14 items-center justify-center rounded-2xl bg-red-50 text-red-600">
              <AlertTriangle className="h-7 w-7" />
            </div>
            <div className="space-y-2">
              <p className="text-[11px] font-semibold uppercase tracking-[0.18em] text-muted">Grammar error</p>
              <h2 className="text-xl font-bold text-navy">We couldn&apos;t load this view</h2>
              <p className="mx-auto max-w-md text-sm leading-6 text-muted">
                Something went wrong while loading grammar. Try again, or head back to your dashboard if the issue persists.
              </p>
            </div>
            <div className="flex flex-wrap justify-center gap-2">
              <Button variant="primary" size="sm" onClick={reset} className="inline-flex items-center gap-1.5">
                <RefreshCcw className="h-3.5 w-3.5" /> Try again
              </Button>
              <Link href="/">
                <Button variant="outline" size="sm">Back to dashboard</Button>
              </Link>
            </div>
          </div>
        </Card>
      </div>
    </LearnerDashboardShell>
  );
}
