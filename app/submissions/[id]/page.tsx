'use client';

import { LearnerPageHero, LearnerSurfaceSectionHeader } from "@/components/domain/learner-surface";
import { LearnerDashboardShell } from "@/components/layout/learner-dashboard-shell";
import { InlineAlert } from '@/components/ui/alert';
import { Button } from '@/components/ui/button';
import { Skeleton } from '@/components/ui/skeleton';
import { analytics } from '@/lib/analytics';
import { fetchSubmissionDetail } from '@/lib/api';
import type { SubmissionDetail } from '@/lib/mock-data';
import {
    ArrowLeft,
    CheckCircle2,
    Clock,
    FileText,
    MessageSquare,
    Mic,
    Send
} from 'lucide-react';
import { useParams, useRouter, useSearchParams } from 'next/navigation';
import { useEffect, useMemo, useState } from 'react';

function reviewRoute(detail: SubmissionDetail): string | null {
  if (!detail.submission.canRequestReview) return null;
  if (detail.submission.subTest === 'Writing') return `/writing/expert-request?id=${detail.submission.id}`;
  if (detail.submission.subTest === 'Speaking') return `/speaking/expert-review/${detail.submission.id}`;
  return null;
}

export default function SubmissionEvidencePage() {
  const params = useParams<{ id: string }>();
  const searchParams = useSearchParams();
  const router = useRouter();
  const submissionId = params?.id;
  const [detail, setDetail] = useState<SubmissionDetail | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    if (!submissionId) return;
    analytics.track('content_view', { page: 'submission-detail', submissionId });
    fetchSubmissionDetail(submissionId)
      .then(setDetail)
      .catch((err) => setError(err instanceof Error ? err.message : 'Could not load submission evidence.'))
      .finally(() => setLoading(false));
  }, [submissionId]);

  const requestReviewHref = useMemo(() => (detail ? reviewRoute(detail) : null), [detail]);
  const requestReviewPrompt = searchParams?.get('requestReview') === '1';

  return (
    <LearnerDashboardShell pageTitle="Submission Evidence" subtitle="Reopen learner evidence, feedback, and next actions." backHref="/submissions">
      <div className="space-y-8">
        <Button variant="ghost" className="gap-2" onClick={() => router.push('/submissions')}>
          <ArrowLeft className="h-4 w-4" />
          Back to history
        </Button>

        {loading ? (
          <div className="space-y-4">
            {[1, 2, 3].map((item) => <Skeleton key={item} className="h-40 rounded-[24px]" />)}
          </div>
        ) : null}

        {!loading && error ? <InlineAlert variant="error">{error}</InlineAlert> : null}

        {!loading && detail ? (
          <>
            <LearnerPageHero
              eyebrow="Evidence Workspace"
              icon={detail.submission.subTest === 'Speaking' ? Mic : FileText}
              accent="slate"
              title={detail.evidenceSummary.title}
              description="This workspace keeps the learner’s attempt, score direction, review state, and next actions visible in one place."
              highlights={[
                { icon: CheckCircle2, label: 'Score', value: detail.evidenceSummary.scoreLabel || 'Pending' },
                { icon: Clock, label: 'Attempt state', value: detail.evidenceSummary.stateLabel },
                { icon: MessageSquare, label: 'Review status', value: detail.evidenceSummary.reviewLabel },
              ]}
            />

            {requestReviewPrompt && requestReviewHref ? (
              <InlineAlert
                variant="info"
                title="Review request available"
                action={(
                  <Button onClick={() => router.push(requestReviewHref)}>
                    <Send className="h-4 w-4" />
                    Request expert review
                  </Button>
                )}
              >
                This submission is eligible for productive-skill expert review. Continue when you are ready to spend credits.
              </InlineAlert>
            ) : null}

            <section className="grid gap-6 lg:grid-cols-[1.2fr_0.8fr]">
              <div className="rounded-[28px] border border-border bg-surface p-6 shadow-sm">
                <LearnerSurfaceSectionHeader
                  eyebrow="Strengths & Issues"
                  title="Keep the main evidence readable first"
                  description="The learner should understand what went well and what needs work before opening deeper panels."
                  className="mb-4"
                />

                <div className="grid gap-4 md:grid-cols-2">
                  <div className="rounded-2xl border border-success/30 bg-success/10 p-4">
                    <p className="text-xs font-black uppercase tracking-widest text-success">Strengths</p>
                    <ul className="mt-3 space-y-2 text-sm text-success">
                      {(detail.strengths.length ? detail.strengths : ['No strengths have been surfaced for this attempt yet.']).map((item) => (
                        <li key={item} className="flex gap-2">
                          <CheckCircle2 className="mt-0.5 h-4 w-4 shrink-0" />
                          <span>{item}</span>
                        </li>
                      ))}
                    </ul>
                  </div>

                  <div className="rounded-2xl border border-warning/30 bg-amber-50/60 p-4">
                    <p className="text-xs font-black uppercase tracking-widest text-warning">Needs Attention</p>
                    <ul className="mt-3 space-y-2 text-sm text-warning">
                      {(detail.issues.length ? detail.issues : ['No issue summary is available for this attempt yet.']).map((item) => (
                        <li key={item} className="flex gap-2">
                          <Clock className="mt-0.5 h-4 w-4 shrink-0" />
                          <span>{item}</span>
                        </li>
                      ))}
                    </ul>
                  </div>
                </div>
              </div>

              <div className="rounded-[28px] border border-border bg-surface p-6 shadow-sm">
                <LearnerSurfaceSectionHeader
                  eyebrow="Next Actions"
                  title="Move from evidence to the next step"
                  description="The learner should know whether to reopen, compare, or request human review."
                  className="mb-4"
                />

                <div className="space-y-3">
                  {detail.submission.actions.compareRoute ? (
                    <Button variant="outline" fullWidth onClick={() => router.push(detail.submission.actions.compareRoute!)}>
                      Compare attempts
                    </Button>
                  ) : null}
                  {requestReviewHref ? (
                    <Button fullWidth onClick={() => router.push(requestReviewHref)}>
                      <Send className="h-4 w-4" />
                      Request expert review
                    </Button>
                  ) : null}
                  <Button variant="ghost" fullWidth onClick={() => router.push('/submissions')}>
                    Return to submission history
                  </Button>
                </div>
              </div>
            </section>

            {detail.criteria?.length ? (
              <section className="rounded-[28px] border border-border bg-surface p-6 shadow-sm">
                <LearnerSurfaceSectionHeader
                  eyebrow="Criterion Evidence"
                  title="Criterion-level scoring stays visible for productive skills"
                  description="This keeps Writing and Speaking evidence aligned with how the product promises human and AI review."
                  className="mb-4"
                />
                <div className="grid gap-4 md:grid-cols-2">
                  {detail.criteria.map((criterion) => (
                    <div key={criterion.name} className="rounded-2xl border border-border bg-background-light p-4">
                      <div className="flex items-center justify-between gap-4">
                        <p className="font-bold text-navy">{criterion.name}</p>
                        <span className="text-sm font-black text-primary">{criterion.score}/{criterion.maxScore}</span>
                      </div>
                      <p className="mt-2 text-sm text-muted">{criterion.explanation}</p>
                    </div>
                  ))}
                </div>
              </section>
            ) : null}

            {detail.transcript?.length ? (
              <section className="rounded-[28px] border border-border bg-surface p-6 shadow-sm">
                <LearnerSurfaceSectionHeader
                  eyebrow="Transcript Evidence"
                  title="Reopen the speaking transcript in the same learner workspace"
                  description="Transcript lines stay attached to the attempt so the learner can see what actually happened."
                  className="mb-4"
                />
                <div className="space-y-3">
                  {detail.transcript.slice(0, 10).map((line) => (
                    <div key={line.id} className="rounded-2xl border border-border bg-background-light p-4">
                      <p className="text-xs font-black uppercase tracking-widest text-muted">{line.speaker}</p>
                      <p className="mt-2 text-sm leading-6 text-navy">{line.text}</p>
                    </div>
                  ))}
                  {detail.submission.evaluationId ? (
                    <Button variant="outline" onClick={() => router.push(`/speaking/transcript/${detail.submission.evaluationId}`)}>
                      <MessageSquare className="h-4 w-4" />
                      Open full transcript review
                    </Button>
                  ) : null}
                </div>
              </section>
            ) : null}

            {detail.questionReview?.length ? (
              <section className="rounded-[28px] border border-border bg-surface p-6 shadow-sm">
                <LearnerSurfaceSectionHeader
                  eyebrow="Question Review"
                  title="Objective evidence stays item by item"
                  description="Reading and Listening review surfaces should still show what the learner answered and why it was right or wrong."
                  className="mb-4"
                />
                <div className="space-y-4">
                  {detail.questionReview.map((item) => (
                    <div key={item.id} className="rounded-2xl border border-border bg-background-light p-4">
                      <div className="flex items-start justify-between gap-4">
                        <div>
                          <p className="text-xs font-black uppercase tracking-widest text-muted">Question {item.number}</p>
                          <p className="mt-2 text-sm font-bold text-navy">{item.text}</p>
                        </div>
                        <span className={`rounded-full px-3 py-1 text-xs font-black uppercase tracking-widest ${item.isCorrect ? 'bg-success/10 text-success' : 'bg-danger/10 text-danger'}`}>
                          {item.isCorrect ? 'Correct' : 'Review'}
                        </span>
                      </div>
                      <div className="mt-4 grid gap-3 md:grid-cols-2">
                        <div className="rounded-2xl border border-border bg-surface p-3">
                          <p className="text-xs font-black uppercase tracking-widest text-muted">Your answer</p>
                          <p className="mt-2 text-sm text-navy">{item.learnerAnswer || 'No answer recorded'}</p>
                        </div>
                        <div className="rounded-2xl border border-border bg-surface p-3">
                          <p className="text-xs font-black uppercase tracking-widest text-muted">Correct answer</p>
                          <p className="mt-2 text-sm text-navy">{item.correctAnswer}</p>
                        </div>
                      </div>
                      <p className="mt-3 text-sm text-muted">{item.explanation}</p>
                      {item.transcriptExcerpt ? (
                        <div className="mt-3 rounded-2xl border border-info/30 bg-info/10 p-3 text-sm text-info">
                          Transcript clue: {item.transcriptExcerpt}
                        </div>
                      ) : null}
                    </div>
                  ))}
                </div>
              </section>
            ) : null}
          </>
        ) : null}
      </div>
    </LearnerDashboardShell>
  );
}
