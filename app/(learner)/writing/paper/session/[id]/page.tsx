'use client';

import { useCallback, useEffect, useRef, useState } from 'react';
import { useParams } from 'next/navigation';
import { useTranslations } from 'next-intl';
import {
  PaperBookletSimulation,
  type PaperBookletContent,
  type WritingPhase,
} from '@/components/domain/writing/PaperBookletSimulation';
import { WritingReadingWindowOverlay } from '@/components/domain/writing/WritingReadingWindowOverlay';
import { WritingReleaseCountdown, releasePollDelayMs } from '@/components/domain/writing/WritingReleaseCountdown';
import type { Highlight } from '@/components/domain/writing/WritingStimulusViewer';
import { Button } from '@/components/ui/button';
import { useFocusExitGuard } from '@/components/layout/focus-exit';
import { useWritingDraftSync, type DraftClockSnapshot, type DraftSyncBaseline } from '@/hooks/use-writing-draft-sync';
import { loadStoredSession } from '@/lib/auth-storage';
import {
  beginWritingMockWriting,
  checkWritingScenarioEligibility,
  createWritingSubmission,
  getWritingDraftV2,
  getWritingHighlights,
  getWritingMockSession,
  getWritingScenario,
  getWritingSubmission,
  putWritingHighlights,
  retryWritingGrade,
  submitWritingMock,
} from '@/lib/writing/api';
import { clearDraftShadow, draftShadowKey, readDraftShadow, reconcileDraft } from '@/lib/writing/draft-sync';
import { countLetterWords } from '@/lib/writing/letter-text';
import { isReleased } from '@/lib/writing/release';
import { createSubmitIdempotencyKey, toCandidateSafeWritingErrorMessage } from '@/lib/writing/submit-keys';
import { showCreditFeedback } from '@/lib/credit-feedback';
import {
  InsufficientCreditsModal,
  isInsufficientCreditsError,
  readInsufficientCreditsMessage,
} from '@/components/domain/InsufficientCreditsModal';
import { parseHighlights, serializeHighlights } from '@/lib/writing/highlights';
import { getWritingTask } from '@/lib/writing/exam-api';
import { useDeadlineCountdown } from '@/lib/writing/useCountdown';
import {
  WRITING_READING_WINDOW_SECONDS,
  WRITING_WINDOW_SECONDS,
} from '@/lib/writing/workflow';
import {
  WRITING_PROFESSION_LABELS,
  type WritingMockSessionDto,
  type WritingScenarioDto,
  type WritingSubmissionDto,
  type WritingTaskDto,
} from '@/lib/writing/types';

/** How long the page keeps watching a queued/grading letter before handing over to Past submissions. */
const GRADING_POLL_LIMIT_MS = 60 * 60 * 1000;
/** Failure codes that a Retry cannot fix. */
const NOT_RETRYABLE = new Set(['task_not_ready', 'manual_review', 'letter_invalid']);

/**
 * Build booklet content from the richest source available. The enriched
 * authored task gives structured case-note sections, recipient, fixed
 * instructions, and a word guide; the plain scenario is the markdown-only
 * fallback (case notes only) when no task is available for the id.
 */
function buildContentFromTask(task: WritingTaskDto): PaperBookletContent {
  return {
    title: task.title,
    professionLabel: WRITING_PROFESSION_LABELS[task.profession] ?? task.profession,
    writerRole: task.writerRole,
    todayDate: task.todayDate,
    taskPromptMarkdown: task.taskPromptMarkdown,
    fixedInstructions: task.fixedInstructions ?? [],
    wordGuideMin: task.wordGuideMin || 180,
    wordGuideMax: task.wordGuideMax || 200,
  };
}

function buildContentFromScenario(scenario: WritingScenarioDto): PaperBookletContent {
  return {
    title: scenario.title,
    professionLabel: WRITING_PROFESSION_LABELS[scenario.profession] ?? scenario.profession,
    writerRole: null,
    todayDate: null,
    taskPromptMarkdown: scenario.taskPromptMarkdown ?? null,
    fixedInstructions: scenario.fixedInstructions ?? [],
    wordGuideMin: scenario.wordGuideMin ?? 180,
    wordGuideMax: scenario.wordGuideMax ?? 200,
  };
}

/**
 * The reading-window overlay's body renders `<WritingStimulus scenario>`, which
 * only reads `stimulusPdfDownloadPath` (→ real PDF) or the case-note text
 * fields (→ printed fallback). When the route resolves only to an authored task
 * we synthesise a minimal scenario-shaped object from it rather than firing a
 * second `getWritingScenario` request: the task already carries everything the
 * stimulus needs, and a task id is NOT guaranteed to also resolve as a scenario
 * id (the task lookup is a distinct admin endpoint). This keeps a single load
 * path that works whether the id resolves to a task or a scenario.
 */
function scenarioFromTask(task: WritingTaskDto): WritingScenarioDto {
  return {
    id: task.id,
    title: task.title,
    letterType: task.letterType,
    profession: task.profession,
    subDiscipline: null,
    topics: [],
    difficulty: task.difficulty,
    caseNotesStructured: [],
    isDiagnostic: false,
    status: task.status,
    createdAt: task.createdAt,
    updatedAt: task.updatedAt,
    internalCode: task.internalCode,
    taskPromptMarkdown: task.taskPromptMarkdown,
    writerRole: task.writerRole,
    todayDate: task.todayDate,
    fixedInstructions: task.fixedInstructions ?? [],
    wordGuideMin: task.wordGuideMin,
    wordGuideMax: task.wordGuideMax,
    readingTimeSeconds: task.readingTimeSeconds,
    writingTimeSeconds: task.writingTimeSeconds,
    simulationModes: task.simulationModes,
    markingMode: task.markingMode,
    stimulusPdfMediaAssetId: task.stimulusPdfMediaAssetId ?? null,
    stimulusPdfDownloadPath: task.stimulusPdfDownloadPath ?? null,
  };
}

/**
 * PAPER-mode visual exam simulation (spec §9).
 *
 * The route param `[id]` is resolved leniently to match how the learner can
 * arrive here:
 *   1. As a MOCK SESSION id (preferred — same strict lifecycle as the
 *      computer-mode mock: reading→writing transition recorded server-side via
 *      `beginWritingMockWriting`, submit via `submitWritingMock`). This is the
 *      path used when paper mode is launched from the mocks list.
 *   2. As a SCENARIO/TASK id (direct launch). We then create a paper-mode
 *      submission with `createWritingSubmission` on submit and drive the timer
 *      locally (client-only deadline; no server session to anchor against).
 *
 * Either way the visual presentation is the booklet; only the data plumbing
 * differs, mirroring the existing computer-mode mock session page. Timing is
 * deadline-anchored in BOTH cases: the mock path anchors to the server's
 * reading-phase end; the direct path anchors to a deadline fixed when the
 * reading window opens. `WritingReadingWindowOverlay` shows the real PDF during
 * reading (exam simulation — `allowSkip={false}`, no early start).
 */
export default function WritingPaperSessionPage() {
  const t = useTranslations();
  const tRef = useRef(t);
  tRef.current = t;
  const params = useParams<{ id: string }>();
  const routeId = String(params?.id ?? '');

  // Resolution: which kind of id did we get?
  const [resolution, setResolution] = useState<'pending' | 'mock' | 'scenario'>('pending');
  const [session, setSession] = useState<WritingMockSessionDto | null>(null);
  const [scenarioId, setScenarioId] = useState<string | null>(null);
  const [content, setContent] = useState<PaperBookletContent | null>(null);
  // Scenario-shaped object feeding the reading-window overlay's stimulus body.
  // Resolved from the real scenario when available, else synthesised from the
  // authored task (see scenarioFromTask).
  const [scenario, setScenario] = useState<WritingScenarioDto | null>(null);

  const [phase, setPhase] = useState<WritingPhase>('reading');
  // Deadline-anchored countdowns (wall-clock epoch ms). Survive tab-backgrounding
  // and refresh — `useDeadlineCountdown` re-derives whole seconds on every tick
  // and on tab refocus. Null until the session/window is resolved.
  const [readingDeadlineMs, setReadingDeadlineMs] = useState<number | null>(null);
  const [writingDeadlineMs, setWritingDeadlineMs] = useState<number | null>(null);
  // Writing seconds still owed when the reading window ends (direct launch).
  const writingRemainingRef = useRef(WRITING_WINDOW_SECONDS);

  const [text, setText] = useState('');
  const [wordCount, setWordCount] = useState(0);
  const [submitting, setSubmitting] = useState(false);
  const [submitted, setSubmitted] = useState(false);
  const [submissionId, setSubmissionId] = useState<string | null>(null);
  // True once the letter is accepted and the backend is grading it. Freezes the
  // editor + timer and drives the grading/progress overlay until the grade lands.
  const [grading, setGrading] = useState(false);
  const [gradingFailed, setGradingFailed] = useState(false);
  // Latest polled submission: drives the delayed / failed / credits / manual states.
  const [gradedSubmission, setGradedSubmission] = useState<WritingSubmissionDto | null>(null);
  const [gradingTimedOut, setGradingTimedOut] = useState(false);
  const [retrying, setRetrying] = useState(false);
  const [retryError, setRetryError] = useState<string | null>(null);
  const [pollRound, setPollRound] = useState(0);
  const [error, setError] = useState<string | null>(null);
  // Draft restore + sync: the answer stays locked until the saved letter is known.
  const [userId] = useState(() => loadStoredSession()?.currentUser?.userId ?? 'anonymous');
  const [baseline, setBaseline] = useState<DraftSyncBaseline | null>(null);
  const [insufficientCreditsMessage, setInsufficientCreditsMessage] = useState<string | null>(null);
  // Case Notes highlights, lifted so they persist across the reading window and
  // the booklet writing view, and so the page can save/restore them per scenario.
  const [pdfHighlights, setPdfHighlights] = useState<Record<number, Highlight[]>>({});
  const highlightsRef = useRef<Record<number, Highlight[]>>({});
  highlightsRef.current = pdfHighlights;
  const lastSavedHighlightsRef = useRef<string>(serializeHighlights({}));

  const startedAtRef = useRef<number>(Date.now());
  // Guard so the reading→writing transition runs at most once: the reading
  // countdown's null-guarded onZero AND the overlay's onAutoClose both fire on
  // the same tick at reading 0:00. Only the first proceeds (mirrors the mock
  // page's begin-writing idempotency).
  const beganWritingRef = useRef(false);
  // Mirror latest content into refs for the timer-expiry auto-submit, which
  // must not re-bind on every keystroke.
  const textRef = useRef('');
  const wordCountRef = useRef(0);
  textRef.current = text;
  wordCountRef.current = wordCount;

  // ── Load + resolve the route id ───────────────────────────────────────────
  useEffect(() => {
    if (!routeId) return;
    let cancelled = false;

    // Resolve the booklet content AND the overlay's scenario object from the
    // richest source: prefer the enriched authored task, fall back to the plain
    // scenario.
    const loadTaskOrScenarioContent = async (id: string) => {
      const task = await getWritingTask(id).catch(() => null);
      if (cancelled) return;
      if (task) {
        setContent(buildContentFromTask(task));
        setScenario(scenarioFromTask(task));
        return;
      }
      const scenarioDto = await getWritingScenario(id).catch(() => null);
      if (cancelled) return;
      if (scenarioDto) {
        setContent(buildContentFromScenario(scenarioDto));
        setScenario(scenarioDto);
      }
    };

    const run = async () => {
      // 1) Try mock session first.
      const mock = await getWritingMockSession(routeId).catch(() => null);
      if (cancelled) return;

      if (mock) {
        setResolution('mock');
        setSession(mock);
        setScenarioId(mock.scenarioId);
        setSubmissionId(mock.submissionId);
        if (mock.status === 'writing') {
          setPhase('writing');
          // Already begun on the server — the transition must not begin again.
          beganWritingRef.current = true;
          setWritingDeadlineMs(
            mock.readingPhaseEndedAt
              ? new Date(mock.readingPhaseEndedAt).getTime() + WRITING_WINDOW_SECONDS * 1000
              : Date.now() + mock.writingSecondsRemaining * 1000,
          );
          startedAtRef.current = mock.readingPhaseEndedAt
            ? new Date(mock.readingPhaseEndedAt).getTime()
            : Date.now();
        } else if (mock.status === 'submitted' || mock.status === 'abandoned') {
          setPhase('completed');
          setSubmitted(mock.status === 'submitted');
          // A refresh while the letter is grading keeps watching the grade.
          if (mock.status === 'submitted' && mock.submissionId) setGrading(true);
        } else {
          setPhase('reading');
          setReadingDeadlineMs(Date.now() + mock.readingSecondsRemaining * 1000);
        }
        if (mock.scenarioId) await loadTaskOrScenarioContent(mock.scenarioId);
        return;
      }

      // 2) Treat the id as a scenario/task id (direct launch). Debit first so
      //    a learner without enough AI credits never sees the paper content.
      //    Mocks stay on their own human-graded path above.
      const eligibility = await checkWritingScenarioEligibility(routeId);
      if (cancelled) return;
      showCreditFeedback(eligibility?.feedbackMessage);
      setResolution('scenario');
      setScenarioId(routeId);
      setPhase('reading');
      setReadingDeadlineMs(Date.now() + WRITING_READING_WINDOW_SECONDS * 1000);
      await loadTaskOrScenarioContent(routeId);
    };

    void run().catch((err) => {
      if (!cancelled) {
        if (isInsufficientCreditsError(err)) {
          setInsufficientCreditsMessage(readInsufficientCreditsMessage(err));
          return;
        }
        // Addendum Rev8 §16: never surface a raw server message; the task
        // content is not shown when the start/eligibility check fails.
        setError(toCandidateSafeWritingErrorMessage(err, tRef.current('writing.paper.error.load')));
      }
    });

    return () => {
      cancelled = true;
    };
  }, [routeId]);

  // ── Draft restore + sync ──────────────────────────────────────────────────
  // The saved letter (server + this device) is restored before the answer
  // unlocks. For a mock session only a draft saved during THIS session counts
  // (an older mock of the same task never pre-fills a fresh one). A failed load
  // keeps the answer locked and retries — never "blank + overwrite".
  const sessionStartedAt = session?.startedAt;
  const sessionDone = session?.status === 'submitted' || session?.status === 'abandoned';
  const [draftAttempt, setDraftAttempt] = useState(0);
  useEffect(() => {
    if (resolution === 'pending' || !scenarioId || sessionDone) return;
    let cancelled = false;
    let retryTimer: number | undefined;
    const started = resolution === 'mock' ? Date.parse(sessionStartedAt ?? '') : Number.NaN;
    const isThisAttempt = (savedAt: number) => !Number.isFinite(started) || savedAt >= started;
    void getWritingDraftV2(scenarioId, 'mock')
      .then((draft) => {
        if (cancelled) return;
        const shadowKey = draftShadowKey(userId, scenarioId, 'mock');
        const active =
          draft && draft.status !== 'submitted' && isThisAttempt(Date.parse(draft.lastSavedAt)) ? draft : null;
        let shadow = readDraftShadow(shadowKey);
        if (shadow && (draft?.status === 'submitted' || !isThisAttempt(shadow.savedAt))) {
          clearDraftShadow(shadowKey);
          shadow = null;
        }
        const restored = reconcileDraft(active, shadow);
        const words = restored.wordCount || countLetterWords(restored.text);
        // Direct launch: the clock is this page's own, so it resumes from the
        // seconds left at the last save (paused while away). A mock session's
        // clock stays server-owned.
        if (resolution === 'scenario' && restored.phase) {
          const now = Date.now();
          const reading = Math.max(0, Math.min(WRITING_READING_WINDOW_SECONDS, restored.readingSecondsRemaining ?? WRITING_READING_WINDOW_SECONDS));
          const writing = Math.max(0, Math.min(WRITING_WINDOW_SECONDS, restored.writingSecondsRemaining ?? WRITING_WINDOW_SECONDS));
          writingRemainingRef.current = writing;
          if (restored.phase === 'writing') {
            beganWritingRef.current = true;
            setWritingDeadlineMs(now + writing * 1000);
            setPhase('writing');
          } else {
            setReadingDeadlineMs(now + reading * 1000);
          }
        }
        setText(restored.text);
        setWordCount(words);
        setError(null);
        setBaseline({
          text: restored.text,
          wordCount: words,
          // A fresh attempt has nothing to save until the learner types.
          serverText: active ? restored.serverText : restored.source === 'device' ? null : '',
          version: active ? restored.version : (draft?.version ?? 0),
          conflict: restored.conflict,
        });
      })
      .catch(() => {
        if (cancelled) return;
        setError(tRef.current('writing.paper.error.load'));
        retryTimer = window.setTimeout(() => setDraftAttempt((n) => n + 1), 5000);
      });
    return () => {
      cancelled = true;
      window.clearTimeout(retryTimer);
    };
  }, [resolution, scenarioId, sessionDone, sessionStartedAt, userId, draftAttempt]);

  // The direct-launch clock is saved with every draft save (and a 10 s
  // heartbeat) so reopening resumes it; a mock session's clock is server-owned.
  const phaseRef = useRef(phase);
  phaseRef.current = phase;
  const readingDeadlineRef = useRef(readingDeadlineMs);
  readingDeadlineRef.current = readingDeadlineMs;
  const writingDeadlineRef = useRef(writingDeadlineMs);
  writingDeadlineRef.current = writingDeadlineMs;
  const getClock = useCallback((): DraftClockSnapshot => {
    const now = Date.now();
    const left = (deadline: number | null) => (deadline == null ? 0 : Math.max(0, Math.ceil((deadline - now) / 1000)));
    if (phaseRef.current === 'reading') {
      return {
        phase: 'reading',
        readingSecondsRemaining: left(readingDeadlineRef.current),
        writingSecondsRemaining: writingRemainingRef.current,
        timeSpentSeconds: 0,
      };
    }
    const writing = phaseRef.current === 'writing' ? left(writingDeadlineRef.current) : 0;
    return { phase: 'writing', readingSecondsRemaining: 0, writingSecondsRemaining: writing, timeSpentSeconds: WRITING_WINDOW_SECONDS - writing };
  }, []);
  const ownClock = resolution === 'scenario';
  const sync = useWritingDraftSync({
    scenarioId: scenarioId ?? '',
    mode: 'mock',
    userId,
    baseline,
    getClock: ownClock ? getClock : undefined,
    heartbeatMs: ownClock && !submitted ? 10_000 : null,
  });
  const { update: updateDraft, flush: flushDraft, discard: discardDraft } = sync;

  // The focus header's Back/Dashboard confirm while the letter is being written
  // (same window as the beforeunload guard below).
  useFocusExitGuard({
    live: phase === 'writing' && !submitted,
    description: 'Your letter is saved as you go and your writing time keeps running. You can come back and continue where you left off.',
  });

  // ── beforeunload guard while actively writing (strict) ────────────────────
  useEffect(() => {
    if (phase !== 'writing' || submitted) return;
    const handler = (e: BeforeUnloadEvent) => {
      e.preventDefault();
      e.returnValue = '';
    };
    window.addEventListener('beforeunload', handler);
    return () => window.removeEventListener('beforeunload', handler);
  }, [phase, submitted]);

  // A mock session has its own results page; a direct launch is a regular submission.
  const resultsHref =
    resolution === 'mock' && session?.id
      ? `/writing/mocks/session/${encodeURIComponent(session.id)}/results`
      : submissionId
        ? `/writing/submissions/${encodeURIComponent(submissionId)}/results`
        : '/writing';

  // ── Case Notes highlights: load once per scenario, autosave (debounced) ────
  useEffect(() => {
    if (!scenarioId) return;
    let cancelled = false;
    void getWritingHighlights(scenarioId)
      .then((hl) => {
        if (cancelled || !hl?.highlightsJson) return;
        const parsed = parseHighlights(hl.highlightsJson);
        setPdfHighlights(parsed);
        lastSavedHighlightsRef.current = serializeHighlights(parsed);
      })
      .catch(() => {});
    return () => {
      cancelled = true;
    };
  }, [scenarioId]);

  useEffect(() => {
    if (!scenarioId || submitted) return;
    const json = serializeHighlights(pdfHighlights);
    if (json === lastSavedHighlightsRef.current) return;
    const timer = window.setTimeout(() => {
      lastSavedHighlightsRef.current = json;
      void putWritingHighlights(scenarioId, json).catch(() => {});
    }, 800);
    return () => window.clearTimeout(timer);
  }, [pdfHighlights, scenarioId, submitted]);

  // ── Autosave — page owns the network write (elapsed-time bookkeeping) ──────
  // The booklet's 5 s cadence just asks the sync engine to save now.
  const handleAutosave = useCallback(() => flushDraft(), [flushDraft]);

  const handleContentChange = useCallback(
    (next: string, words: number) => {
      setText(next);
      setWordCount(words);
      updateDraft(next, words);
    },
    [updateDraft],
  );

  // ── Phase transition: reading → writing ───────────────────────────────────
  // Idempotent: the reading countdown's onZero AND the overlay's onAutoClose
  // both fire on the same tick at reading 0:00. Only the first proceeds. For the
  // mock path this records the transition server-side; for the direct path it
  // simply anchors the writing deadline locally.
  const beginWriting = useCallback(() => {
    if (beganWritingRef.current) return;
    beganWritingRef.current = true;
    // Provisional deadline BEFORE flipping phase so the writing countdown never
    // momentarily reads 0:00 during the begin-writing round-trip; corrected from
    // the server response below for the mock path.
    // Direct launch: the writing seconds restored with the draft (a full window otherwise).
    setWritingDeadlineMs(Date.now() + writingRemainingRef.current * 1000);
    startedAtRef.current = Date.now();
    setPhase('writing');
    if (resolution === 'mock' && session?.id) {
      void beginWritingMockWriting(session.id)
        .then((updated) => {
          setSession(updated);
          // Anchor the writing countdown to the server's reading-phase end so it
          // stays accurate across refresh/backgrounding.
          setWritingDeadlineMs(
            updated.readingPhaseEndedAt
              ? new Date(updated.readingPhaseEndedAt).getTime() + WRITING_WINDOW_SECONDS * 1000
              : Date.now() + updated.writingSecondsRemaining * 1000,
          );
          startedAtRef.current = updated.readingPhaseEndedAt
            ? new Date(updated.readingPhaseEndedAt).getTime()
            : Date.now();
        })
        .catch((err) => {
          setError(err instanceof Error ? err.message : t('writing.paper.error.startWriting'));
        });
    }
  }, [resolution, session?.id, t]);

  // ── Submit ────────────────────────────────────────────────────────────────
  const doSubmit = useCallback(
    async (submitText: string, submitWords: number) => {
      if (submitted || submitting) return;
      setSubmitting(true);
      setError(null);
      const elapsed = Math.round((Date.now() - startedAtRef.current) / 1000);
      // One key per submit action; same-attempt collapsing lives server-side.
      const idempotencyKey = createSubmitIdempotencyKey();
      try {
        if (resolution === 'mock' && session?.id) {
          const result = await submitWritingMock(session.id, {
            letterContent: submitText,
            wordCount: submitWords,
            timeSpentSeconds: elapsed,
          });
          setSubmissionId(result.id ?? null);
          // Seeds the release countdown (15:00) before the first poll lands.
          setGradedSubmission(result ?? null);
        } else if (scenarioId) {
          const result = await createWritingSubmission({
            scenarioId,
            mode: 'mock',
            letterContent: submitText,
            wordCount: submitWords,
            timeSpentSeconds: elapsed,
            inputSource: 'editor',
            caseNoteHighlightsJson: serializeHighlights(highlightsRef.current),
            idempotencyKey,
          });
          setSubmissionId(result.id ?? null);
          setGradedSubmission(result ?? null);
        }
        // Freeze in place — do NOT navigate away. The letter is now being
        // graded; show the grading state and poll until the result is ready.
        discardDraft();
        setSubmitted(true);
        setGrading(true);
        setGradingFailed(false);
        setPhase('completed');
      } catch (err) {
        const code = (err as { code?: string } | null)?.code;
        // A 409 'already being graded' / a transient 429 are NOT hard failures —
        // the letter was accepted and is being graded. Keep the grading state and
        // let the poller surface the result; never bounce the candidate back to an
        // editable, re-submittable letter.
        if (code === 'writing_rubric_already_in_progress' || (err as { status?: number } | null)?.status === 429) {
          setSubmitted(true);
          setGrading(true);
          setGradingFailed(false);
          setPhase('completed');
          return;
        }
        setError(toCandidateSafeWritingErrorMessage(err, t('writing.paper.error.submit')));
        setSubmitting(false);
      }
    },
    [submitted, submitting, resolution, session?.id, scenarioId, t, discardDraft],
  );

  const online = sync.online;
  const handleSubmit = useCallback(() => {
    // A locally queued submit would not exist on the server: block it offline.
    if (!online) {
      setError(t('writing.practice.session.draft.offlineSubmit'));
      return;
    }
    void doSubmit(textRef.current, wordCountRef.current);
  }, [doSubmit, online, t]);

  // ── Grading: watch the submission until the grade lands, then go to results ──
  // The letter is accepted once; the server grades it and retries by itself.
  // The page watches for up to 60 minutes (queued/grading), then hands over to
  // Past submissions. A failed grade offers Retry on the SAME record — never a
  // new submission. A transient error while polling is not a failure.
  const resultsHrefRef = useRef(resultsHref);
  resultsHrefRef.current = resultsHref;
  useEffect(() => {
    if (!grading || !submissionId) return;
    let cancelled = false;
    let timer: number | undefined;
    const startedAt = Date.now();
    let latest: WritingSubmissionDto | null = null;
    const tick = async () => {
      try {
        const sub = await getWritingSubmission(submissionId);
        if (cancelled) return;
        latest = sub;
        setGradedSubmission(sub);
        const status = (sub as { status?: string } | null)?.status;
        // Effective status: a finished letter reads 'grading' until the server releases it.
        if (isReleased(sub) || status === 'completed') {
          setGrading(false);
          window.location.assign(resultsHrefRef.current);
          return;
        }
        if (status === 'failed' || status === 'cancelled') {
          setGrading(false);
          setGradingFailed(true);
          return;
        }
      } catch {
        // transient network / 429 while grading — keep polling
      }
      if (cancelled) return;
      const elapsed = Date.now() - startedAt;
      if (elapsed >= GRADING_POLL_LIMIT_MS) {
        setGrading(false);
        setGradingTimedOut(true);
        setGradingFailed(true);
        return;
      }
      // Release-aware pace: sparse while a finished letter is held, landing just after its release time.
      timer = window.setTimeout(() => void tick(), releasePollDelayMs(latest, elapsed < 2 * 60_000 ? 4000 : 15_000));
    };
    timer = window.setTimeout(() => void tick(), 2500);
    return () => {
      cancelled = true;
      window.clearTimeout(timer);
    };
  }, [grading, submissionId, pollRound]);

  const retryGrading = useCallback(async () => {
    if (!submissionId || retrying) return;
    setRetrying(true);
    setRetryError(null);
    try {
      const retried = await retryWritingGrade(submissionId);
      setGradingFailed(false);
      setGradingTimedOut(false);
      // Same submission, same release anchor: the countdown carries on, it never restarts.
      setGradedSubmission(retried ?? null);
      setGrading(true);
      setPollRound((n) => n + 1);
    } catch (err) {
      setRetryError(toCandidateSafeWritingErrorMessage(err, t('writing.paper.grading.retryFailed')));
    } finally {
      setRetrying(false);
    }
  }, [submissionId, retrying, t]);

  // Auto-submit + lock when the writing window expires.
  const expiredRef = useRef(false);
  const handleWritingExpired = useCallback(() => {
    if (submitted || expiredRef.current) return;
    expiredRef.current = true;
    void doSubmit(textRef.current, wordCountRef.current);
  }, [submitted, doSubmit]);

  // Deadline-anchored seconds. Both hooks run unconditionally (rules of hooks);
  // each gated to its active phase so only the live one counts. The null-guarded
  // onZero fires only for a REAL elapsed deadline — never on the 0 a null
  // deadline yields before load or during the begin-writing round-trip — so
  // transitions cannot fire spuriously. WritingTimerV2 is display-only.
  const readingSeconds = useDeadlineCountdown(
    phase === 'reading' && !submitted ? readingDeadlineMs : null,
    { onZero: beginWriting },
  );
  const writingSeconds = useDeadlineCountdown(
    phase === 'writing' && !submitted ? writingDeadlineMs : null,
    { onZero: handleWritingExpired },
  );

  return (
    <>
      <InsufficientCreditsModal
        open={insufficientCreditsMessage !== null}
        message={insufficientCreditsMessage ?? ''}
        onClose={() => setInsufficientCreditsMessage(null)}
      />
      <PaperBookletSimulation
        attemptId={routeId}
        scenarioId={scenarioId}
        submissionId={submissionId}
        content={content}
        stimulus={{ downloadPath: scenario?.stimulusPdfDownloadPath ?? null }}
        phase={phase}
        readingSecondsRemaining={readingSeconds}
        writingSecondsRemaining={writingSeconds}
        loading={resolution === 'pending' || (!baseline && !submitted)}
        initialText={baseline?.text}
        error={error}
        submitted={submitted}
        submitting={submitting}
        resultsHref={resultsHref}
        onContentChange={handleContentChange}
        onSubmit={handleSubmit}
        onAutosave={handleAutosave}
        highlights={pdfHighlights}
        onHighlightsChange={setPdfHighlights}
      />

      {/* Forced full-screen reading window. Renders only during the reading
          phase; auto-closes into the writing view at 0:00. allowSkip is false —
          paper mode is always an exam simulation, so there is no early start. */}
      <WritingReadingWindowOverlay
        open={phase === 'reading' && !submitted && !!scenario}
        scenario={scenario}
        secondsRemaining={readingSeconds}
        totalSeconds={scenario?.readingTimeSeconds ?? WRITING_READING_WINDOW_SECONDS}
        allowSkip={false}
        onAutoClose={beginWriting}
        title={scenario?.title ?? content?.title ?? undefined}
        highlights={pdfHighlights}
        onHighlightsChange={setPdfHighlights}
      />

      {/* Grading overlay: once the letter is accepted, the editor + timer are
          frozen and the candidate watches a clear grading state until the result
          is ready. The poller navigates to results automatically. A genuine
          failure shows a candidate-friendly message with a way through. */}
      {grading ? (
        <div
          data-testid="writing-paper-grading"
          data-state={gradedSubmission?.autoRetrying ? 'delayed' : 'grading'}
          className="fixed inset-0 z-50 flex flex-col items-center justify-center bg-background/95 backdrop-blur-sm"
        >
          <div className="h-10 w-10 animate-spin rounded-full border-4 border-primary border-t-transparent" aria-hidden="true" />
          {/* The live region is the text only: the ticking timer below must not be announced every second. */}
          <div role="status" aria-live="polite">
            <p className="mt-6 max-w-md px-6 text-center text-lg font-medium">
              {gradedSubmission?.autoRetrying
                ? t('writing.paper.grading.delayedTitle')
                : t('writing.paper.grading.submittedTitle')}
            </p>
            {/* With a release time the countdown below carries the notice; before the
                first response the notice stands alone, and without one (no hold) the
                page does not promise 15 minutes. */}
            {gradedSubmission?.autoRetrying ? (
              <p className="mt-2 max-w-md px-6 text-center text-sm text-muted">{t('writing.paper.grading.delayedBody')}</p>
            ) : !gradedSubmission ? (
              <p className="mt-2 max-w-md px-6 text-center text-sm text-muted">{t('writing.release.notice')}</p>
            ) : !gradedSubmission.releaseAt ? (
              <p className="mt-2 max-w-md px-6 text-center text-sm text-muted">{t('writing.release.finalising')}</p>
            ) : null}
          </div>
          <WritingReleaseCountdown
            className="mt-4 max-w-md px-6 text-center"
            releaseAt={gradedSubmission?.releaseAt}
            serverNow={gradedSubmission?.serverNow}
            releaseState={gradedSubmission?.releaseState}
            showNotice={gradedSubmission?.autoRetrying !== true}
          />
        </div>
      ) : null}

      {gradingFailed && !grading ? (
        <div
          role="alert"
          data-testid="writing-grading-failed"
          className="fixed inset-0 z-50 flex flex-col items-center justify-center bg-background/95 backdrop-blur-sm"
        >
          {(() => {
            const code = gradedSubmission?.failureCode ?? null;
            const manual = (code !== null && NOT_RETRYABLE.has(code)) || gradedSubmission?.canRetry === false;
            const credits = code === 'credits_insufficient';
            const title = gradingTimedOut
              ? t('writing.paper.grading.timedOutTitle')
              : manual
                ? t('writing.paper.grading.manualTitle')
                : credits
                  ? t('writing.paper.grading.creditsTitle')
                  : t('writing.paper.grading.failedTitle');
            const body = gradingTimedOut
              ? t('writing.paper.grading.timedOutBody')
              : manual
                ? t('writing.paper.grading.manualBody')
                : credits
                  ? t('writing.paper.grading.creditsBody')
                  : t('writing.paper.grading.failedBody');
            return (
              <>
                <p className="max-w-md px-6 text-center text-lg font-medium">{title}</p>
                <p className="mt-2 max-w-md px-6 text-center text-sm text-muted">{body}</p>
                {retryError ? <p className="mt-3 max-w-md px-6 text-center text-sm text-danger-strong">{retryError}</p> : null}
                <div className="mt-6 flex flex-wrap justify-center gap-2 px-6">
                  {credits ? (
                    <Button asChild variant="outline">
                      <a href="/ai-packages">{t('writing.paper.grading.buyCredits')}</a>
                    </Button>
                  ) : null}
                  {!manual && submissionId ? (
                    <Button
                      data-testid="writing-grading-retry"
                      loading={retrying}
                      onClick={() => void retryGrading()}
                    >
                      {retrying ? t('writing.paper.grading.retrying') : t('writing.paper.grading.retry')}
                    </Button>
                  ) : null}
                  {submissionId ? (
                    <Button asChild variant="outline">
                      <a href={`/writing/submissions/${encodeURIComponent(submissionId)}`}>
                        {t('writing.paper.grading.viewLetter')}
                      </a>
                    </Button>
                  ) : null}
                </div>
              </>
            );
          })()}
        </div>
      ) : null}
    </>
  );
}
