'use client';

import { useEffect, useRef, useState } from 'react';
import { useParams, useRouter } from 'next/navigation';
import { ArrowLeft, CheckCircle2, Clock, FileLock2, Quote, Target, Volume2, XCircle } from 'lucide-react';
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

function formatMilliseconds(value: number | null | undefined) {
  if (value == null) return null;
  const seconds = Math.floor(value / 1000);
  const minutes = Math.floor(seconds / 60);
  const remainder = seconds % 60;
  return `${minutes}:${remainder.toString().padStart(2, '0')}`;
}

export default function ListeningReviewPage() {
  const params = useParams<{ id?: string | string[] }>();
  const router = useRouter();
  const attemptId = firstParam(params?.id);
  const audioRef = useRef<HTMLAudioElement>(null);
  const evidenceTimerRef = useRef<ReturnType<typeof setTimeout> | null>(null);
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

  useEffect(() => () => {
    if (evidenceTimerRef.current) clearTimeout(evidenceTimerRef.current);
  }, []);

  const playEvidence = (startMs: number | null | undefined, endMs: number | null | undefined) => {
    const audio = audioRef.current;
    if (!audio || startMs == null) return;
    if (evidenceTimerRef.current) clearTimeout(evidenceTimerRef.current);
    audio.currentTime = Math.max(0, startMs / 1000);
    void audio.play();
    if (endMs != null && endMs > startMs) {
      evidenceTimerRef.current = setTimeout(() => audio.pause(), endMs - startMs);
    }
  };

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

            {review.paper.audioUrl ? (
              <section className="rounded-2xl border border-border bg-surface p-6 shadow-sm">
                <LearnerSurfaceSectionHeader
                  eyebrow="Evidence Player"
                  title="Replay the proof window, not the whole paper"
                  description="Use authored evidence times to jump directly to the audio span that supports each answer."
                  className="mb-4"
                />
                <audio ref={audioRef} src={review.paper.audioUrl} controls className="w-full" />
              </section>
            ) : null}

            {(review.paper.extracts?.length ?? 0) > 0 ? (
              <section className="rounded-2xl border border-border bg-surface p-6 shadow-sm">
                <LearnerSurfaceSectionHeader
                  eyebrow="Extract Map"
                  title="Accent, speaker, and audio windows"
                  description="The review keeps the learner oriented by extract instead of treating the paper as one undifferentiated audio file."
                  className="mb-4"
                />
                <div className="grid gap-3 md:grid-cols-2">
                  {review.paper.extracts?.map((extract) => (
                    <div key={`${extract.partCode}-${extract.displayOrder}`} className="rounded-2xl border border-border bg-background-light p-4 text-sm">
                      <div className="flex items-center justify-between gap-2">
                        <p className="font-bold text-navy">{extract.partCode} · {extract.title}</p>
                        <p className="text-xs font-black uppercase tracking-widest text-muted">{extract.kind}</p>
                      </div>
                      <p className="mt-2 text-muted">{extract.accentCode ?? 'Accent not specified'}</p>
                      {extract.speakers.length > 0 ? (
                        <p className="mt-1 text-muted">{extract.speakers.map((speaker) => speaker.role).join(', ')}</p>
                      ) : null}
                      {extract.audioStartMs != null || extract.audioEndMs != null ? (
                        <p className="mt-1 text-muted">
                          {formatMilliseconds(extract.audioStartMs) ?? '0:00'} - {formatMilliseconds(extract.audioEndMs) ?? 'end'}
                        </p>
                      ) : null}
                    </div>
                  ))}
                </div>
              </section>
            ) : null}

            {review.transcriptSegments.length > 0 ? (
              <section className="rounded-2xl border border-border bg-surface p-6 shadow-sm">
                <LearnerSurfaceSectionHeader
                  eyebrow="Transcript Segments"
                  title="Scan the time-coded script"
                  description="Segments are grouped with part and speaker metadata where the author supplied it."
                  className="mb-4"
                />
                <div className="max-h-96 space-y-2 overflow-auto pr-2">
                  {review.transcriptSegments.map((segment, index) => (
                    <button
                      key={`${segment.startMs}-${segment.endMs}-${index}`}
                      type="button"
                      onClick={() => playEvidence(segment.startMs, segment.endMs)}
                      className="block w-full rounded-xl border border-border bg-background-light p-3 text-left text-sm transition hover:border-border-hover hover:bg-surface"
                    >
                      <span className="text-xs font-black uppercase tracking-widest text-muted">
                        {formatMilliseconds(segment.startMs)}-{formatMilliseconds(segment.endMs)} {segment.partCode ?? ''} {segment.speakerId ?? ''}
                      </span>
                      <span className="mt-1 block text-navy">{segment.text}</span>
                    </button>
                  ))}
                </div>
              </section>
            ) : null}

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
                  <div className="mt-4 flex flex-wrap items-center gap-2 text-xs text-muted">
                    {question.speakerAttitude ? (
                      <span className="inline-flex items-center gap-1 rounded-lg bg-background-light px-3 py-2 font-semibold capitalize text-navy">
                        <Quote className="h-4 w-4" /> {question.speakerAttitude.replace(/_/g, ' ')}
                      </span>
                    ) : null}
                    {question.transcriptEvidenceStartMs != null ? (
                      <button
                        type="button"
                        onClick={() => playEvidence(question.transcriptEvidenceStartMs, question.transcriptEvidenceEndMs)}
                        className="inline-flex items-center gap-1 rounded-lg bg-info/10 px-3 py-2 font-semibold text-info transition hover:bg-info/20"
                      >
                        <Volume2 className="h-4 w-4" /> Evidence {formatMilliseconds(question.transcriptEvidenceStartMs)}
                        {question.transcriptEvidenceEndMs != null ? `-${formatMilliseconds(question.transcriptEvidenceEndMs)}` : ''}
                      </button>
                    ) : null}
                    {question.transcriptEvidenceStartMs == null && question.transcriptEvidenceEndMs == null ? (
                      <span className="inline-flex items-center gap-1 rounded-lg bg-background-light px-3 py-2">
                        <Clock className="h-4 w-4" /> No time-coded evidence
                      </span>
                    ) : null}
                  </div>
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
                  {question.optionAnalysis?.length ? (
                    <div className="mt-4 grid gap-3 md:grid-cols-3">
                      {question.optionAnalysis.map((option) => (
                        <div
                          key={`${question.questionId}-${option.optionLabel}`}
                          className={`rounded-2xl border p-4 text-sm ${option.isCorrect ? 'border-success/30 bg-success/10 text-success' : 'border-warning/30 bg-warning/10 text-warning'}`}
                        >
                          <p className="font-bold text-navy">{option.optionLabel}. {option.optionText}</p>
                          <p className="mt-2 text-xs font-black uppercase tracking-widest">{option.isCorrect ? 'Correct' : option.distractorCategory?.replace(/_/g, ' ') ?? 'Distractor'}</p>
                          {option.whyMarkdown ? <p className="mt-2 text-xs leading-5">{option.whyMarkdown}</p> : null}
                        </div>
                      ))}
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
