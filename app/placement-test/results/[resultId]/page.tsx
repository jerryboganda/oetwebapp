'use client';

import { useEffect, useState } from 'react';
import Link from 'next/link';
import { useParams } from 'next/navigation';
import { AlertTriangle, Loader2 } from 'lucide-react';
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
    return (
      <div className="flex min-h-[40vh] flex-col items-center justify-center gap-3 text-sm text-muted">
        {notFound ? (
          <>
            <AlertTriangle className="h-6 w-6 text-warning" aria-hidden />
            <p>That result does not exist or belongs to another account.</p>
            <Link href="/placement-test/history" className="text-primary underline">
              Back to your placement history
            </Link>
          </>
        ) : (
          <>
            {error ? <p role="alert" className="text-danger">{error}</p> : null}
            <Loader2 className="h-5 w-5 animate-spin" aria-hidden /> Loading result…
          </>
        )}
      </div>
    );
  }

  return (
    <div className="space-y-4">
      <ResultReportCard title="Your placement result" report={report} />
      <div className="text-center">
        <Link href="/placement-test/history" className="text-sm text-primary underline">
          Back to your placement history
        </Link>
      </div>
    </div>
  );
}
