'use client';

import { useParams } from 'next/navigation';
import { ReadingAttemptReviewScreen } from '@/components/domain/reading/reading-attempt-review-screen';

/**
 * Expert — privileged (non-redacted) reading attempt review.
 *
 * Mirrors the admin attempt-review route, reusing the same shared review,
 * override, and feedback components with `area="expert"` so the client targets
 * the `/v1/expert/reading` endpoints.
 */
export default function ExpertReadingAttemptReviewPage() {
  const params = useParams();
  const raw = params?.attemptId;
  const attemptId = Array.isArray(raw) ? raw[0] : raw ?? '';

  return (
    <div className="mx-auto max-w-7xl p-6">
      <header className="mb-6">
        <p className="text-xs font-semibold uppercase tracking-wider text-primary">Reading</p>
        <h1 className="mt-1 text-2xl font-bold tracking-tight text-navy">Attempt review</h1>
        <p className="mt-1 text-sm text-muted">
          Full graded breakdown, manual override, and feedback for this reading attempt.
        </p>
      </header>
      {attemptId ? (
        <ReadingAttemptReviewScreen attemptId={attemptId} area="expert" />
      ) : (
        <p className="text-sm text-muted">No attempt id provided.</p>
      )}
    </div>
  );
}
