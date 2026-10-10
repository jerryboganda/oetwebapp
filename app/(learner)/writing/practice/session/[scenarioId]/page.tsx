'use client';

import { useCallback, useEffect, useRef, useState } from 'react';
import Link from 'next/link';
import { useParams, useRouter } from 'next/navigation';
import { useTranslations } from 'next-intl';
import { PenTool } from 'lucide-react';
import { InlineAlert } from '@/components/ui/alert';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Modal } from '@/components/ui/modal';
import { useFocusExitGuard } from '@/components/layout/focus-exit';
import { WritingEditorV2 } from '@/components/domain/writing/WritingEditorV2';
import { WritingTimerV2 } from '@/components/domain/writing/WritingTimerV2';
import { WordCounter } from '@/components/domain/writing/WordCounter';
import { SubmitBar } from '@/components/domain/writing/SubmitBar';
import { DraftConflictNotice, DraftSaveStatus } from '@/components/domain/writing/DraftSaveStatus';
import { WritingStimulus } from '@/components/domain/writing/WritingStimulus';
import type { Highlight } from '@/components/domain/writing/WritingStimulusViewer';
import { WritingReadingWindowOverlay } from '@/components/domain/writing/WritingReadingWindowOverlay';
import {
  InsufficientCreditsModal,
  isInsufficientCreditsError,
  readInsufficientCreditsMessage,
} from '@/components/domain/InsufficientCreditsModal';
import {
  useWritingDraftSync,
  type DraftClockSnapshot,
  type DraftSyncBaseline,
} from '@/hooks/use-writing-draft-sync';
import { loadStoredSession } from '@/lib/auth-storage';
import {
  checkWritingScenarioEligibility,
  createWritingSubmission,
  getWritingDraftV2,
  getWritingHighlights,
  getWritingScenario,
  getWritingSubmission,
  putWritingHighlights,
} from '@/lib/writing/api';
import {
  clearDraftShadow,
  draftShadowKey,
  readDraftShadow,
  reconcileDraft,
  type ReconciledDraft,
} from '@/lib/writing/draft-sync';
import { countLetterWords } from '@/lib/writing/letter-text';
import { createSubmitIdempotencyKey, toCandidateSafeWritingErrorMessage } from '@/lib/writing/submit-keys';
import { showCreditFeedback } from '@/lib/credit-feedback';
import { parseHighlights, serializeHighlights } from '@/lib/writing/highlights';
import { useDeadlineCountdown } from '@/lib/writing/useCountdown';
import { WRITING_READING_WINDOW_SECONDS, WRITING_WINDOW_SECONDS } from '@/lib/writing/workflow';
import type {
  WritingEditorMode,
  WritingScenarioDto,
  WritingSubmissionStatus,
} from '@/lib/writing/types';

type ScenarioMode = Extract<WritingEditorMode, 'practice' | 'coached'>;

// Catalogue codes with a candidate label (writing.practice.library.letterType.*); any other code is never shown.
const LETTER_TYPE_CODES: readonly string[] = ['LT-RR', 'LT-UR', 'LT-DG', 'LT-TR', 'LT-NM', 'LT-OT'];

/** The task's authored value when it is a sane positive number, else the exam default. */
function positiveOr(value: number | null | undefined, fallback: number): number {
  return typeof value === 'number' && Number.isFinite(value) && value > 0 ? value : fallback;
}

/** Waits between time-up auto-submit attempts; the last value repeats. */
const AUTO_SUBMIT_RETRY_MS = [5_000, 15_000, 30_000, 60_000];
/** The server is told the remaining time at least this often while the page is open. */
const CLOCK_HEARTBEAT_MS = 10_000;
/** A submitted attempt whose grade is still running (or failed) belongs on the grading page. */
const GRADING_STATUSES: ReadonlySet<WritingSubmissionStatus> = new Set(['queued', 'preflight', 'grading', 'failed']);

/**
 * A submit failure worth retrying with the SAME idempotency key: no connection,
 * timeout, throttling, a server error, or grading already running for this
 * attempt (the retry collapses onto it). Credits and validation refusals are final.
 */
function isRetryableSubmitError(err: unknown): boolean {
  const { status, code } = (err ?? {}) as { status?: number; code?: string };
  if (status === 402 || code === 'ai_credits_insufficient') return false;
  if (code === 'writing_rubric_already_in_progress') return true;
  return status === undefined || status === 0 || status === 408 || status === 429 || status >= 500;
}

function formatClock(totalSeconds: number): string {
  const safe = Math.max(0, Math.floor(totalSeconds));
  return `${Math.floor(safe / 60).toString().padStart(2, '0')}:${(safe % 60).toString().padStart(2, '0')}`;
}

/**
 * Pause-while-away clock: the seconds LEFT at the last save, clamped to the
 * task's windows. A letter saved before clocks were stored resumes with one
 * fresh writing window; a brand-new attempt starts with the reading window.
 */
function startingClock(restored: ReconciledDraft, resumed: boolean, readingWindow: number, writingWindow: number) {
  const clamp = (value: number | null, max: number) => Math.min(max, Math.max(0, Math.round(value ?? max)));
  if (!restored.phase) {
    return resumed
      ? { phase: 'writing' as const, reading: 0, writing: writingWindow }
      : { phase: 'reading' as const, reading: readingWindow, writing: writingWindow };
  }
  return {
    phase: restored.phase,
    reading: restored.phase === 'reading' ? clamp(restored.readingSecondsRemaining, readingWindow) : 0,
    writing: clamp(restored.writingSecondsRemaining, writingWindow),
  };
}

export default function WritingPracticeSessionPage() {
  const t = useTranslations();
  const params = useParams<{ scenarioId: string }>();
  const router = useRouter();
  const routerRef = useRef(router);
  routerRef.current = router;
  const scenarioId = String(params?.scenarioId ?? '');
  // Scopes this device's draft copy to the signed-in account.
  const [userId] = useState(() => loadStoredSession()?.currentUser?.userId ?? 'anonymous');

  const [scenario, setScenario] = useState<WritingScenarioDto | null>(null);
  const mode: ScenarioMode = 'practice';
  const [content, setContent] = useState('');
  // What the editor mounts with (it reads it once); a new key remounts it with new text.
  const [editorText, setEditorText] = useState('');
  const [editorKey, setEditorKey] = useState(0);
  const [wordCount, setWordCount] = useState(0);
  const [baseline, setBaseline] = useState<DraftSyncBaseline | null>(null);
  const [resumed, setResumed] = useState<{ words: number; seconds: number } | null>(null);
  // Text replaced by "Use the other version", so the learner can take it back.
  const [previousText, setPreviousText] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [submitting, setSubmitting] = useState(false);
  const [noCreditsOpen, setNoCreditsOpen] = useState(false);
  const [insufficientCreditsMessage, setInsufficientCreditsMessage] = useState<string | null>(null);
  // A failed load is a real, recoverable state (Addendum Rev8 §16) — never a
  // permanent "Loading scenario…" with an empty task. Bumping `loadAttempt` re-runs it.
  const [loadError, setLoadError] = useState<{ cause: unknown } | null>(null);
  const [loadAttempt, setLoadAttempt] = useState(0);
  // Latest content for auto-submit (read through refs so typing never restarts timers).
  const contentRef = useRef('');
  contentRef.current = content;
  const wordCountRef = useRef(0);
  wordCountRef.current = wordCount;
  const scenarioRef = useRef<WritingScenarioDto | null>(null);
  scenarioRef.current = scenario;

  // ── Strict 45-minute exam clock, paused while away ─────────────────────────
  // 5 min forced reading (pad LOCKED) → 40 min writing → hard auto-submit. The
  // seconds left are saved with the draft (and on this device), so closing the
  // page — even the browser — pauses the clock and reopening resumes it.
  const [phase, setPhase] = useState<'reading' | 'writing' | 'completed'>('reading');
  const [readingDeadlineMs, setReadingDeadlineMs] = useState<number | null>(null);
  const [writingDeadlineMs, setWritingDeadlineMs] = useState<number | null>(null);
  const phaseRef = useRef(phase);
  phaseRef.current = phase;
  const readingDeadlineRef = useRef(readingDeadlineMs);
  readingDeadlineRef.current = readingDeadlineMs;
  const writingDeadlineRef = useRef(writingDeadlineMs);
  writingDeadlineRef.current = writingDeadlineMs;
  // Writing seconds still owed when the reading window ends.
  const writingRemainingRef = useRef(WRITING_WINDOW_SECONDS);
  // Yellow highlights, lifted here so they persist from the reading window into
  // the writing view (both render the same Case Notes PDF).
  const [pdfHighlights, setPdfHighlights] = useState<Record<number, Highlight[]>>({});
  // Latest highlights for auto-submit (timer expiry snapshots the current marks).
  const highlightsRef = useRef<Record<number, Highlight[]>>({});
  highlightsRef.current = pdfHighlights;
  // Last value persisted to the server — avoids redundant autosaves. Seeded to an
  // empty map so a scenario with no saved marks doesn't trigger a no-op save.
  const lastSavedHighlightsRef = useRef<string>(serializeHighlights({}));

  // Per-task timing and word guide from the authored task, exam defaults otherwise.
  const windowSeconds = positiveOr(scenario?.readingTimeSeconds, WRITING_READING_WINDOW_SECONDS);
  const writingWindowSeconds = positiveOr(scenario?.writingTimeSeconds, WRITING_WINDOW_SECONDS);
  const writingWindowRef = useRef(writingWindowSeconds);
  writingWindowRef.current = writingWindowSeconds;
  const wordGuideMin = positiveOr(scenario?.wordGuideMin, 180);
  const wordGuideMax = positiveOr(scenario?.wordGuideMax, 220);
  const wordTarget = wordGuideMax >= wordGuideMin ? { min: wordGuideMin, max: wordGuideMax } : { min: 180, max: 220 };

  /** The clock as it stands right now, for every draft save. */
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
    return {
      phase: 'writing',
      readingSecondsRemaining: 0,
      writingSecondsRemaining: writing,
      timeSpentSeconds: Math.max(0, writingWindowRef.current - writing),
    };
  }, []);

  const [submitted, setSubmitted] = useState(false);
  const sync = useWritingDraftSync({
    scenarioId,
    mode,
    userId,
    baseline,
    getClock,
    heartbeatMs: submitted || phase === 'completed' ? null : CLOCK_HEARTBEAT_MS,
  });
  const { flush: flushDraft, update: updateDraft } = sync;

  // The focus header's Back/Dashboard confirm until the attempt is over; once
  // it is submitted or the window closes, leaving costs nothing.
  useFocusExitGuard({
    live: !submitted && phase !== 'completed',
    description: 'Your draft is saved and your practice timer keeps running. You can come back and continue where you left off.',
  });

  // ── Load: draft first, then (only for a new attempt) eligibility ───────────
  // 1. Strict draft GET: only a 404 means "no draft" — a 5xx or a dropped
  //    connection is the Retry state, never "blank editor + overwrite".
  // 2. Submitted and still grading, or failed with Retry available → the
  //    grading page (a final failure with no Retry starts a new attempt).
  // 3. An active draft RESUMES without the eligibility call (the attempt was
  //    paid at task open). No draft, or a graded one ("Practice this again"),
  //    starts a new attempt: eligibility first — a learner without credits
  //    never sees the case notes — then the first save anchors the clock.
  useEffect(() => {
    if (!scenarioId) return;
    let cancelled = false;
    const shadowKey = draftShadowKey(userId, scenarioId, mode);
    const load = async () => {
      const draft = await getWritingDraftV2(scenarioId, mode);
      if (cancelled) return;
      if (draft?.status === 'submitted') {
        clearDraftShadow(shadowKey);
        if (draft.submissionId && draft.submissionStatus && GRADING_STATUSES.has(draft.submissionStatus)) {
          // A FINAL failure (no Retry: task_not_ready / manual_review /
          // letter_invalid) would loop the learner back to a dead end: like a
          // graded letter, it opens a new attempt instead.
          const finalFailure =
            draft.submissionStatus === 'failed'
            && (await getWritingSubmission(draft.submissionId)).canRetry === false;
          if (cancelled) return;
          if (!finalFailure) {
            routerRef.current.replace(`/writing/submissions/${encodeURIComponent(draft.submissionId)}/grading`);
            return;
          }
        }
      }
      const active = draft && draft.status !== 'submitted' ? draft : null;
      if (!active) {
        const eligibility = await checkWritingScenarioEligibility(scenarioId);
        if (cancelled) return;
        showCreditFeedback(eligibility?.feedbackMessage);
      }
      const [sc, hl] = await Promise.all([
        getWritingScenario(scenarioId),
        // Saved Case Notes highlights persist per (user, scenario) across attempts.
        getWritingHighlights(scenarioId).catch(() => null),
      ]);
      if (cancelled) return;

      const restored = reconcileDraft(active, readDraftShadow(shadowKey));
      const isResume = Boolean(active) || restored.source === 'device';
      const clock = startingClock(
        restored,
        isResume,
        positiveOr(sc.readingTimeSeconds, WRITING_READING_WINDOW_SECONDS),
        positiveOr(sc.writingTimeSeconds, WRITING_WINDOW_SECONDS),
      );
      const words = restored.wordCount || countLetterWords(restored.text);
      const now = Date.now();
      writingRemainingRef.current = clock.writing;
      setScenario(sc);
      setEditorText(restored.text);
      setContent(restored.text);
      setWordCount(words);
      setPhase(clock.phase);
      if (clock.phase === 'reading') setReadingDeadlineMs(now + clock.reading * 1000);
      else setWritingDeadlineMs(now + clock.writing * 1000);
      setResumed(isResume ? { words, seconds: clock.phase === 'reading' ? clock.reading : clock.writing } : null);
      setBaseline({
        text: restored.text,
        wordCount: words,
        serverText: restored.serverText,
        // "Practice this again" writes the new attempt on top of the submitted row's version.
        version: !active && draft ? (draft.version ?? null) : restored.version,
        conflict: restored.conflict,
      });
      if (hl?.highlightsJson) {
        const parsed = parseHighlights(hl.highlightsJson);
        setPdfHighlights(parsed);
        // Record what's already on the server so the autosave effect doesn't
        // immediately echo the just-loaded marks back.
        lastSavedHighlightsRef.current = serializeHighlights(parsed);
      }
    };
    void load().catch((err) => {
      if (cancelled) return;
      if (isInsufficientCreditsError(err)) {
        setInsufficientCreditsMessage(readInsufficientCreditsMessage(err));
        return;
      }
      setLoadError({ cause: err });
    });
    return () => {
      cancelled = true;
    };
  }, [scenarioId, mode, loadAttempt, userId]);

  // Retry re-runs eligibility too: it is idempotent on the attempt's
  // reference id, so a retry never charges a second credit.
  const retryLoad = useCallback(() => {
    setLoadError(null);
    setLoadAttempt((n) => n + 1);
  }, []);

  const describeLoadError = (cause: unknown): string => {
    const { code, status } = (cause ?? {}) as { code?: string; status?: number };
    if (code === 'writing_task_unavailable' || code === 'writing_scenario_not_found' || status === 404) {
      return t('writing.practice.session.loadError.unavailable');
    }
    if (code === 'writing_task_incomplete') {
      return t('writing.practice.session.loadError.incomplete');
    }
    return toCandidateSafeWritingErrorMessage(cause, t('writing.practice.session.error.load'));
  };

  const handleEditorChange = useCallback(
    (text: string, words: number) => {
      setContent(text);
      setWordCount(words);
      updateDraft(text, words);
    },
    [updateDraft],
  );

  const replaceEditorText = (text: string) => {
    setEditorText(text);
    setEditorKey((key) => key + 1);
    setContent(text);
    setWordCount(countLetterWords(text));
  };

  const switchToOtherVersion = () => {
    setPreviousText(contentRef.current);
    replaceEditorText(sync.takeServer());
  };

  const restorePreviousText = () => {
    if (previousText === null) return;
    replaceEditorText(previousText);
    updateDraft(previousText, countLetterWords(previousText));
    setPreviousText(null);
  };

  // ── Highlight autosave (reading + writing) ───────────────────────────────────
  // Persists Case Notes marks per (user, scenario) the moment they change, so
  // they survive refresh and pre-load on every future attempt. Debounced; skips
  // when nothing changed and once the exam is over.
  useEffect(() => {
    if (!scenarioId || phase === 'completed') return;
    const json = serializeHighlights(pdfHighlights);
    if (json === lastSavedHighlightsRef.current) return;
    const timer = window.setTimeout(() => {
      lastSavedHighlightsRef.current = json;
      void putWritingHighlights(scenarioId, json).catch(() => {
        /* best-effort */
      });
    }, 800);
    return () => window.clearTimeout(timer);
  }, [pdfHighlights, scenarioId, phase]);

  // Time-up auto-submit progress: `waiting` = a retryable failure, next try scheduled.
  const [timeUp, setTimeUp] = useState<'idle' | 'sending' | 'waiting' | 'stopped'>('idle');
  // Submitting while offline is blocked: a locally queued submit would not
  // exist on the server (Past submissions, another device).
  const canSubmit = phase === 'writing' && !submitting && sync.online;

  const helperText = sync.online
    ? t('writing.practice.session.helper.ready')
    : t('writing.practice.session.draft.offlineSubmit');

  // Set once a 429/409 single-retry has been spent for this mount, so an
  // already-in-flight grading attempt is waited on rather than hammered.
  const retriedAfterThrottleRef = useRef(false);

  // One send of the CURRENT letter. Callers mint one idempotency key per
  // logical submit and reuse it for every retry of it, so one Submit can never
  // open two paid grading workflows (the server collapses same-key resends).
  const sendLetter = useCallback(
    (idempotencyKey: string) => {
      const current = scenarioRef.current;
      if (!current) return Promise.reject(new Error('Scenario not loaded'));
      return createWritingSubmission({
        scenarioId: current.id,
        mode,
        letterContent: contentRef.current,
        wordCount: wordCountRef.current,
        timeSpentSeconds: getClock().timeSpentSeconds,
        inputSource: 'editor',
        caseNoteHighlightsJson: serializeHighlights(highlightsRef.current),
        idempotencyKey,
      });
    },
    [mode, getClock],
  );

  const discardDraft = sync.discard;
  const openGrading = useCallback(
    (submissionId: string) => {
      // The server consumed the draft; drop this device's copy and stop syncing.
      setSubmitted(true);
      discardDraft();
      router.push(`/writing/submissions/${encodeURIComponent(submissionId)}/grading`);
    },
    [discardDraft, router],
  );

  // Balance = 0 (spec §9): the AI grading credit pool is exhausted. Surface
  // a dedicated modal with a direct path to the AI Credits storefront rather
  // than a generic inline error. The draft autosaves, so nothing is lost.
  // All other failures render candidate-safe copy (never internal codes).
  const showSubmitFailure = useCallback(
    (err: unknown) => {
      const { code, status } = (err ?? {}) as { code?: string; status?: number };
      if (code === 'ai_credits_insufficient' || status === 402) {
        setNoCreditsOpen(true);
      } else {
        setError(toCandidateSafeWritingErrorMessage(err, t('writing.practice.session.error.submit')));
      }
    },
    [t],
  );

  const submitManually = useCallback(async () => {
    if (!canSubmit) return;
    setSubmitting(true);
    setError(null);
    const idempotencyKey = createSubmitIdempotencyKey();
    try {
      openGrading((await sendLetter(idempotencyKey)).id);
    } catch (err) {
      // One Submit must never surface "Too many requests" as its normal
      // outcome: a 429 (rate limiter tripped by a double-tap race) or a 409
      // (grading already in flight for this attempt) waits briefly and
      // retries ONCE with the same idempotency key, which the server
      // collapses onto the single in-flight submission. Anything else, or a
      // second failure, surfaces normally — the draft autosaves, so no work
      // is lost.
      const { code, status } = (err ?? {}) as { code?: string; status?: number };
      const throttleRetryable =
        code === 'rate_limited' ||
        status === 429 ||
        code === 'writing_rubric_already_in_progress' ||
        status === 409;
      if (throttleRetryable && !retriedAfterThrottleRef.current) {
        retriedAfterThrottleRef.current = true;
        await new Promise((resolve) => setTimeout(resolve, 2500));
        try {
          openGrading((await sendLetter(idempotencyKey)).id);
          return;
        } catch (retryErr) {
          showSubmitFailure(retryErr);
          setSubmitting(false);
          return;
        }
      }
      showSubmitFailure(err);
      setSubmitting(false);
    }
  }, [canSubmit, sendLetter, openGrading, showSubmitFailure]);

  const onSubmit = useCallback(() => {
    void submitManually();
  }, [submitManually]);

  // The time-up loop below runs once per expiry; it reads the latest submit
  // helpers through this ref instead of restarting whenever they change.
  const submitPathRef = useRef({ sendLetter, openGrading, showSubmitFailure });
  submitPathRef.current = { sendLetter, openGrading, showSubmitFailure };
  const submitNowRef = useRef<() => void>(() => {});

  // ── Phase transitions ───────────────────────────────────────────────────────
  const beganWritingRef = useRef(false);
  const handleReadingEnd = useCallback(() => {
    if (beganWritingRef.current) return;
    beganWritingRef.current = true;
    const deadline = Date.now() + writingRemainingRef.current * 1000;
    // Refs first, so the save below already records the writing phase.
    phaseRef.current = 'writing';
    writingDeadlineRef.current = deadline;
    setWritingDeadlineMs(deadline);
    setPhase('writing');
    flushDraft();
  }, [flushDraft]);

  // Deadline-anchored countdowns; each gated to its active phase.
  const readingSeconds = useDeadlineCountdown(
    phase === 'reading' ? readingDeadlineMs : null,
    { onZero: handleReadingEnd },
  );
  const writingSeconds = useDeadlineCountdown(
    phase === 'writing' ? writingDeadlineMs : null,
    { onZero: () => setPhase('completed') },
  );

  // Hard auto-submit when the 40-minute writing window expires: ONE logical
  // submit (one idempotency key) retried after 5/15/30/60 s and the moment the
  // connection returns, until it lands. A retryable failure never loops hot; a
  // credits/validation refusal stops and is shown.
  useEffect(() => {
    if (phase !== 'completed') return;
    let finished = false;
    let sending = false;
    let attempt = 0;
    let retryTimer: number | undefined;
    const idempotencyKey = createSubmitIdempotencyKey();
    const send = async () => {
      if (finished || sending) return;
      window.clearTimeout(retryTimer);
      sending = true;
      setTimeUp('sending');
      try {
        const submission = await submitPathRef.current.sendLetter(idempotencyKey);
        if (finished) return;
        finished = true;
        submitPathRef.current.openGrading(submission.id);
      } catch (err) {
        if (finished) return;
        if (isRetryableSubmitError(err)) {
          setTimeUp('waiting');
          const delay = AUTO_SUBMIT_RETRY_MS[Math.min(attempt, AUTO_SUBMIT_RETRY_MS.length - 1)];
          attempt += 1;
          retryTimer = window.setTimeout(() => void send(), delay);
        } else {
          finished = true;
          setTimeUp('stopped');
          submitPathRef.current.showSubmitFailure(err);
        }
      } finally {
        sending = false;
      }
    };
    submitNowRef.current = () => void send();
    const onOnline = () => void send();
    window.addEventListener('online', onOnline);
    void send();
    return () => {
      finished = true;
      window.clearTimeout(retryTimer);
      window.removeEventListener('online', onOnline);
    };
  }, [phase]);

  const readingActive = phase === 'reading';

  if (insufficientCreditsMessage) {
    return (
      <>
        <InsufficientCreditsModal
          open
          message={insufficientCreditsMessage}
          onClose={() => router.push('/writing')}
        />
      </>
    );
  }

  if (loadError) {
    return (
      <>
        <div className="mx-auto max-w-xl py-8">
          <InlineAlert
            variant="error"
            title={t('writing.practice.session.loadError.title')}
            action={
              <div className="flex flex-wrap gap-2">
                <Button size="sm" onClick={retryLoad}>
                  {t('writing.practice.session.loadError.retry')}
                </Button>
                <Button asChild size="sm" variant="outline">
                  <Link href="/writing/practice/library">{t('writing.practice.session.loadError.backToLibrary')}</Link>
                </Button>
              </div>
            }
          >
            {describeLoadError(loadError.cause)}
          </InlineAlert>
        </div>
      </>
    );
  }

  return (
    <>
      {/* Forced 5-minute reading window — non-skippable. Auto-closes into the
          writing view at 0:00. */}
      <WritingReadingWindowOverlay
        open={readingActive && !!scenario}
        scenario={scenario}
        secondsRemaining={readingSeconds}
        totalSeconds={windowSeconds}
        allowSkip={false}
        onAutoClose={handleReadingEnd}
        title={scenario?.title ?? undefined}
        highlights={pdfHighlights}
        onHighlightsChange={setPdfHighlights}
      />

      <div className="space-y-4 pb-32" aria-busy={!scenario}>
        <header
          className="flex flex-wrap items-center justify-between gap-3 rounded-2xl border border-border bg-surface p-4 shadow-sm"
          aria-label={t('writing.practice.session.controlsLabel')}
        >
          <div className="flex items-center gap-3">
            <PenTool className="h-5 w-5 text-amber-600" aria-hidden="true" />
            <div>
              <p className="eyebrow text-muted">{t('writing.practice.session.eyebrow')}</p>
              {/* Scenario title is OET-authored English content. */}
              <h1 className="text-base font-bold text-navy" dir="ltr">{scenario?.title ?? t('writing.practice.session.scenarioLoading')}</h1>
              <div className="mt-1 flex flex-wrap items-center gap-1">
                <Badge variant="info" size="sm">Practice mode</Badge>
                {scenario ? (
                  <>
                    {LETTER_TYPE_CODES.includes(scenario.letterType) ? (
                      <Badge variant="muted" size="sm">{t(`writing.practice.library.letterType.${scenario.letterType}`)}</Badge>
                    ) : null}
                    <Badge variant="info" size="sm" className="capitalize">{scenario.profession}</Badge>
                  </>
                ) : null}
                <span className="text-2xs font-medium text-muted">
                  {t('writing.practice.session.timing', {
                    reading: Math.round(windowSeconds / 60),
                    writing: Math.round(writingWindowSeconds / 60),
                  })}
                </span>
              </div>
            </div>
          </div>
          <div className="flex flex-wrap items-center gap-4">
            {baseline ? <DraftSaveStatus state={sync.state} /> : null}
            <WordCounter count={wordCount} target={wordTarget} ariaLabelPrefix="Letter length" />
            <WritingTimerV2
              phase={phase}
              readingSecondsRemaining={readingSeconds}
              writingSecondsRemaining={writingSeconds}
              strict
            />
          </div>
        </header>

        {resumed ? (
          <InlineAlert variant="info" live="polite" dismissible onDismiss={() => setResumed(null)} data-testid="writing-resume-banner">
            {t('writing.practice.session.resume.banner', {
              words: resumed.words,
              time: formatClock(resumed.seconds),
            })}
          </InlineAlert>
        ) : null}

        <DraftConflictNotice
          conflict={sync.conflict !== null}
          previousText={previousText}
          onKeepThis={sync.keepLocal}
          onUseOther={switchToOtherVersion}
          onRestorePrevious={restorePreviousText}
          onDismissPrevious={() => setPreviousText(null)}
        />

        {error ? <InlineAlert variant="error">{error}</InlineAlert> : null}

        {timeUp === 'waiting' ? (
          <InlineAlert
            variant="warning"
            data-testid="writing-time-up"
            action={
              <Button size="sm" onClick={() => submitNowRef.current()}>
                {t('writing.practice.session.timeUp.submitNow')}
              </Button>
            }
          >
            {t('writing.practice.session.timeUp.pending')}
          </InlineAlert>
        ) : null}

        <div className="grid gap-4 lg:grid-cols-2">
          {/* Case Notes PDF (with yellow highlighter) or text fallback. */}
          <section
            aria-label={t('writing.practice.session.caseNotesLabel')}
            className="min-h-[60vh] overflow-hidden rounded-2xl border border-border bg-surface"
            dir="ltr"
          >
            {scenario ? (
              <WritingStimulus
                scenario={scenario}
                locked={readingActive}
                title={scenario.title ?? undefined}
                highlights={pdfHighlights}
                onHighlightsChange={setPdfHighlights}
              />
            ) : (
              <p className="p-4 text-sm text-muted">{t('writing.practice.session.caseNotesLoading')}</p>
            )}
          </section>

          <section
            aria-label={t('writing.practice.session.editorLabel')}
            className="flex flex-col gap-3 rounded-2xl border border-border bg-surface p-4"
          >
            {/* Mounted only once the draft is known: the editor reads
                `initialContent` once, so an early mount would start blank. */}
            {baseline ? (
              <WritingEditorV2
                key={editorKey}
                mode={mode}
                initialContent={editorText}
                disabled={phase !== 'writing' || submitted}
                blockPaste
                onChange={handleEditorChange}
                onBlur={() => flushDraft()}
                placeholder={t('writing.practice.session.editorPlaceholder')}
                inputId="practice-editor"
              />
            ) : (
              <p className="p-4 text-sm text-muted">{t('writing.practice.session.scenarioLoading')}</p>
            )}
          </section>
        </div>

        <SubmitBar
          canSubmit={canSubmit}
          submitLabel={t('writing.practice.session.submit')}
          onSubmit={onSubmit}
          loading={submitting || timeUp === 'sending'}
          helperText={helperText}
        />
      </div>

      <Modal
        open={noCreditsOpen}
        onClose={() => setNoCreditsOpen(false)}
        title="Not enough AI credits"
      >
        <div className="space-y-4">
          <p className="text-sm leading-6 text-muted">
            Assessing one Writing letter costs 2 AI credits, and your available balance does not cover it.
            AI credits are used to assess your Writing letters and Speaking cards. Purchase a package to
            continue. Your draft has been saved.
          </p>
          <div className="flex flex-wrap justify-end gap-2">
            <Button variant="outline" onClick={() => setNoCreditsOpen(false)}>
              Not now
            </Button>
            <Button onClick={() => router.push('/ai-packages')}>
              Buy AI Credits
            </Button>
          </div>
        </div>
      </Modal>
    </>
  );
}
