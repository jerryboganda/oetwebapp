'use client';

import { useEffect, useState } from 'react';
import Link from 'next/link';
import { useParams } from 'next/navigation';
import { AlertTriangle, ArrowLeft } from 'lucide-react';
import { Button } from '@/components/ui/button';
import { Card } from '@/components/ui/card';
import { EmptyState, ErrorState } from '@/components/ui/empty-error';
import { Skeleton } from '@/components/ui/skeleton';
import { ResultReportCard } from '@/components/placement/result-report-card';
import { fetchPlacementStoredResult, type PlacementResultReport } from '@/lib/api/placement';
import { readErrorMessage } from '@/lib/read-error-message';

/**
 * Standalone view of one stored placement result. The result id maps to an
 * OET-owned `PlacementResults` row; the API enforces learner ownership, so
 * another account's result is indistinguishable from a missing one.
 */
export default function PlacementResultPage() {
  const params = useParams<{ resultId: string }>();
  const resultId = params?.resultId;
  const [report, setReport] = useState<PlacementResultReport | null>(null);
  const [notFound, setNotFound] = useState(false);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    if (!resultId) return;
    let cancelled = false;
    fetchPlacementStoredResult(resultId)
      .then((report) => {
        if (!cancelled) setReport(report);
      })
      .catch((err) => {
        if (cancelled) return;
        const message = readErrorMessage(err, 'Could not load this result.');
        if (/not_found|404/i.test(message)) {
          setNotFound(true);
        } else {
          setError(message);
        }
      });
    return () => {
      cancelled = true;
    };
  }, [resultId]);

  if (!report) {
    if (notFound) {
      return (
        <EmptyState
          icon={<AlertTriangle className="h-7 w-7 text-warning-strong" aria-hidden="true" />}
          title="That result does not exist or belongs to another account."
          action={{ label: 'Back to your placement history', href: '/placement-test/history' }}
        />
      );
    }
    if (error) return <ErrorState title="Result unavailable" message={error} />;
    return (
      <div role="status" aria-busy="true" aria-label="Loading result…">
        <Skeleton className="h-96 rounded-2xl" />
      </div>
    );
  }

  return (
    <>
      {/* The result is the page's h1 block. */}
      <Card padding="lg">
        <ResultReportCard title="Your placement result" report={report} embedded headingLevel={1} />
      </Card>
      <div>
        <Button asChild variant="outline" size="sm">
          <Link href="/placement-test/history">
            <ArrowLeft className="h-4 w-4 rtl:rotate-180" aria-hidden="true" />
            Back to your placement history
          </Link>
        </Button>
      </div>
    </>
  );
}
