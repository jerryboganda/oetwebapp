'use client';

import { useEffect, useState } from 'react';
import { useParams, useRouter } from 'next/navigation';
import { ArrowLeft, CheckCircle2, FileLock2, Quote, Target, XCircle } from 'lucide-react';
import { LearnerDashboardShell } from '@/components/layout';
import { Button } from '@/components/ui/button';
import { InlineAlert } from '@/components/ui/alert';
import { Skeleton } from '@/components/ui/skeleton';
import { LearnerPageHero, LearnerSurfaceSectionHeader } from '@/components/domain';
import { SelectionToVocab } from '@/components/domain/vocabulary';
import { analytics } from '@/lib/analytics';
import { getListeningReview, type ListeningReviewDto } from '@/lib/listening-api';

function firstParam(value: string | string[] | undefined) {
  return Array.isArray(value) ? value[0] : value;
}

function transcriptStateCopy(state: ListeningReviewDto['transcriptAccess']['state']) {
  if (state === 'available') return 'Transcript excerpts are available for all reviewed questions.';
  if (state === 'partial') return 'Transcript excerpts are available only for authored items that allow evidence reveal.';
  return 'Transcript excerpts are restricted for this result. The answer review remains available after submit.';
}

export default function ListeningReviewPage() {
  const params = useParams<{ id?: string | string[] }>();
  const router = useRouter();
  const attemptId = firstParam(params?.id);
  const [review, setReview] = useState<ListeningReviewDto | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    if (!attemptId) return;
    analytics.track('content_view', { page: 'listening-review', attemptId });
    getListeningReview(attemptId)
      .then(setReview)
      .catch((err) => setError(err instanceof Error ? err.message : 'Could not load transcript-backed review.'))
      .finally(() => setLoading(false));
  }, [attemptId]);

  return (
    <LearnerDashboardShell pageTitle="Listening Review" subtitle={review?.paper.title ?? 'Transcript-backed evidence for listening mistakes and distractors.'} backHref="/listening">
      <div className="space-y-8">
        <Button variant="ghost" className="gap-2" onClick={() => router.push('/listening')}>
          <ArrowLeft className="h-4 w-4" />
          Back to listening
        </Button>

        {loading ? <Skeleton className="h-48 rounded-2xl" /> : null}
        {!loading && error ? <InlineAlert variant="error">{error}</InlineAlert> : null}

        {!loading && review ? (
          <>
            <LearnerPageHero
              eyebrow="Transcript-backed Review"
              icon={Quote}
              accent="indigo"
              title="Use transcript clues to see why the answer changed"
              description={transcriptStateCopy(review.transcriptAccess.state)}
              highlights={[
                { icon: Quote, label: 'Policy', value: review.transcriptAccess.policy.replace(/_/g, ' ') },
                { icon: Target, label: 'Questions', value: `${review.itemReview.length} reviewed` },
                { icon: Target, label: 'Next drill', value: review.recommendedNextDrill ? 'Recommended' : 'Not assigned' },
              ]}
            />

            <section className="rounded-2xl border border-border bg-surface p-6 shadow-sm">
              <LearnerSurfaceSectionHeader
                eyebrow="Review Policy"
                title="Transcript support is controlled item by item"
                description="Listening transcripts should stay evidence-based and only reveal the snippets that the learner is allowed to revisit after the attempt."
                className="mb-4"
              />
              <div className="grid gap-4 md:grid-cols-[1fr_auto] md:items-center">
                <div className="rounded-2xl border border-border bg-background-light p-4 text-sm text-muted">
                  <span className="font-bold text-navy">Policy:</span> {review.transcriptAccess.policy.replace(/_/g, ' ')}
                  <br />
                  {review.transcriptAccess.reason}
                </div>
                <div className="rounded-2xl border border-primary/30 bg-primary/10 px-4 py-3 text-sm font-bold capitalize text-primary">
                  {review.transcriptAccess.state}
                </div>
              </div>
            </section>

            <section className="space-y-4">
              {review.itemReview.map((question) => (
                <div key={question.questionId} className="rounded-2xl border border-border bg-surface p-5 shadow-sm">
                  <div className="flex items-start gap-3">
                    {question.isCorrect ? (
                      <CheckCircle2 className="mt-1 h-5 w-5 shrink-0 text-success" />
                    ) : (
                      <XCircle className="mt-1 h-5 w-5 shrink-0 text-danger" />
                    )}
                    <div>
                      <p className="text-xs font-black uppercase tracking-widest text-muted">
                        Part {question.partCode} / Question {question.number}
                      </p>
                      <h2 className="mt-2 text-lg font-bold text-navy">{question.prompt}</h2>
                    </div>
                  </div>
                  <div className="mt-4 grid gap-3 md:grid-cols-2">
                    <div className="rounded-2xl border border-border bg-background-light p-4">
                      <p className="text-xs font-black uppercase tracking-widest text-muted">Your answer</p>
                      <p className="mt-2 text-sm text-navy">{question.learnerAnswer || 'No answer recorded'}</p>
                    </div>
                    <div className="rounded-2xl border border-border bg-background-light p-4">
                      <p className="text-xs font-black uppercase tracking-widest text-muted">Correct answer</p>
                      <p className="mt-2 text-sm text-navy">{question.correctAnswer}</p>
                    </div>
                  </div>
                  <p className="mt-4 text-sm text-muted">{question.explanation}</p>
                  {question.transcript?.allowed && question.transcript.excerpt ? (
                    <SelectionToVocab source="listening" sourceRefPrefix={`listening:${attemptId}:${question.questionId}`}>
                      <div className="mt-4 rounded-2xl border border-info/30 bg-info/10 p-4 text-sm text-info">
                        Transcript clue: {question.transcript.excerpt}
                      </div>
                    </SelectionToVocab>
                  ) : (
                    <div className="mt-4 flex items-start gap-3 rounded-2xl border border-border bg-background-light p-4 text-sm text-muted">
                      <FileLock2 className="mt-0.5 h-4 w-4 shrink-0" />
                      Transcript excerpt restricted for this item.
                    </div>
                  )}
                  {question.distractorExplanation ? (
                    <div className="mt-3 rounded-2xl border border-warning/30 bg-warning/10 p-4 text-sm text-warning">
                      Distractor explanation: {question.distractorExplanation}
                    </div>
                  ) : null}
                </div>
              ))}
            </section>

            {review.recommendedNextDrill ? (
              <section className="rounded-2xl border border-border bg-surface p-6 shadow-sm">
                <LearnerSurfaceSectionHeader
                  eyebrow="Next Drill"
                  title="Move directly into the error-type drill"
                  description="The learner should not need to hunt for the next recommended practice step after understanding the transcript evidence."
                  className="mb-4"
                />
                <div className="flex flex-col gap-4 rounded-2xl border border-border bg-background-light p-4 md:flex-row md:items-center md:justify-between">
                  <div>
                    <p className="font-bold text-navy">{review.recommendedNextDrill.title}</p>
                    <p className="mt-1 text-sm text-muted">{review.recommendedNextDrill.description}</p>
                  </div>
                  <Button onClick={() => router.push(review.recommendedNextDrill.launchRoute)}>
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
