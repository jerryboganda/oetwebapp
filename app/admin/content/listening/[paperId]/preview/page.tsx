'use client';

import { useEffect, useMemo, useState } from 'react';
import Link from 'next/link';
import { ArrowLeft, CheckCircle2, Clock3, Eye, KeyRound, ShieldCheck } from 'lucide-react';
import { useParams } from 'next/navigation';

import { AdminSettingsLayout } from '@/components/admin/layout/admin-settings-layout';
import { Card, CardContent, CardHeader, CardTitle } from '@/components/admin/ui/card';
import { Button } from '@/components/admin/ui/button';
import { Badge } from '@/components/ui/badge';
import { Skeleton } from '@/components/admin/ui/skeleton';
import { InlineAlert } from '@/components/ui/alert';
import { ListeningQuestionPaperViewer } from '@/components/domain/listening/ListeningQuestionPaperViewer';
import { AudioPlayerWaveform } from '@/components/domain/audio-player-waveform';
import { useAdminAuth } from '@/lib/hooks/use-admin-auth';
import {
  getListeningCandidatePreview,
  getListeningStructure,
  type ListeningAuthoredQuestion,
  type ListeningCandidatePreview,
} from '@/lib/listening-authoring-api';

type PreviewMode = 'candidate' | 'marking';
const PREVIEW_SECTION_CODES = ['A1', 'A2', 'B', 'C1', 'C2'] as const;
type PreviewSectionCode = (typeof PREVIEW_SECTION_CODES)[number];

function firstParam(value: string | string[] | undefined) {
  return Array.isArray(value) ? value[0] : value;
}

function formatAudioWindow(startMs: number | null, endMs: number | null): string {
  if (startMs == null && endMs == null) return 'No authored cue window';
  const start = startMs == null ? '—' : `${(startMs / 1000).toFixed(1)}s`;
  const end = endMs == null ? '—' : `${(endMs / 1000).toFixed(1)}s`;
  return `${start}–${end}`;
}

function formatPreviewTimer(seconds: number | null): string {
  if (seconds == null) return 'No timer';
  const safeSeconds = Math.max(0, seconds);
  return `${Math.floor(safeSeconds / 60)}:${String(safeSeconds % 60).padStart(2, '0')}`;
}

function extractForPreviewSection(
  extracts: ListeningCandidatePreview['extracts'],
  section: PreviewSectionCode,
) {
  const exact = extracts.find((extract) => extract.partCode.toUpperCase() === section);
  if (exact || section !== 'B') return exact ?? null;
  return extracts
    .filter((extract) => extract.partCode.toUpperCase().startsWith('B'))
    .sort((left, right) => left.displayOrder - right.displayOrder)[0] ?? null;
}

export default function ListeningPreviewPage() {
  const params = useParams<{ paperId?: string | string[] }>();
  const paperId = firstParam(params?.paperId);
  const { isAuthenticated, role } = useAdminAuth();
  const [mode, setMode] = useState<PreviewMode>('candidate');
  const [candidate, setCandidate] = useState<ListeningCandidatePreview | null>(null);
  const [marking, setMarking] = useState<ListeningAuthoredQuestion[]>([]);
  const [previewAnswers, setPreviewAnswers] = useState<Record<string, string>>({});
  const [activePreviewSection, setActivePreviewSection] = useState<PreviewSectionCode>('A1');
  const [previewStarted, setPreviewStarted] = useState(false);
  const [remainingPreviewSeconds, setRemainingPreviewSeconds] = useState<number | null>(null);
  const [loading, setLoading] = useState(true);
  const [candidateError, setCandidateError] = useState<string | null>(null);
  const [markingError, setMarkingError] = useState<string | null>(null);

  useEffect(() => {
    if (!paperId || !isAuthenticated || role !== 'admin') return;
    let cancelled = false;
    setLoading(true);
    setCandidate(null);
    setMarking([]);
    setCandidateError(null);
    setMarkingError(null);
    Promise.allSettled([getListeningCandidatePreview(paperId), getListeningStructure(paperId)])
      .then(([candidateResult, markingResult]) => {
        if (cancelled) return;

        if (candidateResult.status === 'fulfilled') {
          setCandidate(candidateResult.value);
          setPreviewAnswers({});
        } else {
          setCandidateError(candidateResult.reason instanceof Error
            ? candidateResult.reason.message
            : 'Could not load the learner-safe Listening preview.');
        }

        if (markingResult.status === 'fulfilled') {
          setMarking(markingResult.value.questions);
        } else {
          setMarkingError(markingResult.reason instanceof Error
            ? markingResult.reason.message
            : 'Could not load the protected Listening marking preview.');
        }
      })
      .finally(() => {
        if (!cancelled) setLoading(false);
      });
    return () => { cancelled = true; };
  }, [isAuthenticated, paperId, role]);

  const questions = useMemo(() => {
    if (mode === 'candidate') return candidate?.questions ?? [];
    return marking;
  }, [candidate?.questions, marking, mode]);
  const questionPaperAssets = candidate?.paper.questionPaperAssets ?? [];
  const audioAssets = candidate?.paper.audioAssets ?? [];
  const activePreviewExtract = candidate
    ? extractForPreviewSection(candidate.extracts, activePreviewSection)
    : null;
  const activePreviewLimitSeconds = activePreviewExtract?.timeLimitSeconds ?? null;

  useEffect(() => {
    setPreviewStarted(false);
    setRemainingPreviewSeconds(activePreviewLimitSeconds);
  }, [activePreviewLimitSeconds, activePreviewSection]);

  useEffect(() => {
    if (!previewStarted || remainingPreviewSeconds == null || remainingPreviewSeconds <= 0) return;
    const timerId = window.setInterval(() => {
      setRemainingPreviewSeconds((seconds) => seconds == null ? null : Math.max(0, seconds - 1));
    }, 1000);
    return () => window.clearInterval(timerId);
  }, [previewStarted, remainingPreviewSeconds]);

  const breadcrumbs = [
    { label: 'Admin', href: '/admin' },
    { label: 'Content', href: '/admin/content' },
    { label: 'Listening', href: '/admin/content/listening' },
    { label: 'Preview' },
  ];

  if (!isAuthenticated || role !== 'admin') {
    return (
      <AdminSettingsLayout title="Listening preview" breadcrumbs={breadcrumbs}>
        <Card><CardContent className="p-6"><p className="text-sm text-admin-fg-muted">Admin access required.</p></CardContent></Card>
      </AdminSettingsLayout>
    );
  }

  return (
    <AdminSettingsLayout
      eyebrow="Listening authoring"
      title="Preview before publish"
      description="Review the learner-facing paper and the answer-key marking view before publishing."
      icon={<Eye className="h-5 w-5" />}
      breadcrumbs={breadcrumbs}
      actions={
        <Button variant="ghost" size="sm" asChild>
          <Link href={`/admin/content/listening/${paperId}/structure`}>
            <ArrowLeft className="mr-1.5 h-4 w-4" />
            Back to structure
          </Link>
        </Button>
      }
    >
      <div className="space-y-6">
        {mode === 'candidate' && candidateError && <InlineAlert variant="error">{candidateError}</InlineAlert>}
        {mode === 'marking' && markingError && <InlineAlert variant="warning">{markingError}</InlineAlert>}
        {loading ? <Skeleton className="h-56 rounded-admin" /> : candidate ? (
          <>
            <Card>
              <CardHeader>
                <div className="flex flex-wrap items-start justify-between gap-3">
                  <div>
                    <CardTitle>{candidate.paper.title}</CardTitle>
                    <p className="mt-1 text-sm text-admin-fg-muted">
                      {candidate.paper.subtestCode} · {candidate.paper.estimatedDurationMinutes} minutes
                    </p>
                  </div>
                  <div className="flex flex-wrap gap-2">
                    <Badge variant="info">{candidate.counts.totalItems} / 42 questions</Badge>
                    <Badge variant={mode === 'candidate' ? 'success' : 'warning'}>
                      {mode === 'candidate' ? 'Candidate view' : 'Marking view'}
                    </Badge>
                  </div>
                </div>
              </CardHeader>
              <CardContent className="space-y-4">
                <div className="flex flex-wrap gap-2" role="tablist" aria-label="Preview mode">
                  <Button
                    type="button"
                    variant={mode === 'candidate' ? 'primary' : 'outline'}
                    size="sm"
                    onClick={() => setMode('candidate')}
                    startIcon={<ShieldCheck className="h-4 w-4" />}
                  >
                    Candidate preview
                  </Button>
                  <Button
                    type="button"
                    variant={mode === 'marking' ? 'primary' : 'outline'}
                    size="sm"
                    onClick={() => setMode('marking')}
                    startIcon={<KeyRound className="h-4 w-4" />}
                  >
                    Marking preview
                  </Button>
                </div>

            <InlineAlert variant={mode === 'candidate' ? 'info' : 'warning'}>
                  {mode === 'candidate'
                    ? 'Candidate preview uses a server-side learner-safe projection. Correct answers, accepted variants, and explanations are not returned.'
                    : 'Marking preview is admin-only and shows the answer key for final authoring review. It is never a learner projection.'}
                </InlineAlert>
              </CardContent>
            </Card>

            {mode === 'candidate' ? (
              <Card>
                <CardHeader>
                  <CardTitle>Timed candidate preview</CardTitle>
                </CardHeader>
                <CardContent className="space-y-4">
                  <InlineAlert variant="info">
                    Local authoring preview only. This countdown does not create an attempt, change server time, or alter scored timer rules.
                  </InlineAlert>
                  <div className="flex flex-wrap gap-2" role="tablist" aria-label="Listening preview sections">
                    {PREVIEW_SECTION_CODES.map((section) => (
                      <Button
                        key={section}
                        type="button"
                        size="sm"
                        variant={activePreviewSection === section ? 'primary' : 'outline'}
                        onClick={() => setActivePreviewSection(section)}
                      >
                        {section}
                      </Button>
                    ))}
                  </div>
                  <div className="flex flex-wrap items-center gap-3 rounded-admin border border-admin-border bg-admin-bg-surface p-4">
                    <div>
                      <p className="text-xs font-semibold uppercase tracking-wider text-admin-fg-muted">{activePreviewSection} timer</p>
                      <p className="text-2xl font-semibold tabular-nums text-admin-fg-strong">
                        {formatPreviewTimer(remainingPreviewSeconds)}
                      </p>
                    </div>
                    <div className="text-sm text-admin-fg-muted">
                      {activePreviewExtract ? `${activePreviewExtract.title || 'Untitled extract'} · ${activePreviewExtract.kind}` : 'No authored extract for this section'}
                    </div>
                    {activePreviewLimitSeconds != null ? (
                      <Button
                        type="button"
                        size="sm"
                        variant={previewStarted ? 'secondary' : 'primary'}
                        onClick={() => {
                          if (previewStarted || remainingPreviewSeconds === 0) {
                            setPreviewStarted(false);
                            setRemainingPreviewSeconds(activePreviewLimitSeconds);
                          } else {
                            setPreviewStarted(true);
                          }
                        }}
                      >
                        {previewStarted ? 'Reset local timer' : remainingPreviewSeconds === 0 ? 'Reset local timer' : 'Start local timer'}
                      </Button>
                    ) : null}
                  </div>
                </CardContent>
              </Card>
            ) : null}

            {mode === 'candidate' && candidate.extracts.length > 0 ? (
              <Card>
                <CardHeader>
                  <CardTitle>Candidate extract context and timing</CardTitle>
                </CardHeader>
                <CardContent className="grid gap-3 md:grid-cols-2">
                  {candidate.extracts
                    .slice()
                    .sort((left, right) => left.displayOrder - right.displayOrder)
                    .map((extract) => (
                      <article
                        key={`${extract.partCode}-${extract.displayOrder}`}
                        className="rounded-admin border border-admin-border bg-admin-bg-surface p-4"
                      >
                        <div className="flex flex-wrap items-start justify-between gap-2">
                          <div>
                            <p className="font-mono text-xs text-admin-fg-muted">{extract.partCode}</p>
                            <h3 className="mt-1 text-sm font-semibold text-admin-fg-strong">
                              {extract.title || 'Untitled extract'}
                            </h3>
                          </div>
                          <Badge variant="muted">{extract.kind}</Badge>
                        </div>
                        {extract.contextIntro ? (
                          <p className="mt-3 text-sm leading-6 text-admin-fg-default">{extract.contextIntro}</p>
                        ) : null}
                        <div className="mt-3 flex flex-wrap gap-2 text-xs text-admin-fg-muted">
                          {extract.accentCode ? <Badge variant="info">{extract.accentCode}</Badge> : null}
                          <span className="inline-flex items-center gap-1 rounded-admin border border-admin-border px-2 py-1">
                            <Clock3 className="h-3.5 w-3.5" aria-hidden />
                            {extract.timeLimitSeconds != null ? `${extract.timeLimitSeconds}s limit` : 'No section limit'}
                          </span>
                          <span className="rounded-admin border border-admin-border px-2 py-1">
                            Audio window: {formatAudioWindow(extract.audioStartMs, extract.audioEndMs)}
                          </span>
                        </div>
                        {extract.speakers.length > 0 ? (
                          <p className="mt-3 text-xs text-admin-fg-muted">
                            Speakers: {extract.speakers.map((speaker) => speaker.role).filter(Boolean).join(', ')}
                          </p>
                        ) : null}
                      </article>
                    ))}
                </CardContent>
              </Card>
            ) : null}

            {mode === 'candidate' && questionPaperAssets.length > 0 ? (
              <Card>
                <CardHeader>
                  <CardTitle>Candidate question paper</CardTitle>
                </CardHeader>
                <CardContent className="grid gap-4 lg:grid-cols-2">
                  {questionPaperAssets
                    .slice()
                    .sort((left, right) => left.part?.localeCompare(right.part ?? '') ?? 0)
                    .map((asset) => (
                      <article
                        key={asset.id}
                        className="rounded-admin border border-admin-border bg-admin-bg-surface p-4"
                      >
                        <h3 className="mb-3 text-sm font-semibold text-admin-fg-strong">
                          {asset.title} {asset.part ? `(${asset.part})` : ''}
                        </h3>
                        <ListeningQuestionPaperViewer url={asset.downloadPath} partLabel={asset.part} />
                      </article>
                    ))}
                </CardContent>
              </Card>
            ) : null}

            {mode === 'candidate' && audioAssets.length > 0 ? (
              <Card>
                <CardHeader>
                  <CardTitle>Candidate audio preview</CardTitle>
                </CardHeader>
                <CardContent className="space-y-4">
                  <InlineAlert variant="info">
                    This is an authoring preview of the learner audio. It does not create an attempt or change the scored one-play rules.
                  </InlineAlert>
                  {audioAssets
                    .slice()
                    .sort((left, right) => left.part?.localeCompare(right.part ?? '') ?? 0)
                    .map((asset) => (
                      <article
                        key={asset.id}
                        className="rounded-admin border border-admin-border bg-admin-bg-surface p-4"
                      >
                        <div className="mb-3 flex flex-wrap items-center justify-between gap-2">
                          <h3 className="text-sm font-semibold text-admin-fg-strong">
                            {asset.title} {asset.part ? `(${asset.part})` : ''}
                          </h3>
                          {asset.durationSeconds != null ? (
                            <Badge variant="muted">{asset.durationSeconds}s</Badge>
                          ) : null}
                        </div>
                        <AudioPlayerWaveform audioUrl={asset.downloadPath} />
                      </article>
                    ))}
                </CardContent>
              </Card>
            ) : null}

            <Card>
              <CardHeader><CardTitle>{mode === 'candidate' ? 'Candidate paper' : 'Marking answer key'}</CardTitle></CardHeader>
              <CardContent className="space-y-3">
                {questions.length === 0 ? (
                  <p className="text-sm text-admin-fg-muted">No authored questions are available for preview.</p>
                ) : questions.map((question) => (
                  <PreviewQuestion
                    key={question.id}
                    question={question}
                    showAnswer={mode === 'marking'}
                    interactive={mode === 'candidate'}
                    answerValue={previewAnswers[question.id] ?? ''}
                    onAnswerChange={(value) => setPreviewAnswers((current) => ({ ...current, [question.id]: value }))}
                  />
                ))}
              </CardContent>
            </Card>
          </>
        ) : mode === 'marking' ? (
          <MarkingOnlyPreview questions={marking} />
        ) : (
          <Card>
            <CardHeader><CardTitle>Candidate preview unavailable</CardTitle></CardHeader>
            <CardContent className="space-y-4">
              <p className="text-sm text-admin-fg-muted">
                The learner-safe projection could not be loaded. The protected marking projection can still be reviewed separately.
              </p>
              <Button type="button" variant="outline" size="sm" onClick={() => setMode('marking')}>
                View marking preview
              </Button>
            </CardContent>
          </Card>
        )}
      </div>
    </AdminSettingsLayout>
  );
}

function MarkingOnlyPreview({ questions }: { questions: ListeningAuthoredQuestion[] }) {
  return (
    <Card>
      <CardHeader><CardTitle>Marking answer key</CardTitle></CardHeader>
      <CardContent className="space-y-3">
        <InlineAlert variant="warning">
          Candidate preview is unavailable; this protected admin-only marking projection is shown separately.
        </InlineAlert>
        {questions.length === 0 ? (
          <p className="text-sm text-admin-fg-muted">No authored questions are available for preview.</p>
        ) : questions.map((question) => (
          <PreviewQuestion key={question.id} question={question} showAnswer />
        ))}
      </CardContent>
    </Card>
  );
}

function PreviewQuestion({
  question,
  showAnswer,
  interactive = false,
  answerValue = '',
  onAnswerChange,
}: {
  question: ListeningCandidatePreview['questions'][number] | ListeningAuthoredQuestion;
  showAnswer: boolean;
  interactive?: boolean;
  answerValue?: string;
  onAnswerChange?: (value: string) => void;
}) {
  const options = question.options ?? [];
  const authoredQuestion = 'correctAnswer' in question ? question : null;
  const answer = authoredQuestion?.correctAnswer ?? null;
  const distractorRows = authoredQuestion
    ? options.map((option, index) => ({
      option,
      label: String.fromCharCode(65 + index),
      category: authoredQuestion.optionDistractorCategory?.[index] ?? null,
      why: authoredQuestion.optionDistractorWhy?.[index] ?? null,
    })).filter((row) => row.category || row.why)
    : [];

  return (
    <article className="rounded-admin border border-admin-border bg-admin-bg-surface p-4">
      <div className="flex flex-wrap items-start justify-between gap-2">
        <div className="flex items-start gap-2">
          <span className="font-mono text-sm text-admin-fg-muted">Q{question.number}</span>
          <p className="text-sm font-semibold text-admin-fg-strong">{question.stem || 'Untitled question'}</p>
        </div>
        <Badge variant="muted">{question.partCode}</Badge>
      </div>

      {options.length > 0 && (
        interactive ? (
          <fieldset className="mt-3 space-y-2 pl-2">
            <legend className="sr-only">Answer for question {question.number}</legend>
            {options.map((option, index) => {
              const optionKey = String.fromCharCode(65 + index);
              return (
                <label key={`${question.id}-${index}`} className="flex items-center gap-2 text-sm text-admin-fg-default">
                  <input
                    type="radio"
                    aria-label={`${optionKey}. ${option}`}
                    name={`listening-preview-${question.id}`}
                    value={optionKey}
                    checked={answerValue === optionKey}
                    onChange={(event) => onAnswerChange?.(event.target.value)}
                  />
                  <span><span className="mr-1 font-semibold">{optionKey}.</span>{option}</span>
                </label>
              );
            })}
          </fieldset>
        ) : (
          <ul className="mt-3 space-y-1.5 pl-7">
            {options.map((option, index) => (
              <li key={`${question.id}-${index}`} className="text-sm text-admin-fg-default">
                <span className="mr-1 font-semibold">{String.fromCharCode(65 + index)}.</span>{option}
              </li>
            ))}
          </ul>
        )
      )}

      {interactive && options.length === 0 ? (
        <input
          type="text"
          aria-label={`Answer for question ${question.number}`}
          value={answerValue}
          onChange={(event) => onAnswerChange?.(event.target.value)}
          placeholder="Type candidate answer"
          className="mt-3 w-full rounded-admin border border-admin-border bg-admin-bg-surface px-3 py-2 text-sm text-admin-fg-strong outline-none focus:border-[var(--admin-primary)] focus:ring-2 focus:ring-[var(--admin-primary)]/20"
        />
      ) : null}

      {showAnswer && answer && (
        <p className="mt-3 flex items-center gap-1.5 text-sm font-semibold text-[var(--admin-success)]">
          <CheckCircle2 className="h-4 w-4" />
          Correct answer: {answer}
        </p>
      )}

      {showAnswer && authoredQuestion ? (
        <div className="mt-4 space-y-3 rounded-admin border border-admin-border bg-admin-bg-subtle p-3">
          {authoredQuestion.acceptedAnswers.length > 0 ? (
            <div>
              <p className="text-xs font-semibold uppercase tracking-wider text-admin-fg-muted">Accepted variants</p>
              <p className="mt-1 text-sm text-admin-fg-default">{authoredQuestion.acceptedAnswers.join(', ')}</p>
            </div>
          ) : null}
          {authoredQuestion.explanation ? (
            <div>
              <p className="text-xs font-semibold uppercase tracking-wider text-admin-fg-muted">Approved rationale</p>
              <p className="mt-1 text-sm leading-6 text-admin-fg-default">{authoredQuestion.explanation}</p>
            </div>
          ) : null}
          {authoredQuestion.transcriptExcerpt ? (
            <div>
              <p className="text-xs font-semibold uppercase tracking-wider text-admin-fg-muted">Transcript evidence</p>
              <p className="mt-1 text-sm leading-6 text-admin-fg-default">{authoredQuestion.transcriptExcerpt}</p>
            </div>
          ) : null}
          {authoredQuestion.validationStatus ? (
            <div>
              <p className="text-xs font-semibold uppercase tracking-wider text-admin-fg-muted">Validation status</p>
              <p className="mt-1 text-sm text-admin-fg-default">{authoredQuestion.validationStatus}</p>
            </div>
          ) : null}
          {authoredQuestion.speakerAttitude ? (
            <div>
              <p className="text-xs font-semibold uppercase tracking-wider text-admin-fg-muted">Speaker attitude</p>
              <p className="mt-1 text-sm text-admin-fg-default">{authoredQuestion.speakerAttitude}</p>
            </div>
          ) : null}
          {authoredQuestion.distractorExplanation ? (
            <div>
              <p className="text-xs font-semibold uppercase tracking-wider text-admin-fg-muted">Distractor explanation</p>
              <p className="mt-1 text-sm leading-6 text-admin-fg-default">{authoredQuestion.distractorExplanation}</p>
            </div>
          ) : null}
          {distractorRows.length > 0 ? (
            <div>
              <p className="text-xs font-semibold uppercase tracking-wider text-admin-fg-muted">Distractor authoring</p>
              <ul className="mt-2 space-y-2">
                {distractorRows.map((row) => (
                  <li key={`${question.id}-${row.label}`} className="text-sm leading-6 text-admin-fg-default">
                    <span className="font-semibold">{row.label}. {row.option}</span>
                    {row.category ? <span className="ml-2 text-xs uppercase tracking-wide text-admin-fg-muted">{row.category.replace(/_/g, ' ')}</span> : null}
                    {row.why ? <p className="text-admin-fg-muted">{row.why}</p> : null}
                  </li>
                ))}
              </ul>
            </div>
          ) : null}
        </div>
      ) : null}
    </article>
  );
}
