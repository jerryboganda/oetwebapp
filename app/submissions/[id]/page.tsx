'use client';

import { useCallback, useEffect, useMemo, useState } from 'react';
import { useParams, useRouter, useSearchParams } from 'next/navigation';
import {
  ArrowLeft,
  CheckCircle2,
  Clock,
  FileText,
  MessageSquare,
  Mic,
  Send,
  Eye,
  EyeOff,
} from 'lucide-react';
import { LearnerDashboardShell } from '@/components/layout';
import { Button } from '@/components/ui/button';
import { InlineAlert } from '@/components/ui/alert';
import { Skeleton } from '@/components/ui/skeleton';
import { LearnerPageHero, LearnerSurfaceSectionHeader } from '@/components/domain';
import {
  ScoreWithPassBadge,
  RevisionLineageChip,
  ReviewLineageCard,
} from '@/components/domain/submissions';
import { analytics } from '@/lib/analytics';
import {
  fetchSubmissionDetail,
  hideSubmission,
  unhideSubmission,
} from '@/lib/api';
import type { SubmissionDetail } from '@/lib/mock-data';

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
  const [hideBusy, setHideBusy] = useState(false);

  const load = useCallback(async (id: string) => {
    setLoading(true);
    analytics.track('content_view', { page: 'submission-detail', submissionId: id });
    try {
      const d = await fetchSubmissionDetail(id);
      setDetail(d);
      setError(null);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Could not load submission evidence.');
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    if (!submissionId) return;
    load(submissionId).catch(() => {});
  }, [submissionId, load]);

  const requestReviewHref = useMemo(() => (detail ? reviewRoute(detail) : null), [detail]);
  const requestReviewPrompt = searchParams?.get('requestReview') === '1';

  async function toggleHide() {
    if (!detail) return;
    setHideBusy(true);
    try {
      if (detail.submission.isHidden) {
        await unhideSubmission(detail.submission.id);
        analytics.track('submissions_unhidden', { submissionId: detail.submission.id });
        setDetail({ ...detail, submission: { ...detail.submission, isHidden: false } });
      } else {
        await hideSubmission(detail.submission.id);
        analytics.track('submissions_hidden', { submissionId: detail.submission.id });
        setDetail({ ...detail, submission: { ...detail.submission, isHidden: true } });
      }
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Hide/unhide failed.');
    } finally {
      setHideBusy(false);
    }
  }

  return (
    <LearnerDashboardShell pageTitle="Submission Evidence" subtitle="Reopen learner evidence, feedback, and next actions." backHref="/submissions">
      <div className="space-y-8">
        <Button variant="ghost" className="gap-2" onClick={() => router.push('/submissions')}>
          <ArrowLeft className="h-4 w-4" />
          Back to history
        </Button>

        {loading ? (
          <div className="space-y-4" aria-busy="true">
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

            {/* Score + pass badge */}
            <section className="rounded-[28px] border border-gray-200 bg-surface p-6 shadow-sm flex flex-col md:flex-row md:items-center gap-6">
              <div className="flex-1">
                <p className="text-xs font-black uppercase tracking-widest text-muted">Canonical score</p>
                <p className="mt-1 text-sm text-muted max-w-xl">
                  OET reports use the 0–500 scale. Pass is <b>Grade B (350)</b> for UK/IE/AU/NZ/CA Writing and universally for Listening, Reading, and Speaking. Writing pass for US/QA is <b>Grade C+ (300)</b>.
                </p>
              </div>
              <ScoreWithPassBadge
                scaledScore={detail.submission.scaledScore ?? null}
                scoreLabel={detail.submission.scoreEstimate}
                passState={detail.submission.passState}
                passLabel={detail.submission.passLabel}
                grade={detail.submission.grade}
              />
            </section>

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

            {/* Revision lineage */}
            {detail.revisionLineage && detail.revisionLineage.length > 1 ? (
              <RevisionLineageChip nodes={detail.revisionLineage} />
            ) : null}

            {/* Review lineage */}
            {detail.reviewLineage ? (
              <ReviewLineageCard lineage={detail.reviewLineage} />
            ) : null}

            <section className="grid gap-6 lg:grid-cols-[1.2fr_0.8fr]">
              <div className="rounded-[28px] border border-gray-200 bg-surface p-6 shadow-sm">
                <LearnerSurfaceSectionHeader
                  eyebrow="Strengths & Issues"
                  title="Keep the main evidence readable first"
                  description="The learner should understand what went well and what needs work before opening deeper panels."
                  className="mb-4"
                />

                <div className="grid gap-4 md:grid-cols-2">
                  <div className="rounded-2xl border border-emerald-100 bg-emerald-50 p-4">
                    <p className="text-xs font-black uppercase tracking-widest text-emerald-700">Strengths</p>
                    <ul className="mt-3 space-y-2 text-sm text-emerald-900">
                      {(detail.strengths.length ? detail.strengths : ['No strengths have been surfaced for this attempt yet.']).map((item) => (
                        <li key={item} className="flex gap-2">
                          <CheckCircle2 className="mt-0.5 h-4 w-4 shrink-0" />
                          <span>{item}</span>
                        </li>
                      ))}
                    </ul>
                  </div>

                  <div className="rounded-2xl border border-amber-100 bg-amber-50 p-4">
                    <p className="text-xs font-black uppercase tracking-widest text-amber-700">Needs Attention</p>
                    <ul className="mt-3 space-y-2 text-sm text-amber-900">
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

              <div className="rounded-[28px] border border-gray-200 bg-surface p-6 shadow-sm">
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
                  <Button
                    variant="outline"
                    fullWidth
                    loading={hideBusy}
                    onClick={toggleHide}
                  >
                    {detail.submission.isHidden ? <Eye className="h-4 w-4" /> : <EyeOff className="h-4 w-4" />}
                    {detail.submission.isHidden ? 'Restore to History' : 'Hide from History'}
                  </Button>
                  <Button variant="ghost" fullWidth onClick={() => router.push('/submissions')}>
                    Return to submission history
                  </Button>
                </div>
              </div>
            </section>

            {detail.criteria?.length ? (
              <section className="rounded-[28px] border border-gray-200 bg-surface p-6 shadow-sm">
                <LearnerSurfaceSectionHeader
                  eyebrow="Criterion Evidence"
                  title="Criterion-level scoring stays visible for productive skills"
                  description="This keeps Writing and Speaking evidence aligned with how the product promises human and AI review."
                  className="mb-4"
                />
                <div className="grid gap-4 md:grid-cols-2">
                  {detail.criteria.map((criterion) => (
                    <div key={criterion.name} className="rounded-2xl border border-gray-200 bg-background-light p-4">
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
              <section className="rounded-[28px] border border-gray-200 bg-surface p-6 shadow-sm">
                <LearnerSurfaceSectionHeader
                  eyebrow="Transcript Evidence"
                  title="Reopen the speaking transcript in the same learner workspace"
                  description="Transcript lines stay attached to the attempt so the learner can see what actually happened."
                  className="mb-4"
                />
                <TranscriptPanel lines={detail.transcript} evaluationId={detail.submission.evaluationId} router={router} />
              </section>
            ) : null}

            {detail.questionReview?.length ? (
              <section className="rounded-[28px] border border-gray-200 bg-surface p-6 shadow-sm">
                <LearnerSurfaceSectionHeader
                  eyebrow="Question Review"
                  title="Objective evidence stays item by item"
                  description="Reading and Listening review surfaces should still show what the learner answered and why it was right or wrong."
                  className="mb-4"
                />
                <div className="space-y-4">
                  {detail.questionReview.map((item) => (
                    <div key={item.id} className="rounded-2xl border border-gray-100 bg-background-light p-4">
                      <div className="flex items-start justify-between gap-4">
                        <div>
                          <p className="text-xs font-black uppercase tracking-widest text-muted">Question {item.number}</p>
                          <p className="mt-2 text-sm font-bold text-navy">{item.text}</p>
                        </div>
                        <span className={`rounded-full px-3 py-1 text-xs font-black uppercase tracking-widest ${item.isCorrect ? 'bg-emerald-50 text-emerald-700' : 'bg-rose-50 text-rose-700'}`}>
                          {item.isCorrect ? 'Correct' : 'Review'}
                        </span>
                      </div>
                      <div className="mt-4 grid gap-3 md:grid-cols-2">
                        <div className="rounded-2xl border border-gray-200 bg-white p-3">
                          <p className="text-xs font-black uppercase tracking-widest text-muted">Your answer</p>
                          <p className="mt-2 text-sm text-navy">{item.learnerAnswer || 'No answer recorded'}</p>
                        </div>
                        <div className="rounded-2xl border border-gray-200 bg-white p-3">
                          <p className="text-xs font-black uppercase tracking-widest text-muted">Correct answer</p>
                          <p className="mt-2 text-sm text-navy">{item.correctAnswer}</p>
                        </div>
                      </div>
                      <p className="mt-3 text-sm text-muted">{item.explanation}</p>
                      {item.transcriptExcerpt ? (
                        <div className="mt-3 rounded-2xl border border-blue-100 bg-blue-50 p-3 text-sm text-blue-900">
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

function TranscriptPanel({
  lines,
  evaluationId,
  router,
}: {
  lines: NonNullable<SubmissionDetail['transcript']>;
  evaluationId?: string;
  router: ReturnType<typeof useRouter>;
}) {
  const [expanded, setExpanded] = useState(false);
  const PAGE = 10;
  const visible = expanded ? lines : lines.slice(0, PAGE);
  return (
    <div className="space-y-3">
      {visible.map((line) => (
        <div key={line.id} className="rounded-2xl border border-gray-100 bg-background-light p-4">
          <p className="text-xs font-black uppercase tracking-widest text-muted">{line.speaker}</p>
          <p className="mt-2 text-sm leading-6 text-navy">{line.text}</p>
        </div>
      ))}
      <div className="flex gap-2 flex-wrap">
        {lines.length > PAGE ? (
          <Button variant="ghost" onClick={() => setExpanded((v) => !v)}>
            {expanded ? 'Show less' : `Show all ${lines.length} lines`}
          </Button>
        ) : null}
        {evaluationId ? (
          <Button variant="outline" onClick={() => router.push(`/speaking/transcript/${evaluationId}`)}>
            <MessageSquare className="h-4 w-4" />
            Open full transcript review
          </Button>
        ) : null}
      </div>
    </div>
  );
}
