'use client';

import { useEffect, useState } from 'react';
import { useParams, useRouter } from 'next/navigation';
import { Button } from '@/components/ui/button';
import { InlineAlert } from '@/components/ui/alert';
import { fetchSpeakingReviewDetail, fetchWritingReviewDetail, isApiError } from '@/lib/api';

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
      try {
        await fetchWritingReviewDetail(reviewId);
        if (!cancelled) {
          router.replace(`/expert/review/writing/${reviewId}`);
        }
        return;
      } catch (writingError) {
        if (cancelled) {
          return;
        }

        try {
          await fetchSpeakingReviewDetail(reviewId);
          if (!cancelled) {
            router.replace(`/expert/review/speaking/${reviewId}`);
          }
        } catch (speakingError) {
          if (!cancelled) {
            const fallbackError = isApiError(speakingError)
              ? speakingError.userMessage
              : isApiError(writingError)
                ? writingError.userMessage
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
      <div className="w-full space-y-4 rounded-3xl border border-gray-200 bg-white p-6 shadow-sm">
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
