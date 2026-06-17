'use client';

import { Suspense, use, useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { useRouter, useSearchParams } from 'next/navigation';
import {
  AlertCircle,
  ArrowRight,
  CheckCircle2,
  Clock,
  Headphones,
  Loader2,
  Lock,
  Play,
  Save,
  Send,
  Volume2,
} from 'lucide-react';
import { LearnerDashboardShell } from '@/components/layout';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { InlineAlert } from '@/components/ui/alert';
import { Skeleton } from '@/components/ui/skeleton';
import { Modal } from '@/components/ui/modal';
import { cn } from '@/lib/utils';
import { useTimer } from '@/hooks/useTimer';
import { fetchAuthorizedObjectUrl } from '@/lib/api';
import { readErrorMessage } from '@/lib/read-error-message';
import { ContentLockedNotice, isContentLockedError, readContentLockedMessage } from '@/components/domain/ContentLockedNotice';
import { PartANotesDocument } from '@/components/domain/listening/PartANotesDocument';
import { QuestionPaperPdfViewer, type ReadingPdfAsset } from '@/components/domain/reading-pdf-viewer';
import { completeMockSection } from '@/lib/api';
import {
  advanceListeningSection,
  createListeningPaperAnnotation,
  deleteListeningPaperAnnotation,
  getListeningPaperAnnotations,
  getListeningSession,
  saveListeningAnswer,
  startListeningAttempt,
  submitListeningAttempt,
  type ListeningAttemptDto,
  type ListeningSessionDto,
  type ListeningSessionMode,
  type ListeningSessionQuestionDto,
} from '@/lib/listening-api';
import type { ReadingPaperAnnotationDto, ReadingPaperAnnotationKind } from '@/lib/reading-authoring-api';
import {
  buildListeningExamSubSections,
  LISTENING_EXAM_DEFAULT_TIME_LIMIT_SECONDS,
  type ListeningExamSubSection,
} from '@/lib/listening-exam-sections';

type SaveState = 'idle' | 'saving' | 'saved' | 'error';

// Only the strict one-way exam surface lives here. The legacy
// diagnostic/mock/practice player (app/listening/player/[id]) still serves
// every other mode. Default to "exam" so a bare deep link to this route is a
// graded one-way attempt; "paper" is accepted for the printable-paper variant.
function normalizeExamMode(raw: string | null): ListeningSessionMode {
  const value = (raw ?? '').trim().toLowerCase();
  return value === 'paper' ? 'paper' : 'exam';
}

/**
 * Resolve the learner-facing question-paper PDF URL for a sub-section. Mirrors
 * the Reading PDF resolution (and the backend's `questionPaperUrlByPart` keys):
 * try the exact section code (e.g. "B1"), then fall back to the parent part
 * ("B"). Part B/C are PDF-backed; Part A is note-completion (no PDF).
 */
function resolveQuestionPaperUrl(
  session: ListeningSessionDto,
  sectionCode: string,
): string | null {
  const map = session.paper.questionPaperUrlByPart;
  if (!map) return null;
  const code = sectionCode.trim().toUpperCase();
  const parent = code.length > 1 ? code.slice(0, 1) : code;
  return map[code] ?? map[parent] ?? null;
}

/**
 * Extract the media asset id from a `/v1/media/{id}/content` URL. Used as the
 * stable annotation key for the question paper (the learner never sees the
 * ContentPaperAsset row id; the backend validates this against the paper's
 * QuestionPaper asset `MediaAssetId`).
 */
function mediaAssetIdFromUrl(url: string | null): string | null {
  if (!url) return null;
  const match = url.match(/\/v1\/media\/([^/?#]+)\/content/);
  return match ? match[1] : null;
}

export default function ListeningPaperPlayerPage({ params }: { params: Promise<{ paperId: string }> }) {
  return (
    <Suspense fallback={<LearnerDashboardShell pageTitle="Listening"><Skeleton className="h-64" /></LearnerDashboardShell>}>
      <ListeningPaperPlayerContent params={params} />
    </Suspense>
  );
}

function ListeningPaperPlayerContent({ params }: { params: Promise<{ paperId: string }> }) {
  const { paperId } = use(params);
  const search = useSearchParams();
  const router = useRouter();
  const resumeAttemptId = search?.get('attemptId') ?? '';
  const mode = normalizeExamMode(search?.get('mode'));
  // Mocks V2 — when this player is launched as a section of a mock attempt the
  // launch route carries these so submission can write the score back.
  const mockAttemptId = search?.get('mockAttemptId') ?? null;
  const mockSectionId = search?.get('mockSectionId') ?? null;

  const [session, setSession] = useState<ListeningSessionDto | null>(null);
  const [attempt, setAttempt] = useState<ListeningAttemptDto | null>(null);
  const [answers, setAnswers] = useState<Record<string, string>>({});
  const [loading, setLoading] = useState(true);
  const [starting, setStarting] = useState(false);
  const [submitting, setSubmitting] = useState(false);
  const [advancing, setAdvancing] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [contentLockedMessage, setContentLockedMessage] = useState<string | null>(null);
  const [saveState, setSaveState] = useState<SaveState>('idle');
  // One-way cursor. Only ever increments (Next or timer auto-advance).
  const [currentIndex, setCurrentIndex] = useState(0);
  // Per-paper question-paper annotations (Part B/C highlight/strikethrough).
  const [annotations, setAnnotations] = useState<ReadingPaperAnnotationDto[]>([]);

  const saveTimers = useRef<Record<string, ReturnType<typeof setTimeout>>>({});
  const dirtyQuestionIds = useRef<Set<string>>(new Set());
  // Guards the advance pipeline so a Next-click racing the timer's onExpire
  // cannot fire two advances (and two backend section-cursor writes).
  const advanceInFlight = useRef(false);

  const subSections = useMemo<ListeningExamSubSection[]>(
    () => (session ? buildListeningExamSubSections(session) : []),
    [session],
  );
  const activeSubSection = subSections[currentIndex] ?? null;
  const isLastSection = subSections.length > 0 && currentIndex >= subSections.length - 1;
  const onePlayOnly = session?.modePolicy.onePlayOnly ?? true;

  useEffect(() => () => {
    Object.values(saveTimers.current).forEach(clearTimeout);
  }, []);

  // Load the learner's question-paper annotations once per paper (independent
  // of the attempt; they carry across attempts like the Reading module).
  useEffect(() => {
    if (!paperId) return;
    let cancelled = false;
    getListeningPaperAnnotations(paperId)
      .then((rows) => { if (!cancelled) setAnnotations(rows); })
      .catch(() => { if (!cancelled) setAnnotations([]); });
    return () => { cancelled = true; };
  }, [paperId]);

  const handleCreateAnnotation = useCallback(async (a: {
    contentPaperAssetId: string;
    pageNumber: number;
    kind: ReadingPaperAnnotationKind;
    geometryJson: unknown;
  }) => {
    const created = await createListeningPaperAnnotation(paperId, a);
    setAnnotations((prev) => [...prev, created]);
  }, [paperId]);

  const handleDeleteAnnotation = useCallback(async (annotationId: string) => {
    await deleteListeningPaperAnnotation(paperId, annotationId);
    setAnnotations((prev) => prev.filter((x) => x.id !== annotationId));
  }, [paperId]);

  const load = useCallback(async () => {
    setLoading(true);
    setError(null);
    setContentLockedMessage(null);
    try {
      const loaded = await getListeningSession(paperId, {
        mode,
        attemptId: resumeAttemptId || undefined,
      });
      setSession(loaded);
      if (loaded.attempt) {
        setAttempt(loaded.attempt);
        const restored: Record<string, string> = {};
        for (const [questionId, value] of Object.entries(loaded.attempt.answers ?? {})) {
          if (typeof value === 'string') restored[questionId] = value;
        }
        setAnswers(restored);
        dirtyQuestionIds.current.clear();
      }
    } catch (err) {
      if (isContentLockedError(err)) {
        setContentLockedMessage(readContentLockedMessage(err));
      } else {
        setError(readErrorMessage(err, 'Failed to load Listening paper.'));
      }
    } finally {
      setLoading(false);
    }
  }, [mode, paperId, resumeAttemptId]);

  useEffect(() => {
    void load();
  }, [load]);

  const start = useCallback(async () => {
    setStarting(true);
    setError(null);
    setContentLockedMessage(null);
    try {
      const started = await startListeningAttempt(paperId, mode, { mockAttemptId, mockSectionId });
      setAttempt(started);
      const restored: Record<string, string> = {};
      for (const [questionId, value] of Object.entries(started.answers ?? {})) {
        if (typeof value === 'string') restored[questionId] = value;
      }
      setAnswers(restored);
      setCurrentIndex(0);
      dirtyQuestionIds.current.clear();
      if (mockAttemptId && mockSectionId && !resumeAttemptId) {
        const next = new URLSearchParams(search?.toString());
        next.set('attemptId', started.attemptId);
        router.replace(`/listening/paper/${encodeURIComponent(paperId)}?${next.toString()}`);
      }
    } catch (err) {
      if (isContentLockedError(err)) {
        setContentLockedMessage(readContentLockedMessage(err));
      } else {
        setError(readErrorMessage(err, 'Could not start Listening attempt.'));
      }
    } finally {
      setStarting(false);
    }
  }, [mockAttemptId, mockSectionId, mode, paperId, resumeAttemptId, router, search]);

  const persistAnswer = useCallback(async (questionId: string, value: string) => {
    if (!attempt) return;
    setSaveState('saving');
    try {
      await saveListeningAnswer(attempt.attemptId, questionId, value);
      dirtyQuestionIds.current.delete(questionId);
      setSaveState('saved');
    } catch (err) {
      setSaveState('error');
      setError(readErrorMessage(err, 'Autosave failed.'));
    }
  }, [attempt]);

  // Debounced autosave, mirroring the Reading player's 400ms settle.
  const setAnswer = useCallback((question: ListeningSessionQuestionDto, value: string) => {
    if (!attempt) return;
    setAnswers((prev) => ({ ...prev, [question.id]: value }));
    dirtyQuestionIds.current.add(question.id);
    setSaveState('saving');
    if (saveTimers.current[question.id]) clearTimeout(saveTimers.current[question.id]);
    saveTimers.current[question.id] = setTimeout(() => {
      void persistAnswer(question.id, value);
    }, 400);
  }, [attempt, persistAnswer]);

  // Flush every dirty answer for the section we are leaving (one-way: it can
  // never be edited again, so its answers must land before we advance).
  const flushPendingAnswers = useCallback(async () => {
    if (!attempt) return;
    Object.values(saveTimers.current).forEach(clearTimeout);
    saveTimers.current = {};
    const pending = Array.from(dirtyQuestionIds.current)
      .map((questionId) => [questionId, answers[questionId]] as const)
      .filter(([, value]) => typeof value === 'string');
    if (pending.length === 0) return;
    setSaveState('saving');
    try {
      await Promise.all(pending.map(([questionId, value]) => saveListeningAnswer(attempt.attemptId, questionId, value)));
      pending.forEach(([questionId]) => dirtyQuestionIds.current.delete(questionId));
      setSaveState('saved');
    } catch (err) {
      setSaveState('error');
      setError(readErrorMessage(err, 'Autosave failed.'));
    }
  }, [answers, attempt]);

  const submit = useCallback(async () => {
    if (!attempt || submitting) return;
    setSubmitting(true);
    setError(null);
    try {
      await flushPendingAnswers();
      const graded = await submitListeningAttempt(attempt.attemptId);
      if (mockAttemptId && mockSectionId) {
        try {
          await completeMockSection(mockAttemptId, mockSectionId, {
            contentAttemptId: attempt.attemptId,
            rawScore: graded.rawScore,
            rawScoreMax: graded.maxRawScore,
            scaledScore: graded.scaledScore,
            grade: graded.grade,
            evidence: { source: 'listening_paper_player' },
          });
        } catch {
          // Do not lose the learner's submission on a mock-write failure; the
          // results route surfaces a retry. Keep this non-fatal.
          if (typeof window !== 'undefined') {
            try {
              window.sessionStorage.setItem(
                `oet-mock-section-complete-pending:${attempt.attemptId}`,
                JSON.stringify({
                  mockAttemptId,
                  mockSectionId,
                  rawScore: graded.rawScore,
                  rawScoreMax: graded.maxRawScore,
                  scaledScore: graded.scaledScore,
                  grade: graded.grade,
                }),
              );
            } catch { /* sessionStorage may be full or blocked */ }
          }
        }
      }
      router.push(`/listening/results/${encodeURIComponent(attempt.attemptId)}`);
    } catch (err) {
      setError(readErrorMessage(err, 'Could not submit Listening attempt.'));
      setSubmitting(false);
    }
  }, [attempt, flushPendingAnswers, mockAttemptId, mockSectionId, router, submitting]);

  // Single forward advance — shared by the Next button and the timer's
  // onExpire. Flushes the leaving section's answers, calls the server-side
  // one-way cursor, then either moves to the next section or submits (on C2).
  const advance = useCallback(async () => {
    if (!attempt || advanceInFlight.current) return;
    advanceInFlight.current = true;
    setAdvancing(true);
    try {
      await flushPendingAnswers();
      if (isLastSection) {
        await submit();
        return;
      }
      const nextIndex = currentIndex + 1;
      try {
        await advanceListeningSection(attempt.attemptId, nextIndex);
      } catch (err) {
        // The server rejects backward moves; a forward move should succeed.
        // Surface a soft warning but still progress the client cursor so the
        // candidate is never stuck on a section whose timer already expired.
        setError(readErrorMessage(err, 'Could not record section advance.'));
      }
      setCurrentIndex(nextIndex);
      setSaveState('idle');
    } finally {
      advanceInFlight.current = false;
      setAdvancing(false);
    }
  }, [attempt, currentIndex, flushPendingAnswers, isLastSection, submit]);

  if (loading) {
    return <LearnerDashboardShell pageTitle="Listening"><Skeleton className="h-64" /></LearnerDashboardShell>;
  }

  if (contentLockedMessage) {
    return (
      <LearnerDashboardShell pageTitle="Listening" backHref="/listening">
        <ContentLockedNotice message={contentLockedMessage} />
      </LearnerDashboardShell>
    );
  }

  if (!session) {
    return (
      <LearnerDashboardShell pageTitle="Listening" backHref="/listening">
        <InlineAlert variant="error">{error ?? 'Listening paper not found.'}</InlineAlert>
      </LearnerDashboardShell>
    );
  }

  const answeredCount = Object.values(answers).filter((value) => value.trim().length > 0).length;
  const totalQuestions = session.questions.length;

  return (
    <LearnerDashboardShell pageTitle={session.paper.title} backHref="/listening">
      <main className="space-y-5">
        {error ? <InlineAlert variant="error">{error}</InlineAlert> : null}
        {mockAttemptId ? (
          <InlineAlert variant="info">
            You&rsquo;re taking this section as part of a mock. Submitting marks this section complete and returns you to the mock dashboard.
          </InlineAlert>
        ) : null}
        <div className="md:hidden">
          <InlineAlert variant="warning">The Listening exam is designed for a tablet or desktop-sized screen and headphones.</InlineAlert>
        </div>

        {!attempt ? (
          <IntroCard
            title={session.paper.title}
            sectionCount={subSections.length}
            audioAvailable={session.paper.audioAvailable}
            audioUnavailableReason={session.paper.audioUnavailableReason}
            starting={starting}
            onStart={() => void start()}
          />
        ) : subSections.length === 0 ? (
          <InlineAlert variant="warning">
            This Listening paper has no playable sub-sections yet. Structured audio and questions for A1–C2 have not been authored.
          </InlineAlert>
        ) : (
          <>
            <ExamToolbar
              answeredCount={answeredCount}
              totalQuestions={totalQuestions}
              saveState={saveState}
              currentIndex={currentIndex}
              sectionCount={subSections.length}
              activeLabel={activeSubSection?.label ?? ''}
            />

            <SectionProgress subSections={subSections} currentIndex={currentIndex} />

            {activeSubSection ? (
              <ActiveSubSectionPanel
                // Remount on every advance so the timer + audio fully reset for
                // the new sub-section (one-way: the prior section is gone).
                key={`${attempt.attemptId}:${activeSubSection.index}`}
                attemptId={attempt.attemptId}
                paperId={paperId}
                subSection={activeSubSection}
                questionPaperUrl={resolveQuestionPaperUrl(session, activeSubSection.partCode)}
                annotations={annotations}
                onCreateAnnotation={handleCreateAnnotation}
                onDeleteAnnotation={handleDeleteAnnotation}
                answers={answers}
                onePlayOnly={onePlayOnly}
                isLastSection={isLastSection}
                advancing={advancing || submitting}
                onAnswerChange={setAnswer}
                onAdvance={() => void advance()}
              />
            ) : null}
          </>
        )}
      </main>
    </LearnerDashboardShell>
  );
}

function IntroCard({
  title,
  sectionCount,
  audioAvailable,
  audioUnavailableReason,
  starting,
  onStart,
}: {
  title: string;
  sectionCount: number;
  audioAvailable: boolean;
  audioUnavailableReason: string | null;
  starting: boolean;
  onStart: () => void;
}) {
  return (
    <section className="rounded-[20px] border border-border bg-surface px-5 py-8 text-center shadow-sm">
      <Headphones className="mx-auto h-7 w-7 text-info" aria-hidden="true" />
      <h1 className="mt-4 text-2xl font-semibold tracking-tight text-navy">{title}</h1>
      <p className="mx-auto mt-2 max-w-2xl text-sm leading-6 text-muted">
        Each sub-section plays its own audio once and runs its own countdown. When the timer reaches zero
        the exam moves on automatically — you can never return to a previous sub-section. Use headphones.
      </p>
      <p
        className="mx-auto mt-4 max-w-2xl rounded-2xl border border-border bg-background-light px-4 py-3 text-xs font-semibold leading-5 text-muted"
        role="note"
      >
        OET test content is confidential. Do not redistribute or share questions outside this practice context.
      </p>
      {!audioAvailable ? (
        <div className="mx-auto mt-4 max-w-2xl">
          <InlineAlert variant="warning">{audioUnavailableReason ?? 'Audio is not available for this Listening paper yet.'}</InlineAlert>
        </div>
      ) : null}
      <div className="mt-5 flex flex-col items-center gap-2">
        <Button variant="primary" onClick={onStart} loading={starting} disabled={sectionCount === 0}>
          <Play className="h-4 w-4" aria-hidden="true" />
          Start exam
        </Button>
        {sectionCount > 0 ? (
          <p className="text-xs font-semibold text-muted">{sectionCount} sub-section{sectionCount === 1 ? '' : 's'} · audio plays once</p>
        ) : null}
      </div>
    </section>
  );
}

function ExamToolbar({
  answeredCount,
  totalQuestions,
  saveState,
  currentIndex,
  sectionCount,
  activeLabel,
}: {
  answeredCount: number;
  totalQuestions: number;
  saveState: SaveState;
  currentIndex: number;
  sectionCount: number;
  activeLabel: string;
}) {
  return (
    <section className="rounded-[20px] border border-border bg-surface p-4 shadow-sm" aria-label="Attempt status">
      <div className="flex flex-col gap-4 lg:flex-row lg:items-center lg:justify-between">
        <div className="flex flex-wrap items-center gap-3">
          <Badge variant="info">Sub-section {currentIndex + 1} of {sectionCount}</Badge>
          <span className="text-sm font-bold text-navy">{activeLabel}</span>
          <Badge variant="warning">One-way · no going back</Badge>
          <span className="text-sm font-semibold text-muted" aria-label={`${answeredCount} of ${totalQuestions} questions answered`}>
            {answeredCount}/{totalQuestions} answered
          </span>
        </div>
        <SaveStatus state={saveState} />
      </div>
    </section>
  );
}

function SaveStatus({ state }: { state: SaveState }) {
  const label = { idle: 'Autosave ready', saving: 'Saving...', saved: 'Saved', error: 'Save failed' }[state];
  const Icon = state === 'saving' ? Loader2 : state === 'error' ? AlertCircle : Save;
  return (
    <span
      className={cn('inline-flex items-center gap-2 text-sm font-semibold', state === 'error' ? 'text-danger' : 'text-muted')}
      role="status"
      aria-live="polite"
    >
      <Icon className={cn('h-4 w-4', state === 'saving' && 'motion-safe:animate-spin')} aria-hidden="true" />
      {label}
    </span>
  );
}

// Forward-only stepper. Past sub-sections render as locked/done, the current as
// active, future as not-yet-reached. There is intentionally no click handler —
// the cursor only moves forward via Next/auto-advance.
function SectionProgress({
  subSections,
  currentIndex,
}: {
  subSections: ListeningExamSubSection[];
  currentIndex: number;
}) {
  return (
    <ol
      className="flex gap-2 overflow-x-auto rounded-2xl border border-border bg-surface p-2 shadow-sm"
      aria-label="Listening sub-sections"
    >
      {subSections.map((section) => {
        const isDone = section.index < currentIndex;
        const isActive = section.index === currentIndex;
        return (
          <li
            key={section.partCode}
            aria-current={isActive ? 'step' : undefined}
            className={cn(
              'flex min-h-10 shrink-0 items-center gap-1.5 rounded-xl px-3 py-2 text-sm font-bold',
              isActive && 'bg-primary text-white shadow-sm',
              isDone && 'bg-background-light text-muted',
              !isActive && !isDone && 'bg-background-light text-muted/70',
            )}
          >
            {isDone ? (
              <CheckCircle2 className="h-4 w-4" aria-hidden="true" />
            ) : isActive ? (
              <Volume2 className="h-4 w-4" aria-hidden="true" />
            ) : (
              <Lock className="h-3.5 w-3.5" aria-hidden="true" />
            )}
            <span>{section.partCode}</span>
            <span className="sr-only">
              {isDone ? ' (completed and locked)' : isActive ? ' (current)' : ' (not yet reached)'}
            </span>
          </li>
        );
      })}
    </ol>
  );
}

function ActiveSubSectionPanel({
  attemptId,
  paperId,
  subSection,
  questionPaperUrl,
  annotations,
  onCreateAnnotation,
  onDeleteAnnotation,
  answers,
  onePlayOnly,
  isLastSection,
  advancing,
  onAnswerChange,
  onAdvance,
}: {
  attemptId: string;
  paperId: string;
  subSection: ListeningExamSubSection;
  questionPaperUrl: string | null;
  annotations: ReadingPaperAnnotationDto[];
  onCreateAnnotation: (a: {
    contentPaperAssetId: string;
    pageNumber: number;
    kind: ReadingPaperAnnotationKind;
    geometryJson: unknown;
  }) => Promise<void>;
  onDeleteAnnotation: (annotationId: string) => Promise<void>;
  answers: Record<string, string>;
  onePlayOnly: boolean;
  isLastSection: boolean;
  advancing: boolean;
  onAnswerChange: (question: ListeningSessionQuestionDto, value: string) => void;
  onAdvance: () => void;
}) {
  const [showConfirm, setShowConfirm] = useState(false);
  // Auto-advance when this sub-section's countdown hits zero. Reset is implicit
  // because the panel is remounted (keyed by attemptId:index) on every advance.
  const expiredRef = useRef(false);
  const handleExpire = useCallback(() => {
    if (expiredRef.current) return;
    expiredRef.current = true;
    onAdvance();
  }, [onAdvance]);

  const { remaining } = useTimer(
    subSection.timeLimitSeconds > 0 ? subSection.timeLimitSeconds : LISTENING_EXAM_DEFAULT_TIME_LIMIT_SECONDS,
    'down',
    handleExpire,
    // Listening-namespaced sessionStorage key so a mid-countdown refresh
    // resumes this sub-section (and never collides with the Reading timer).
    `listening-exam:${attemptId}:${subSection.index}`,
  );

  const unansweredInSection = subSection.questions.filter((q) => (answers[q.id] ?? '').trim().length === 0).length;

  const requestAdvance = () => {
    if (advancing) return;
    if (isLastSection || unansweredInSection > 0) {
      setShowConfirm(true);
      return;
    }
    onAdvance();
  };

  // Part A is note-completion (inline gaps); Part B/C are PDF-backed answer
  // sheets. This mirrors the Reading paper player: the question paper PDF is the
  // authoritative surface for B/C, while Part A renders the structured notes
  // with inline gap inputs positioned in the running text (real OET layout).
  const isPartA = subSection.partCode.startsWith('A');
  const notesBody = subSection.extract?.notesBody?.trim() || '';
  const showNotes = isPartA && notesBody.length > 0;
  // Annotations key off the media asset id (parsed from the PDF URL); the viewer
  // matches annotations to the asset by this id, so it must be stable + match
  // what the create callback sends. Fall back to a synthetic id (display only,
  // annotations disabled) if the URL is not the expected /v1/media shape.
  const mediaAssetId = mediaAssetIdFromUrl(questionPaperUrl);
  const assetId = mediaAssetId ?? `qp-${subSection.partCode}`;
  const pdfAssets: ReadingPdfAsset[] = questionPaperUrl
    ? [{ id: assetId, part: subSection.partCode, title: subSection.title, downloadPath: questionPaperUrl }]
    : [];
  const showPdf = !isPartA && pdfAssets.length > 0;
  const canAnnotate = showPdf && mediaAssetId !== null;

  return (
    <div
      className={cn(
        'grid grid-cols-1 gap-4',
        showPdf
          ? 'xl:grid-cols-[minmax(0,1.05fr)_minmax(360px,0.95fr)]'
          : 'xl:grid-cols-[minmax(0,0.9fr)_minmax(360px,1.1fr)]',
      )}
    >
      <section className="space-y-4 xl:sticky xl:top-4 xl:self-start">
        <SubSectionTimer label={subSection.label} remaining={remaining} />
        <SubSectionAudio
          attemptId={attemptId}
          subSection={subSection}
          onePlayOnly={onePlayOnly}
        />
        {showPdf ? (
          <QuestionPaperPdfViewer
            paperId={paperId}
            partCode={subSection.partCode}
            assets={pdfAssets}
            annotations={annotations}
            readOnly={!canAnnotate}
            onCreateAnnotation={onCreateAnnotation}
            onDeleteAnnotation={onDeleteAnnotation}
            documentNoun="Listening paper"
          />
        ) : null}
      </section>

      <section className="rounded-[20px] border border-border bg-surface p-5 shadow-sm" aria-label={`Questions for ${subSection.label}`}>
        <div className="mb-4 flex items-center justify-between gap-3">
          <h2 className="text-sm font-black uppercase tracking-[0.18em] text-muted">Questions</h2>
          <Badge variant="info">{subSection.questions.length} item{subSection.questions.length === 1 ? '' : 's'}</Badge>
        </div>

        {subSection.questions.length === 0 ? (
          <p className="text-sm text-muted">This sub-section has no questions — listen, then continue.</p>
        ) : showNotes ? (
          <PartANotesDocument
            partLabel={subSection.label}
            notesBody={notesBody}
            questions={subSection.questions.map((q) => ({ id: q.id, number: q.number }))}
            answers={answers}
            onAnswerChange={(id, value) => {
              const q = subSection.questions.find((item) => item.id === id);
              if (q) onAnswerChange(q, value);
            }}
            highlightingEnabled={false}
          />
        ) : (
          <div className="space-y-6">
            {subSection.questions.map((question) => (
              <QuestionItem
                key={question.id}
                question={question}
                value={answers[question.id] ?? ''}
                onChange={(value) => onAnswerChange(question, value)}
              />
            ))}
          </div>
        )}

        <div className="mt-6 flex items-center justify-end">
          <Button variant="primary" onClick={requestAdvance} loading={advancing} aria-label={isLastSection ? 'Submit attempt' : 'Advance to next sub-section'}>
            {isLastSection ? <Send className="h-4 w-4" aria-hidden="true" /> : <ArrowRight className="h-4 w-4" aria-hidden="true" />}
            {isLastSection ? 'Submit' : 'Next sub-section'}
          </Button>
        </div>
      </section>

      <Modal
        open={showConfirm}
        onClose={() => setShowConfirm(false)}
        title={isLastSection ? 'Submit Listening attempt?' : 'Move to the next sub-section?'}
      >
        <div className="space-y-4">
          <p className="text-sm leading-6 text-muted">
            {isLastSection
              ? 'This is the final sub-section. Submitting grades your attempt and you cannot return.'
              : 'You cannot return to this sub-section once you continue. Its audio and answers will be locked.'}
          </p>
          {unansweredInSection > 0 ? (
            <InlineAlert variant="warning">
              {unansweredInSection} unanswered question{unansweredInSection === 1 ? '' : 's'} in this sub-section will score zero.
            </InlineAlert>
          ) : null}
        </div>
        <div className="mt-5 flex flex-col-reverse gap-2 sm:flex-row sm:justify-end">
          <Button variant="ghost" onClick={() => setShowConfirm(false)}>Keep working</Button>
          <Button
            variant="primary"
            onClick={() => { setShowConfirm(false); onAdvance(); }}
            loading={advancing}
          >
            {isLastSection ? 'Submit now' : 'Continue'}
          </Button>
        </div>
      </Modal>
    </div>
  );
}

function SubSectionTimer({ label, remaining }: { label: string; remaining: number }) {
  const low = remaining <= 10;
  return (
    <div
      className="flex items-center gap-3 rounded-[20px] border border-border bg-surface p-4 shadow-sm"
      role="timer"
      aria-live="polite"
      aria-atomic="true"
      aria-label={`${label}, ${formatCountdown(remaining)} remaining`}
    >
      <Clock className={cn('h-5 w-5', low ? 'text-danger' : 'text-primary')} aria-hidden="true" />
      <div className="flex flex-col">
        <span className="text-xs font-bold uppercase tracking-[0.14em] text-muted">{label}</span>
        <span className={cn('font-mono text-2xl font-bold', low ? 'text-danger' : 'text-navy')}>{formatCountdown(remaining)}</span>
      </div>
    </div>
  );
}

// Per-sub-section audio. Uploaded `/v1/media/{id}/content` URLs need the Bearer
// token, which a bare <audio src> cannot attach, so those are blob-fetched via
// fetchAuthorizedObjectUrl. Anonymous TTS `/v1/listening/audio/{sha}.wav` URLs
// load directly. Autoplay is gesture-chained (the candidate clicked
// Start/Next), and AbortError from the autoplay race is swallowed.
function SubSectionAudio({
  attemptId,
  subSection,
  onePlayOnly,
}: {
  attemptId: string;
  subSection: ListeningExamSubSection;
  onePlayOnly: boolean;
}) {
  const audioRef = useRef<HTMLAudioElement>(null);
  const [resolvedSrc, setResolvedSrc] = useState<string | null>(
    subSection.audioRequiresAuth ? null : subSection.audioUrl,
  );
  const [audioError, setAudioError] = useState<string | null>(null);
  const [hasPlayedToEnd, setHasPlayedToEnd] = useState(false);

  // Resolve an authenticated media URL into a local blob URL once per section.
  useEffect(() => {
    if (!subSection.audioUrl || !subSection.audioRequiresAuth) return;
    let cancelled = false;
    let objectUrl: string | null = null;
    setResolvedSrc(null);
    setAudioError(null);
    (async () => {
      try {
        const url = await fetchAuthorizedObjectUrl(subSection.audioUrl as string);
        objectUrl = url;
        if (cancelled) {
          URL.revokeObjectURL(url);
          return;
        }
        setResolvedSrc(url);
      } catch (err) {
        if (!cancelled) setAudioError(err instanceof Error ? err.message : 'Audio could not be loaded.');
      }
    })();
    return () => {
      cancelled = true;
      if (objectUrl) URL.revokeObjectURL(objectUrl);
    };
  }, [subSection.audioUrl, subSection.audioRequiresAuth]);

  // Autoplay as soon as the source is ready. Gesture-chained via the prior
  // Start/Next click, so most browsers allow it; the AbortError that fires when
  // the element is torn down mid-play (advance) is intentionally ignored.
  useEffect(() => {
    if (!resolvedSrc) return;
    const el = audioRef.current;
    if (!el) return;
    const result = el.play();
    if (result && typeof result.catch === 'function') {
      result.catch((err: unknown) => handleAudioPlaybackError(err, setAudioError));
    }
  }, [resolvedSrc]);

  if (!subSection.audioUrl) {
    return (
      <div className="rounded-[20px] border border-border bg-surface p-5 shadow-sm">
        <div className="flex items-center gap-2 text-sm font-semibold text-muted">
          <Volume2 className="h-4 w-4" aria-hidden="true" />
          No audio is attached to this sub-section.
        </div>
      </div>
    );
  }

  // One-play: once the audio ends, replay is disabled for graded exams. The
  // native controls stay visible (so the candidate can adjust volume) but a
  // replay attempt is blocked by snapping the playhead back to the end.
  const blockReplay = onePlayOnly && hasPlayedToEnd;

  return (
    <div className="rounded-[20px] border border-border bg-surface p-5 shadow-sm">
      <div className="mb-3 flex items-center justify-between gap-2">
        <span className="inline-flex items-center gap-2 text-sm font-bold text-navy">
          <Volume2 className="h-4 w-4 text-primary" aria-hidden="true" />
          {subSection.title}
        </span>
        {onePlayOnly ? <Badge variant="warning">Plays once</Badge> : null}
      </div>
      {resolvedSrc ? (
        <audio
          ref={audioRef}
          src={resolvedSrc}
          autoPlay
          controls={!onePlayOnly}
          controlsList={onePlayOnly ? 'nodownload noplaybackrate' : undefined}
          preload="auto"
          className="w-full"
          onEnded={() => setHasPlayedToEnd(true)}
          onError={() => setAudioError('Audio failed to load. The media asset may still be processing.')}
          onSeeking={() => {
            // Forward-only / one-play: block any rewind once the clip has ended.
            const el = audioRef.current;
            if (blockReplay && el) el.currentTime = el.duration || el.currentTime;
          }}
        />
      ) : (
        <div className="flex items-center gap-2 text-sm text-muted">
          <Loader2 className="h-4 w-4 motion-safe:animate-spin" aria-hidden="true" />
          Loading audio…
        </div>
      )}
      {audioError ? <p className="mt-2 text-xs font-semibold text-danger">{audioError}</p> : null}
    </div>
  );
}

function QuestionItem({
  question,
  value,
  onChange,
}: {
  question: ListeningSessionQuestionDto;
  value: string;
  onChange: (value: string) => void;
}) {
  const isMcq = question.type === 'multiple_choice_3';
  return (
    <div className="space-y-3 border-b border-border pb-5 last:border-b-0 last:pb-0">
      <div>
        <p className="text-xs font-black uppercase tracking-[0.16em] text-muted">Question {question.number}</p>
        <h3 className="mt-2 text-base font-semibold leading-7 text-navy">{question.text}</h3>
      </div>
      {isMcq ? (
        <McqControl question={question} value={value} onChange={onChange} />
      ) : (
        <TextAnswerControl value={value} onChange={onChange} />
      )}
    </div>
  );
}

function McqControl({
  question,
  value,
  onChange,
}: {
  question: ListeningSessionQuestionDto;
  value: string;
  onChange: (value: string) => void;
}) {
  return (
    <div className="space-y-2">
      {question.options.map((optionText, index) => {
        const letter = String.fromCharCode(65 + index);
        const selected = value === letter;
        return (
          <label
            key={`${letter}-${optionText}`}
            className={cn(
              'flex min-h-11 cursor-pointer items-start gap-3 rounded-lg border border-border bg-background-light p-3 text-sm transition-colors',
              selected && 'border-primary bg-primary/5',
            )}
          >
            <input
              type="radio"
              name={question.id}
              className="mt-1"
              checked={selected}
              onChange={() => onChange(letter)}
            />
            <span className="font-mono font-bold text-navy">{letter}.</span>
            <span className="leading-6 text-navy">{optionText}</span>
          </label>
        );
      })}
    </div>
  );
}

function TextAnswerControl({
  value,
  onChange,
}: {
  value: string;
  onChange: (value: string) => void;
}) {
  return (
    <input
      className="min-h-11 w-full rounded-lg border border-border bg-background-light px-3 py-2 text-sm text-navy outline-none transition focus:border-primary focus:ring-2 focus:ring-primary/20"
      placeholder="Type your answer"
      value={value}
      onChange={(event) => onChange(event.target.value)}
    />
  );
}

// Swallow the autoplay AbortError ("play() request was interrupted") that fires
// when the audio element is torn down mid-play on advance; surface anything else.
function handleAudioPlaybackError(error: unknown, onVisibleError: (message: string) => void) {
  const message = error instanceof Error ? error.message : String(error);
  if ((error instanceof DOMException && error.name === 'AbortError') || message.includes('play() request was interrupted')) {
    return;
  }
  onVisibleError('Audio could not start. Check your device output, then reload the page.');
}

function formatCountdown(totalSec: number): string {
  const minutes = Math.floor(totalSec / 60);
  const seconds = totalSec % 60;
  return `${minutes}:${String(seconds).padStart(2, '0')}`;
}
