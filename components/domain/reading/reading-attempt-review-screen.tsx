'use client';

import { useCallback, useEffect, useState } from 'react';
import { AlertCircle, Loader2 } from 'lucide-react';
import {
  getPrivilegedReadingAttemptReview,
  type ReadingPrivilegedAttemptReview,
  type ReadingTutorArea,
} from '@/lib/reading-tutor-api';
import { PrivilegedAttemptReview } from './privileged-attempt-review';
import { ReadingOverridePanel } from './reading-override-panel';
import { ReadingFeedbackPanel } from './reading-feedback-panel';

/**
 * ReadingAttemptReviewScreen — composes the privileged review, score override,
 * and feedback panels with shared data-loading + loading/empty/error states.
 * Mounted by both the admin and expert attempt-review routes (the only
 * difference is the `area` prop, which selects the route prefix in the client).
 */

export interface ReadingAttemptReviewScreenProps {
  attemptId: string;
  area: ReadingTutorArea;
}

export function ReadingAttemptReviewScreen({ attemptId, area }: ReadingAttemptReviewScreenProps) {
  const [review, setReview] = useState<ReadingPrivilegedAttemptReview | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  const load = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      const data = await getPrivilegedReadingAttemptReview(attemptId, area);
      setReview(data);
    } catch {
      setError('Failed to load the attempt review.');
    } finally {
      setLoading(false);
    }
  }, [attemptId, area]);

  useEffect(() => {
    void load();
  }, [load]);

  if (loading) {
    return (
      <div
        role="status"
        className="flex items-center justify-center gap-2 rounded-xl border border-slate-200 bg-white py-16 text-sm text-slate-500 dark:border-slate-700 dark:bg-slate-900 dark:text-slate-400"
      >
        <Loader2 className="h-5 w-5 animate-spin" aria-hidden="true" />
        Loading attempt review…
      </div>
    );
  }

  if (error || !review) {
    return (
      <div
        role="alert"
        className="flex flex-col items-center gap-3 rounded-xl border border-red-200 bg-red-50 py-16 text-center text-sm text-red-700 dark:border-red-900 dark:bg-red-950/40 dark:text-red-300"
      >
        <AlertCircle className="h-6 w-6" aria-hidden="true" />
        <p>{error ?? 'Attempt not found.'}</p>
        <button
          type="button"
          onClick={() => void load()}
          className="rounded-lg border border-red-300 px-4 py-2 font-medium text-red-700 transition-colors hover:bg-red-100 dark:border-red-800 dark:text-red-300 dark:hover:bg-red-950/60"
        >
          Retry
        </button>
      </div>
    );
  }

  return (
    <div className="grid gap-6 lg:grid-cols-[minmax(0,1fr)_360px]">
      <PrivilegedAttemptReview review={review} />
      <div className="space-y-6">
        <ReadingOverridePanel
          attemptId={attemptId}
          area={area}
          review={review}
          onUpdated={setReview}
        />
        <ReadingFeedbackPanel attemptId={attemptId} area={area} />
      </div>
    </div>
  );
}
