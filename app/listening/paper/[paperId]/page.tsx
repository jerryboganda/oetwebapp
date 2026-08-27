'use client';

import { Suspense, use, useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { useRouter, useSearchParams } from 'next/navigation';
import {
  AlertCircle,
  ArrowRight,
  CheckCircle2,
  Clock,
  Flag,
  Headphones,
  Loader2,
  Lock,
  Pause,
  Play,
  Save,
  Send,
  Volume2,
} from 'lucide-react';
import { cleanListeningPrompt, cleanListeningOption } from '@/lib/listening-question-clean';
import { LearnerDashboardShell } from '@/components/layout';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { InlineAlert } from '@/components/ui/alert';
import { Skeleton } from '@/components/ui/skeleton';
import { Modal } from '@/components/ui/modal';
import { cn } from '@/lib/utils';
import { ApiError } from '@/lib/api';
import { useTimer } from '@/hooks/useTimer';
import { fetchAuthorizedObjectUrl } from '@/lib/api';
import { readErrorMessage } from '@/lib/read-error-message';
import { ContentLockedNotice, isContentLockedError, readContentLockedMessage } from '@/components/domain/ContentLockedNotice';
import { PartANotesDocument } from '@/components/domain/listening/PartANotesDocument';
import { BCQuestionRenderer } from '@/components/domain/listening/BCQuestionRenderer';
import { ListeningAudioTransport } from '@/components/domain/listening/player/ListeningAudioTransport';
import { TechReadinessCheck } from '@/components/domain/listening/TechReadinessCheck';
import { QuestionPaperPdfViewer, type ReadingPdfAsset } from '@/components/domain/reading-pdf-viewer';
import { completeMockSection } from '@/lib/api';
import { buildTechReadinessProbe } from '@/lib/listening/tech-readiness-probe';
import { listeningV2Api } from '@/lib/listening/v2-api';
import { submitAudioCheck } from '@/lib/listening-pathway-api';
import {
  advanceListeningSection,
  createListeningPaperAnnotation,
  deleteListeningPaperAnnotation,
  getListeningPaperAnnotations,
  getListeningSession,
  recordListeningIntegrityEvent,
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
  enableAutoSync,
  markAttemptConflict,
  markAttemptSynced,
  queueOfflineAttempt,
  syncPendingAttempts,
  type OfflineAttempt,
} from '@/lib/mobile/offline-sync';
import { reconcileOfflineAnswer, type OfflineAnswerPayload } from '@/lib/mobile/offline-answer-reconciliation';
import { showCreditFeedback } from '@/lib/credit-feedback';
import {
  buildListeningExamSubSections,
  LISTENING_EXAM_DEFAULT_TIME_LIMIT_SECONDS,
  type ListeningExamSubSection,
} from '@/lib/listening-exam-sections';
import { resolveBlockedSeekTarget, shouldResumeAfterBlockedPause } from '@/lib/listening/audio-integrity';
import { correctedNowMs, readServerClockOffsetMs } from '@/lib/server-clock';

type SaveState = 'idle' | 'saving' | 'saved' | 'offline-saved' | 'conflict' | 'error';
type AnswerSaveResult = 'server' | 'offline';

function isNetworkInterruption(error: unknown): boolean {
  return (error instanceof ApiError && error.status === 0)
    || (typeof navigator !== 'undefined' && !navigator.onLine);
}

// Only the strict one-way exam surface lives here. The legacy
// diagnostic/mock/practice player (app/listening/player/[id]) still serves
// every other mode. Default to "exam" so a bare deep link to this route is a
// graded one-way computer attempt. Legacy paper query values fail closed to
// the computer exam surface and are never sent to the API.
function normalizeExamMode(raw: string | null): ListeningSessionMode {
  return 'exam';
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

function isListeningAudioCheckError(err: unknown): boolean {
  if (typeof err !== 'object' || err === null) return false;
  const e = err as { code?: unknown; message?: unknown; detail?: { code?: unknown; message?: unknown } };
  const code = typeof e.code === 'string' ? e.code : typeof e.detail?.code === 'string' ? e.detail.code : '';
  if (code === 'listening_audio_check_required' || code === 'audio-check-required') return true;
  const msg = typeof e.message === 'string' ? e.message : typeof e.detail?.message === 'string' ? e.detail.message : '';
  return msg.includes('Pass the Listening sound check');
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
  const [techReadiness, setTechReadiness] = useState<{ audioOk: boolean; durationMs: number } | null>(null);
  const [submitting, setSubmitting] = useState(false);
  const [advancing, setAdvancing] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [contentLockedMessage, setContentLockedMessage] = useState<string | null>(null);
  const [saveState, setSaveState] = useState<SaveState>('idle');
  // One-way cursor. Only ever increments (Next or timer auto-advance).
  const [currentIndex, setCurrentIndex] = useState(0);
  const [showExamSubmitConfirm, setShowExamSubmitConfirm] = useState(false);
  // Per-paper question-paper annotations (Part B/C highlight/strikethrough).
  const [annotations, setAnnotations] = useState<ReadingPaperAnnotationDto[]>([]);

  const saveTimers = useRef<Record<string, ReturnType<typeof setTimeout>>>({});
  const dirtyQuestionIds = useRef<Set<string>>(new Set());
  const serverAnswers = useRef<Record<string, string | null>>({});
  const answerBaseValues = useRef<Record<string, string | null>>({});
  const serverClockOffsetMs = useRef(0);
  // Guards the advance pipeline so a Next-click racing the timer's onExpire
  // cannot fire two advances (and two backend section-cursor writes).
  const advanceInFlight = useRef(false);

  const syncServerClock = useCallback((serverNow: string | null | undefined) => {
    serverClockOffsetMs.current = readServerClockOffsetMs(serverNow);
  }, []);
  const correctedClockNow = useCallback(
    () => correctedNowMs(serverClockOffsetMs.current),
    [],
  );

  const logIntegrityEvent = useCallback((
    eventType: Parameters<typeof recordListeningIntegrityEvent>[1],
    details?: Record<string, unknown>,
  ) => {
    if (!attempt?.attemptId) return;
    void recordListeningIntegrityEvent(
      attempt.attemptId,
      eventType,
      details ? JSON.stringify(details) : undefined,
    ).catch(() => undefined);
  }, [attempt?.attemptId]);

  useEffect(() => {
    if (!attempt?.attemptId || typeof document === 'undefined') return;
    const logVisibility = () => logIntegrityEvent(
      document.visibilityState === 'hidden' ? 'page_hidden' : 'page_visible',
    );
    const onBlur = () => logIntegrityEvent('window_blur');
    const onFocus = () => logIntegrityEvent('window_focus');
    document.addEventListener('visibilitychange', logVisibility);
    window.addEventListener('blur', onBlur);
    window.addEventListener('focus', onFocus);
    return () => {
      document.removeEventListener('visibilitychange', logVisibility);
      window.removeEventListener('blur', onBlur);
      window.removeEventListener('focus', onFocus);
    };
  }, [attempt?.attemptId, logIntegrityEvent]);

  const subSections = useMemo<ListeningExamSubSection[]>(
    () => (session ? buildListeningExamSubSections(session) : []),
    [session],
  );
  const activeSubSection = subSections[currentIndex] ?? null;
  const isLastSection = subSections.length > 0 && currentIndex >= subSections.length - 1;
  const onePlayOnly = session?.modePolicy.onePlayOnly ?? true;
  const allSectionsAudioReady = subSections.length > 0
    && subSections.every((subSection) => Boolean(subSection.audioUrl));
  const scoredAudioUrls = useMemo(() => {
    // Keep the verify set minimal — per-section URLs are the playback source.
    // Extract URLs duplicate the section map and would double the verification
    // work for the full exam (5 sections vs 1 for a single part), which made
    // full-exam sound checks timeout while part practice succeeded.
    const urls = [
      session?.paper.audioUrl ?? null,
      ...Object.values(session?.paper.audioUrlByPart ?? {}),
      ...subSections.map((section) => section.audioUrl),
    ];
    return [...new Set(urls
      .filter((url): url is string => Boolean(url?.trim()))
      .map((url) => url.trim()))];
  }, [session?.paper.audioUrl, session?.paper.audioUrlByPart, subSections]);

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
    setTechReadiness(null);
    try {
      const loaded = await getListeningSession(paperId, {
        mode,
        attemptId: resumeAttemptId || undefined,
      });
      setSession(loaded);
      syncServerClock(loaded.serverNow ?? loaded.attempt?.serverNow);
      if (loaded.attempt) {
        const loadedSections = buildListeningExamSubSections(loaded);
        const loadedCursor = Math.max(0, loaded.attempt.sectionCursor ?? 0);
        setCurrentIndex(loadedSections.length > 0
          ? Math.min(loadedCursor, loadedSections.length - 1)
          : 0);
        setAttempt(loaded.attempt);
        const restored: Record<string, string> = {};
        for (const [questionId, value] of Object.entries(loaded.attempt.answers ?? {})) {
          if (typeof value === 'string') restored[questionId] = value;
        }
        setAnswers(restored);
        serverAnswers.current = Object.fromEntries(
          Object.entries(loaded.attempt.answers ?? {}).map(([questionId, value]) => [questionId, typeof value === 'string' ? value : null]),
        );
        answerBaseValues.current = {};
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
  }, [mode, paperId, resumeAttemptId, syncServerClock]);

  useEffect(() => {
    void load();
  }, [load]);

  const start = useCallback(async () => {
    const readiness = techReadiness;
    if (!readiness?.audioOk) {
      setError('Complete the audio readiness check before starting this strict Listening attempt.');
      return;
    }
    setStarting(true);
    setError(null);
    setContentLockedMessage(null);
    try {
      // Ensure the 24h pathway sound-check gate is satisfied before creating
      // the attempt. The backend fix refreshes the timestamp on every successful
      // check, so a single retry is sufficient to recover from an expired gate.
      try {
        await submitAudioCheck({ outcome: 'clear' });
      } catch {
        // Non-fatal — startListeningAttempt will surface the authoritative error
        // if the check truly failed. Swallow so we don't mask the start error.
      }
      let started: Awaited<ReturnType<typeof startListeningAttempt>>;
      try {
        started = await startListeningAttempt(paperId, mode, { mockAttemptId, mockSectionId });
      } catch (err) {
        if (isListeningAudioCheckError(err)) {
          // Gate was expired — one more sound-check refresh then retry start.
          await submitAudioCheck({ outcome: 'clear' });
          started = await startListeningAttempt(paperId, mode, { mockAttemptId, mockSectionId });
        } else {
          throw err;
        }
      }
      showCreditFeedback(started.feedbackMessage);
      syncServerClock(started.serverNow);
      const probe = await buildTechReadinessProbe({
        audioOk: readiness.audioOk,
        durationMs: readiness.durationMs,
      });
      await listeningV2Api.recordTechReadiness(started.attemptId, probe);
      setAttempt(started);
      const restored: Record<string, string> = {};
      for (const [questionId, value] of Object.entries(started.answers ?? {})) {
        if (typeof value === 'string') restored[questionId] = value;
      }
      setAnswers(restored);
      serverAnswers.current = Object.fromEntries(
        Object.entries(started.answers ?? {}).map(([questionId, value]) => [questionId, typeof value === 'string' ? value : null]),
      );
      answerBaseValues.current = {};
      setCurrentIndex(Math.max(0, started.sectionCursor ?? 0));
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
  }, [mockAttemptId, mockSectionId, mode, paperId, resumeAttemptId, router, search, syncServerClock, techReadiness]);

  const persistAnswer = useCallback(async (
    questionId: string,
    value: string,
    baseValue = answerBaseValues.current[questionId] ?? serverAnswers.current[questionId] ?? null,
  ): Promise<AnswerSaveResult> => {
    if (!attempt) return 'server';
    setSaveState('saving');
    try {
      await saveListeningAnswer(attempt.attemptId, questionId, value);
      dirtyQuestionIds.current.delete(questionId);
      serverAnswers.current[questionId] = value;
      delete answerBaseValues.current[questionId];
      setSaveState('saved');
      return 'server';
    } catch (err) {
      if (isNetworkInterruption(err)) {
        try {
          const payload: OfflineAnswerPayload = { questionId, value, baseValue };
          await queueOfflineAttempt(
            'listening-answer',
            attempt.attemptId,
            payload,
            { id: `listening-answer:${encodeURIComponent(attempt.attemptId)}:${encodeURIComponent(questionId)}` },
          );
          setSaveState('offline-saved');
          return 'offline';
        } catch {
          // Encryption is mandatory for offline answer recovery. If it is not
          // available, keep the answer dirty and surface the normal failure.
        }
      }
      setSaveState('error');
      setError(readErrorMessage(err, 'Autosave failed.'));
      return 'server';
    }
  }, [attempt]);

  // Debounced autosave, mirroring the Reading player's 400ms settle.
  const setAnswer = useCallback((question: ListeningSessionQuestionDto, value: string) => {
    if (!attempt) return;
    if (!Object.prototype.hasOwnProperty.call(answerBaseValues.current, question.id)) {
      answerBaseValues.current[question.id] = serverAnswers.current[question.id] ?? null;
    }
    setAnswers((prev) => ({ ...prev, [question.id]: value }));
    logIntegrityEvent('answer_changed', { questionId: question.id });
    dirtyQuestionIds.current.add(question.id);
    setSaveState('saving');
    if (saveTimers.current[question.id]) clearTimeout(saveTimers.current[question.id]);
    saveTimers.current[question.id] = setTimeout(() => {
      void persistAnswer(question.id, value, answerBaseValues.current[question.id]);
    }, 400);
  }, [attempt, logIntegrityEvent, persistAnswer]);

  // Flush every dirty answer for the section we are leaving (one-way: it can
  // never be edited again, so its answers must land before we advance).
  const flushPendingAnswers = useCallback(async () => {
    if (!attempt) return;
    Object.values(saveTimers.current).forEach(clearTimeout);
    saveTimers.current = {};
    const pending = Array.from(dirtyQuestionIds.current)
      .map((questionId) => [questionId, answers[questionId], answerBaseValues.current[questionId] ?? serverAnswers.current[questionId] ?? null] as const)
      .filter(([, value]) => typeof value === 'string');
    if (pending.length === 0) return;
    setSaveState('saving');
    try {
      const results = await Promise.all(pending.map(([questionId, value, baseValue]) => persistAnswer(questionId, value, baseValue)));
      if (results.every((result) => result === 'server')) setSaveState('saved');
      else setSaveState('offline-saved');
    } catch (err) {
      setSaveState('error');
      setError(readErrorMessage(err, 'Autosave failed.'));
    }
  }, [answers, attempt, persistAnswer]);

  const reconcilePendingOfflineAnswer = useCallback(async (queued: OfflineAttempt): Promise<boolean> => {
    if (!attempt || queued.subtest !== 'listening-answer' || queued.contentId !== attempt.attemptId) return false;
    const payload = queued.payload as Partial<OfflineAnswerPayload>;
    if (typeof payload.questionId !== 'string' || typeof payload.value !== 'string') return false;

    const latest = await getListeningSession(paperId, { mode, attemptId: attempt.attemptId });
    const serverValue = latest.attempt?.answers?.[payload.questionId] ?? null;
    const decision = reconcileOfflineAnswer(serverValue, {
      questionId: payload.questionId,
      value: payload.value,
      baseValue: typeof payload.baseValue === 'string' ? payload.baseValue : null,
    });

    if (decision === 'already-synced') {
      await markAttemptSynced(queued.id);
      dirtyQuestionIds.current.delete(payload.questionId);
      serverAnswers.current[payload.questionId] = payload.value;
      delete answerBaseValues.current[payload.questionId];
      setSaveState('saved');
      return true;
    }
    if (decision === 'conflict') {
      await markAttemptConflict(queued.id);
      setSaveState('conflict');
      setError('A newer Listening answer is already saved on the server. Your offline answer was not applied.');
      return true;
    }

    try {
      await saveListeningAnswer(attempt.attemptId, payload.questionId, payload.value);
      await markAttemptSynced(queued.id);
      dirtyQuestionIds.current.delete(payload.questionId);
      serverAnswers.current[payload.questionId] = payload.value;
      delete answerBaseValues.current[payload.questionId];
      setSaveState('saved');
      return true;
    } catch (err) {
      if (isNetworkInterruption(err)) return false;
      throw err;
    }
  }, [attempt, mode, paperId]);

  useEffect(() => {
    if (!attempt) return;
    void syncPendingAttempts(reconcilePendingOfflineAnswer).catch(() => undefined);
    return enableAutoSync(reconcilePendingOfflineAnswer);
  }, [attempt, reconcilePendingOfflineAnswer]);

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
        const advanced = await advanceListeningSection(attempt.attemptId, nextIndex);
        logIntegrityEvent('section_transition', {
          from: activeSubSection?.partCode ?? currentIndex,
          to: subSections[advanced.sectionCursor]?.partCode ?? advanced.sectionCursor,
        });
        setCurrentIndex(advanced.sectionCursor);
      } catch (err) {
        // The server owns the one-way cursor. Never move the client forward
        // after a failed cursor write, otherwise refresh could expose a section
        // whose answers are not authoritative on the server.
        setError(readErrorMessage(err, 'Could not record section advance.'));
        return;
      }
      setSaveState('idle');
    } finally {
      advanceInFlight.current = false;
      setAdvancing(false);
    }
  }, [activeSubSection?.partCode, attempt, currentIndex, flushPendingAnswers, isLastSection, logIntegrityEvent, subSections, submit]);

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
  const unansweredQuestions = session.questions.filter((question) => (answers[question.id] ?? '').trim().length === 0);
  const unansweredQuestionNumbers = unansweredQuestions
    .map((question) => question.number)
    .sort((a, b) => a - b);
  const unansweredQuestionList = unansweredQuestionNumbers.map((number) => `Q${number}`).join(', ');

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
            allSectionsAudioReady={allSectionsAudioReady}
            audioUrls={scoredAudioUrls}
            techReadiness={techReadiness}
            preflight={session.preflight}
            starting={starting}
            onTechReadinessReady={(result) => {
              setTechReadiness(result);
              setError(null);
            }}
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
              submitting={submitting}
              onSubmit={() => setShowExamSubmitConfirm(true)}
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
                nowMs={correctedClockNow}
                resumeAudioState={attempt.audioPlaybackSection === activeSubSection.partCode ? attempt.audioPlaybackState : 'not_started'}
                resumeAudioAtMs={attempt.audioPlaybackSection === activeSubSection.partCode ? attempt.audioResumeAtMs : null}
                resumeAudioQuestionIndex={attempt.audioPlaybackSection === activeSubSection.partCode ? attempt.audioQuestionIndex : null}
                onAnswerChange={setAnswer}
                onPersistAnswer={persistAnswer}
                onIntegrityEvent={logIntegrityEvent}
                onAdvance={() => void advance()}
                saveState={saveState}
                answeredCount={answeredCount}
                totalQuestions={totalQuestions}
                onSubmit={() => setShowExamSubmitConfirm(true)}
              />
            ) : null}

            <Modal
              open={showExamSubmitConfirm}
              onClose={() => setShowExamSubmitConfirm(false)}
              title="Submit listening task?"
            >
              <div className="space-y-4">
                <p className="text-sm leading-6 text-muted">
                  Are you sure you want to submit this exam? You will not be able to continue this attempt after submission.
                </p>
                {unansweredQuestionNumbers.length > 0 ? (
                  <InlineAlert variant="warning">
                    {unansweredQuestionNumbers.length} unanswered question{unansweredQuestionNumbers.length === 1 ? '' : 's'} will score zero if you submit now: {unansweredQuestionList}.
                  </InlineAlert>
                ) : null}
              </div>
              <div className="mt-5 flex flex-col-reverse gap-2 sm:flex-row sm:justify-end">
                <Button variant="ghost" onClick={() => setShowExamSubmitConfirm(false)}>Keep working</Button>
                <Button
                  variant="primary"
                  loading={submitting}
                  onClick={() => {
                    setShowExamSubmitConfirm(false);
                    void submit();
                  }}
                >
                  Submit now
                </Button>
              </div>
            </Modal>
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
  allSectionsAudioReady,
  audioUrls,
  techReadiness,
  preflight,
  starting,
  onTechReadinessReady,
  onStart,
}: {
  title: string;
  sectionCount: number;
  audioAvailable: boolean;
  audioUnavailableReason: string | null;
  allSectionsAudioReady: boolean;
  audioUrls: string[];
  techReadiness: { audioOk: boolean; durationMs: number } | null;
  preflight: ListeningSessionDto['preflight'];
  starting: boolean;
  onTechReadinessReady: (result: { audioOk: boolean; durationMs: number }) => void;
  onStart: () => void;
}) {
  return (
    <section className="rounded-[20px] border border-border bg-surface px-5 py-8 text-center shadow-sm">
      <Headphones className="mx-auto h-7 w-7 text-info" aria-hidden="true" />
      <h1 className="mt-4 text-2xl font-semibold tracking-tight text-navy">{title}</h1>
      {preflight ? (
        <div className="mx-auto mt-5 max-w-2xl rounded-2xl border border-border bg-background-light p-4 text-left" data-testid="listening-preflight-summary">
          <h2 className="text-xs font-black uppercase tracking-[0.16em] text-muted">Confirm your test</h2>
          <dl className="mt-3 grid gap-2 text-sm sm:grid-cols-2">
            <div>
              <dt className="text-xs font-semibold uppercase text-muted">Candidate</dt>
              <dd className="font-semibold text-navy">{preflight.candidate.displayName}</dd>
            </div>
            <div>
              <dt className="text-xs font-semibold uppercase text-muted">Profession</dt>
              <dd className="font-semibold text-navy">{preflight.candidate.professionLabel ?? preflight.candidate.professionId ?? 'Not specified'}</dd>
            </div>
            <div>
              <dt className="text-xs font-semibold uppercase text-muted">Selected test</dt>
              <dd className="font-semibold text-navy">{preflight.selectedTest.title}</dd>
            </div>
            <div>
              <dt className="text-xs font-semibold uppercase text-muted">Eligibility</dt>
              <dd className={cn('font-semibold', preflight.eligibility.eligible ? 'text-success' : 'text-danger')}>
                {preflight.eligibility.eligible ? 'Checked — eligible to start' : preflight.eligibility.reason ?? 'Not eligible to start'}
              </dd>
            </div>
          </dl>
        </div>
      ) : null}
      <p className="mx-auto mt-2 max-w-2xl text-sm leading-6 text-muted">
        Each sub-section plays its own audio once and runs its own countdown. When the timer reaches zero,
        a confirmation is required before the sub-section locks — you can never return to a previous
        sub-section. Use headphones.
      </p>
      <div className="mx-auto mt-5 max-w-2xl text-left">
        <TechReadinessCheck audioUrls={audioUrls} onReady={onTechReadinessReady} />
      </div>
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
      {audioAvailable && !allSectionsAudioReady ? (
        <div className="mx-auto mt-4 max-w-2xl">
          <InlineAlert variant="warning">
            One or more scored Listening sections does not have a complete audio asset yet. Starting is disabled until the paper is repaired.
          </InlineAlert>
        </div>
      ) : null}
      <div className="mt-5 flex flex-col items-center gap-2">
        <Button
          variant="primary"
          onClick={onStart}
          loading={starting}
          disabled={sectionCount === 0 || !audioAvailable || !allSectionsAudioReady || !techReadiness?.audioOk || preflight?.eligibility.eligible === false}
        >
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
  submitting,
  onSubmit,
}: {
  answeredCount: number;
  totalQuestions: number;
  saveState: SaveState;
  currentIndex: number;
  sectionCount: number;
  activeLabel: string;
  submitting: boolean;
  onSubmit: () => void;
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
        <div className="flex flex-wrap items-center gap-3">
          <SaveStatus state={saveState} />
          <Button
            variant="primary"
            onClick={onSubmit}
            loading={submitting}
            data-testid="listening-submit-exam"
          >
            <Send className="h-4 w-4" aria-hidden="true" />
            Submit
          </Button>
        </div>
      </div>
    </section>
  );
}

function SaveStatus({ state }: { state: SaveState }) {
  const label = {
    idle: 'Autosave ready',
    saving: 'Saving...',
    saved: 'Saved',
    'offline-saved': 'Saved securely offline',
    conflict: 'Server answer kept',
    error: 'Save failed',
  }[state];
  const Icon = state === 'saving' ? Loader2 : state === 'error' || state === 'conflict' ? AlertCircle : Save;
  return (
    <span
      className={cn('inline-flex items-center gap-2 text-sm font-semibold', state === 'error' || state === 'conflict' ? 'text-danger' : 'text-muted')}
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
  nowMs,
  resumeAudioState,
  resumeAudioAtMs,
  resumeAudioQuestionIndex,
  onAnswerChange,
  onPersistAnswer,
  onIntegrityEvent,
  onAdvance,
  saveState,
  answeredCount,
  totalQuestions,
  onSubmit,
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
  nowMs: () => number;
  resumeAudioState?: 'not_started' | 'active' | 'ended';
  resumeAudioAtMs?: number | null;
  resumeAudioQuestionIndex?: number | null;
  onAnswerChange: (question: ListeningSessionQuestionDto, value: string) => void;
  onPersistAnswer: (questionId: string, value: string) => Promise<unknown>;
  onIntegrityEvent: (
    eventType: Parameters<typeof recordListeningIntegrityEvent>[1],
    details?: Record<string, unknown>,
  ) => void;
  onAdvance: () => void;
  saveState: SaveState;
  answeredCount: number;
  totalQuestions: number;
  onSubmit: () => void;
}) {
  const [showConfirm, setShowConfirm] = useState(false);
  const [timerExpired, setTimerExpired] = useState(false);
  const [audioFailure, setAudioFailure] = useState(false);
  // The timed flow must stop while scored audio is buffering or stalled. The
  // server deadline remains authoritative, so this never grants extra time;
  // it only prevents the client timer from racing ahead while playback is
  // unavailable.
  const [audioBuffering, setAudioBuffering] = useState(true);
  const isPartB = subSection.partCode === 'B';
  const partBExtracts = isPartB
    ? (subSection.extracts?.length ? subSection.extracts : subSection.extract ? [subSection.extract] : [])
    : [];
  // Exam Part B slicing is only valid when each of the 6 questions has its own
  // cue-bounded extract. Legacy/monolithic papers with a single 08:33 extract
  // for 6 questions must show all 6 at once — otherwise Q2..Q6 are unreachable.
  const shouldSliceExamPartB = isPartB
    && partBExtracts.length > 1
    && subSection.questions.length > 1
    && partBExtracts.length === subSection.questions.length;
  const [partBQuestionIndex, setPartBQuestionIndex] = useState(
    shouldSliceExamPartB
      ? Math.max(0, Math.min(subSection.questions.length - 1, resumeAudioQuestionIndex ?? 0))
      : 0,
  );
  const activePartBQuestion = shouldSliceExamPartB ? (subSection.questions[partBQuestionIndex] ?? null) : null;
  const activePartBExtract = shouldSliceExamPartB ? (partBExtracts[partBQuestionIndex] ?? null) : null;
  const visibleQuestions = shouldSliceExamPartB
    ? (activePartBQuestion ? [activePartBQuestion] : [])
    : subSection.questions;
  const [partBExtractEnded, setPartBExtractEnded] = useState(false);
  const canMoveToNextPartBQuestion = shouldSliceExamPartB
    && !timerExpired
    && partBQuestionIndex < subSection.questions.length - 1;
  // Timer expiry opens the same explicit boundary confirmation as the Next
  // button. Reset is implicit because the panel is remounted on every advance.
  const expiredRef = useRef(false);
  const handleExpire = useCallback(() => {
    if (expiredRef.current) return;
    expiredRef.current = true;
    setTimerExpired(true);
    setShowConfirm(true);
  }, []);

  const handleAudioFailure = useCallback(() => setAudioFailure(true), []);
  const handlePartBExtractComplete = useCallback(() => setPartBExtractEnded(true), []);

  useEffect(() => {
    setPartBExtractEnded(false);
  }, [partBQuestionIndex]);

  useEffect(() => {
    setAudioFailure(false);
  }, [subSection.index, partBQuestionIndex]);

  const { remaining, pause: pauseTimer, resume: resumeTimer } = useTimer(
    subSection.timeLimitSeconds > 0 ? subSection.timeLimitSeconds : LISTENING_EXAM_DEFAULT_TIME_LIMIT_SECONDS,
    'down',
    handleExpire,
    // Listening-namespaced sessionStorage key so a mid-countdown refresh
    // resumes this sub-section (and never collides with the Reading timer).
    `listening-exam:${attemptId}:${subSection.index}`,
    nowMs,
  );

  useEffect(() => {
    if (audioBuffering) pauseTimer();
    else resumeTimer();
  }, [audioBuffering, pauseTimer, resumeTimer]);

  const unansweredInSection = visibleQuestions.filter((q) => (answers[q.id] ?? '').trim().length === 0).length;

  const requestAdvance = () => {
    if (advancing || audioFailure) return;
    if (shouldSliceExamPartB && !partBExtractEnded && !timerExpired) return;
    setShowConfirm(true);
  };

  const isPartA = subSection.partCode.startsWith('A');
  const notesBody = subSection.extract?.notesBody?.trim() || '';
  const showNotes = isPartA && notesBody.length > 0;
  const mediaAssetId = mediaAssetIdFromUrl(questionPaperUrl);
  const assetId = mediaAssetId ?? `qp-${subSection.partCode}`;
  const pdfAssets: ReadingPdfAsset[] = questionPaperUrl
    ? [{ id: assetId, part: subSection.partCode, title: subSection.title, downloadPath: questionPaperUrl }]
    : [];
  const showPdf = false;
  const canAnnotate = false;

  return (
    <div className="space-y-6">
      <SubSectionAudio
        key={`${attemptId}:${subSection.index}:${shouldSliceExamPartB ? partBQuestionIndex : 'all'}`}
        attemptId={attemptId}
        subSection={subSection}
        cueStartMs={shouldSliceExamPartB ? (activePartBExtract?.audioStartMs ?? null) : null}
        cueEndMs={shouldSliceExamPartB ? (activePartBExtract?.audioEndMs ?? null) : null}
        onExtractComplete={shouldSliceExamPartB ? handlePartBExtractComplete : undefined}
        resumeState={resumeAudioState}
        resumeAtMs={resumeAudioAtMs}
        questionIndex={shouldSliceExamPartB ? partBQuestionIndex : null}
        onIntegrityEvent={onIntegrityEvent}
        onBufferingChange={setAudioBuffering}
        onAudioFailure={handleAudioFailure}
        audioFailure={audioFailure}
        onePlayOnly={onePlayOnly}
        saveState={saveState}
        answeredCount={answeredCount}
        totalQuestions={totalQuestions}
        attemptSecondsRemaining={remaining}
        onSubmit={onSubmit}
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

      <section className="rounded-[20px] border border-border bg-surface p-5 shadow-sm" aria-label={`Questions for ${subSection.label}`}>
        <div className="mb-4 flex items-center justify-between gap-3">
          <div className="flex items-center gap-2">
            <h2 className="text-sm font-black uppercase tracking-[0.18em] text-muted">Questions</h2>
            <span className="text-sm font-bold text-navy">{subSection.label}</span>
          </div>
          <Badge variant="info">
            {shouldSliceExamPartB
              ? `Question ${partBQuestionIndex + 1} of ${subSection.questions.length}`
              : `${subSection.questions.length} item${subSection.questions.length === 1 ? '' : 's'}`}
          </Badge>
        </div>

        {visibleQuestions.length === 0 ? (
          <p className="text-sm text-muted">This sub-section has no questions — listen, then continue.</p>
        ) : showNotes ? (
          <PartANotesDocument
            partLabel={subSection.label}
            notesBody={notesBody}
            questions={visibleQuestions.map((q) => ({ id: q.id, number: q.number }))}
            answers={answers}
            onAnswerChange={(id, value) => {
              if (audioFailure) return;
              const q = visibleQuestions.find((item) => item.id === id);
              if (q) onAnswerChange(q, value);
            }}
            locked={audioFailure}
            highlightingEnabled={false}
          />
        ) : (
          <div className="space-y-6">
            {visibleQuestions.map((question) => (
              <BCQuestionRenderer
                key={question.id}
                questionNumber={question.number}
                partLabel={subSection.label}
                prompt={question.text}
                options={question.options}
                optionKeys={question.optionKeys}
                value={answers[question.id] ?? ''}
                locked={audioFailure}
                onChange={(value) => {
                  if (!audioFailure) onAnswerChange(question, value);
                }}
              />
            ))}
          </div>
        )}

        <div className="mt-6 flex items-center justify-end">
          <Button
            variant="primary"
            onClick={requestAdvance}
            loading={advancing}
            disabled={audioFailure || (shouldSliceExamPartB && !partBExtractEnded && !timerExpired)}
            aria-label={canMoveToNextPartBQuestion ? 'Next question' : isLastSection ? 'Submit attempt' : 'Advance to next sub-section'}
          >
            {canMoveToNextPartBQuestion ? (
              <>
                <ArrowRight className="h-4 w-4" aria-hidden="true" />
                Next question
              </>
            ) : isLastSection ? (
              <>
                <Send className="h-4 w-4" aria-hidden="true" />
                Submit
              </>
            ) : (
              <>
                <ArrowRight className="h-4 w-4" aria-hidden="true" />
                Next sub-section
              </>
            )}
          </Button>
        </div>
      </section>

      <Modal
        open={showConfirm}
        onClose={() => setShowConfirm(false)}
        title={canMoveToNextPartBQuestion
          ? 'Move to the next Part B question?'
          : isLastSection ? 'Submit Listening attempt?' : 'Move to the next sub-section?'}
      >
        <div className="space-y-4">
          <p className="text-sm leading-6 text-muted">
            {timerExpired
              ? 'The sub-section timer has ended. Confirm below to save and permanently lock this sub-section.'
              : canMoveToNextPartBQuestion
              ? `Question ${activePartBQuestion?.number ?? ''} will be locked. The next short extract will start and you cannot return to this question.`
              : isLastSection
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
            onClick={() => {
              setShowConfirm(false);
              if (canMoveToNextPartBQuestion) {
                const nextQuestionIndex = partBQuestionIndex + 1;
                const nextExtract = partBExtracts[nextQuestionIndex];
                if (activePartBQuestion) {
                  const currentVal = answers[activePartBQuestion.id] ?? '';
                  if (currentVal) {
                    void onPersistAnswer(activePartBQuestion.id, currentVal);
                  }
                }
                onIntegrityEvent('audio_started', {
                  section: subSection.partCode,
                  cuePointMs: nextExtract?.audioStartMs ?? 0,
                  questionIndex: nextQuestionIndex,
                  playbackIntent: 'confirmed_next_question',
                });
                setPartBQuestionIndex(nextQuestionIndex);
                return;
              }
              onAdvance();
            }}
            loading={advancing}
          >
            {canMoveToNextPartBQuestion
              ? 'Lock & start next'
              : isLastSection ? 'Submit now' : 'Continue'}
          </Button>
        </div>
      </Modal>
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
  cueStartMs,
  cueEndMs,
  onExtractComplete,
  resumeState = 'not_started',
  resumeAtMs,
  questionIndex,
  onIntegrityEvent,
  onBufferingChange,
  onAudioFailure,
  audioFailure,
  onePlayOnly,
  saveState,
  answeredCount,
  totalQuestions,
  attemptSecondsRemaining,
  onSubmit,
}: {
  attemptId: string;
  subSection: ListeningExamSubSection;
  cueStartMs?: number | null;
  cueEndMs?: number | null;
  onExtractComplete?: () => void;
  resumeState?: 'not_started' | 'active' | 'ended';
  resumeAtMs?: number | null;
  questionIndex?: number | null;
  onIntegrityEvent: (
    eventType: Parameters<typeof recordListeningIntegrityEvent>[1],
    details?: Record<string, unknown>,
  ) => void;
  onBufferingChange: (buffering: boolean) => void;
  onAudioFailure: () => void;
  audioFailure: boolean;
  onePlayOnly: boolean;
  saveState: SaveState;
  answeredCount: number;
  totalQuestions: number;
  attemptSecondsRemaining: number | null;
  onSubmit: () => void;
}) {
  const audioRef = useRef<HTMLAudioElement>(null);
  const [resolvedSrc, setResolvedSrc] = useState<string | null>(
    subSection.audioRequiresAuth ? null : subSection.audioUrl,
  );
  const [audioError, setAudioError] = useState<string | null>(null);
  const [isBuffering, setIsBuffering] = useState(true);
  const [hasPlayedToEnd, setHasPlayedToEnd] = useState(resumeState === 'ended');
  const [needsUserPlay, setNeedsUserPlay] = useState(false);
  const [audioRetryKey, setAudioRetryKey] = useState(0);
  const [progressSeconds, setProgressSeconds] = useState(0);
  const [durationSeconds, setDurationSeconds] = useState(0);
  const [isPlaying, setIsPlaying] = useState(false);
  const lastKnownTimeRef = useRef(0);
  const lastProgressLoggedAtRef = useRef(0);
  const allowedProgrammaticPauseRef = useRef(false);
  const programmaticSeekTargetRef = useRef<number | null>(null);
  const hasStartedRef = useRef(false);
  const autoPlayTriedRef = useRef(false);

  const playAbortPendingRef = useRef(false);
  const playAbortCountRef = useRef(0);
  const onAudioFailureRef = useRef(onAudioFailure);
  useEffect(() => {
    onAudioFailureRef.current = onAudioFailure;
  }, [onAudioFailure]);

  const setBuffering = useCallback((buffering: boolean) => {
    setIsBuffering(buffering);
    onBufferingChange(buffering);
  }, [onBufferingChange]);

  // Resolve an authenticated media URL into a local blob URL once per section.
  // Includes a retry key so the user can recover from transient network failures.
  useEffect(() => {
    if (!subSection.audioUrl) {
      setResolvedSrc(null);
      setBuffering(false);
      return;
    }
    if (!subSection.audioRequiresAuth) {
      setResolvedSrc(subSection.audioUrl);
      return;
    }
    let cancelled = false;
    let objectUrl: string | null = null;
    setResolvedSrc(null);
    setAudioError(null);
    setNeedsUserPlay(false);
    autoPlayTriedRef.current = false;
    playAbortPendingRef.current = false;
    playAbortCountRef.current = 0;
    setBuffering(true);
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
        if (!cancelled) {
          // Network/auth failure — show retry, don't leave in infinite buffering.
          setBuffering(false);
          onIntegrityEvent('audio_error', {
            section: subSection.partCode,
            questionIndex,
            playbackValidity: 'admin_review_required',
            loadFailure: true,
          });
          onAudioFailureRef.current();
          setAudioError(err instanceof Error ? err.message : 'Audio could not be loaded. Check your connection and retry.');
        }
      }
    })();
    return () => {
      cancelled = true;
      if (objectUrl) URL.revokeObjectURL(objectUrl);
    };
  }, [audioRetryKey, onIntegrityEvent, questionIndex, setBuffering, subSection.audioRequiresAuth, subSection.audioUrl, subSection.partCode]);

  const tryPlay = useCallback(() => {
    const el = audioRef.current;
    if (!el) return;
    setNeedsUserPlay(false);
    const result = el.play();
    if (result && typeof result.catch === 'function') {
      result.catch((err: unknown) => {
        const msg = err instanceof Error ? err.message : String(err);
        const isAbort = err instanceof DOMException && err.name === 'AbortError';
        const isNotAllowed = err instanceof DOMException && err.name === 'NotAllowedError';
        if (isAbort || msg.includes('play() request was interrupted')) {
          setBuffering(true);
          if (playAbortCountRef.current >= 2) {
            setNeedsUserPlay(true);
            setAudioError(null);
            return;
          }
          playAbortCountRef.current += 1;
          playAbortPendingRef.current = true;
          return;
        }
        if (isNotAllowed || msg.includes('gesture') || msg.includes('user') || msg.includes('NotAllowed')) {
          setBuffering(true);
          setNeedsUserPlay(true);
          setAudioError(null);
          return;
        }
        setBuffering(true);
        handleAudioPlaybackError(err, setAudioError);
      });
    }
  }, []);

  const handleLoadedMetadata = useCallback(() => {
    const el = audioRef.current;
    if (el) {
      const dur = Number.isFinite(el.duration) ? el.duration : 0;
      if (dur > 0) setDurationSeconds(dur);
    }
    if (!el || !onePlayOnly) return;
    el.defaultPlaybackRate = 1;
    el.playbackRate = 1;
    const cueStart = cueStartMs != null && cueStartMs >= 0 ? cueStartMs / 1000 : null;
    const resumeAt = resumeState === 'active' && resumeAtMs != null && resumeAtMs >= 0
      ? resumeAtMs / 1000
      : null;
    const initialTime = resumeAt ?? cueStart;
    if (initialTime != null && Number.isFinite(initialTime)) {
      try {
        if (Math.abs(el.currentTime - initialTime) > 0.05) {
          programmaticSeekTargetRef.current = initialTime;
          el.currentTime = initialTime;
        } else {
          programmaticSeekTargetRef.current = null;
        }
        lastKnownTimeRef.current = initialTime;
        setProgressSeconds(initialTime);
      } catch {
        programmaticSeekTargetRef.current = null;
      }
    }
  }, [cueStartMs, onePlayOnly, resumeAtMs, resumeState]);

  const handleCanPlay = useCallback(() => {
    setBuffering(false);
    onIntegrityEvent('audio_buffering_end', { section: subSection.partCode, questionIndex });
    if (resumeState === 'ended') return;
    const el = audioRef.current;
    if (el && !el.paused && !el.ended) {
      autoPlayTriedRef.current = true;
      playAbortPendingRef.current = false;
      return;
    }
    if (autoPlayTriedRef.current && !playAbortPendingRef.current) return;
    playAbortPendingRef.current = false;
    autoPlayTriedRef.current = true;
    tryPlay();
  }, [onIntegrityEvent, questionIndex, resumeState, subSection.partCode, tryPlay]);

  const handleTogglePlayPause = useCallback(() => {
    const el = audioRef.current;
    if (!el) return;
    if (isPlaying) {
      if (!onePlayOnly) el.pause();
      return;
    }
    tryPlay();
  }, [isPlaying, onePlayOnly, tryPlay]);

  if (!subSection.audioUrl) {
    return (
      <div className="rounded-2xl bg-navy p-4 text-white shadow-xl shadow-navy/10 sm:p-5" data-testid="listening-black-player">
        <div className="flex items-center gap-2 text-sm font-semibold text-white/80">
          <Volume2 className="h-4 w-4" aria-hidden="true" />
          No audio is attached to this sub-section.
        </div>
      </div>
    );
  }

  return (
    <div className="space-y-3">
      <ListeningAudioTransport
        isPlaying={isPlaying}
        progressSeconds={progressSeconds}
        durationSeconds={durationSeconds}
        canScrub={false}
        canPause={false}
        isPreviewPhase={false}
        isHalted={audioFailure || Boolean(audioError)}
        audioState={isBuffering ? 'buffering' : audioError ? 'error' : 'ready'}
        saveState={saveState}
        answeredCount={answeredCount}
        totalQuestions={totalQuestions}
        attemptSecondsRemaining={attemptSecondsRemaining}
        onTogglePlayPause={handleTogglePlayPause}
        onScrub={() => {}}
        onSubmit={onSubmit}
        submitDisabled={audioFailure || Boolean(audioError)}
      />

      {needsUserPlay && !audioError ? (
        <div className="flex flex-wrap items-center justify-between gap-3 rounded-2xl border border-warning/30 bg-warning/10 p-3.5 text-navy">
          <div className="flex items-center gap-2 text-sm font-semibold">
            <Volume2 className="h-4 w-4 text-warning shrink-0" aria-hidden="true" />
            <span>Audio is ready. Tap Play to begin playback (plays once).</span>
          </div>
          <Button
            variant="primary"
            onClick={() => tryPlay()}
            className="gap-2 bg-white text-navy hover:bg-white/90"
          >
            <Play className="h-4 w-4" aria-hidden="true" /> Tap to Play — {subSection.title}
          </Button>
        </div>
      ) : null}

      {audioError ? (
        <div className="flex flex-wrap items-center justify-between gap-3 rounded-2xl border border-danger/30 bg-danger/10 p-3.5 text-danger">
          <p className="text-xs sm:text-sm font-semibold">{audioError}</p>
          <Button
            variant="ghost"
            onClick={() => {
              setAudioError(null);
              setNeedsUserPlay(false);
              autoPlayTriedRef.current = false;
              setAudioRetryKey((k) => k + 1);
            }}
            className="h-8 px-3 text-xs text-danger hover:bg-danger/20"
          >
            Retry
          </Button>
        </div>
      ) : null}

      {isBuffering && !audioError && !needsUserPlay ? (
        <p className="text-xs font-semibold text-muted" role="status">
          Audio is buffering; the section timer is paused until playback is ready.
        </p>
      ) : null}

      {resolvedSrc ? (
        <audio
          ref={audioRef}
          src={resolvedSrc}
          autoPlay={false}
          controls={false}
          controlsList="nodownload noplaybackrate nofullscreen noremoteplayback"
          preload="auto"
          className="hidden"
          onLoadedMetadata={handleLoadedMetadata}
          onTimeUpdate={() => {
            const el = audioRef.current;
            if (!el) return;
            if (!el.seeking && Number.isFinite(el.currentTime)) setProgressSeconds(el.currentTime);
            if (el.duration && Number.isFinite(el.duration) && el.duration > 0) setDurationSeconds(el.duration);
            if (!el || el.seeking || !onePlayOnly) return;
            if (cueEndMs != null && el.currentTime * 1000 >= cueEndMs && !hasPlayedToEnd) {
              allowedProgrammaticPauseRef.current = true;
              onIntegrityEvent('audio_stopped', {
                section: subSection.partCode,
                cuePointMs: Math.round(el.currentTime * 1000),
                questionIndex,
                reason: 'programmatic',
              });
              hasStartedRef.current = false;
              setHasPlayedToEnd(true);
              setIsPlaying(false);
              setBuffering(false);
              el.pause();
              onIntegrityEvent('audio_ended', {
                section: subSection.partCode,
                cuePointMs: Math.round(el.currentTime * 1000),
                questionIndex,
              });
              onExtractComplete?.();
              return;
            }
            if (el.currentTime > lastKnownTimeRef.current) lastKnownTimeRef.current = el.currentTime;
            if (el.currentTime > 0 && Date.now() - lastProgressLoggedAtRef.current >= 1500) {
              lastProgressLoggedAtRef.current = Date.now();
              onIntegrityEvent('audio_progress', {
                section: subSection.partCode,
                cuePointMs: Math.round(el.currentTime * 1000),
                questionIndex,
              });
            }
          }}
          onEnded={() => {
            setBuffering(false);
            setIsPlaying(false);
            hasStartedRef.current = false;
            setHasPlayedToEnd(true);
            onIntegrityEvent('audio_ended', {
              section: subSection.partCode,
              cuePointMs: Math.round((audioRef.current?.currentTime ?? 0) * 1000),
              questionIndex,
            });
            if (onExtractComplete) onExtractComplete();
          }}
          onError={() => {
            setBuffering(true);
            onIntegrityEvent('audio_error', {
              section: subSection.partCode,
              questionIndex,
              playbackValidity: 'admin_review_required',
            });
            onAudioFailure();
            setAudioError('Audio failed to load. The attempt has been flagged for administrator review; do not replay the scored audio.');
          }}
          onWaiting={() => {
            setBuffering(true);
            onIntegrityEvent('audio_buffering_start', { section: subSection.partCode, questionIndex });
          }}
          onStalled={() => {
            setBuffering(true);
            onIntegrityEvent('audio_stalled', { section: subSection.partCode, questionIndex });
          }}
          onCanPlay={handleCanPlay}
          onRateChange={() => {
            const el = audioRef.current;
            if (!onePlayOnly || !el || el.playbackRate === 1) return;
            const requestedRate = Number(el.playbackRate);
            onIntegrityEvent('audio_speed_change_blocked', {
              section: subSection.partCode,
              questionIndex,
              requestedRate: Number.isFinite(requestedRate) ? requestedRate : null,
            });
            el.defaultPlaybackRate = 1;
            el.playbackRate = 1;
          }}
          onPlay={() => {
            setBuffering(false);
            setIsPlaying(true);
            if (audioRef.current?.duration && Number.isFinite(audioRef.current.duration) && audioRef.current.duration > 0) {
              setDurationSeconds(audioRef.current.duration);
            }
            playAbortCountRef.current = 0;
            playAbortPendingRef.current = false;
            const el = audioRef.current;
            if (!el || !onePlayOnly) return;
            if (hasPlayedToEnd) {
              allowedProgrammaticPauseRef.current = true;
              el.pause();
              setIsPlaying(false);
              return;
            }
            if (!hasStartedRef.current) {
              onIntegrityEvent('audio_started', {
                section: subSection.partCode,
                cuePointMs: Math.round(el.currentTime * 1000),
                questionIndex,
              });
            }
            hasStartedRef.current = true;
          }}
          onPause={() => {
            setIsPlaying(false);
            const el = audioRef.current;
            const wasStarted = hasStartedRef.current;
            const wasProgrammatic = allowedProgrammaticPauseRef.current;
            if (wasStarted) {
              onIntegrityEvent('audio_stopped', {
                section: subSection.partCode,
                cuePointMs: Math.round((el?.currentTime ?? 0) * 1000),
                questionIndex,
                reason: wasProgrammatic ? 'programmatic' : 'pause',
              });
            }
            if (!el || !shouldResumeAfterBlockedPause({
              canPause: !onePlayOnly,
              phase: 'audio',
              hasStarted: hasStartedRef.current,
              hasReachedEnd: hasPlayedToEnd,
              allowedProgrammaticPause: allowedProgrammaticPauseRef.current,
            })) return;
            allowedProgrammaticPauseRef.current = false;
            el.play().catch((err: unknown) => handleAudioPlaybackError(err, setAudioError));
          }}
          onSeeking={() => {
            const el = audioRef.current;
            if (!el || !onePlayOnly) return;
            if (programmaticSeekTargetRef.current != null
              && Math.abs(el.currentTime - programmaticSeekTargetRef.current) < 0.25) {
              return;
            }
            const blockedTarget = resolveBlockedSeekTarget({
              canScrub: false,
              requestedTime: el.currentTime,
              lastKnownTime: hasPlayedToEnd ? (el.duration || lastKnownTimeRef.current) : lastKnownTimeRef.current,
              allowedProgrammaticTarget: null,
            });
            if (blockedTarget !== null) el.currentTime = blockedTarget;
          }}
          onSeeked={() => {
            programmaticSeekTargetRef.current = null;
            if (playAbortPendingRef.current) {
              playAbortPendingRef.current = false;
              if (playAbortCountRef.current >= 2) {
                setNeedsUserPlay(true);
                setAudioError(null);
                return;
              }
              playAbortCountRef.current += 1;
              tryPlay();
            }
          }}
        />
      ) : (
        <div className="flex items-center gap-2 text-sm text-muted">
          <Loader2 className="h-4 w-4 motion-safe:animate-spin" aria-hidden="true" />
          Loading audio…
        </div>
      )}
    </div>
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
