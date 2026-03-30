'use client';

import { useEffect, useState } from 'react';
import { useParams, useRouter } from 'next/navigation';
import { ArrowLeft, Quote, Target } from 'lucide-react';
import { LearnerDashboardShell } from '@/components/layout';
import { Button } from '@/components/ui/button';
import { InlineAlert } from '@/components/ui/alert';
import { Skeleton } from '@/components/ui/skeleton';
import { LearnerPageHero, LearnerSurfaceSectionHeader } from '@/components/domain';
import { analytics } from '@/lib/analytics';
import { fetchListeningReview } from '@/lib/api';
import type { ListeningReview } from '@/lib/mock-data';

export default function ListeningReviewPage() {
  const params = useParams<{ id: string }>();
  const router = useRouter();
  const taskId = params?.id;
  const [review, setReview] = useState<ListeningReview | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    if (!taskId) return;
    analytics.track('content_view', { page: 'listening-review', taskId });
    fetchListeningReview(taskId)
      .then(setReview)
      .catch((err) => setError(err instanceof Error ? err.message : 'Could not load transcript-backed review.'))
      .finally(() => setLoading(false));
  }, [taskId]);

  return (
    <LearnerDashboardShell pageTitle="Listening Review" subtitle="Transcript-backed evidence for listening mistakes and distractors." backHref="/listening">
      <div className="space-y-8">
        <Button variant="ghost" className="gap-2" onClick={() => router.push('/listening')}>
          <ArrowLeft className="h-4 w-4" />
          Back to listening
        </Button>

        {loading ? <Skeleton className="h-48 rounded-[24px]" /> : null}
        {!loading && error ? <InlineAlert variant="error">{error}</InlineAlert> : null}

        {!loading && review ? (
          <>
            <LearnerPageHero
              eyebrow="Transcript-backed Review"
              icon={Quote}
              accent="indigo"
              title="Use transcript clues to see why the answer changed"
              description="Review the evidence behind each listening error before you move into the next drill."
              highlights={[
                { icon: Quote, label: 'Policy', value: review.transcriptPolicy.replace(/_/g, ' ') },
                { icon: Target, label: 'Questions', value: `${review.questions.length} reviewed` },
                { icon: Target, label: 'Next drill', value: review.recommendedDrill ? 'Recommended' : 'Not assigned' },
              ]}
            />

            <section className="rounded-[28px] border border-gray-200 bg-surface p-6 shadow-sm">
              <LearnerSurfaceSectionHeader
                eyebrow="Review Policy"
                title="Transcript support is controlled item by item"
                description="Listening transcripts should stay evidence-based and only reveal the snippets that the learner is allowed to revisit after the attempt."
                className="mb-4"
              />
              <div className="rounded-2xl border border-gray-100 bg-background-light p-4 text-sm text-muted">
                Policy: {review.transcriptPolicy.replace(/_/g, ' ')}
              </div>
            </section>

            <section className="space-y-4">
              {review.questions.map((question) => (
                <div key={question.id} className="rounded-[24px] border border-gray-200 bg-surface p-5 shadow-sm">
                  <p className="text-xs font-black uppercase tracking-widest text-muted">Question {question.number}</p>
                  <h2 className="mt-2 text-lg font-bold text-navy">{question.text}</h2>
                  <div className="mt-4 grid gap-3 md:grid-cols-2">
                    <div className="rounded-2xl border border-gray-100 bg-background-light p-4">
                      <p className="text-xs font-black uppercase tracking-widest text-muted">Your answer</p>
                      <p className="mt-2 text-sm text-navy">{question.learnerAnswer || 'No answer recorded'}</p>
                    </div>
                    <div className="rounded-2xl border border-gray-100 bg-background-light p-4">
                      <p className="text-xs font-black uppercase tracking-widest text-muted">Correct answer</p>
                      <p className="mt-2 text-sm text-navy">{question.correctAnswer}</p>
                    </div>
                  </div>
                  <p className="mt-4 text-sm text-muted">{question.explanation}</p>
                  {question.transcriptExcerpt ? (
                    <div className="mt-4 rounded-2xl border border-blue-100 bg-blue-50 p-4 text-sm text-blue-900">
                      Transcript clue: {question.transcriptExcerpt}
                    </div>
                  ) : null}
                  {question.distractorExplanation ? (
                    <div className="mt-3 rounded-2xl border border-amber-100 bg-amber-50 p-4 text-sm text-amber-900">
                      Distractor explanation: {question.distractorExplanation}
                    </div>
                  ) : null}
                </div>
              ))}
            </section>

            {review.recommendedDrill ? (
              <section className="rounded-[28px] border border-gray-200 bg-surface p-6 shadow-sm">
                <LearnerSurfaceSectionHeader
                  eyebrow="Next Drill"
                  title="Move directly into the error-type drill"
                  description="The learner should not need to hunt for the next recommended practice step after understanding the transcript evidence."
                  className="mb-4"
                />
                <div className="flex flex-col gap-4 rounded-2xl border border-gray-100 bg-background-light p-4 md:flex-row md:items-center md:justify-between">
                  <div>
                    <p className="font-bold text-navy">{review.recommendedDrill.title}</p>
                    <p className="mt-1 text-sm text-muted">{review.recommendedDrill.description}</p>
                  </div>
                  <Button onClick={() => router.push(`/listening/drills/${review.recommendedDrill!.id}`)}>
                    <Target className="h-4 w-4" />
                    Open recommended drill
                  </Button>
                </div>
              </section>
            ) : null}
          </>
        ) : null}
      </div>
    </LearnerDashboardShell>
  );
}
