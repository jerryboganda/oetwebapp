'use client';

import { Suspense, use, useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { useRouter, useSearchParams } from 'next/navigation';
import { AlertCircle, Clock, Eye, Flag, Loader2, Play, RotateCcw, Save, Send, Settings, Strikethrough, ZoomIn, ZoomOut } from 'lucide-react';
import { LearnerDashboardShell } from '@/components/layout';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { InlineAlert } from '@/components/ui/alert';
import { Skeleton } from '@/components/ui/skeleton';
import { Modal } from '@/components/ui/modal';
import { Input } from '@/components/ui/form-controls';
import { cn } from '@/lib/utils';
import { useReadingAnnotations } from '@/hooks/use-reading-annotations';
import {
  getReadingAttempt,
  getReadingPaperAnnotations,
  getReadingStructureLearner,
  resumeReadingBreak,
  saveReadingAnswer,
  startReadingAttempt,
  submitReadingAttempt,
  createReadingPaperAnnotation,
  deleteReadingPaperAnnotation,
  clearReadingPaperAnnotations,
  type ReadingPaperAnnotationDto,
  type ReadingAttemptStarted,
  type ReadingAttemptStatus,
  type ReadingLearnerStructureDto,
  type ReadingPartCode,
  type ReadingQuestionLearnerDto,
} from '@/lib/reading-authoring-api';
import { ContentLockedNotice, isContentLockedError, readContentLockedMessage } from '@/components/domain/ContentLockedNotice';
import { ReadingPdfViewer } from '@/components/domain/reading-pdf-viewer';
import { readingPublicDisplayNumber } from '@/lib/reading-display-number';
import { completeMockSection } from '@/lib/api';
import { readErrorMessage } from '@/lib/read-error-message';
import { deriveDeliveryMode, deliveryModeToReadingPresentation } from '@/lib/mocks/delivery-mode';

type SaveState = 'idle' | 'saving' | 'saved' | 'error';

type ReadingSectionCode = 'B1' | 'B2' | 'B3' | 'B4' | 'B5' | 'B6' | 'C1' | 'C2';

const SECTION_LABELS: Record<ReadingSectionCode, string> = {
  B1: 'B1',
  B2: 'B2',
  B3: 'B3',
  B4: 'B4',
  B5: 'B5',
  B6: 'B6',
  C1: 'C1',
  C2: 'C2',
};

interface ActiveAttempt {
  attemptId: string;
  startedAt: string;
  deadlineAt: string;
  partADeadlineAt: string;
  partBCDeadlineAt: string;
  paperTitle: string;
  partATimerMinutes: number;
  partBCTimerMinutes: number;
  answeredCount: number;
  canResume: boolean;
  partABreakAvailable: boolean;
  partABreakResumed: boolean;
  partBCTimerPausedAt: string | null;
  partBCPausedSeconds: number;
  partABreakMaxSeconds: number;
  status: ReadingAttemptStatus;
  /** Phase 3: which practice mode this attempt is running under. */
  mode: 'Exam' | 'Learning' | 'Drill' | 'MiniTest' | 'ErrorBank';
  /** Subset modes only — in-scope question IDs (filter the structure to these). */
  scopeQuestionIds: string[] | null;
}

function getSectionCodeForQuestion(partCode: ReadingPartCode, displayOrder: number): ReadingSectionCode | null {
  if (partCode === 'B') {
    return (`B${Math.min(6, Math.max(1, displayOrder))}` as ReadingSectionCode);
  }
  if (partCode === 'C') {
    return displayOrder <= 8 ? 'C1' : 'C2';
  }
  return null;
}

function getSectionsForPart(part: ReadingLearnerStructureDto['parts'][number]) {
  if (part.partCode === 'A') {
    return [] as Array<{ code: ReadingSectionCode; label: string; questions: ReadingQuestionLearnerDto[] }>;
  }

  if (part.sections && part.sections.length > 0) {
    return part.sections.map((section) => ({
      code: section.sectionCode,
      label: SECTION_LABELS[section.sectionCode],
      questions: section.questions,
    }));
  }

  const sectionOrder: ReadingSectionCode[] = part.partCode === 'B'
    ? ['B1', 'B2', 'B3', 'B4', 'B5', 'B6']
    : ['C1', 'C2'];

  return sectionOrder
    .map((code) => ({
      code,
      label: SECTION_LABELS[code],
      questions: part.questions.filter((question) => getSectionCodeForQuestion(part.partCode, question.displayOrder) === code),
    }))
    ;
}

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
  // Mocks attach the chosen delivery mode (paper | computer | oet_home) via
  // BuildLaunchRoute's `&deliveryMode=`. paper → the printed-booklet
  // presentation; computer and oet_home are on-screen. Falls back to the
  // legacy `?presentation=paper` alias so existing deep links keep working.
  const deliveryMode = deriveDeliveryMode(search);
  const requestedPresentation = deliveryModeToReadingPresentation(deliveryMode);
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
  const autoSubmitTriggered = useRef(false);
  const warnedMiniTest2min = useRef(false);
  const warnedMiniTest1min = useRef(false);
  const dirtyQuestionIds = useRef<Set<string>>(new Set());
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
    const interval = setInterval(() => setNowMs(Date.now()), 1000);
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
        });
        setAnswers(restoredAnswers);
        // R08 — hydrate persisted rule-out / highlight marks for this attempt.
        setInitialAnnotationsJson(saved.annotationsJson);
        dirtyQuestionIds.current.clear();
      }
    } catch (err) {
      setError(readErrorMessage(err, 'Failed to load Reading paper.'));
    } finally {
      setLoading(false);
    }
  }, [paperId, resumeAttemptId]);

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
    if (displayedCurrentPart.partCode === 'A') return displayedCurrentPart.questions;
    const currentSection = displayedSections.find((section) => section.code === activeSection) ?? displayedSections[0] ?? null;
    return currentSection?.questions ?? displayedCurrentPart.questions;
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
  const presentation = requestedPresentation === 'paper' && structure?.paper.allowPaperReadingMode !== false
    ? 'paper'
    : 'computer';
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
    && Boolean(attempt && nowMs > partADeadlineMs);
  const breakPending = Boolean(
    attempt?.mode === 'Exam'
    && attempt.partABreakAvailable
    && partALocked
    && !attempt.partABreakResumed
    && nowMs < breakWindowEndsAtMs,
  );
  const partBCWindowEnded = Boolean(attempt && !breakPending && nowMs > partBCDeadlineMs);
  const paperExpired = Boolean(attempt && nowMs > overallDeadlineMs);
  const attemptInputsLocked = breakPending || partBCWindowEnded || paperExpired;

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
      setAttempt(fromStartedAttempt(started));
      if (mockAttemptId && mockSectionId && !resumeAttemptId) {
        const nextParams = new URLSearchParams(search?.toString());
        nextParams.set('attemptId', started.attemptId);
        router.replace(`/reading/paper/${encodeURIComponent(paperId)}?${nextParams.toString()}`);
      }
      setAnswers({});
      setFlagged(new Set());
      setInitialAnnotationsJson(null);
      autoSubmitTriggered.current = false;
      warnedMiniTest2min.current = false;
      warnedMiniTest1min.current = false;
      dirtyQuestionIds.current.clear();
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
    try {
      await saveReadingAnswer(attempt.attemptId, questionId, valueJson, elapsedMs);
      // Reset the focus timestamp so the next save only counts the delta
      // since this save (no double-counting). If the learner switches tabs,
      // the visibilitychange handler also resets this entry.
      questionFocusStartedAt.current[questionId] = Date.now();
      dirtyQuestionIds.current.delete(questionId);
      setSaveState('saved');
    } catch (err) {
      setSaveState('error');
      setError(readErrorMessage(err, 'Autosave failed.'));
    }
  }, [attempt, questionPartById]);

  const setAnswer = (question: ReadingQuestionLearnerDto, value: unknown) => {
    if (!attempt || isQuestionLocked(activePart, partALocked, attemptInputsLocked, breakPending)) return;

    const json = JSON.stringify(value);
    setAnswers((prev) => ({ ...prev, [question.id]: json }));
    dirtyQuestionIds.current.add(question.id);
    setSaveState('saving');
    if (saveTimers.current[question.id]) clearTimeout(saveTimers.current[question.id]);
    const activeDeadlineMs = new Date(activePart === 'A' ? attempt.partADeadlineAt : attempt.partBCDeadlineAt).getTime();
    if (activeDeadlineMs - Date.now() <= 5000) {
      void persistAnswer(question.id, json);
      return;
    }

    saveTimers.current[question.id] = setTimeout(() => {
      void persistAnswer(question.id, json);
    }, 400);
  };

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
      });
      // R08 — land any debounced rule-out / highlight edits before grading.
      await annotations.flush();
      const graded = await submitReadingAttempt(attempt.attemptId);
      if (mockAttemptId && mockSectionId) {
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
        router.push(`/reading/paper/${paperId}/results?attemptId=${attempt.attemptId}`);
        return;
      }
      router.push(`/reading/paper/${paperId}/results?attemptId=${attempt.attemptId}`);
    } catch (err) {
      setError(readErrorMessage(err, 'Could not submit Reading attempt.'));
    } finally {
      setSubmitting(false);
    }
  }, [answers, attempt, breakPending, mockAttemptId, mockSectionId, paperId, paperExpired, partALocked, partBCWindowEnded, questionPartById, router, submitting]);

  const resumeBreak = useCallback(async () => {
    if (!attempt) return;
    setError(null);
    try {
      const resumed = await resumeReadingBreak(attempt.attemptId);
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
      } : current);
      setActivePart('B');
      setTimingNotice('Break ended. Parts B and C are now active.');
      // Resuming the break already served as the explicit Part A → B/C
      // transition, so suppress the standalone transition screen.
      setPartTransitionAcknowledged(true);
    } catch (err) {
      setError(readErrorMessage(err, 'Failed to resume. Please try again.'));
    }
  }, [attempt]);

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
        {requestedPresentation === 'paper' && presentation !== 'paper' ? (
          <InlineAlert variant="warning">Paper simulation is disabled by the current Reading policy. Computer-delivered mode is open.</InlineAlert>
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
              onZoomChange={setZoomLevel}
              onSubmit={() => setShowConfirm(true)}
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

            {!breakPending && !showPartTransition && displayedSections.length > 0 ? (
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
                  assetKey={activeSection ?? displayedCurrentPart?.partCode ?? 'A'}
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
      </main>
    </LearnerDashboardShell>
  );
}

function AttemptToolbar({
  attempt,
  activePart,
  nowMs,
  answeredCount,
  totalQuestions,
  saveState,
  paperExpired,
  partALocked,
  breakPending,
  zoomLevel,
  displayWarnings,
  submitting,
  onZoomChange,
  onSubmit,
  a11yPolicy,
  fontScale,
  highContrast,
  screenReaderHints,
  onFontScaleChange,
  onHighContrastChange,
  onScreenReaderHintsChange,
}: {
  attempt: ActiveAttempt;
  activePart: ReadingPartCode;
  nowMs: number;
  answeredCount: number;
  totalQuestions: number;
  saveState: SaveState;
  paperExpired: boolean;
  partALocked: boolean;
  breakPending: boolean;
  zoomLevel: number;
  displayWarnings: string[];
  submitting: boolean;
  onZoomChange: (next: number) => void;
  onSubmit: () => void;
  /**
   * Phase 5 closure — resolved a11y policy. When null or every flag is
   * false the settings dropdown is hidden so we don't tease a learner
   * with a control they cannot actually use.
   */
  a11yPolicy: { fontScaleUserControl: boolean; highContrastMode: boolean; screenReaderOptimised: boolean } | null;
  fontScale: 90 | 100 | 110 | 125;
  highContrast: boolean;
  screenReaderHints: boolean;
  onFontScaleChange: (next: 90 | 100 | 110 | 125) => void;
  onHighContrastChange: (next: boolean) => void;
  onScreenReaderHintsChange: (next: boolean) => void;
}) {
  const breakStartedAt = attempt.partBCTimerPausedAt ?? attempt.partADeadlineAt;
  const breakSecondsLeft = Math.max(
    0,
    attempt.partABreakMaxSeconds - Math.floor((nowMs - new Date(breakStartedAt).getTime()) / 1000),
  );
  const activeDeadline = attempt.mode === 'Exam' && activePart === 'A'
    ? attempt.partADeadlineAt
    : attempt.partBCDeadlineAt;
  const secondsLeft = breakPending
    ? breakSecondsLeft
    : Math.max(0, Math.floor((new Date(activeDeadline).getTime() - nowMs) / 1000));
  const timerLabel = attempt.mode === 'Exam'
    ? (breakPending ? 'Optional break' : activePart === 'A' ? 'Part A window' : 'B/C shared window')
    : 'Practice timer';

  return (
    <section className="rounded-[20px] border border-border bg-surface p-4 shadow-sm" aria-label="Attempt status">
      <div className="flex flex-col gap-4 lg:flex-row lg:items-center lg:justify-between">
        <div className="flex flex-wrap items-center gap-3">
          <div
            className="flex items-center gap-2 rounded-xl bg-background-light px-3 py-2"
            role="timer"
            aria-live="polite"
            aria-atomic="true"
            aria-label={`${timerLabel}, ${formatCountdown(secondsLeft)} remaining`}
          >
            <Clock className="h-4 w-4 text-primary" aria-hidden="true" />
            <span className="text-xs font-bold uppercase tracking-[0.14em] text-muted">{timerLabel}</span>
            <span className="font-mono text-base font-bold text-navy">{formatCountdown(secondsLeft)}</span>
          </div>
          {partALocked ? <Badge variant="warning">Part A locked</Badge> : null}
          {breakPending ? <Badge variant="info">B/C paused</Badge> : null}
          {paperExpired ? <Badge variant="danger">Time expired</Badge> : null}
          {/* 2026-05-27 audit fix — Reading rule R10.5: copy/paste in Part A
              is unreliable across exam centres. Show a one-time advisory chip
              while the candidate is on Part A. */}
          {activePart === 'A' && !partALocked ? (
            <Badge variant="info" data-testid="reading-part-a-copy-paste-warning">
              Copy/paste may be unavailable. Type directly (R10.5)
            </Badge>
          ) : null}
          <span className="text-sm font-semibold text-muted" aria-label={`${answeredCount} of ${totalQuestions} questions answered`}>
            {answeredCount}/{totalQuestions} answered
          </span>
        </div>

        <div className="flex flex-col gap-3 sm:flex-row sm:items-center">
          <ReadingZoomControls zoomLevel={zoomLevel} onZoomChange={onZoomChange} />
          <ReadingA11ySettings
            policy={a11yPolicy}
            fontScale={fontScale}
            highContrast={highContrast}
            screenReaderHints={screenReaderHints}
            onFontScaleChange={onFontScaleChange}
            onHighContrastChange={onHighContrastChange}
            onScreenReaderHintsChange={onScreenReaderHintsChange}
          />
          <SaveStatus state={saveState} />
          <Button variant="primary" onClick={onSubmit} loading={submitting} disabled={breakPending || paperExpired} aria-label="Submit attempt for grading">
            <Send className="h-4 w-4" aria-hidden="true" />
            Submit
          </Button>
        </div>
      </div>
      {displayWarnings.length > 0 ? (
        <div className="mt-3 flex flex-wrap items-center gap-2 text-xs font-semibold text-amber-700">
          <AlertCircle className="h-4 w-4" aria-hidden="true" />
          {displayWarnings.map((warning) => <span key={warning}>{warning}</span>)}
        </div>
      ) : null}
    </section>
  );
}

function ReadingZoomControls({ zoomLevel, onZoomChange }: { zoomLevel: number; onZoomChange: (next: number) => void }) {
  const changeZoom = (next: number) => onZoomChange(Math.min(125, Math.max(80, next)));
  const hint = 'Use the in-app zoom; browser Ctrl+/- may misalign the timer.';

  return (
    <div
      className="inline-flex items-center gap-1 rounded-xl border border-border bg-background-light p-1"
      aria-label="Reading zoom controls"
      aria-describedby="reading-zoom-hint"
      title={hint}
    >
      <Button variant="ghost" size="sm" className="h-9 w-9 px-0" onClick={() => changeZoom(zoomLevel - 5)} aria-label="Zoom out" title="Zoom out">
        <ZoomOut className="h-4 w-4" aria-hidden="true" />
      </Button>
      <span className="w-12 text-center font-mono text-xs font-bold text-navy" aria-live="polite">{zoomLevel}%</span>
      <Button variant="ghost" size="sm" className="h-9 w-9 px-0" onClick={() => changeZoom(zoomLevel + 5)} aria-label="Zoom in" title="Zoom in">
        <ZoomIn className="h-4 w-4" aria-hidden="true" />
      </Button>
      <Button variant="ghost" size="sm" className="h-9 w-9 px-0" onClick={() => changeZoom(100)} aria-label="Reset zoom" title="Reset zoom">
        <RotateCcw className="h-4 w-4" aria-hidden="true" />
      </Button>
      <span id="reading-zoom-hint" className="sr-only">
        {hint}
      </span>
    </div>
  );
}

/**
 * Phase 5 closure — learner-facing accessibility controls. Renders
 * nothing when the policy disables every flag (so we never tease a
 * control the policy refuses to honour). Stored values are persisted
 * per-paper by the player via localStorage.
 */
function ReadingA11ySettings({
  policy,
  fontScale,
  highContrast,
  screenReaderHints,
  onFontScaleChange,
  onHighContrastChange,
  onScreenReaderHintsChange,
}: {
  policy: { fontScaleUserControl: boolean; highContrastMode: boolean; screenReaderOptimised: boolean } | null;
  fontScale: 90 | 100 | 110 | 125;
  highContrast: boolean;
  screenReaderHints: boolean;
  onFontScaleChange: (next: 90 | 100 | 110 | 125) => void;
  onHighContrastChange: (next: boolean) => void;
  onScreenReaderHintsChange: (next: boolean) => void;
}) {
  if (!policy
    || (!policy.fontScaleUserControl && !policy.highContrastMode && !policy.screenReaderOptimised)) {
    return null;
  }

  return (
    <details className="group relative inline-block">
      <summary
        className="inline-flex h-9 cursor-pointer list-none items-center gap-1.5 rounded-xl border border-border bg-background-light px-3 text-xs font-bold text-navy hover:border-border-hover focus:outline-none focus:ring-4 focus:ring-primary/15"
        role="button"
        aria-label="Accessibility settings"
      >
        <Settings className="h-4 w-4" aria-hidden />
        A11y
      </summary>
      <div
        className="absolute right-0 z-10 mt-2 w-72 space-y-3 rounded-2xl border border-border bg-surface p-3 text-sm shadow-lg"
        role="dialog"
        aria-label="Accessibility settings"
      >
        {policy.fontScaleUserControl ? (
          <div className="space-y-1">
            <p className="text-xs font-bold uppercase tracking-[0.14em] text-muted">Font size</p>
            <div className="flex items-center gap-1">
              {[90, 100, 110, 125].map((value) => (
                <button
                  key={value}
                  type="button"
                  onClick={() => onFontScaleChange(value as 90 | 100 | 110 | 125)}
                  aria-pressed={fontScale === value}
                  className={
                    'flex-1 rounded-lg border px-2 py-1.5 text-xs font-bold transition-colors ' +
                    (fontScale === value
                      ? 'border-primary bg-primary text-white dark:bg-violet-700'
                      : 'border-border bg-background-light text-navy hover:bg-surface')
                  }
                >
                  {value}%
                </button>
              ))}
            </div>
          </div>
        ) : null}
        {policy.highContrastMode ? (
          <label className="flex items-center gap-2 rounded-lg bg-background-light p-2 text-xs">
            <input
              type="checkbox"
              className="h-4 w-4 rounded border-border text-primary focus:ring-primary/20"
              checked={highContrast}
              onChange={(e) => onHighContrastChange(e.target.checked)}
            />
            <span className="font-semibold text-navy">High-contrast palette</span>
          </label>
        ) : null}
        {policy.screenReaderOptimised ? (
          <label className="flex items-center gap-2 rounded-lg bg-background-light p-2 text-xs">
            <input
              type="checkbox"
              className="h-4 w-4 rounded border-border text-primary focus:ring-primary/20"
              checked={screenReaderHints}
              onChange={(e) => onScreenReaderHintsChange(e.target.checked)}
            />
            <span className="font-semibold text-navy">Extra screen-reader hints</span>
          </label>
        ) : null}
        <p className="text-[10px] text-muted">
          <Eye className="mr-1 inline h-3 w-3" aria-hidden />
          Settings are saved per paper. Changes here only affect this paper.
        </p>
      </div>
    </details>
  );
}

function ReadingBreakScreen({ attempt, nowMs, onResume }: { attempt: ActiveAttempt; nowMs: number; onResume: () => void }) {
  const breakStartedAt = attempt.partBCTimerPausedAt ?? attempt.partADeadlineAt;
  const secondsLeft = Math.max(
    0,
    attempt.partABreakMaxSeconds - Math.floor((nowMs - new Date(breakStartedAt).getTime()) / 1000),
  );

  return (
    <section className="rounded-[20px] border border-border bg-surface p-6 shadow-sm" aria-label="Part A break">
      <div className="mx-auto flex max-w-2xl flex-col items-center gap-4 text-center">
        <Badge variant="info">Part A collected</Badge>
        <div className="flex items-center gap-3 rounded-2xl bg-background-light px-5 py-4" role="timer" aria-live="polite" aria-label={`${formatCountdown(secondsLeft)} break time remaining`}>
          <Clock className="h-5 w-5 text-primary" aria-hidden="true" />
          <span className="font-mono text-3xl font-bold text-navy">{formatCountdown(secondsLeft)}</span>
        </div>
        <Button variant="primary" onClick={onResume} aria-label="Resume Reading test">
          <Play className="h-4 w-4" aria-hidden="true" />
          Resume Test
        </Button>
      </div>
    </section>
  );
}

function ReadingPartTransitionScreen({ minutes, onContinue }: { minutes: number; onContinue: () => void }) {
  return (
    <section
      className="rounded-[20px] border border-border bg-surface p-6 shadow-sm"
      aria-label="Part A locked — continue to Parts B and C"
      role="alertdialog"
      aria-modal="false"
    >
      <div className="mx-auto flex max-w-2xl flex-col items-center gap-4 text-center">
        <Badge variant="warning">Part A submitted &amp; locked</Badge>
        <h2 className="text-xl font-semibold tracking-tight text-navy">Part A is complete</h2>
        <p className="text-sm leading-6 text-muted">
          Part A has been submitted and locked — you cannot return to it. You now have{' '}
          <strong className="text-navy">{minutes} minute{minutes === 1 ? '' : 's'}</strong> for Parts B &amp; C.
        </p>
        <Button variant="primary" onClick={onContinue} aria-label="Continue to Parts B and C">
          <Play className="h-4 w-4" aria-hidden="true" />
          Continue to Parts B &amp; C
        </Button>
      </div>
    </section>
  );
}

function SaveStatus({ state }: { state: SaveState }) {
  const label = {
    idle: 'Autosave ready',
    saving: 'Saving...',
    saved: 'Saved',
    error: 'Save failed',
  }[state];
  const Icon = state === 'saving' ? Loader2 : state === 'error' ? AlertCircle : Save;

  return (
    <span
      className={cn(
        'inline-flex items-center gap-2 text-sm font-semibold',
        state === 'error' ? 'text-danger' : 'text-muted',
      )}
      role="status"
      aria-live="polite"
    >
      <Icon className={cn('h-4 w-4', state === 'saving' && 'motion-safe:animate-spin')} aria-hidden="true" />
      {label}
    </span>
  );
}

function PartTabs({
  structure,
  activePart,
  answers,
  flagged,
  partALocked,
  partBCAccessible,
  onChange,
}: {
  structure: ReadingLearnerStructureDto;
  activePart: ReadingPartCode;
  answers: Record<string, string>;
  flagged: Set<string>;
  partALocked: boolean;
  partBCAccessible: boolean;
  onChange: (part: ReadingPartCode) => void;
}) {
  return (
    <div
      className="flex gap-2 overflow-x-auto border-b border-border"
      role="tablist"
      aria-label="Reading parts"
    >
      {structure.parts.map((part) => {
        const answered = part.questions.filter((question) => isAnsweredJson(answers[question.id])).length;
        const flaggedCount = part.questions.filter((question) => flagged.has(question.id)).length;
        const isActive = activePart === part.partCode;
        const isLocked = (part.partCode === 'A' && partALocked)
          || ((part.partCode === 'B' || part.partCode === 'C') && !partBCAccessible);
        const partALabel = isLocked ? ' (locked)' : '';
        return (
          <button
            key={part.partCode}
            type="button"
            role="tab"
            id={`reading-part-tab-${part.partCode}`}
            aria-controls={`reading-part-panel-${part.partCode}`}
            aria-selected={isActive}
            aria-label={`Part ${part.partCode}, ${answered} of ${part.questions.length} answered${flaggedCount ? `, ${flaggedCount} flagged` : ''}${partALabel}`}
            tabIndex={isActive ? 0 : -1}
            onClick={() => onChange(part.partCode)}
            disabled={isLocked}
            className={cn(
              'min-h-11 shrink-0 border-b-2 px-4 py-2 text-left text-sm font-bold transition-colors focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-primary',
              isActive
                ? 'border-primary text-primary'
                : 'border-transparent text-muted hover:text-navy',
              isLocked && 'cursor-not-allowed opacity-60 hover:text-muted',
            )}
          >
            <span>Part {part.partCode}</span>
            <span className="ml-2 text-xs font-semibold text-muted" aria-hidden="true">{answered}/{part.questions.length}</span>
            {flaggedCount ? <span className="ml-2 text-xs text-warning" aria-hidden="true">{flaggedCount} flagged</span> : null}
            {isLocked ? <span className="ml-2 text-xs text-danger" aria-hidden="true">locked</span> : null}
          </button>
        );
      })}
    </div>
  );
}

function SectionTabs({
  sections,
  activeSection,
  onChange,
}: {
  sections: Array<{ code: ReadingSectionCode; label: string; questions: ReadingQuestionLearnerDto[] }>;
  activeSection: ReadingSectionCode | null;
  onChange: (section: ReadingSectionCode) => void;
}) {
  return (
    <div
      className="flex gap-2 overflow-x-auto rounded-2xl border border-border bg-surface p-2 shadow-sm"
      aria-label="Reading sections"
    >
      {sections.map((section) => {
        const isActive = activeSection === section.code;
        return (
          <button
            key={section.code}
            type="button"
            aria-pressed={isActive}
            aria-label={`Section ${section.label}, ${section.questions.length} question${section.questions.length === 1 ? '' : 's'}`}
            onClick={() => onChange(section.code)}
            className={cn(
              'min-h-10 shrink-0 rounded-xl px-4 py-2 text-sm font-bold transition-colors focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-primary',
              isActive
                ? 'bg-primary text-white shadow-sm'
                : 'bg-background-light text-muted hover:text-navy',
            )}
          >
            {section.label}
            <span className="ml-2 text-xs font-semibold opacity-80" aria-hidden="true">{section.questions.length}</span>
          </button>
        );
      })}
    </div>
  );
}

function PartBody({
  paperId,
  part,
  assetKey,
  questionPaperAssets,
  pdfAnnotations,
  answers,
  flagged,
  activeQuestionId,
  eliminatedChoices,
  locked,
  onCreatePdfAnnotation,
  onDeletePdfAnnotation,
  onClearPdfAsset,
  onClearPdfPaper,
  onActiveQuestionChange,
  onToggleFlag,
  onToggleEliminated,
  onAnswerChange,
}: {
  paperId: string;
  part: ReadingLearnerStructureDto['parts'][number];
  assetKey: string;
  questionPaperAssets: NonNullable<ReadingLearnerStructureDto['paper']['questionPaperAssets']>;
  pdfAnnotations: ReadingPaperAnnotationDto[];
  answers: Record<string, string>;
  flagged: Set<string>;
  activeQuestionId: string | null;
  eliminatedChoices: Set<string>;
  locked: boolean;
  onCreatePdfAnnotation: (body: Parameters<typeof createReadingPaperAnnotation>[1]) => Promise<void>;
  onDeletePdfAnnotation: (annotationId: string) => Promise<void>;
  onClearPdfAsset: (assetId: string) => Promise<void>;
  onClearPdfPaper: () => Promise<void>;
  onActiveQuestionChange: (questionId: string) => void;
  onToggleFlag: (questionId: string) => void;
  onToggleEliminated: (questionId: string, optionValue: string) => void;
  onAnswerChange: (question: ReadingQuestionLearnerDto, value: unknown) => void;
}) {
  if (part.questions.length === 0) {
    return (
      <div
        className="rounded-[20px] border border-border bg-surface p-8 text-center shadow-sm"
        role="tabpanel"
        id={`reading-part-panel-${part.partCode}`}
        aria-labelledby={`reading-part-tab-${part.partCode}`}
      >
        <p className="text-sm text-muted">No questions available for this section in the selected drill.</p>
      </div>
    );
  }

  const activeQuestion = part.questions.find((question) => question.id === activeQuestionId) ?? part.questions[0];

  return (
    <div
      className="grid grid-cols-1 gap-4 xl:grid-cols-[minmax(0,1.1fr)_minmax(360px,0.9fr)]"
      role="tabpanel"
      id={`reading-part-panel-${part.partCode}`}
      aria-labelledby={`reading-part-tab-${part.partCode}`}
    >
      <ReadingPdfViewer
        paperId={paperId}
        partCode={assetKey}
        assets={questionPaperAssets}
        annotations={pdfAnnotations}
        onCreateAnnotation={onCreatePdfAnnotation}
        onDeleteAnnotation={onDeletePdfAnnotation}
        onClearAsset={onClearPdfAsset}
        onClearPaper={onClearPdfPaper}
      />

      <section
        className="rounded-[20px] border border-border bg-surface p-5 shadow-sm"
        aria-label={`Questions for Part ${part.partCode}`}
      >
        <div className="mb-4 flex flex-col gap-3">
          <div className="flex items-center justify-between gap-3">
            <h2 className="text-sm font-black uppercase tracking-[0.18em] text-muted">Questions</h2>
            {locked ? <Badge variant="warning">Inputs locked</Badge> : null}
          </div>
          <QuestionNavigator
            partCode={part.partCode}
            questions={part.questions}
            answers={answers}
            flagged={flagged}
            activeQuestionId={activeQuestion?.id ?? null}
            onSelect={onActiveQuestionChange}
          />
        </div>

        {activeQuestion ? (
          <QuestionInput
            partCode={part.partCode}
            question={activeQuestion}
            texts={part.texts}
            valueJson={answers[activeQuestion.id] ?? ''}
            flagged={flagged.has(activeQuestion.id)}
            eliminatedChoices={eliminatedChoices}
            locked={locked}
            onToggleFlag={() => onToggleFlag(activeQuestion.id)}
            onToggleEliminated={(optionValue) => onToggleEliminated(activeQuestion.id, optionValue)}
            onChange={(value) => onAnswerChange(activeQuestion, value)}
          />
        ) : null}
      </section>
    </div>
  );
}

function QuestionNavigator({
  partCode,
  questions,
  answers,
  flagged,
  activeQuestionId,
  onSelect,
}: {
  partCode: ReadingPartCode;
  questions: ReadingQuestionLearnerDto[];
  answers: Record<string, string>;
  flagged: Set<string>;
  activeQuestionId: string | null;
  onSelect: (questionId: string) => void;
}) {
  const handleKeyDown = useCallback(
    (event: React.KeyboardEvent<HTMLDivElement>) => {
      // Arrow-key navigation between question buttons. Left/Up = previous,
      // Right/Down = next, Home = first, End = last. Wraps at the ends.
      const key = event.key;
      if (!['ArrowLeft', 'ArrowRight', 'ArrowUp', 'ArrowDown', 'Home', 'End'].includes(key))
        return;
      if (questions.length === 0) return;
      event.preventDefault();
      const currentIndex = Math.max(0, questions.findIndex((q) => q.id === activeQuestionId));
      let nextIndex = currentIndex;
      if (key === 'ArrowLeft' || key === 'ArrowUp') {
        nextIndex = (currentIndex - 1 + questions.length) % questions.length;
      } else if (key === 'ArrowRight' || key === 'ArrowDown') {
        nextIndex = (currentIndex + 1) % questions.length;
      } else if (key === 'Home') {
        nextIndex = 0;
      } else if (key === 'End') {
        nextIndex = questions.length - 1;
      }
      const next = questions[nextIndex];
      if (next) onSelect(next.id);
    },
    [questions, activeQuestionId, onSelect],
  );

  return (
    <div
      className="grid grid-cols-[repeat(auto-fill,minmax(42px,1fr))] gap-2"
      role="group"
      aria-label="Question navigator (use arrow keys to move between questions)"
      onKeyDown={handleKeyDown}
    >
      {questions.map((question) => {
        const answered = isAnsweredJson(answers[question.id]);
        const isActive = question.id === activeQuestionId;
        const isFlagged = flagged.has(question.id);
        const publicNumber = readingPublicDisplayNumber(partCode, question.displayOrder);
        return (
          <button
            key={question.id}
            type="button"
            onClick={() => onSelect(question.id)}
            tabIndex={isActive ? 0 : -1}
            aria-current={isActive ? 'true' : undefined}
            aria-label={`Question ${publicNumber}${answered ? ', answered' : ', unanswered'}${isFlagged ? ', flagged' : ''}`}
            className={cn(
              'relative min-h-11 rounded-lg border text-sm font-bold transition-colors focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-primary',
              isActive ? 'border-primary bg-primary text-white dark:bg-violet-700' : 'border-border bg-background-light text-navy hover:border-primary/40',
              answered && !isActive && 'border-success/30 bg-success/10 text-success',
              isFlagged && !isActive && 'border-warning/30 bg-warning/10 text-warning',
            )}
          >
            {publicNumber}
            {isFlagged ? <Flag className="absolute right-1 top-1 h-3 w-3 fill-current" aria-hidden="true" /> : null}
          </button>
        );
      })}
    </div>
  );
}

function QuestionInput({
  partCode,
  question,
  texts,
  valueJson,
  flagged,
  eliminatedChoices,
  locked,
  onToggleFlag,
  onToggleEliminated,
  onChange,
}: {
  partCode: ReadingPartCode;
  question: ReadingQuestionLearnerDto;
  texts: ReadingLearnerStructureDto['parts'][number]['texts'];
  valueJson: string;
  flagged: boolean;
  eliminatedChoices: Set<string>;
  locked: boolean;
  onToggleFlag: () => void;
  onToggleEliminated: (optionValue: string) => void;
  onChange: (value: unknown) => void;
}) {
  const current = useMemo(() => parseAnswer(valueJson), [valueJson]);

  return (
    <div className="space-y-5">
      <div className="flex items-start justify-between gap-3">
        <div>
          <p className="text-xs font-black uppercase tracking-[0.16em] text-muted">Question {readingPublicDisplayNumber(partCode, question.displayOrder)}</p>
          <h3 className="mt-2 text-base font-semibold leading-7 text-navy selection:bg-warning/30" data-reading-highlight-scope="stem">{question.stem}</h3>
        </div>
        <Button variant="ghost" size="sm" onClick={onToggleFlag} aria-pressed={flagged}>
          <Flag className={cn('h-4 w-4', flagged && 'fill-current text-warning')} />
          {flagged ? 'Flagged' : 'Flag'}
        </Button>
      </div>

      {question.questionType === 'MultipleChoice3'
        || question.questionType === 'MultipleChoice4'
        || question.questionType === 'MultipleChoiceFlexible' ? (
        <McqControl
          question={question}
          current={current}
          eliminatedChoices={eliminatedChoices}
          locked={locked}
          onToggleEliminated={onToggleEliminated}
          onChange={onChange}
        />
      ) : question.questionType === 'MatchingTextReference' ? (
        <MatchingControl question={question} texts={texts} current={current} locked={locked} onChange={onChange} />
      ) : question.questionType === 'ShortAnswerLabeled' ? (
        <LabeledTextAnswerControl question={question} current={current} locked={locked} onChange={onChange} />
      ) : (
        <TextAnswerControl current={current} locked={locked} onChange={onChange} />
      )}
    </div>
  );
}

function McqControl({
  question,
  current,
  eliminatedChoices,
  locked,
  onToggleEliminated,
  onChange,
}: {
  question: ReadingQuestionLearnerDto;
  current: unknown;
  eliminatedChoices: Set<string>;
  locked: boolean;
  onToggleEliminated: (optionValue: string) => void;
  onChange: (value: unknown) => void;
}) {
  const options = toOptionList(question.options);
  // R08 — accessible status mirroring BCQuestionRenderer: announce how many
  // options the learner has ruled out so screen-reader users get parity with
  // the visual line-through.
  const statusId = `mcq-${question.id}-status`;
  const struckCount = options.reduce((count, option, index) => {
    const letter = option.value || String.fromCharCode(65 + index);
    return count + (eliminatedChoices.has(`${question.id}:${letter}`) ? 1 : 0);
  }, 0);
  const struckSummary = struckCount === 0
    ? 'No options are ruled out.'
    : `${struckCount} option${struckCount === 1 ? '' : 's'} ruled out.`;

  return (
    <div className="space-y-2">
      {options.map((option, index) => {
        const letter = option.value || String.fromCharCode(65 + index);
        const eliminated = eliminatedChoices.has(`${question.id}:${letter}`);
        return (
          <div key={`${letter}-${option.label}`} className="flex items-stretch gap-2">
            <label
              data-reading-answer-choice="true"
              aria-describedby={eliminated ? statusId : undefined}
              onContextMenu={(event) => {
                event.preventDefault();
                if (!locked) onToggleEliminated(letter);
              }}
              className={cn(
                'flex min-h-11 flex-1 cursor-pointer items-start gap-3 rounded-lg border border-border bg-background-light p-3 text-sm transition-colors',
                current === letter && 'border-primary bg-primary/5',
                eliminated && 'text-muted line-through decoration-2',
                locked && 'cursor-not-allowed opacity-70',
              )}
            >
              <input
                type="radio"
                name={question.id}
                className="mt-1"
                disabled={locked}
                checked={current === letter}
                onChange={() => onChange(letter)}
              />
              <span className="font-mono font-bold text-navy">{letter}.</span>
              <span className={cn('leading-6 text-navy', eliminated && 'text-muted')}>{option.label}</span>
            </label>
            {/* Visible rule-out toggle (parity with Listening's BCQuestionRenderer).
                Sits outside the label so it never toggles the radio; right-click
                on the option still works for mouse users. */}
            <Button
              type="button"
              variant={eliminated ? 'secondary' : 'outline'}
              size="sm"
              aria-pressed={eliminated}
              aria-label={`${eliminated ? 'Restore' : 'Rule out'} option ${letter}`}
              onClick={() => { if (!locked) onToggleEliminated(letter); }}
              disabled={locked}
              className="self-stretch px-3"
            >
              <Strikethrough className="h-4 w-4" aria-hidden="true" />
            </Button>
          </div>
        );
      })}
      <p id={statusId} className="sr-only" aria-live="polite">
        {struckSummary}
      </p>
    </div>
  );
}

function MatchingControl({
  question,
  texts,
  current,
  locked,
  onChange,
}: {
  question: ReadingQuestionLearnerDto;
  texts: ReadingLearnerStructureDto['parts'][number]['texts'];
  current: unknown;
  locked: boolean;
  onChange: (value: unknown) => void;
}) {
  const options = toMatchingOptions(question.options, texts);
  const multi = question.points > 1 || Array.isArray(current);
  const selected = Array.isArray(current)
    ? current.map(String)
    : typeof current === 'string' && current ? [current] : [];

  const toggle = (value: string) => {
    if (!multi) {
      onChange(value);
      return;
    }
    const next = selected.includes(value)
      ? selected.filter((item) => item !== value)
      : [...selected, value];
    onChange(next);
  };

  return (
    <div className="grid grid-cols-1 gap-2 sm:grid-cols-2">
      {options.map((option) => {
        const isSelected = selected.includes(option.value);
        return (
          <button
            key={option.value}
            type="button"
            disabled={locked}
            onClick={() => toggle(option.value)}
            className={cn(
              'min-h-12 rounded-lg border border-border bg-background-light px-3 py-2 text-left transition-colors disabled:cursor-not-allowed disabled:opacity-70',
              isSelected && 'border-primary bg-primary/5',
            )}
          >
            <span className="block text-sm font-bold text-navy">Text {option.value}</span>
            {option.label ? <span className="block text-xs text-muted">{option.label}</span> : null}
          </button>
        );
      })}
    </div>
  );
}

function TextAnswerControl({
  current,
  locked,
  onChange,
}: {
  current: unknown;
  locked: boolean;
  onChange: (value: unknown) => void;
}) {
  return (
    <input
      className="min-h-11 w-full rounded-lg border border-border bg-background-light px-3 py-2 text-sm text-navy outline-none transition focus:border-primary focus:ring-2 focus:ring-primary/20 disabled:cursor-not-allowed disabled:opacity-70"
      placeholder="Type your answer"
      disabled={locked}
      value={typeof current === 'string' ? current : ''}
      onChange={(event) => onChange(event.target.value)}
    />
  );
}

function LabeledTextAnswerControl({
  question,
  current,
  locked,
  onChange,
}: {
  question: ReadingQuestionLearnerDto;
  current: unknown;
  locked: boolean;
  onChange: (value: unknown) => void;
}) {
  const options = toOptionList(question.options);
  const answerMap = current && typeof current === 'object' && !Array.isArray(current)
    ? current as Record<string, unknown>
    : {};
  const fields = options.length > 0
    ? options
    : [{ value: 'answer1', label: 'Answer 1' }];

  const update = (key: string, value: string) => {
    onChange({
      ...answerMap,
      [key]: value,
    });
  };

  return (
    <div className="space-y-3">
      {fields.map((field, index) => {
        const key = field.value || `answer${index + 1}`;
        const value = typeof answerMap[key] === 'string' ? String(answerMap[key]) : '';
        return (
          <Input
            key={key}
            label={field.label || `Answer ${index + 1}`}
            value={value}
            disabled={locked}
            onChange={(event) => update(key, event.target.value)}
            placeholder={`Type ${field.label || `answer ${index + 1}`}...`}
          />
        );
      })}
    </div>
  );
}

function fromStartedAttempt(started: ReadingAttemptStarted): ActiveAttempt {
  return {
    attemptId: started.attemptId,
    startedAt: started.startedAt,
    deadlineAt: started.deadlineAt,
    partADeadlineAt: started.partADeadlineAt,
    partBCDeadlineAt: started.partBCDeadlineAt,
    paperTitle: started.paperTitle,
    partATimerMinutes: started.partATimerMinutes,
    partBCTimerMinutes: started.partBCTimerMinutes,
    answeredCount: started.answeredCount,
    canResume: started.canResume,
    partABreakAvailable: started.partABreakAvailable,
    partABreakResumed: started.partABreakResumed,
    partBCTimerPausedAt: started.partBCTimerPausedAt,
    partBCPausedSeconds: started.partBCPausedSeconds,
    partABreakMaxSeconds: started.partABreakMaxSeconds,
    status: 'InProgress',
    // The /attempts/{id} POST returns the full canonical attempt; mode is
    // always Exam at this entry point. Practice modes are launched via the
    // dedicated practice endpoints which redirect to this page with
    // ?attemptId=… so the resume path picks up `mode` from the GET.
    mode: 'Exam',
    scopeQuestionIds: null,
  };
}

function isQuestionLocked(activePart: ReadingPartCode, partALocked: boolean, paperExpired: boolean, breakPending: boolean) {
  return paperExpired || breakPending || (activePart === 'A' && partALocked);
}

function practiceModeLabel(mode: 'Exam' | 'Learning' | 'Drill' | 'MiniTest' | 'ErrorBank'): string {
  switch (mode) {
    case 'Learning': return 'Learning Mode';
    case 'Drill': return 'Skill Drill';
    case 'MiniTest': return 'Mini-Test';
    case 'ErrorBank': return 'Error Bank Retest';
    default: return 'Practice';
  }
}

function isSubsetPracticeMode(mode: 'Exam' | 'Learning' | 'Drill' | 'MiniTest' | 'ErrorBank') {
  return mode === 'Drill' || mode === 'MiniTest' || mode === 'ErrorBank';
}

function useReadingBrowserZoomGuard() {
  useEffect(() => {
    const onKeyDown = (event: KeyboardEvent) => {
      if (!(event.ctrlKey || event.metaKey)) return;
      if (event.key === '+' || event.key === '=' || event.key === '-' || event.key === '0') {
        event.preventDefault();
      }
    };
    const onWheel = (event: WheelEvent) => {
      if (event.ctrlKey || event.metaKey) {
        event.preventDefault();
      }
    };
    window.addEventListener('keydown', onKeyDown);
    window.addEventListener('wheel', onWheel, { passive: false });
    return () => {
      window.removeEventListener('keydown', onKeyDown);
      window.removeEventListener('wheel', onWheel);
    };
  }, []);
}

function toOptionList(options: unknown): Array<{ value: string; label: string }> {
  if (!Array.isArray(options)) return [];
  return options.map((option, index) => {
    if (typeof option === 'string') return { value: String.fromCharCode(65 + index), label: option };
    if (typeof option === 'number') return { value: String.fromCharCode(65 + index), label: String(option) };
    if (option && typeof option === 'object') {
      const record = option as Record<string, unknown>;
      return {
        value: String(record.value ?? record.key ?? record.letter ?? String.fromCharCode(65 + index)),
        label: String(record.label ?? record.text ?? record.title ?? record.value ?? ''),
      };
    }
    return { value: String.fromCharCode(65 + index), label: String(option ?? '') };
  });
}

function toMatchingOptions(
  options: unknown,
  texts: ReadingLearnerStructureDto['parts'][number]['texts'],
): Array<{ value: string; label: string }> {
  const parsed = toOptionList(options).map((option, index) => ({
    value: option.value || String.fromCharCode(65 + index),
    label: option.label,
  }));
  if (parsed.length > 0) return parsed;
  return texts
    .slice()
    .sort((a, b) => a.displayOrder - b.displayOrder)
    .map((text, index) => {
      const value = String.fromCharCode(65 + index);
      return { value, label: `Text ${value}: ${text.title}` };
    });
}

function parseAnswer(valueJson: string): unknown {
  if (!valueJson) return null;
  try {
    return JSON.parse(valueJson);
  } catch {
    return valueJson;
  }
}

function isAnsweredJson(valueJson: string | undefined): boolean {
  const value = parseAnswer(valueJson ?? '');
  if (Array.isArray(value)) return value.length > 0;
  if (value && typeof value === 'object') {
    return Object.values(value as Record<string, unknown>).some((entry) => {
      if (typeof entry === 'string') return entry.trim().length > 0;
      return entry !== null && entry !== undefined && String(entry).trim().length > 0;
    });
  }
  return typeof value === 'string' ? value.trim().length > 0 : value !== null && value !== undefined;
}

function toggleSetValue(source: Set<string>, value: string): Set<string> {
  const next = new Set(source);
  if (next.has(value)) next.delete(value);
  else next.add(value);
  return next;
}

function formatCountdown(totalSec: number): string {
  const minutes = Math.floor(totalSec / 60);
  const seconds = totalSec % 60;
  return `${minutes}:${String(seconds).padStart(2, '0')}`;
}

function minutesBetween(start: string, end: string): number {
  return Math.max(0, Math.round((new Date(end).getTime() - new Date(start).getTime()) / 60000));
}
