'use client';

import { useEffect, useMemo, useRef, useState } from 'react';
import { useParams, useRouter } from 'next/navigation';
import { ArrowLeft, CheckCircle2, Clock, GraduationCap, Headphones, MinusCircle, Quote, Tag, Target, Volume2, XCircle } from 'lucide-react';
import { LearnerDashboardShell } from '@/components/layout';
import { Button } from '@/components/ui/button';
import { InlineAlert } from '@/components/ui/alert';
import { Skeleton } from '@/components/ui/skeleton';
import { LearnerPageHero, LearnerSurfaceSectionHeader } from '@/components/domain';
import { MarkdownContent } from '@/components/ui/markdown-content';
import { AnswerComparisonCard } from '@/components/domain/results/answer-comparison-card';
import { ReportAnswerControl } from '@/components/domain/results/report-answer-control';
import { ResultsScorePanel } from '@/components/domain/results/results-score-panel';
import { ScoreBandGraph } from '@/components/domain/results/score-band-graph';
import { ScoreConversionEvidence } from '@/components/domain/results/score-conversion-evidence';
import { ListeningPartBreakdown } from '@/components/domain/results/listening-part-breakdown';
import { TimeUsedSummary } from '@/components/domain/results/time-used-summary';
import { ListeningQuestionPaperViewer } from '@/components/domain/listening/ListeningQuestionPaperViewer';
import { ListeningFullTranscriptViewer } from '@/components/domain/listening/ListeningFullTranscriptViewer';
import { analytics } from '@/lib/analytics';
import { fetchAuthorizedObjectUrl } from '@/lib/api';
import { getListeningReview, listListeningAnswerKeyReports, type ListeningReviewDto } from '@/lib/listening-api';
import { hasApprovedListeningConversion } from '@/lib/listening-result-display';
import { getListeningExpertFeedback } from '@/lib/expert-listening-api';
import type { ListeningExpertBundle } from '@/lib/types/expert';

function firstParam(value: string | string[] | undefined) {
  return Array.isArray(value) ? value[0] : value;
}

function transcriptStateCopy(review: ListeningReviewDto | null) {
  if (!review) return 'Full transcript for the submitted part is available after submission.';
  const hasSegments = (review.transcriptSegments?.length ?? 0) > 0;
  const parentParts = new Set<string>();
  for (const seg of review.transcriptSegments ?? []) {
    const p = (seg.partCode ?? '').trim().toUpperCase();
    if (p.startsWith('A')) parentParts.add('A');
    else if (p.startsWith('C')) parentParts.add('C');
    else parentParts.add('B');
  }
  // Fallback to itemReview when segments not authored yet
  if (parentParts.size === 0) {
    for (const item of review.itemReview ?? []) {
      const p = (item.partCode ?? '').trim().toUpperCase();
      if (p.startsWith('A')) parentParts.add('A');
      else if (p.startsWith('C')) parentParts.add('C');
      else parentParts.add('B');
    }
  }
  const partsLabel = ['A', 'B', 'C'].filter((p) => parentParts.has(p)).join(', ') || 'submitted part';
  if (hasSegments) return `Full transcript available for the submitted part${parentParts.size > 1 ? 's' : ''}: Part ${partsLabel}. Other parts remain hidden until submitted. Audio replay is unlimited.`;
  if (review.transcriptAccess.state === 'available') return 'Full transcript for the submitted part is available. Audio replay is unlimited.';
  if (review.transcriptAccess.state === 'partial') return `Transcript available for submitted part${parentParts.size > 1 ? 's' : ''}: Part ${partsLabel}. Other parts remain hidden until submitted.`;
  return 'Full transcript will be available after the part is submitted. Transcript-backed evidence is shown for reviewed questions.';
}

function formatMilliseconds(value: number | null | undefined) {
  if (value == null) return null;
  const seconds = Math.floor(value / 1000);
  const minutes = Math.floor(seconds / 60);
  const remainder = seconds % 60;
  return `${minutes}:${remainder.toString().padStart(2, '0')}`;
}

const LISTENING_REVIEW_AUDIO_SECTIONS = ['A1', 'A2', 'B', 'C1', 'C2'] as const;

const LISTENING_REVIEW_SECTION_LABEL: Record<string, string> = {
  A1: 'Part A · 1',
  A2: 'Part A · 2',
  B: 'Part B',
  C1: 'Part C · 1',
  C2: 'Part C · 2',
};

/**
 * Collapse an extract/question part code (A1, A2, B1..B6, C1, C2) to the
 * learner audio section. Part B's six sub-parts all share one "B" file.
 */
function reviewSectionForPartCode(partCode: string | null | undefined): string {
  const code = (partCode ?? '').trim().toUpperCase();
  return code.startsWith('B') ? 'B' : code;
}

/**
 * Map the relational grader's `missReason` (Wave 1) and the legacy
 * JSON-pathway `errorType` to a single human-readable chip label. Returns
 * null when the answer was correct or no reason was recorded.
 */
function missReasonChip(item: ListeningReviewDto['itemReview'][number]): {
  label: string;
  hint: string;
} | null {
  if (item.isCorrect) return null;
  const reason = (item.missReason ?? item.errorType ?? '').toString();
  if (!reason) return null;
  const key = reason.toLowerCase();
  switch (key) {
    case 'spellingerror':
    case 'spelling':
      return { label: 'Spelling', hint: 'Close to the answer. Review medical spelling drills.' };
    case 'wrongnumber':
    case 'numbers_and_frequencies':
    case 'grammar_number':
      return { label: 'Number / quantity', hint: 'The digit or quantity heard did not match what you wrote.' };
    case 'extrainfo':
    case 'extra_info':
      return { label: 'Extra information', hint: 'Only write the words required for the gap.' };
    case 'wrongsection':
    case 'wrong_section':
      return { label: 'Right answer, wrong gap', hint: 'You wrote a correct phrase but for a different question.' };
    case 'paraphrase':
      return { label: 'Paraphrase', hint: 'OET Part A grades on the exact words heard, so avoid rewording.' };
    case 'empty':
      return { label: 'Unanswered', hint: 'No answer was recorded for this question.' };
    case 'distractor_confusion':
      return { label: 'Distractor confusion', hint: 'A keyword from the audio appeared in the wrong option.' };
    case 'other':
    case 'detail_capture':
      return { label: 'Accuracy', hint: 'Listen again for the exact detail asked for.' };
    default:
      return { label: reason.replace(/_/g, ' '), hint: 'Review the transcript clue for context.' };
  }
}

export default function ListeningReviewPage() {
  const params = useParams<{ id?: string | string[] }>();
  const router = useRouter();
  const attemptId = firstParam(params?.id);
  const audioRef = useRef<HTMLAudioElement>(null);
  const evidenceTimerRef = useRef<ReturnType<typeof setTimeout> | null>(null);
  const [review, setReview] = useState<ListeningReviewDto | null>(null);
  const [reportedQuestionIds, setReportedQuestionIds] = useState<Set<string>>(new Set());
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [tutorFeedback, setTutorFeedback] = useState<ListeningExpertBundle['existingFeedback'] | null>(null);
  // Per-section audio (A1, A2, one Part B, C1, C2). The evidence player loads the
  // section's own file from `paper.audioUrlByPart`; legacy papers (empty map)
  // keep the single combined `paper.audioUrl`.
  const [activeSection, setActiveSection] = useState<string | null>(null);
  const [activeAudioUrl, setActiveAudioUrl] = useState<string | null>(null);
  const pendingSeekRef = useRef<{ startMs: number; endMs: number | null } | null>(null);
  // Authenticated blob resolution: /v1/media/{id}/content is bearer-protected,
  // so <audio src="…"> 401s without a token. Mirror the exam player's
  // fetchAuthorizedObjectUrl → blob URL pattern so the evidence player is
  // replayable post-submit on every section, indefinitely.
  const [resolvedAudioSrc, setResolvedAudioSrc] = useState<string | null>(null);
  const [audioLoading, setAudioLoading] = useState(false);
  const [audioResolveError, setAudioResolveError] = useState<string | null>(null);
  const [audioRetryKey, setAudioRetryKey] = useState(0);
  const [highlightedEvidence, setHighlightedEvidence] = useState<{
    questionNumber: number;
    partCode: string;
    startMs: number | null;
    endMs: number | null;
    excerpt: string | null;
  } | null>(null);

  useEffect(() => {
    if (!attemptId) return;
    analytics.track('content_view', { page: 'listening-review', attemptId });
    Promise.all([
      getListeningReview(attemptId),
      getListeningExpertFeedback(attemptId),
      listListeningAnswerKeyReports(attemptId).catch(() => ({ items: [] })),
    ])
      .then(([reviewData, feedback, reports]) => {
        setReview(reviewData);
        setTutorFeedback(feedback ?? null);
        setReportedQuestionIds(new Set((reports.items ?? []).map((item) => item.questionId)));
      })
      .catch((err) => setError(err instanceof Error ? err.message : 'Could not load transcript-backed review.'))
      .finally(() => setLoading(false));
  }, [attemptId]);

  useEffect(() => () => {
    if (evidenceTimerRef.current) clearTimeout(evidenceTimerRef.current);
  }, []);

  const audioByPart = review?.paper.audioUrlByPart ?? {};
  const audioSections = LISTENING_REVIEW_AUDIO_SECTIONS.filter((section) => audioByPart[section]);
  const usingPerSectionAudio = audioSections.length > 0;

  // Per-section start offset (the min authored extract-window start) used to map
  // the authored evidence/segment times — historically offsets into the combined
  // paper audio — onto the section's own file. Missing windows → 0, which is also
  // correct for freshly authored per-section papers whose times are already
  // section-relative.
  const sectionStartMs = useMemo(() => {
    const map: Record<string, number> = {};
    for (const extract of review?.paper.extracts ?? []) {
      if (extract.audioStartMs == null) continue;
      const section = reviewSectionForPartCode(extract.partCode);
      map[section] = map[section] == null
        ? extract.audioStartMs
        : Math.min(map[section], extract.audioStartMs);
    }
    return map;
  }, [review]);

  // Seed the evidence player when the review loads: first section for per-section
  // papers, else the legacy combined file.
  useEffect(() => {
    if (!review) return;
    const byPart = review.paper.audioUrlByPart ?? {};
    const sections = LISTENING_REVIEW_AUDIO_SECTIONS.filter((section) => byPart[section]);
    if (sections.length > 0) {
      setActiveSection(sections[0]);
      setActiveAudioUrl(byPart[sections[0]] ?? null);
    } else {
      setActiveSection(null);
      setActiveAudioUrl(review.paper.audioUrl ?? null);
    }
  }, [review]);

  // Resolve the active section's raw URL ( /v1/media/{id}/content is bearer-
  // protected) to an authenticated blob URL the <audio> element can play
  // without auth headers. Mirrors app/listening/player/[id]/page.tsx.
  // TTS fallback (/v1/listening/audio/{sha}.wav) is AllowAnonymous, but
  // fetching it via the authorized helper still succeeds and keeps logic uniform.
  useEffect(() => {
    const rawUrl = activeAudioUrl;
    if (!rawUrl) {
      setResolvedAudioSrc(null);
      setAudioLoading(false);
      setAudioResolveError(null);
      return;
    }
    if (/^https?:\/\//i.test(rawUrl)) {
      setResolvedAudioSrc(rawUrl);
      setAudioLoading(false);
      setAudioResolveError(null);
      return;
    }
    let cancelled = false;
    let objectUrl: string | null = null;
    setResolvedAudioSrc(null);
    setAudioLoading(true);
    setAudioResolveError(null);
    fetchAuthorizedObjectUrl(rawUrl)
      .then((blobUrl) => {
        if (cancelled) {
          URL.revokeObjectURL(blobUrl);
          return;
        }
        objectUrl = blobUrl;
        setResolvedAudioSrc(blobUrl);
        setAudioLoading(false);
      })
      .catch((err) => {
        if (cancelled) return;
        setAudioResolveError(err instanceof Error ? err.message : 'Audio could not be loaded.');
        setAudioLoading(false);
      });
    return () => {
      cancelled = true;
      if (objectUrl) URL.revokeObjectURL(objectUrl);
    };
  }, [activeAudioUrl, audioRetryKey]);

  const seekAndPlay = (audio: HTMLAudioElement, startMs: number, endMs: number | null) => {
    if (evidenceTimerRef.current) clearTimeout(evidenceTimerRef.current);
    audio.currentTime = Math.max(0, startMs / 1000);
    void audio.play();
    if (endMs != null && endMs > startMs) {
      evidenceTimerRef.current = setTimeout(() => audio.pause(), endMs - startMs);
    }
  };

  // Switch the evidence player to a section's file (manual section tab).
  const loadSection = (section: string) => {
    pendingSeekRef.current = null;
    setActiveSection(section);
    setActiveAudioUrl(audioByPart[section] ?? null);
  };

  const playEvidence = (
    startMs: number | null | undefined,
    endMs: number | null | undefined,
    partCode?: string | null,
  ) => {
    const audio = audioRef.current;
    if (!audio || startMs == null) return;

    if (!usingPerSectionAudio) {
      // Legacy single combined-audio paper: seek the one file directly.
      seekAndPlay(audio, startMs, endMs ?? null);
      return;
    }

    const mapped = partCode ? reviewSectionForPartCode(partCode) : null;
    const section = mapped && audioByPart[mapped]
      ? mapped
      : activeSection ?? audioSections[0];
    const base = sectionStartMs[section] ?? 0;
    const relStart = Math.max(0, startMs - base);
    const relEnd = endMs != null ? Math.max(relStart, endMs - base) : null;

    if (section !== activeSection) {
      // Switch to the section's file first, then seek once it has loaded.
      pendingSeekRef.current = { startMs: relStart, endMs: relEnd };
      setActiveSection(section);
      setActiveAudioUrl(audioByPart[section] ?? null);
      return;
    }
    seekAndPlay(audio, relStart, relEnd);
  };

  return (
    <LearnerDashboardShell pageTitle="Listening Review" subtitle={review?.paper.title ?? 'Transcript-backed evidence for listening mistakes and distractors.'} backHref="/listening">
      <div className="space-y-5 sm:space-y-8">
        <Button variant="ghost" className="gap-2" onClick={() => router.push('/listening')}>
          <ArrowLeft className="h-4 w-4" />
          Back to listening
        </Button>

        {loading ? <Skeleton className="h-48 rounded-2xl" /> : null}
        {!loading && error ? <InlineAlert variant="error">{error}</InlineAlert> : null}

        {!loading && review ? (
          <>
            {(() => {
              const hasApprovedConversion = hasApprovedListeningConversion(review);
              return (
                <>
            <p className="rounded-xl border border-warning/30 bg-warning/10 px-4 py-3 text-sm font-semibold text-warning">
              AI Practice Score — not an official OET result.
            </p>
            <ResultsScorePanel
              eyebrow="Listening review"
              icon={Headphones}
              title={review.paper.title}
              subtitle={hasApprovedConversion && review.scoreDisplay
                ? review.scoreDisplay
                : `${review.rawScore}/${review.maxRawScore} raw${hasApprovedConversion ? ` · ${review.scaledScore}/500 scaled` : ''}`}
              gaugeValue={review.maxRawScore > 0 ? (review.rawScore / review.maxRawScore) * 100 : 0}
              gaugeLabel="Accuracy"
              gaugeColor={hasApprovedConversion ? (review.passed ? 'var(--color-success)' : 'var(--color-warning)') : 'var(--color-primary)'}
              grade={hasApprovedConversion ? { label: `Grade ${review.grade}`, tone: review.passed ? 'success' : 'warning' } : null}
               stats={[
                { label: 'Correct', value: review.correctCount, tone: 'success', icon: <CheckCircle2 /> },
                { label: 'Incorrect', value: review.incorrectCount, tone: 'danger', icon: <XCircle /> },
                { label: 'Unanswered', value: review.unansweredCount, tone: 'warning', icon: <MinusCircle /> },
                {
                  label: hasApprovedConversion ? 'Scaled' : 'Raw',
                  value: hasApprovedConversion ? `${review.scaledScore}/500` : `${review.rawScore}/${review.maxRawScore}`,
                  tone: 'info',
                  icon: <Target />,
                 },
               ]}
               chartSlot={(
                 <ScoreBandGraph
                   rawScore={review.rawScore}
                   maxRawScore={review.maxRawScore}
                   scaledScore={hasApprovedConversion ? review.scaledScore : null}
                   passed={hasApprovedConversion ? review.passed : null}
                   grade={hasApprovedConversion ? review.grade : null}
                   tableVersion={hasApprovedConversion ? review.scoreConversionTableVersionKey : null}
                 />
               )}
             />

            <ScoreConversionEvidence
              assessment="Listening"
              rawScore={review.rawScore}
              maxRawScore={review.maxRawScore}
              scaledScore={hasApprovedConversion ? review.scaledScore : null}
              passed={hasApprovedConversion ? review.passed : null}
              grade={hasApprovedConversion ? review.grade : null}
              tableVersion={hasApprovedConversion ? review.scoreConversionTableVersionKey : null}
              errorCode={review.scoreConversionErrorCode}
            />
            <ListeningPartBreakdown items={review.itemReview} />
            <TimeUsedSummary
              totalMilliseconds={review.timeUsed?.totalMilliseconds ?? null}
              sections={(review.timeUsed?.sections ?? []).map((section) => ({
                label: section.sectionCode,
                milliseconds: section.elapsedMilliseconds,
              }))}
              description="Time is reported from server-persisted attempt and audio telemetry. Unavailable telemetry is shown as not recorded."
            />
            <p className="mt-3 rounded-xl border border-border bg-surface px-4 py-3 text-sm leading-6 text-muted">
              This platform grades minor spelling variations strictly to build exam-safe habits — some real OET examiners may allow minor variants at their discretion.
            </p>
                </>
              );
            })()}

            <LearnerPageHero
              eyebrow="Transcript-backed Review"
              icon={Quote}
              accent="indigo"
              title="Answers, full transcript, and unlimited replay"
              description={transcriptStateCopy(review)}
              highlights={[
                { icon: Quote, label: 'Transcript', value: `${review.transcriptSegments.length} segments` },
                { icon: Target, label: 'Questions', value: `${review.itemReview.length} reviewed` },
                { icon: Target, label: 'Next drill', value: review.recommendedNextDrill ? 'Recommended' : 'Not assigned' },
              ]}
            />

            <section className="rounded-2xl border border-border bg-surface p-6 shadow-sm">
              <LearnerSurfaceSectionHeader
                eyebrow="Review Policy"
                title="Full transcript for the submitted part only"
                description="Each part's full transcript (Part A with A1/A2, Part B, Part C with C1/C2) is revealed only after that part is submitted. Non-submitted parts remain hidden so you cannot see their transcript or answers before attempting them. After submission you can reopen the transcript and replay the audio as many times as you want — this access is permanent."
                className="mb-4"
              />
              <div className="grid gap-4 md:grid-cols-[1fr_auto] md:items-center">
                <div className="rounded-2xl border border-border bg-background-light p-4 text-sm leading-6 text-muted">
                  <span className="font-bold text-navy">What you can review now: </span>
                  {(() => {
                    const parts = new Set<string>();
                    for (const seg of review.transcriptSegments ?? []) {
                      const p = (seg.partCode ?? '').trim().toUpperCase();
                      if (p.startsWith('A')) parts.add('Part A');
                      else if (p.startsWith('C')) parts.add('Part C');
                      else if (p) parts.add('Part B');
                    }
                    if (parts.size === 0) {
                      for (const it of review.itemReview ?? []) {
                        const p = (it.partCode ?? '').trim().toUpperCase();
                        if (p.startsWith('A')) parts.add('Part A');
                        else if (p.startsWith('C')) parts.add('Part C');
                        else parts.add('Part B');
                      }
                    }
                    const label = parts.size === 0 ? 'submitted part' : Array.from(parts).sort().join(', ');
                    return `Full transcript and audio for ${label}. Other parts remain hidden until submitted. Your review access never expires.`;
                  })()}
                  <span className="mt-1 block text-xs text-muted">Answers and score are always shown after submission. Vocabulary lookup works on every word of the visible transcript.</span>
                </div>
                <div className="rounded-2xl border border-success/30 bg-success/10 px-4 py-3 text-sm font-bold capitalize text-success">
                  Available
                </div>
              </div>
            </section>

            {usingPerSectionAudio || review.paper.audioUrl ? (
              <section className="rounded-2xl border border-border bg-surface p-6 shadow-sm">
                <LearnerSurfaceSectionHeader
                  eyebrow="Evidence Player"
                  title="Replay the proof window, not the whole paper"
                  description={usingPerSectionAudio
                    ? 'Each section has its own audio (Part B is one shared clip). Pick a section, or use the evidence buttons below to jump straight to the supporting span.'
                    : 'Use authored evidence times to jump directly to the audio span that supports each answer.'}
                  className="mb-4"
                />
                {usingPerSectionAudio ? (
                  <div className="mb-4 flex flex-wrap gap-2">
                    {audioSections.map((section) => (
                      <button
                        key={section}
                        type="button"
                        onClick={() => loadSection(section)}
                        aria-pressed={section === activeSection}
                        className={`rounded-lg px-3 py-2 text-sm font-semibold transition ${
                          section === activeSection
                            ? 'bg-primary text-white'
                            : 'bg-background-light text-navy hover:bg-surface'
                        }`}
                      >
                        {LISTENING_REVIEW_SECTION_LABEL[section] ?? section}
                      </button>
                    ))}
                  </div>
                ) : null}
                {audioResolveError ? (
                  <InlineAlert variant="error" className="mb-4">
                    {audioResolveError}{' '}
                    <button
                      type="button"
                      onClick={() => setAudioRetryKey((k) => k + 1)}
                      className="ml-2 font-semibold underline"
                    >
                      Retry
                    </button>
                  </InlineAlert>
                ) : null}
                {audioLoading ? (
                  <div className="flex items-center gap-3 rounded-xl border border-border bg-background-light px-4 py-4 text-sm text-muted">
                    <span className="h-4 w-4 animate-spin rounded-full border-2 border-primary border-t-transparent" aria-hidden />
                    Loading audio…
                  </div>
                ) : null}
                <audio
                  ref={audioRef}
                  key={resolvedAudioSrc ?? activeAudioUrl ?? 'no-audio'}
                  src={resolvedAudioSrc ?? undefined}
                  controls
                  preload="metadata"
                  className="w-full"
                  onLoadedMetadata={() => {
                    const audio = audioRef.current;
                    if (!audio) return;
                    const pending = pendingSeekRef.current;
                    if (pending) {
                      pendingSeekRef.current = null;
                      seekAndPlay(audio, pending.startMs, pending.endMs);
                    }
                  }}
                  onError={() => {
                    setAudioResolveError('Audio failed to load. Please retry.');
                  }}
                />
                <p className="mt-3 text-xs leading-5 text-muted">
                  Audios remain replayable after submit — switch sections and use evidence buttons to jump to any span at any time.
                </p>
              </section>
            ) : (
              <InlineAlert variant="info">
                No audio is attached to this paper yet. The evidence player will appear once section audio is published.
              </InlineAlert>
            )}

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
                        <div className="flex items-center gap-2">
                          {usingPerSectionAudio && audioByPart[reviewSectionForPartCode(extract.partCode)] ? (
                            <button
                              type="button"
                              onClick={() => playEvidence(extract.audioStartMs ?? 0, extract.audioEndMs, extract.partCode)}
                              className="inline-flex items-center gap-1 rounded-lg bg-info/10 px-2 py-1 text-xs font-semibold text-info transition hover:bg-info/20"
                            >
                              <Volume2 className="h-3.5 w-3.5" /> Play
                            </button>
                          ) : null}
                          <p className="text-xs font-black uppercase tracking-widest text-muted">{extract.kind}</p>
                        </div>
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

            {review.paper.audioScriptUrl ? (
              <section className="rounded-2xl border border-border bg-surface p-6 shadow-sm">
                <LearnerSurfaceSectionHeader
                  eyebrow="Audio Script"
                  title="Full audio script (authored PDF)"
                  description="The complete transcript PDF authored for this paper. This is shown post-submit alongside the time-coded transcript tabs below."
                  className="mb-4"
                />
                <ListeningQuestionPaperViewer url={review.paper.audioScriptUrl} partLabel="Audio Script" />
                <p className="mt-3 text-xs text-muted">The script is fetched with your learner entitlement and remains available permanently after submission. If the viewer fails, ensure the paper has an AudioScript PDF attached and you are entitled to this paper.</p>
              </section>
            ) : null}

            <ListeningFullTranscriptViewer
              transcriptSegments={review.transcriptSegments}
              extracts={review.paper.extracts}
              highlightedEvidence={highlightedEvidence}
              onPlayEvidence={playEvidence}
              attemptId={attemptId ?? ''}
            />

            <section className="space-y-4">
              <LearnerSurfaceSectionHeader
                eyebrow="Item review"
                title="Question-by-question review"
                description="Your answer beside the correct answer, colour-coded green for correct and red for incorrect, with transcript evidence and distractor analysis."
                className="mb-1"
              />
              {review.itemReview.map((question) => {
                const chip = missReasonChip(question);
                const unanswered = !question.learnerAnswer;
                return (
                  <AnswerComparisonCard
                    key={question.questionId}
                    testId={`listening-review-item-${question.questionId}`}
                    label={`Part ${question.partCode} · Question ${question.number}`}
                    stem={question.prompt}
                    isCorrect={question.isCorrect}
                    unanswered={!question.isCorrect && unanswered}
                    yourAnswer={question.learnerAnswer || 'No answer recorded'}
                    correctAnswer={question.correctAnswer}
                    missReason={chip ? { title: `Missed because: ${chip.label}`, detail: chip.hint } : null}
                    explanation={question.explanation ? <p>{question.explanation}</p> : null}
                  >
                    <ReportAnswerControl
                      assessment="listening"
                      attemptId={attemptId ?? ''}
                      questionId={question.questionId}
                      alreadyReported={reportedQuestionIds.has(question.questionId)}
                    />
                    {question.distractorExplanation ? (
                      <div className="rounded-2xl border border-warning/30 bg-warning/10 p-4 text-sm text-warning">
                        Distractor explanation: {question.distractorExplanation}
                      </div>
                    ) : null}
                    {question.optionAnalysis?.length ? (
                      <div className="grid gap-3 md:grid-cols-3">
                        {question.optionAnalysis.map((option) => (
                          <div
                            key={`${question.questionId}-${option.optionLabel}`}
                            className={`rounded-2xl border p-4 text-sm ${option.isCorrect ? 'border-success/30 bg-success/10 text-success' : 'border-warning/30 bg-warning/10 text-warning'}`}
                          >
                            <p className="font-bold text-navy">{option.optionLabel}. {option.optionText}</p>
                            <p className="mt-2 text-xs font-black uppercase tracking-widest">{option.isCorrect ? 'Correct' : option.distractorCategory?.replace(/_/g, ' ') ?? 'Distractor'}</p>
                            {option.whyMarkdown ? <MarkdownContent markdown={option.whyMarkdown} className="mt-2 text-xs leading-5" /> : null}
                          </div>
                        ))}
                      </div>
                    ) : null}
                  </AnswerComparisonCard>
                );
              })}
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

            {tutorFeedback ? (
              <section className="rounded-2xl border border-primary/20 bg-primary/5 p-6 shadow-sm">
                <div className="mb-5 flex items-center gap-3">
                  <GraduationCap className="h-6 w-6 shrink-0 text-primary" aria-hidden />
                  <div>
                    <h2 className="text-lg font-bold text-navy">Tutor Feedback</h2>
                    <p className="text-xs text-muted">
                      Submitted{' '}
                      {new Date(tutorFeedback.submittedAt).toLocaleDateString(
                        undefined,
                        { year: 'numeric', month: 'long', day: 'numeric' },
                      )}
                    </p>
                  </div>
                </div>

                {/* Overall feedback — rendered as pre-wrap to preserve markdown line breaks */}
                <MarkdownContent
                  markdown={tutorFeedback.overallFeedbackMarkdown}
                  className="rounded-2xl border border-primary/10 bg-surface p-5 text-sm leading-relaxed text-navy"
                />

                {/* Per-question comments */}
                {tutorFeedback.perQuestionFeedbackJson && (() => {
                  let perQ: Array<{ questionNumber: number; comment: string }> = [];
                  try {
                    perQ = JSON.parse(tutorFeedback.perQuestionFeedbackJson) as typeof perQ;
                  } catch {
                    return null;
                  }
                  if (!Array.isArray(perQ) || perQ.length === 0) return null;
                  return (
                    <div className="mt-4">
                      <h3 className="mb-3 text-xs font-bold uppercase tracking-widest text-muted">
                        Per-Question Comments
                      </h3>
                      <div className="space-y-2">
                        {perQ.map(({ questionNumber, comment }) => (
                          <div
                            key={questionNumber}
                            className="flex gap-3 rounded-xl border border-primary/10 bg-surface p-3 text-sm"
                          >
                            <span className="shrink-0 font-bold text-primary">
                              Q{questionNumber}
                            </span>
                            <span className="text-navy">{comment}</span>
                          </div>
                        ))}
                      </div>
                    </div>
                  );
                })()}

                {/* Recommended areas */}
                {tutorFeedback.recommendedAreasJson && (() => {
                  let areas: string[] = [];
                  try {
                    areas = JSON.parse(tutorFeedback.recommendedAreasJson) as string[];
                  } catch {
                    return null;
                  }
                  if (!Array.isArray(areas) || areas.length === 0) return null;
                  return (
                    <div className="mt-4">
                      <h3 className="mb-2 text-xs font-bold uppercase tracking-widest text-muted">
                        Recommended Practice Areas
                      </h3>
                      <div className="flex flex-wrap gap-2">
                        {areas.map((area) => (
                          <span
                            key={area}
                            className="inline-flex items-center gap-1 rounded-full border border-primary/30 bg-primary/10 px-3 py-1 text-xs font-medium text-primary"
                          >
                            <Tag className="h-3 w-3" />
                            {area.replace(/_/g, ' ')}
                          </span>
                        ))}
                      </div>
                    </div>
                  );
                })()}

                {/* Score override note */}
                {tutorFeedback.rawScoreOverride != null && (
                  <div className="mt-4 rounded-xl border border-warning/30 bg-warning/10 px-4 py-3 text-sm text-warning">
                    <span className="font-semibold">Score adjusted by tutor: </span>
                    {tutorFeedback.rawScoreOverride}
                    {tutorFeedback.scoreOverrideReason
                      ? `: ${tutorFeedback.scoreOverrideReason}`
                      : ''}
                  </div>
                )}
              </section>
            ) : null}
          </>
        ) : null}
      </div>
    </LearnerDashboardShell>
  );
}
