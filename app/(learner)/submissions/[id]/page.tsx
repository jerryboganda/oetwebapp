'use client';

import { useEffect, useMemo, useState } from 'react';
import { useParams, useRouter, useSearchParams } from 'next/navigation';
import {
  ArrowLeft,
  CheckCircle2,
  Clock,
  FileText,
  MessageSquare,
  Mic,
  Send,
} from 'lucide-react';
import { Button } from '@/components/ui/button';
import { InlineAlert } from '@/components/ui/alert';
import { Badge } from '@/components/ui/badge';
import { Card } from '@/components/ui/card';
import { MotionSection } from '@/components/ui/motion-primitives';
import { Skeleton } from '@/components/ui/skeleton';
import { LearnerPageHero, LearnerSurfaceSectionHeader } from '@/components/domain';
import { analytics } from '@/lib/analytics';
import { fetchAuthorizedObjectUrl, fetchSubmissionDetail } from '@/lib/api';
import type { SubmissionDetail } from '@/lib/mock-data';

function reviewRoute(detail: SubmissionDetail): string | null {
  if (!detail.submission.canRequestReview) return null;
  if (detail.submission.subTest === 'Writing') return `/writing/expert-request?id=${detail.submission.id}`;
  if (detail.submission.subTest === 'Speaking') return `/speaking/expert-review/${detail.submission.id}`;
  return null;
}

function CriterionTile({ name, score, maxScore, explanation }: { name: string; score: number; maxScore: number; explanation: string }) {
  return (
    <div className="rounded-2xl border border-border bg-background-light p-4">
      <div className="flex items-center justify-between gap-4">
        <p className="min-w-0 font-bold text-navy">{name}</p>
        <span className="shrink-0 text-sm font-bold tabular-nums text-primary">{score}/{maxScore}</span>
      </div>
      <p className="mt-2 text-sm text-muted">{explanation}</p>
    </div>
  );
}

export default function SubmissionEvidencePage() {
  const params = useParams<{ id: string }>();
  const searchParams = useSearchParams();
  const router = useRouter();
  const submissionId = params?.id;
  const [detail, setDetail] = useState<SubmissionDetail | null>(null);
  const [voiceNoteUrls, setVoiceNoteUrls] = useState<Record<string, string>>({});
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

  useEffect(() => {
    const notes = detail?.voiceNotes ?? [];
    if (!notes.length) {
      return;
    }

    let cancelled = false;
    const createdUrls: string[] = [];
    Promise.all(notes.map(async (note) => {
      const objectUrl = await fetchAuthorizedObjectUrl(note.url);
      createdUrls.push(objectUrl);
      return [note.id, objectUrl] as const;
    }))
      .then((entries) => {
        if (!cancelled) setVoiceNoteUrls(Object.fromEntries(entries));
      })
      .catch(() => {
        if (!cancelled) setVoiceNoteUrls({});
      });

    return () => {
      cancelled = true;
      createdUrls.forEach((url) => URL.revokeObjectURL(url));
    };
  }, [detail?.voiceNotes]);

  const requestReviewHref = useMemo(() => (detail ? reviewRoute(detail) : null), [detail]);
  const requestReviewPrompt = searchParams?.get('requestReview') === '1';

  return (
    <>
      <div>
        <Button variant="ghost" onClick={() => router.push('/submissions')}>
          <ArrowLeft className="h-4 w-4 rtl:rotate-180" aria-hidden="true" />
          Back to history
        </Button>
      </div>

      {loading ? (
        <div className="space-y-4" aria-hidden="true">
          {[1, 2, 3].map((item) => <Skeleton key={item} className="h-40 rounded-2xl" />)}
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
                  <Send className="h-4 w-4" aria-hidden="true" />
                  Request tutor review
                </Button>
              )}
            >
              This submission is eligible for productive-skill tutor review. Continue when you are ready to spend credits.
            </InlineAlert>
          ) : null}

          <section className="grid grid-cols-1 gap-6 lg:grid-cols-[minmax(0,1.2fr)_minmax(0,0.8fr)]">
            <MotionSection delayIndex={0}>
              <Card padding="lg" className="h-full">
                <LearnerSurfaceSectionHeader
                  eyebrow="Strengths & Issues"
                  title="Keep the main evidence readable first"
                  className="mb-4"
                />

                <div className="grid grid-cols-1 gap-4 md:grid-cols-2">
                  <div className="rounded-2xl border border-success/30 bg-success/10 p-4">
                    <p className="eyebrow text-success-strong">Strengths</p>
                    <ul className="mt-3 space-y-2 text-sm text-success-strong">
                      {(detail.strengths.length ? detail.strengths : ['No strengths have been surfaced for this attempt yet.']).map((item) => (
                        <li key={item} className="flex gap-2">
                          <CheckCircle2 className="mt-0.5 h-4 w-4 shrink-0" aria-hidden="true" />
                          <span>{item}</span>
                        </li>
                      ))}
                    </ul>
                  </div>

                  <div className="rounded-2xl border border-warning/30 bg-warning/10 p-4">
                    <p className="eyebrow text-warning-strong">Needs Attention</p>
                    <ul className="mt-3 space-y-2 text-sm text-warning-strong">
                      {(detail.issues.length ? detail.issues : ['No issue summary is available for this attempt yet.']).map((item) => (
                        <li key={item} className="flex gap-2">
                          <Clock className="mt-0.5 h-4 w-4 shrink-0" aria-hidden="true" />
                          <span>{item}</span>
                        </li>
                      ))}
                    </ul>
                  </div>
                </div>
              </Card>
            </MotionSection>

            <MotionSection delayIndex={1}>
              <Card padding="lg" className="h-full">
                <LearnerSurfaceSectionHeader
                  eyebrow="Next Actions"
                  title="Move from evidence to the next step"
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
                      <Send className="h-4 w-4" aria-hidden="true" />
                      Request tutor review
                    </Button>
                  ) : null}
                  <Button variant="ghost" fullWidth onClick={() => router.push('/submissions')}>
                    Return to submission history
                  </Button>
                </div>
              </Card>
            </MotionSection>
          </section>

          {detail.voiceNotes?.length ? (
            <MotionSection>
              <Card padding="lg">
                <LearnerSurfaceSectionHeader
                  eyebrow="Dr. Ahmed Review"
                  title="Voice-note feedback"
                  description="Listen to the returned tutor feedback and keep the written notes beside the attempt evidence."
                  className="mb-4"
                />
                <div className="space-y-4">
                  {detail.voiceNotes.map((note) => (
                    <div key={note.id} className="rounded-2xl border border-border bg-background-light p-4">
                      <div className="flex flex-col gap-2 sm:flex-row sm:items-start sm:justify-between">
                        <div className="min-w-0">
                          <p className="break-words text-sm font-bold text-navy">{note.fileName}</p>
                          <p className="text-xs text-muted">Returned {new Date(note.createdAt).toLocaleString()}</p>
                        </div>
                        {note.durationSeconds ? <span className="shrink-0 text-xs font-bold tabular-nums text-muted">{Math.round(note.durationSeconds / 60)} min</span> : null}
                      </div>
                      <audio controls className="mt-4 w-full" src={voiceNoteUrls[note.id]} preload="metadata" />
                      {note.writtenNotes ? <p className="mt-4 max-w-prose text-sm leading-6 text-navy">{note.writtenNotes}</p> : null}
                      {note.transcriptText ? (
                        <details className="mt-3 rounded-xl border border-border bg-surface p-3">
                          <summary className="cursor-pointer rounded-control text-sm font-bold text-navy focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary">Transcript</summary>
                          <p className="mt-2 max-w-prose whitespace-pre-wrap text-sm leading-6 text-muted">{note.transcriptText}</p>
                        </details>
                      ) : null}
                    </div>
                  ))}
                </div>
              </Card>
            </MotionSection>
          ) : null}

          {detail.expertReview ? (
            <MotionSection>
              <Card padding="lg">
                <LearnerSurfaceSectionHeader
                  eyebrow="Dr. Ahmed Result"
                  title="Submitted rubric and final comment"
                  className="mb-4"
                />
                <div className="rounded-2xl border border-border bg-background-light p-4">
                  <div className="flex flex-col gap-2 sm:flex-row sm:items-start sm:justify-between">
                    <p className="text-sm font-bold text-navy">Final comment</p>
                    {detail.expertReview.scoreLabel ? <span className="eyebrow text-primary">{detail.expertReview.scoreLabel}</span> : null}
                  </div>
                  <p className="mt-3 max-w-prose whitespace-pre-wrap text-sm leading-6 text-muted">{detail.expertReview.finalComment}</p>
                </div>
                {detail.expertReview.criteria.length ? (
                  <div className="mt-4 grid grid-cols-1 gap-4 md:grid-cols-2">
                    {detail.expertReview.criteria.map((criterion) => (
                      <CriterionTile key={criterion.name} {...criterion} />
                    ))}
                  </div>
                ) : null}
              </Card>
            </MotionSection>
          ) : null}

          {detail.criteria?.length ? (
            <MotionSection>
              <Card padding="lg">
                <LearnerSurfaceSectionHeader
                  eyebrow="Criterion Evidence"
                  title="Criterion-level scoring stays visible for productive skills"
                  className="mb-4"
                />
                <div className="grid grid-cols-1 gap-4 md:grid-cols-2">
                  {detail.criteria.map((criterion) => (
                    <CriterionTile key={criterion.name} {...criterion} />
                  ))}
                </div>
              </Card>
            </MotionSection>
          ) : null}

          {detail.transcript?.length ? (
            <MotionSection>
              <Card padding="lg">
                <LearnerSurfaceSectionHeader
                  eyebrow="Transcript Evidence"
                  title="Reopen the speaking transcript in the same learner workspace"
                  className="mb-4"
                />
                <div className="space-y-3">
                  {detail.transcript.slice(0, 10).map((line) => (
                    <div key={line.id} className="rounded-2xl border border-border bg-background-light p-4">
                      <p className="eyebrow text-muted">{line.speaker}</p>
                      <p className="mt-2 max-w-prose text-sm leading-6 text-navy">{line.text}</p>
                    </div>
                  ))}
                  {detail.submission.evaluationId ? (
                    <Button variant="outline" onClick={() => router.push(`/speaking/transcript/${detail.submission.evaluationId}`)}>
                      <MessageSquare className="h-4 w-4" aria-hidden="true" />
                      Open full transcript review
                    </Button>
                  ) : null}
                </div>
              </Card>
            </MotionSection>
          ) : null}

          {detail.questionReview?.length ? (
            <MotionSection>
              <Card padding="lg">
                <LearnerSurfaceSectionHeader
                  eyebrow="Question Review"
                  title="Objective evidence stays item by item"
                  className="mb-4"
                />
                <div className="space-y-4">
                  {detail.questionReview.map((item) => (
                    <div key={item.id} className="rounded-2xl border border-border bg-background-light p-4">
                      <div className="flex items-start justify-between gap-4">
                        <div className="min-w-0">
                          <p className="eyebrow text-muted">Question {item.number}</p>
                          <p className="mt-2 text-sm font-bold text-navy">{item.text}</p>
                        </div>
                        <Badge variant={item.isCorrect ? 'success' : 'danger'} className="shrink-0">
                          {item.isCorrect ? 'Correct' : 'Review'}
                        </Badge>
                      </div>
                      <div className="mt-4 grid grid-cols-1 gap-3 md:grid-cols-2">
                        <div className="rounded-xl border border-border bg-surface p-3">
                          <p className="eyebrow text-muted">Your answer</p>
                          <p className="mt-2 text-sm text-navy">{item.learnerAnswer || 'No answer recorded'}</p>
                        </div>
                        <div className="rounded-xl border border-border bg-surface p-3">
                          <p className="eyebrow text-muted">Correct answer</p>
                          <p className="mt-2 text-sm text-navy">{item.correctAnswer}</p>
                        </div>
                      </div>
                      <p className="mt-3 max-w-prose text-sm text-muted">{item.explanation}</p>
                      {item.transcriptExcerpt ? (
                        <div className="mt-3 rounded-xl border border-info/30 bg-info/10 p-3 text-sm text-info">
                          Transcript clue: {item.transcriptExcerpt}
                        </div>
                      ) : null}
                    </div>
                  ))}
                </div>
              </Card>
            </MotionSection>
          ) : null}
        </>
      ) : null}
    </>
  );
}
