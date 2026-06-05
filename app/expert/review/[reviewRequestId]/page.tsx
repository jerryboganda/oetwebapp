'use client';

import { useEffect, useState } from 'react';
import { useParams, useRouter } from 'next/navigation';
import { Button } from '@/components/ui/button';
import { InlineAlert } from '@/components/ui/alert';
import { fetchSpeakingReviewDetail, isApiError } from '@/lib/api';
import { getListeningExpertBundle } from '@/lib/expert-listening-api';

export default function ExpertReviewRedirectPage() {
  const params = useParams();
  const router = useRouter();
  const reviewRequestId = Array.isArray(params?.reviewRequestId) ? params.reviewRequestId[0] : params?.reviewRequestId;
  const [error, setError] = useState<string | null>(null);
  const missingIdError = reviewRequestId ? null : 'This review link is missing its review id.';

  useEffect(() => {
    const reviewId = reviewRequestId ?? '';
    if (!reviewId) {
      return;
    }

    let cancelled = false;

    async function resolveReviewRoute() {
      // Writing reviews are V2 submission-based and open directly at
      // /expert/review/writing/{submissionId} from the writing review queue. A
      // ReviewRequest (`review-…`) id has no WritingSubmission and cannot resolve in
      // the submission-keyed marking workspace, so this dispatcher only routes the
      // remaining ReviewRequest-keyed surfaces (speaking, then listening).
      try {
        await fetchSpeakingReviewDetail(reviewId);
        if (!cancelled) {
          router.replace(`/expert/review/speaking/${reviewId}`);
        }
        return;
      } catch (speakingError) {
        if (cancelled) return;

        try {
          await getListeningExpertBundle(reviewId);
          if (!cancelled) {
            router.replace(`/expert/review/listening/${reviewId}`);
          }
        } catch (listeningError) {
          if (!cancelled) {
            const fallbackError =
              isApiError(listeningError)
                ? listeningError.userMessage
                : isApiError(speakingError)
                  ? speakingError.userMessage
                  : 'We could not resolve this review workspace.';
            setError(fallbackError);
          }
        }
      }
    }

    void resolveReviewRoute();

    return () => {
      cancelled = true;
    };
  }, [reviewRequestId, router]);

  const visibleError = missingIdError ?? error;

  if (!visibleError) {
    return (
      <div className="flex min-h-[50vh] items-center justify-center px-6">
        <div className="text-center">
          <p className="text-lg font-semibold text-navy">Opening review workspace...</p>
          <p className="mt-2 text-sm text-muted">We are routing you to the correct review surface.</p>
        </div>
      </div>
    );
  }

  return (
    <div className="mx-auto flex min-h-[50vh] max-w-xl items-center justify-center px-6">
      <div className="w-full space-y-4 rounded-3xl border border-border bg-surface p-6 shadow-sm">
        <InlineAlert variant="error">{visibleError}</InlineAlert>
        <div className="flex justify-end">
          <Button type="button" onClick={() => router.push('/expert/queue')}>
            Back To Review Queue
          </Button>
        </div>
      </div>
    </div>
  );
}
