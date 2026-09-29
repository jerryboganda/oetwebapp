'use client';

import { Suspense, use, useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { useRouter, useSearchParams } from 'next/navigation';
import { Clock } from 'lucide-react';
import { LearnerDashboardShell } from '@/components/layout';
import { Button } from '@/components/ui/button';
import { InlineAlert } from '@/components/ui/alert';
import { Skeleton } from '@/components/ui/skeleton';
import { Modal } from '@/components/ui/modal';
import { useReadingAnnotations } from '@/hooks/use-reading-annotations';
import { getReadingAttempt, getReadingPaperAnnotations, getReadingStructureLearner, lockReadingPartA, resumeReadingBreak, saveReadingAnswer, startReadingAttempt, submitReadingAttempt, createReadingPaperAnnotation, deleteReadingPaperAnnotation, clearReadingPaperAnnotations, type ReadingPaperAnnotationDto, type ReadingLearnerStructureDto, type ReadingPartCode, type ReadingQuestionLearnerDto } from '@/lib/reading-authoring-api';
import { ContentLockedNotice, isContentLockedError, readContentLockedMessage } from '@/components/domain/ContentLockedNotice';
import { completeMockSection } from '@/lib/api';
import { readErrorMessage } from '@/lib/read-error-message';
import { correctedNowMs, readServerClockOffsetMs } from '@/lib/server-clock';
import { enableAutoSync, markAttemptConflict, markAttemptSynced, queueOfflineAttempt, syncPendingAttempts, type OfflineAttempt } from '@/lib/mobile/offline-sync';
import { reconcileOfflineAnswer, type OfflineAnswerPayload } from '@/lib/mobile/offline-answer-reconciliation';
import { showCreditFeedback } from '@/lib/credit-feedback';
import { type SaveState, type PendingReadingAnswer, isNetworkInterruption, isBenignLockedSave, type ReadingSectionCode, type ActiveAttempt, getSectionsForPart, fromStartedAttempt, isQuestionLocked, practiceModeLabel, isSubsetPracticeMode, useReadingBrowserZoomGuard, isAnsweredJson, toggleSetValue, minutesBetween } from './reading-paper-helpers';
import { AttemptToolbar, ReadingBreakScreen, ReadingPartTransitionScreen } from './_components/attempt-chrome';
import { PartTabs, SectionTabs, PartBody } from './_components/part-navigation';

export default function ReadingPaperPlayerPage({ params }: { params: Promise<{ paperId: string }> }) {
  return (
    <Suspense fallback={<LearnerDashboardShell pageTitle="Reading"><Skeleton className="h-64" /></LearnerDashboardShell>}>
      <ReadingPaperPlayerContent params={params} />
    </Suspense>
  );
}

function ReadingPaperPlayerContent({ params }: { params: Promise<{ paperId: string }> }) {
  const { paperId } = use(params);
  const search = useSearchParams();
  const resumeAttemptId = search?.get('attemptId') ?? '';
  // Mocks V2 — BuildLaunchRoute attaches mockAttemptId/mockSectionId when
  // this paper is launched as a section of a mock attempt. Submission then
  // writes the score back via completeMockSection so the mock report is
  // not stuck on "Pending".
  const mockAttemptId = search?.get('mockAttemptId') ?? null;
  const mockSectionId = search?.get('mockSectionId') ?? null;
  const router = useRouter();

  const [structure, setStructure] = useState<ReadingLearnerStructureDto | null>(null);
  const [pdfAnnotations, setPdfAnnotations] = useState<ReadingPaperAnnotationDto[]>([]);
  const [attempt, setAttempt] = useState<ActiveAttempt | null>(null);
  const [answers, setAnswers] = useState<Record<string, string>>({});
  const [flagged, setFlagged] = useState<Set<string>>(() => new Set());
  const [loading, setLoading] = useState(true);
  const [starting, setStarting] = useState(false);
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [contentLockedMessage, setContentLockedMessage] = useState<string | null>(null);
  const [activePart, setActivePart] = useState<ReadingPartCode>('A');
  const [activeSection, setActiveSection] = useState<ReadingSectionCode | null>(null);
  const [activeQuestionId, setActiveQuestionId] = useState<string | null>(null);
  const [nowMs, setNowMs] = useState(() => Date.now());
  const [showConfirm, setShowConfirm] = useState(false);
  const [showSubmitPartAConfirm, setShowSubmitPartAConfirm] = useState(false);
  const [lockingPartA, setLockingPartA] = useState(false);
  const [saveState, setSaveState] = useState<SaveState>('idle');
  const [timingNotice, setTimingNotice] = useState<string | null>(null);
  const [zoomLevel, setZoomLevel] = useState(100);
  /**
   * Phase 5 closure — learner-controlled accessibility settings,
   * persisted to localStorage under `oet-reading-a11y:{paperId}`. Each
   * field is only surfaced when the resolved policy enables it
   * (see structure.paper.policy). Defaults are conservative so a
   * fresh learner sees the standard render.
   */
  const [fontScale, setFontScale] = useState<90 | 100 | 110 | 125>(100);
  const [highContrast, setHighContrast] = useState(false);
  const [screenReaderHints, setScreenReaderHints] = useState(false);
  const [displayWarnings, setDisplayWarnings] = useState<string[]>([]);
  // R08 — rule-out (strikethrough) marks are persisted on the attempt via
  // useReadingAnnotations so they survive refresh, resume, and navigation
  // (previously ephemeral local state). The hook stores per-question
  // `struckOptions[]`; `initialAnnotationsJson` is hydrated from the resume
  // load below. Editing is gated to in-progress attempts (a reopened submitted
  // attempt shows marks read-only).
  const [initialAnnotationsJson, setInitialAnnotationsJson] = useState<string | null>(null);
  const annotations = useReadingAnnotations({
    attemptId: attempt?.attemptId ?? null,
    initialAnnotationsJson,
    disabled: attempt?.status !== 'InProgress',
  });
  // Flatten the hook's per-question struckOptions into the
  // "{questionId}:{letter}" Set the existing option renderer already consumes,
  // so the PartBody → McqControl prop chain stays unchanged.
  const eliminatedChoices = useMemo(() => {
    const set = new Set<string>();
    for (const [questionId, annotation] of Object.entries(annotations.state.byQuestion)) {
      for (const letter of annotation.struckOptions ?? []) set.add(`${questionId}:${letter}`);
    }
    return set;
  }, [annotations.state]);
  const toggleEliminated = useCallback((questionId: string, optionValue: string) => {
    annotations.update(questionId, (current) => {
      const next = new Set(current.struckOptions ?? []);
      if (next.has(optionValue)) next.delete(optionValue);
      else next.add(optionValue);
      return { ...current, struckOptions: Array.from(next) };
    });
  }, [annotations.update]);
  /**
   * One-shot acknowledgement for the Part A → Parts B/C transition screen.
   * Once the learner presses Continue (or resumes a break, which already
   * served as the transition) we never show the screen again this session.
   */
  const [partTransitionAcknowledged, setPartTransitionAcknowledged] = useState(false);

  const saveTimers = useRef<Record<string, ReturnType<typeof setTimeout>>>({});
  // Latest debounced answer values awaiting the server. This is an in-flight
  // queue only; the attempt row remains the durable source of truth.
  const pendingAnswersRef = useRef<Record<string, PendingReadingAnswer>>({});
  // Handles for saves already on the wire. `pendingAnswersRef` only records a
  // boolean, which cannot be awaited — this lets an early Part A submission
  // wait for in-flight autosaves to settle before it locks the section.
  const inFlightSaves = useRef<Set<Promise<unknown>>>(new Set());
  // Synchronous re-entrancy guard for "Submit Part A". Two clicks in the
  // same tick both observe the pre-render `lockingPartA` state, so only a
  // ref actually stops the second request; the state drives the button's
  // loading/disabled rendering.
  const lockPartAInFlight = useRef(false);
  const serverAnswers = useRef<Record<string, string | null>>({});
  const autoSubmitTriggered = useRef(false);
  const warnedMiniTest2min = useRef(false);
  const warnedMiniTest1min = useRef(false);
  const dirtyQuestionIds = useRef<Set<string>>(new Set());
  const serverClockOffsetMs = useRef(0);
  const timingState = useRef({ partALocked: false, partBCWindowEnded: false, paperExpired: false, breakPending: false });
  /**
   * Phase 1 closure — wall-clock timestamp (ms) at which the currently
   * focused question was last "shown" or "saved". On the next autosave
   * we compute `Date.now() - questionFocusStartedAt[id]` and ship that
   * as `elapsedMs` to the server, which accumulates the total. After
   * sending, the entry is reset to `Date.now()` so subsequent saves only
   * count the delta since the last save (no double-counting).
   *
   * Entries are seeded when `activeQuestionId` flips to a new question
   * and cleared on attempt change. Tab-hidden detection (visibilitychange)
   * also resets the timer so backgrounded tabs do not inflate timings.
   */
  const questionFocusStartedAt = useRef<Record<string, number>>({});

  const syncServerClock = useCallback((serverNow: string | null | undefined) => {
    serverClockOffsetMs.current = readServerClockOffsetMs(serverNow);
    setNowMs(correctedNowMs(serverClockOffsetMs.current));
  }, []);

  const flushPendingReadingAnswers = useCallback((keepalive = false) => {
    const now = Date.now();
    Object.entries(pendingAnswersRef.current).forEach(([questionId, pending]) => {
      clearTimeout(saveTimers.current[questionId]);
      const focusedAt = questionFocusStartedAt.current[questionId];
      const elapsedMs = pending.inFlight || focusedAt == null || now <= focusedAt
        ? null
        : Math.min(now - focusedAt, 14_400_000);
      pending.inFlight = true;
      setSaveState('saving');
      const request = saveReadingAnswer(pending.attemptId, questionId, pending.valueJson, elapsedMs, { keepalive })
        .then(() => {
          const current = pendingAnswersRef.current[questionId];
          if (
            current?.attemptId === pending.attemptId
            && current.valueJson === pending.valueJson
          ) {
            delete pendingAnswersRef.current[questionId];
            dirtyQuestionIds.current.delete(questionId);
            questionFocusStartedAt.current[questionId] = Date.now();
            setSaveState('saved');
          } else if (current) {
            current.inFlight = false;
            setSaveState('saving');
          }
        })
        .catch((err) => {
          const current = pendingAnswersRef.current[questionId];
          if (isBenignLockedSave(err)) {
            // The section closed under us — the answer is either already
            // persisted or no longer accepted. Settle quietly.
            delete pendingAnswersRef.current[questionId];
            dirtyQuestionIds.current.delete(questionId);
            setSaveState('saved');
            return;
          }
          if (current?.attemptId === pending.attemptId && current.valueJson === pending.valueJson) {
            current.inFlight = false;
            if (isNetworkInterruption(err)) {
              void queueOfflineAttempt(
                'reading-answer',
                pending.attemptId,
                {
                  questionId,
                  value: pending.valueJson,
                  baseValue: current.baseValueJson,
                } satisfies OfflineAnswerPayload,
                { id: `reading-answer:${encodeURIComponent(pending.attemptId)}:${encodeURIComponent(questionId)}` },
              ).then(() => setSaveState('offline-saved')).catch(() => setSaveState('error'));
            } else {
              setSaveState('error');
            }
          }
        });
      inFlightSaves.current.add(request);
      void request.finally(() => inFlightSaves.current.delete(request));
    });
  }, []);

  // Flush before backgrounding, navigation, or unmount so the debounce window
  // cannot erase the last typed answer. The keepalive request stays owned by
  // the authenticated API client and never stores answers in browser storage.
  useEffect(() => {
    const flush = () => flushPendingReadingAnswers(true);
    const onVisibilityChange = () => {
      if (document.visibilityState === 'hidden') flush();
    };
    document.addEventListener('visibilitychange', onVisibilityChange);
    window.addEventListener('pagehide', flush);
    return () => {
      document.removeEventListener('visibilitychange', onVisibilityChange);
      window.removeEventListener('pagehide', flush);
      flush();
    };
  }, [flushPendingReadingAnswers]);

  useReadingBrowserZoomGuard();

  const readingA11yHintId = screenReaderHints ? `reading-a11y-hints-${paperId}` : undefined;

  /**
   * Phase 5 closure — load any persisted a11y settings for this paper.
   * Keyed per-paper so a learner who uses one paper with high-contrast
   * does not inherit it on another.
   */
  useEffect(() => {
    if (typeof window === 'undefined') return;
    try {
      const raw = window.localStorage.getItem(`oet-reading-a11y:${paperId}`);
      if (!raw) return;
      const parsed = JSON.parse(raw) as {
        fontScale?: number;
        highContrast?: boolean;
        screenReaderHints?: boolean;
      };
      if (parsed.fontScale === 90 || parsed.fontScale === 100
        || parsed.fontScale === 110 || parsed.fontScale === 125) {
        setFontScale(parsed.fontScale);
      }
      if (typeof parsed.highContrast === 'boolean') setHighContrast(parsed.highContrast);
      if (typeof parsed.screenReaderHints === 'boolean') setScreenReaderHints(parsed.screenReaderHints);
    } catch { /* ignore corrupt entry */ }
  }, [paperId]);

  useEffect(() => {
    if (typeof window === 'undefined') return;
    try {
      window.localStorage.setItem(
        `oet-reading-a11y:${paperId}`,
        JSON.stringify({ fontScale, highContrast, screenReaderHints }),
      );
    } catch { /* localStorage may be full or disabled */ }
  }, [paperId, fontScale, highContrast, screenReaderHints]);

  useEffect(() => {
    const readWarnings = () => {
      if (typeof window === 'undefined') return;
      const next: string[] = [];
      if (window.screen.width < 1024 || window.screen.height < 600) {
        next.push('Display below 1024 x 600');
      }
      if (window.devicePixelRatio > 3) {
        next.push(`Display scale or browser zoom ${Math.round(window.devicePixelRatio * 100)}%`);
      }
      setDisplayWarnings(next);
    };
    readWarnings();
    window.addEventListener('resize', readWarnings);
    return () => window.removeEventListener('resize', readWarnings);
  }, []);

  useEffect(() => {
    const interval = setInterval(() => setNowMs(correctedNowMs(serverClockOffsetMs.current)), 1000);
    return () => clearInterval(interval);
  }, []);

  useEffect(() => () => {
    Object.values(saveTimers.current).forEach(clearTimeout);
  }, []);

  const load = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      const loadedStructure = await getReadingStructureLearner(paperId);
      setStructure(loadedStructure);
      // Secondary, non-blocking load. PDF annotations are a learner aid, not a
      // prerequisite for rendering the paper or resuming an attempt, so a slow
      // or failed annotations fetch must never block the structure/attempt load
      // (mirrors the sibling results page fix).
      void getReadingPaperAnnotations(paperId)
        .then((annotations) => setPdfAnnotations(annotations))
        .catch(() => setPdfAnnotations([]));

      if (resumeAttemptId) {
        const saved = await getReadingAttempt(resumeAttemptId);
        syncServerClock(saved.serverNow);
        const restoredAnswers = Object.fromEntries(
          saved.answers.map((answer) => [answer.readingQuestionId, answer.userAnswerJson]),
        );
        setAttempt({
          attemptId: saved.id,
          startedAt: saved.startedAt,
          deadlineAt: saved.deadlineAt ?? saved.partBCDeadlineAt,
          partADeadlineAt: saved.partADeadlineAt,
          partBCDeadlineAt: saved.partBCDeadlineAt,
          paperTitle: loadedStructure.paper.title,
          partATimerMinutes: minutesBetween(saved.startedAt, saved.partADeadlineAt),
          partBCTimerMinutes: Math.max(0, minutesBetween(saved.partADeadlineAt, saved.partBCDeadlineAt)),
          answeredCount: saved.answeredCount,
          canResume: saved.canResume,
          partABreakAvailable: saved.partABreakAvailable,
          partABreakResumed: saved.partABreakResumed,
          partBCTimerPausedAt: saved.partBCTimerPausedAt,
          partBCPausedSeconds: saved.partBCPausedSeconds,
          partABreakMaxSeconds: saved.partABreakMaxSeconds,
          status: saved.status,
          mode: saved.mode,
          scopeQuestionIds: saved.scopeQuestionIds,
          isUntimed: saved.isUntimed,
          // Pre-existing omission: `serverNow` is required on ActiveAttempt
          // and every other setAttempt call supplies it, so a resumed attempt
          // was the one path that left it undefined.
          serverNow: saved.serverNow,
        });
        setAnswers(restoredAnswers);
        serverAnswers.current = Object.fromEntries(
          saved.answers.map((answer) => [answer.readingQuestionId, answer.userAnswerJson]),
        );
        // R08 — hydrate persisted rule-out / highlight marks for this attempt.
        setInitialAnnotationsJson(saved.annotationsJson);
        dirtyQuestionIds.current.clear();
        pendingAnswersRef.current = {};
      }
    } catch (err) {
      setError(readErrorMessage(err, 'Failed to load Reading paper.'));
    } finally {
      setLoading(false);
    }
  }, [paperId, resumeAttemptId, syncServerClock]);

  useEffect(() => {
    void load();
  }, [load]);

  // Phase 3: when a subset attempt is in progress, restrict the rendered
  // questions to the in-scope set so the player only shows what the
  // grader will actually mark.
  const displayedStructure = useMemo<ReadingLearnerStructureDto | null>(() => {
    if (!structure) return null;
    if (!attempt?.scopeQuestionIds || attempt.scopeQuestionIds.length === 0) return structure;
    const scope = new Set(attempt.scopeQuestionIds);
    return {
      ...structure,
      parts: structure.parts
        .map((part) => ({
          ...part,
          questions: part.questions.filter((q) => scope.has(q.id)),
          sections: part.sections?.map((section) => ({
            ...section,
            questions: section.questions.filter((question) => scope.has(question.id)),
          })),
        }))
        .filter((part) => part.questions.length > 0),
    };
  }, [attempt?.scopeQuestionIds, structure]);

  const displayedCurrentPart = useMemo(
    () => displayedStructure?.parts.find((part) => part.partCode === activePart) ?? null,
    [activePart, displayedStructure],
  );
  const firstDisplayedPart = displayedStructure?.parts.find((part) => part.questions.length > 0) ?? null;
  const displayedSections = useMemo(() => (displayedCurrentPart ? getSectionsForPart(displayedCurrentPart) : []), [displayedCurrentPart]);
  const displayedQuestions = useMemo(() => {
    if (!displayedCurrentPart) return [] as ReadingQuestionLearnerDto[];
    // Part B uses one collective booklet on the left and every B question on the right.
    if (displayedCurrentPart.partCode !== 'C') return displayedCurrentPart.questions;
    const currentSection = displayedSections.find((section) => section.code === activeSection) ?? displayedSections[0] ?? null;
    return currentSection?.questions.length ? currentSection.questions : displayedCurrentPart.questions;
  }, [activeSection, displayedCurrentPart, displayedSections]);
  const displayedPartForRender = useMemo(() => {
    if (!displayedCurrentPart) return null;
    return {
      ...displayedCurrentPart,
      questions: displayedQuestions,
    };
  }, [displayedCurrentPart, displayedQuestions]);

  const questionPartById = useMemo(() => {
    const map = new Map<string, ReadingPartCode>();
    structure?.parts.forEach((part) => {
      part.questions.forEach((question) => map.set(question.id, part.partCode));
    });
    return map;
  }, [structure]);

  useEffect(() => {
    const part = displayedCurrentPart?.questions.length ? displayedCurrentPart : firstDisplayedPart;
    if (!part?.questions.length) return;
    if (activePart !== part.partCode) {
      setActivePart(part.partCode);
    }
  }, [activePart, displayedCurrentPart, firstDisplayedPart]);

  useEffect(() => {
    if (!displayedCurrentPart) return;
    if (displayedCurrentPart.partCode === 'A') {
      setActiveSection(null);
      return;
    }
    const nextSection = displayedSections[0]?.code ?? null;
    setActiveSection((current) => (displayedSections.some((section) => section.code === current) ? current : nextSection));
  }, [displayedCurrentPart, displayedSections]);

  useEffect(() => {
    if (!displayedQuestions.length) return;
    if (!activeQuestionId || !displayedQuestions.some((question) => question.id === activeQuestionId)) {
      setActiveQuestionId(displayedQuestions[0].id);
    }
  }, [activeQuestionId, displayedQuestions]);

  /**
   * Phase 1 closure — seed/refresh the per-question focus timestamp
   * whenever the learner switches questions, so the next autosave can
   * report the elapsed milliseconds. Reset to "now" when the tab is
   * hidden (visibilitychange → "hidden") so backgrounded tabs don't
   * inflate timings; when it becomes visible again we restart the clock.
   */
  useEffect(() => {
    if (!activeQuestionId) return;
    questionFocusStartedAt.current[activeQuestionId] = Date.now();
  }, [activeQuestionId]);

  useEffect(() => {
    if (typeof document === 'undefined') return;
    const onVisibility = () => {
      if (!activeQuestionId) return;
      // Discard any accumulated time when the tab is hidden, and restart
      // when visible. We never persist time spent in a hidden tab.
      questionFocusStartedAt.current[activeQuestionId] = Date.now();
    };
    document.addEventListener('visibilitychange', onVisibility);
    return () => document.removeEventListener('visibilitychange', onVisibility);
  }, [activeQuestionId]);

  const isPracticeMode = attempt !== null && attempt.mode !== 'Exam';
  const totalQuestions = useMemo(() => {
    if (!structure) return 0;
    if (attempt?.scopeQuestionIds && attempt.scopeQuestionIds.length > 0) {
      const scope = new Set(attempt.scopeQuestionIds);
      return structure.parts.reduce(
        (sum, part) => sum + part.questions.filter((q) => scope.has(q.id)).length,
        0,
      );
    }
    return structure.parts.reduce((sum, part) => sum + part.questions.length, 0);
  }, [attempt?.scopeQuestionIds, structure]);
  const answeredCount = Object.values(answers).filter(isAnsweredJson).length;
  const unansweredCount = Math.max(0, totalQuestions - answeredCount);
  const partADeadlineMs = attempt ? Date.parse(attempt.partADeadlineAt) : Number.NaN;
  const serverPartBCDeadlineMs = attempt ? Date.parse(attempt.partBCDeadlineAt) : Number.NaN;
  const overallDeadlineMs = attempt ? Date.parse(attempt.deadlineAt) : Number.NaN;
  const breakWindowEndsAtMs = attempt
    ? Math.min(
        Number.isFinite(overallDeadlineMs) ? overallDeadlineMs : Number.POSITIVE_INFINITY,
        partADeadlineMs + Math.max(0, attempt.partABreakMaxSeconds) * 1000,
      )
    : Number.NaN;
  const fullUnresumedBreakPartBCDeadlineMs = attempt
    ? partADeadlineMs
      + Math.max(0, attempt.partBCTimerMinutes) * 60_000
      + Math.max(0, attempt.partABreakMaxSeconds) * 1000
    : Number.NaN;
  const partBCDeadlineMs = attempt
    && attempt.mode === 'Exam'
    && attempt.partABreakAvailable
    && !attempt.partABreakResumed
    && Number.isFinite(serverPartBCDeadlineMs)
    && Number.isFinite(breakWindowEndsAtMs)
    && Number.isFinite(fullUnresumedBreakPartBCDeadlineMs)
    && nowMs >= breakWindowEndsAtMs
      ? Math.min(
          Number.isFinite(overallDeadlineMs) ? overallDeadlineMs : Number.POSITIVE_INFINITY,
          Math.max(serverPartBCDeadlineMs, fullUnresumedBreakPartBCDeadlineMs),
        )
      : serverPartBCDeadlineMs;
  // Practice modes ignore the Part-A hard lock, but their own timer still
  // controls autosave, input locking, and auto-submit.
  const partALocked = !isPracticeMode
    && Boolean(attempt && nowMs >= partADeadlineMs);
  const breakPending = Boolean(
    attempt?.mode === 'Exam'
    && attempt.partABreakAvailable
    && partALocked
    && !attempt.partABreakResumed
    && nowMs < breakWindowEndsAtMs,
  );
  const partBCWindowEnded = Boolean(attempt && !breakPending && nowMs >= partBCDeadlineMs);
  const paperExpired = Boolean(attempt && nowMs >= overallDeadlineMs);
  const attemptInputsLocked = breakPending || partBCWindowEnded || paperExpired;
  /**
   * Owner request 2026-08-29 — a candidate who finishes Part A early may end
   * it themselves instead of waiting out the rest of the 15-minute window.
   * Exam mode only, and only while Part A is genuinely open: a reopened
   * submitted attempt still computes `activePart`, so the InProgress check is
   * what stops the action appearing on a read-only review.
   */
  const canSubmitPartAEarly = Boolean(
    attempt
    && attempt.mode === 'Exam'
    && attempt.status === 'InProgress'
    && activePart === 'A'
    && !partALocked
    && !breakPending
    && !partBCWindowEnded
    && !paperExpired,
  );

  /**
   * Part A → Parts B/C transition gate. Shown once, only in real exam mode,
   * after Part A locks and any optional break has ended. It is purely a
   * dismissible information screen — the B/C deadline is already running
   * server-side, so Continue only reveals the section and does NOT start or
   * restart any timer.
   */
  const showPartTransition = Boolean(
    attempt
    && !isPracticeMode
    && partALocked
    && !breakPending
    && !partBCWindowEnded
    && !paperExpired
    && !partTransitionAcknowledged,
  );
  const partBCMinutesRemaining = Number.isFinite(partBCDeadlineMs)
    ? Math.max(0, Math.round((partBCDeadlineMs - nowMs) / 60_000))
    : 0;

  useEffect(() => {
    timingState.current = { partALocked, partBCWindowEnded, paperExpired, breakPending };
  }, [breakPending, paperExpired, partALocked, partBCWindowEnded]);

  useEffect(() => {
    if (!attempt || !partALocked) {
      setTimingNotice(null);
      return;
    }

    if (activePart === 'A' && !breakPending) setActivePart('B');
    setTimingNotice(breakPending
      ? 'Part A is locked. Resume the test to begin the B/C shared window.'
      : 'Part A is locked. Parts B and C are now active.');
  }, [activePart, attempt, breakPending, partALocked]);

  // MiniTest time warnings at 2 min and 1 min remaining
  useEffect(() => {
    if (!attempt || attempt.mode !== 'MiniTest') return;
    const remainingSec = Math.max(0, Math.floor((partBCDeadlineMs - nowMs) / 1000));
    if (remainingSec <= 120 && remainingSec > 60 && !warnedMiniTest2min.current) {
      warnedMiniTest2min.current = true;
      setTimingNotice('2 minutes remaining in your mini-test.');
    } else if (remainingSec <= 60 && remainingSec > 0 && !warnedMiniTest1min.current) {
      warnedMiniTest1min.current = true;
      setTimingNotice('1 minute remaining in your mini-test.');
    }
  }, [attempt, nowMs, partBCDeadlineMs]);

  const start = async () => {
    setStarting(true);
    setError(null);
    setContentLockedMessage(null);
    try {
      const started = await startReadingAttempt(paperId, { mockAttemptId, mockSectionId });
      showCreditFeedback(started.feedbackMessage);
      syncServerClock(started.serverNow);
      setAttempt(fromStartedAttempt(started));
      if (mockAttemptId && mockSectionId && !resumeAttemptId) {
        const nextParams = new URLSearchParams(search?.toString());
        nextParams.set('attemptId', started.attemptId);
        router.replace(`/reading/paper/${encodeURIComponent(paperId)}?${nextParams.toString()}`);
      }
      setAnswers({});
      serverAnswers.current = {};
      setFlagged(new Set());
      setInitialAnnotationsJson(null);
      autoSubmitTriggered.current = false;
      warnedMiniTest2min.current = false;
      warnedMiniTest1min.current = false;
      dirtyQuestionIds.current.clear();
      pendingAnswersRef.current = {};
      setTimingNotice(null);
      setActivePart('A');
    } catch (err) {
      if (isContentLockedError(err)) {
        setContentLockedMessage(readContentLockedMessage(err));
      } else {
        setError(readErrorMessage(err, 'Could not start Reading attempt.'));
      }
    } finally {
      setStarting(false);
    }
  };

  const persistAnswer = useCallback(async (questionId: string, valueJson: string) => {
    if (!attempt) return;
    const questionPart = questionPartById.get(questionId);
    const currentTiming = timingState.current;
    if ((questionPart === 'A' && currentTiming.partALocked)
      || ((questionPart === 'B' || questionPart === 'C') && currentTiming.breakPending)
      || currentTiming.partBCWindowEnded
      || currentTiming.paperExpired) {
      dirtyQuestionIds.current.delete(questionId);
      setSaveState('saved');
      return;
    }

    setSaveState('saving');
    // Phase 1 closure — compute elapsed ms since last focus/save for this
    // question. Capped client-side at 4 h to match the server cap.
    const focusedAt = questionFocusStartedAt.current[questionId];
    const nowTs = Date.now();
    const elapsedMs = focusedAt != null && nowTs > focusedAt
      ? Math.min(nowTs - focusedAt, 14_400_000)
      : null;
    const pending = pendingAnswersRef.current[questionId];
    if (pending && pending.attemptId === attempt.attemptId && pending.valueJson === valueJson) {
      pending.inFlight = true;
    }
    const request = saveReadingAnswer(attempt.attemptId, questionId, valueJson, elapsedMs);
    inFlightSaves.current.add(request);
    void request.catch(() => {}).finally(() => inFlightSaves.current.delete(request));
    try {
      await request;
      // Reset the focus timestamp so the next save only counts the delta
      // since this save (no double-counting). If the learner switches tabs,
      // the visibilitychange handler also resets this entry.
      questionFocusStartedAt.current[questionId] = Date.now();
      const currentPending = pendingAnswersRef.current[questionId];
      const isCurrentValue = !currentPending
        || (currentPending.attemptId === attempt.attemptId && currentPending.valueJson === valueJson);
      if (isCurrentValue) {
        serverAnswers.current[questionId] = valueJson;
        dirtyQuestionIds.current.delete(questionId);
        delete pendingAnswersRef.current[questionId];
        setSaveState('saved');
      } else {
        setSaveState('saving');
      }
    } catch (err) {
      if (isBenignLockedSave(err)) {
        // The section closed under us (most often right after the candidate
        // pressed Submit Part A). Settle quietly rather than surfacing a
        // spurious "Autosave failed." banner.
        delete pendingAnswersRef.current[questionId];
        dirtyQuestionIds.current.delete(questionId);
        setSaveState('saved');
        return;
      }
      if (
        pendingAnswersRef.current[questionId]?.attemptId === attempt.attemptId
        && pendingAnswersRef.current[questionId]?.valueJson === valueJson
      ) {
        pendingAnswersRef.current[questionId].inFlight = false;
      }
      if (isNetworkInterruption(err)) {
        try {
          await queueOfflineAttempt(
            'reading-answer',
            attempt.attemptId,
            {
              questionId,
              value: valueJson,
              baseValue: pending?.baseValueJson ?? serverAnswers.current[questionId] ?? null,
            } satisfies OfflineAnswerPayload,
            { id: `reading-answer:${encodeURIComponent(attempt.attemptId)}:${encodeURIComponent(questionId)}` },
          );
          setSaveState('offline-saved');
          return;
        } catch {
          // Encryption is mandatory for offline answer recovery. Keep the
          // authenticated server path as the only fallback when unavailable.
        }
      }
      setSaveState('error');
      setError(readErrorMessage(err, 'Autosave failed.'));
    }
  }, [attempt, questionPartById]);

  const setAnswer = (question: ReadingQuestionLearnerDto, value: unknown) => {
    if (!attempt || isQuestionLocked(activePart, partALocked, attemptInputsLocked, breakPending)) return;

    const json = JSON.stringify(value);
    setAnswers((prev) => ({ ...prev, [question.id]: json }));
    dirtyQuestionIds.current.add(question.id);
    pendingAnswersRef.current[question.id] = {
      attemptId: attempt.attemptId,
      valueJson: json,
      baseValueJson: pendingAnswersRef.current[question.id]?.baseValueJson
        ?? serverAnswers.current[question.id]
        ?? null,
      inFlight: false,
    };
    setSaveState('saving');
    if (saveTimers.current[question.id]) clearTimeout(saveTimers.current[question.id]);
    const activeDeadlineMs = new Date(activePart === 'A' ? attempt.partADeadlineAt : attempt.partBCDeadlineAt).getTime();
    if (activeDeadlineMs - correctedNowMs(serverClockOffsetMs.current) <= 5000) {
      void persistAnswer(question.id, json);
      return;
    }

    saveTimers.current[question.id] = setTimeout(() => {
      void persistAnswer(question.id, json);
    }, 400);
  };

  const reconcilePendingOfflineAnswer = useCallback(async (queued: OfflineAttempt): Promise<boolean> => {
    if (!attempt || queued.subtest !== 'reading-answer' || queued.contentId !== attempt.attemptId) return false;
    const payload = queued.payload as Partial<OfflineAnswerPayload>;
    if (typeof payload.questionId !== 'string' || typeof payload.value !== 'string') return false;

    const latest = await getReadingAttempt(attempt.attemptId);
    const serverValue = latest.answers.find((answer) => answer.readingQuestionId === payload.questionId)?.userAnswerJson ?? null;
    const decision = reconcileOfflineAnswer(serverValue, {
      questionId: payload.questionId,
      value: payload.value,
      baseValue: typeof payload.baseValue === 'string' ? payload.baseValue : null,
    });

    if (decision === 'already-synced') {
      await markAttemptSynced(queued.id);
      delete pendingAnswersRef.current[payload.questionId];
      dirtyQuestionIds.current.delete(payload.questionId);
      serverAnswers.current[payload.questionId] = payload.value;
      setSaveState('saved');
      return true;
    }
    if (decision === 'conflict') {
      await markAttemptConflict(queued.id);
      delete pendingAnswersRef.current[payload.questionId];
      dirtyQuestionIds.current.delete(payload.questionId);
      setSaveState('conflict');
      setError('A newer Reading answer is already saved on the server. Your offline answer was not applied.');
      return true;
    }

    try {
      // Do not replay client-measured elapsed time from an offline interval;
      // the server remains authoritative for deadline and saved-answer state.
      await saveReadingAnswer(attempt.attemptId, payload.questionId, payload.value);
      await markAttemptSynced(queued.id);
      delete pendingAnswersRef.current[payload.questionId];
      dirtyQuestionIds.current.delete(payload.questionId);
      serverAnswers.current[payload.questionId] = payload.value;
      setSaveState('saved');
      return true;
    } catch (err) {
      if (isNetworkInterruption(err)) return false;
      throw err;
    }
  }, [attempt]);

  useEffect(() => {
    if (!attempt) return;
    void syncPendingAttempts(reconcilePendingOfflineAnswer).catch(() => undefined);
    return enableAutoSync(reconcilePendingOfflineAnswer);
  }, [attempt, reconcilePendingOfflineAnswer]);

  const submit = useCallback(async () => {
    if (!attempt) return;
    if (submitting) return;
    if (breakPending) {
      setError('Resume the test before submitting the Reading attempt.');
      return;
    }
    setSubmitting(true);
    setError(null);
    try {
      Object.values(saveTimers.current).forEach(clearTimeout);
      const lockedQuestionIds: string[] = [];
      const answersToFlush = Object.entries(answers).filter(([questionId]) => {
        if (!dirtyQuestionIds.current.has(questionId)) return false;
        const questionPart = questionPartById.get(questionId);
        if ((questionPart === 'A' && partALocked)
          || ((questionPart === 'B' || questionPart === 'C') && breakPending)
          || partBCWindowEnded
          || paperExpired) {
          lockedQuestionIds.push(questionId);
          return false;
        }
        return true;
      });

      lockedQuestionIds.forEach((questionId) => dirtyQuestionIds.current.delete(questionId));
      // Phase 1 closure — final flush also reports per-question elapsed ms
      // so the last batch of unsaved answers contributes to TotalElapsedMs.
      const submitFlushNow = Date.now();
      await Promise.all(answersToFlush.map(([questionId, valueJson]) => {
        const focusedAt = questionFocusStartedAt.current[questionId];
        const elapsedMs = focusedAt != null && submitFlushNow > focusedAt
          ? Math.min(submitFlushNow - focusedAt, 14_400_000)
          : null;
        return saveReadingAnswer(attempt.attemptId, questionId, valueJson, elapsedMs);
      }));
      answersToFlush.forEach(([questionId]) => {
        questionFocusStartedAt.current[questionId] = Date.now();
        dirtyQuestionIds.current.delete(questionId);
        delete pendingAnswersRef.current[questionId];
      });
      // R08 — land any debounced rule-out / highlight edits before grading.
      await annotations.flush();
      const graded = await submitReadingAttempt(attempt.attemptId);
      if (mockAttemptId && mockSectionId) {
        let mockCompletionRecorded = true;
        try {
          await completeMockSection(mockAttemptId, mockSectionId, {
            contentAttemptId: attempt.attemptId,
            rawScore: graded.rawScore,
            rawScoreMax: graded.maxRawScore,
            scaledScore: graded.scaledScore,
            grade: graded.gradeLetter,
            evidence: { source: 'reading_player' },
          });
        } catch (mockErr) {
          mockCompletionRecorded = false;
          // Phase 6 closure — do not lose the learner's submission on
          // mock-write failure. Persist a pending-completion marker so the
          // results route surfaces a retry CTA, and surface a non-blocking
          // warning on the player too.
          // P0-K 2026-05 hardening: route diagnostic to Sentry in production
          // instead of console.warn so it does not leak in browser devtools
          // during a customer support session.
          if (typeof process !== 'undefined' && process.env.NODE_ENV !== 'production') {
            // eslint-disable-next-line no-console
            console.warn('Could not mark mock reading section complete', mockErr);
          }
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
                  grade: graded.gradeLetter,
                }),
              );
            } catch { /* sessionStorage may be full or blocked */ }
          }
          setError(
            'Reading attempt submitted, but the mock dashboard did not receive '
            + 'the score. Open results to retry the mock-completion step.',
          );
        }
        // Mock context: return to the mock dashboard as the banner promises.
        // The full section review stays held back until the whole mock is
        // submitted and the report releases — landing on the reading results
        // page here leaked the item review mid-mock. On a failed completion
        // write we still go to results, where the retry CTA lives.
        router.push(
          mockCompletionRecorded
            ? `/mocks/player/${mockAttemptId}`
            : `/reading/paper/${paperId}/results?attemptId=${attempt.attemptId}`,
        );
        return;
      }
      router.push(`/reading/paper/${paperId}/results?attemptId=${attempt.attemptId}`);
    } catch (err) {
      setError(readErrorMessage(err, 'Could not submit Reading attempt.'));
    } finally {
      setSubmitting(false);
    }
  }, [answers, attempt, breakPending, mockAttemptId, mockSectionId, paperId, paperExpired, partALocked, partBCWindowEnded, questionPartById, router, submitting]);

  /**
   * Ends Part A at the candidate's request. Everything downstream — the locked
   * Part A tab, the break screen, the 10-minute countdown — is derived from
   * `partADeadlineAt`, so the only jobs here are to land the candidate's
   * Part A work first and then patch the server's new deadlines into state.
   *
   * Deliberately does NOT touch `partATimerMinutes`, `partBCTimerMinutes`,
   * `activePart` or `partTransitionAcknowledged`: `resumeBreak` leaves them
   * alone too, and rewriting `partBCTimerMinutes` would shift the
   * `fullUnresumedBreakPartBCDeadlineMs` fallback above.
   */
  const lockPartA = useCallback(async () => {
    if (!attempt || lockPartAInFlight.current) return;
    // The 1s tick may have locked Part A between opening the dialog and
    // confirming it; the server would no-op, but skip the round trip.
    if (partALocked || breakPending || partBCWindowEnded || paperExpired) return;

    lockPartAInFlight.current = true;
    setLockingPartA(true);
    setError(null);
    try {
      // Land every Part A answer before the section closes: cancel the
      // debounce, flush what is dirty, then wait for anything already on the
      // wire. Saves that still lose the race are absorbed by the
      // `part_a_locked` benign-rejection guard.
      Object.values(saveTimers.current).forEach(clearTimeout);
      const partAAnswersToFlush = Object.entries(answers).filter(([questionId]) =>
        dirtyQuestionIds.current.has(questionId) && questionPartById.get(questionId) === 'A');
      const flushNow = Date.now();
      const flushResults = await Promise.allSettled(partAAnswersToFlush.map(([questionId, valueJson]) => {
        const focusedAt = questionFocusStartedAt.current[questionId];
        const elapsedMs = focusedAt != null && flushNow > focusedAt
          ? Math.min(flushNow - focusedAt, 14_400_000)
          : null;
        return saveReadingAnswer(attempt.attemptId, questionId, valueJson, elapsedMs);
      }));
      // A real failure here (offline, 500) must abort: locking would discard
      // that answer for good. A `part_a_locked` race is different — the
      // section is already closed server-side, so carry on to the break.
      const flushFailure = flushResults.find(
        (result) => result.status === 'rejected' && !isBenignLockedSave(result.reason),
      );
      if (flushFailure?.status === 'rejected') throw flushFailure.reason;
      partAAnswersToFlush.forEach(([questionId]) => {
        questionFocusStartedAt.current[questionId] = Date.now();
        dirtyQuestionIds.current.delete(questionId);
        delete pendingAnswersRef.current[questionId];
      });
      await Promise.allSettled([...inFlightSaves.current]);
      // R08 — land any debounced rule-out / highlight edits too.
      await annotations.flush();

      const locked = await lockReadingPartA(attempt.attemptId);
      syncServerClock(locked.serverNow);
      setAttempt((current) => current ? {
        ...current,
        deadlineAt: locked.deadlineAt,
        partADeadlineAt: locked.partADeadlineAt,
        partBCDeadlineAt: locked.partBCDeadlineAt,
        partABreakAvailable: locked.partABreakAvailable,
        partABreakResumed: locked.partABreakResumed,
        partBCTimerPausedAt: locked.partBCTimerPausedAt,
        partBCPausedSeconds: locked.partBCPausedSeconds,
        partABreakMaxSeconds: locked.partABreakMaxSeconds,
        serverNow: locked.serverNow,
      } : current);
      setSaveState('saved');
    } catch (err) {
      setError(readErrorMessage(err, 'Could not submit Part A. Please try again.'));
    } finally {
      lockPartAInFlight.current = false;
      setLockingPartA(false);
    }
  }, [
    annotations,
    answers,
    attempt,
    breakPending,
    paperExpired,
    partALocked,
    partBCWindowEnded,
    questionPartById,
    syncServerClock,
  ]);

  const resumeBreak = useCallback(async () => {
    if (!attempt) return;
    setError(null);
    try {
      const resumed = await resumeReadingBreak(attempt.attemptId);
      syncServerClock(resumed.serverNow);
      setAttempt((current) => current ? {
        ...current,
        deadlineAt: resumed.deadlineAt,
        partADeadlineAt: resumed.partADeadlineAt,
        partBCDeadlineAt: resumed.partBCDeadlineAt,
        partABreakAvailable: resumed.partABreakAvailable,
        partABreakResumed: resumed.partABreakResumed,
        partBCTimerPausedAt: resumed.partBCTimerPausedAt,
        partBCPausedSeconds: resumed.partBCPausedSeconds,
        partABreakMaxSeconds: resumed.partABreakMaxSeconds,
        serverNow: resumed.serverNow,
      } : current);
      setActivePart('B');
      setTimingNotice('Break ended. Parts B and C are now active.');
      // Resuming the break already served as the explicit Part A → B/C
      // transition, so suppress the standalone transition screen.
      setPartTransitionAcknowledged(true);
    } catch (err) {
      setError(readErrorMessage(err, 'Failed to resume. Please try again.'));
    }
  }, [attempt, syncServerClock]);

  useEffect(() => {
    if (!attempt || attempt.status !== 'InProgress' || !partBCWindowEnded || autoSubmitTriggered.current) return;
    autoSubmitTriggered.current = true;
    void submit();
  }, [attempt, partBCWindowEnded, submit]);

  const reloadPdfAnnotations = useCallback(async () => {
    setPdfAnnotations(await getReadingPaperAnnotations(paperId));
  }, [paperId]);

  const handleCreatePdfAnnotation = useCallback(async (body: Parameters<typeof createReadingPaperAnnotation>[1]) => {
    await createReadingPaperAnnotation(paperId, body);
    await reloadPdfAnnotations();
  }, [paperId, reloadPdfAnnotations]);

  const handleDeletePdfAnnotation = useCallback(async (annotationId: string) => {
    await deleteReadingPaperAnnotation(paperId, annotationId);
    await reloadPdfAnnotations();
  }, [paperId, reloadPdfAnnotations]);

  const handleClearPdfAsset = useCallback(async (assetId: string) => {
    await clearReadingPaperAnnotations(paperId, { scope: 'asset', assetId });
    await reloadPdfAnnotations();
  }, [paperId, reloadPdfAnnotations]);

  const handleClearPdfPaper = useCallback(async () => {
    await clearReadingPaperAnnotations(paperId, { scope: 'paper' });
    await reloadPdfAnnotations();
  }, [paperId, reloadPdfAnnotations]);

  if (loading) {
    return <LearnerDashboardShell pageTitle="Reading"><Skeleton className="h-64" /></LearnerDashboardShell>;
  }

  if (contentLockedMessage) {
    return (
      <LearnerDashboardShell pageTitle="Reading" backHref="/reading">
        <ContentLockedNotice message={contentLockedMessage} />
      </LearnerDashboardShell>
    );
  }

  if (!structure) {
    return (
      <LearnerDashboardShell pageTitle="Reading" backHref="/reading">
        <InlineAlert variant="error">{error ?? 'Paper not found.'}</InlineAlert>
      </LearnerDashboardShell>
    );
  }

  return (
    <LearnerDashboardShell pageTitle={structure.paper.title} backHref="/reading">
      <main
        // Phase 5 closure — `--reading-font-scale` lets the player text
        // grow without the SSR layout shift that a full body zoom causes,
        // and the `data-reading-contrast` attribute lets `app/globals.css`
        // (or a future Tailwind plugin) flip palette tokens on demand.
        className="space-y-5"
        data-reading-contrast={highContrast ? 'high' : 'standard'}
        data-reading-screen-reader-hints={screenReaderHints ? 'on' : 'off'}
        aria-describedby={readingA11yHintId}
        style={{
          ['--reading-player-scale' as string]: zoomLevel / 100,
          ['--reading-font-scale' as string]: fontScale / 100,
          fontSize: `${fontScale}%`,
        }}
      >
        {screenReaderHints ? (
          <p id={readingA11yHintId} className="sr-only">
            Screen reader hints are enabled. Use the part tabs to move between sections, then move through the question navigator with the arrow keys.
            Select passage text first, then choose Highlight or Clear highlights for the current part.
          </p>
        ) : null}
        {error ? <InlineAlert variant="error">{error}</InlineAlert> : null}
        {timingNotice ? <InlineAlert variant="warning">{timingNotice}</InlineAlert> : null}
        {mockAttemptId ? (
          <InlineAlert variant="info">
            You&rsquo;re taking this section as part of a mock. Submitting will mark this section complete and return you to the mock dashboard.
          </InlineAlert>
        ) : null}
        <div className="md:hidden">
          <InlineAlert variant="warning">Full Reading exam mode is designed for a tablet or desktop-sized screen.</InlineAlert>
        </div>

        {!attempt ? (
          <section className="rounded-[20px] border border-border bg-surface px-5 py-8 text-center shadow-sm">
            <Clock className="mx-auto h-7 w-7 text-info" aria-hidden="true" />
            <h1 className="mt-4 text-2xl font-semibold tracking-tight text-navy">{structure.paper.title}</h1>
            <p className="mx-auto mt-2 max-w-2xl text-sm leading-6 text-muted">
              Start a server-authoritative Reading attempt. Part A locks after its window, then Parts B and C share the remaining timer.
            </p>
            <p
              className="mx-auto mt-4 max-w-2xl rounded-2xl border border-border bg-background-light px-4 py-3 text-xs font-semibold leading-5 text-muted"
              data-testid="reading-integrity-reminder"
              role="note"
            >
              OET test content is confidential. Do not redistribute or share questions outside this practice context.
            </p>
            <div className="mt-5 flex justify-center">
              <Button variant="primary" onClick={() => void start()} loading={starting}>
                Start attempt
              </Button>
            </div>
          </section>
        ) : (
          <>
            {isSubsetPracticeMode(attempt.mode) ? (
              <InlineAlert variant="info">
                <strong>{practiceModeLabel(attempt.mode)}: practice only.</strong>{' '}
                This attempt does not produce an OET 0–500 scaled score and does not consume an exam attempt.
              </InlineAlert>
            ) : null}
            <AttemptToolbar
              attempt={attempt}
              activePart={activePart}
              nowMs={nowMs}
              answeredCount={answeredCount}
              totalQuestions={totalQuestions}
              saveState={saveState}
              paperExpired={partBCWindowEnded || paperExpired}
              partALocked={partALocked}
              breakPending={breakPending}
              zoomLevel={zoomLevel}
              displayWarnings={displayWarnings}
              submitting={submitting}
              showSubmitPartA={canSubmitPartAEarly}
              lockingPartA={lockingPartA}
              onZoomChange={setZoomLevel}
              onSubmit={() => setShowConfirm(true)}
              onSubmitPartA={() => setShowSubmitPartAConfirm(true)}
              a11yPolicy={structure.paper.policy ?? null}
              fontScale={fontScale}
              highContrast={highContrast}
              screenReaderHints={screenReaderHints}
              onFontScaleChange={setFontScale}
              onHighContrastChange={setHighContrast}
              onScreenReaderHintsChange={setScreenReaderHints}
            />

            {breakPending ? (
              <ReadingBreakScreen attempt={attempt} nowMs={nowMs} onResume={() => void resumeBreak()} />
            ) : null}

            {showPartTransition ? (
              <ReadingPartTransitionScreen
                minutes={partBCMinutesRemaining}
                onContinue={() => setPartTransitionAcknowledged(true)}
              />
            ) : null}

            {!breakPending && !showPartTransition ? (
              <PartTabs
                structure={displayedStructure ?? structure}
                activePart={activePart}
                answers={answers}
                flagged={flagged}
                partALocked={partALocked}
                partBCAccessible={isPracticeMode || (partALocked && !breakPending)}
                onChange={(part) => setActivePart(part)}
              />
            ) : null}

            {!breakPending && !showPartTransition && activePart === 'C' && displayedSections.length > 0 ? (
              <SectionTabs
                sections={displayedSections}
                activeSection={activeSection}
                onChange={(section) => setActiveSection(section)}
              />
            ) : null}

            {!breakPending && !showPartTransition && displayedPartForRender ? (
              <div className="origin-top" style={{ transform: 'scale(var(--reading-player-scale))', transformOrigin: 'top center' }}>
                <PartBody
                  paperId={paperId}
                  part={displayedPartForRender}
                  questionPaperAssets={structure.paper.questionPaperAssets ?? []}
                  pdfAnnotations={pdfAnnotations}
                  answers={answers}
                  flagged={flagged}
                  activeQuestionId={activeQuestionId}
                  eliminatedChoices={eliminatedChoices}
                  locked={attemptInputsLocked || (displayedCurrentPart?.partCode === 'A' && partALocked)}
                  assetKey={displayedCurrentPart?.partCode ?? 'A'}
                  onCreatePdfAnnotation={handleCreatePdfAnnotation}
                  onDeletePdfAnnotation={handleDeletePdfAnnotation}
                  onClearPdfAsset={handleClearPdfAsset}
                  onClearPdfPaper={handleClearPdfPaper}
                  onActiveQuestionChange={setActiveQuestionId}
                  onToggleFlag={(questionId) => setFlagged((prev) => toggleSetValue(prev, questionId))}
                  onToggleEliminated={toggleEliminated}
                  onAnswerChange={setAnswer}
                />
              </div>
            ) : null}
          </>
        )}

        <Modal open={showConfirm} onClose={() => setShowConfirm(false)} title="Submit Reading attempt?">
          <div className="space-y-4">
            <p className="text-sm leading-6 text-muted">
              You have answered <strong className="text-navy">{answeredCount}</strong> of{' '}
              <strong className="text-navy">{totalQuestions}</strong> questions. Unanswered questions score zero.
            </p>
            {unansweredCount > 0 ? (
              <InlineAlert variant="warning">
                {unansweredCount} unanswered question{unansweredCount === 1 ? '' : 's'} will score zero if you submit now.
              </InlineAlert>
            ) : null}
          </div>
          <div className="mt-5 flex flex-col-reverse gap-2 sm:flex-row sm:justify-end">
            <Button variant="ghost" onClick={() => setShowConfirm(false)}>Keep working</Button>
            <Button variant="primary" onClick={() => { setShowConfirm(false); void submit(); }} loading={submitting}>
              Submit now
            </Button>
          </div>
        </Modal>

        {/* Early Part A submission. Kept separate from the whole-attempt
            confirm above so the two dialogs can never both be open, and so
            the copy talks about Part A rather than the 42-question total.
            Cancel, the close button, the backdrop and Escape all leave the
            candidate in Part A — Modal handles those. */}
        <Modal
          open={showSubmitPartAConfirm}
          onClose={() => setShowSubmitPartAConfirm(false)}
          title="Submit Part A?"
        >
          <div className="space-y-4">
            <p className="text-sm leading-6 text-muted">Are you sure you want to submit Part A?</p>
            <InlineAlert variant="warning">
              Part A locks straight away and cannot be reopened. Your Part A answers are saved.
              You&rsquo;ll go to the break screen next &mdash; press Resume Test whenever you&rsquo;re
              ready to start Parts B &amp; C.
            </InlineAlert>
          </div>
          <div className="mt-5 flex flex-col-reverse gap-2 sm:flex-row sm:justify-end">
            <Button variant="ghost" onClick={() => setShowSubmitPartAConfirm(false)}>Cancel</Button>
            <Button
              variant="primary"
              onClick={() => { setShowSubmitPartAConfirm(false); void lockPartA(); }}
              loading={lockingPartA}
              data-testid="reading-confirm-submit-part-a"
            >
              Yes, submit Part A
            </Button>
          </div>
        </Modal>
      </main>
    </LearnerDashboardShell>
  );
}
