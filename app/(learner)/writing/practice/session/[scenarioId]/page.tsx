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
import { WritingEditorV2 } from '@/components/domain/writing/WritingEditorV2';
import { WritingTimerV2 } from '@/components/domain/writing/WritingTimerV2';
import { WordCounter } from '@/components/domain/writing/WordCounter';
import { SubmitBar } from '@/components/domain/writing/SubmitBar';
import { WritingStimulus } from '@/components/domain/writing/WritingStimulus';
import type { Highlight } from '@/components/domain/writing/WritingStimulusViewer';
import { WritingReadingWindowOverlay } from '@/components/domain/writing/WritingReadingWindowOverlay';
import {
  InsufficientCreditsModal,
  isInsufficientCreditsError,
  readInsufficientCreditsMessage,
} from '@/components/domain/InsufficientCreditsModal';
import {
  checkWritingScenarioEligibility,
  createWritingSubmission,
  getWritingDraftV2,
  getWritingHighlights,
  getWritingScenario,
  putWritingDraftV2,
  putWritingHighlights,
} from '@/lib/writing/api';
import { createSubmitIdempotencyKey, toCandidateSafeWritingErrorMessage } from '@/lib/writing/submit-keys';
import { showCreditFeedback } from '@/lib/credit-feedback';
import { parseHighlights, serializeHighlights } from '@/lib/writing/highlights';
import { useDeadlineCountdown } from '@/lib/writing/useCountdown';
import { WRITING_READING_WINDOW_SECONDS, WRITING_WINDOW_SECONDS } from '@/lib/writing/workflow';
import type {
  WritingEditorMode,
  WritingScenarioDto,
} from '@/lib/writing/types';

type ScenarioMode = Extract<WritingEditorMode, 'practice' | 'coached'>;

/** The task's authored value when it is a sane positive number, else the exam default. */
function positiveOr(value: number | null | undefined, fallback: number): number {
  return typeof value === 'number' && Number.isFinite(value) && value > 0 ? value : fallback;
}

/** Waits between time-up auto-submit attempts; the last value repeats. */
const AUTO_SUBMIT_RETRY_MS = [5_000, 15_000, 30_000, 60_000];

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

export default function WritingPracticeSessionPage() {
  const t = useTranslations();
  const params = useParams<{ scenarioId: string }>();
  const router = useRouter();
  const scenarioId = String(params?.scenarioId ?? '');

  const [scenario, setScenario] = useState<WritingScenarioDto | null>(null);
  const mode: ScenarioMode = 'practice';
  const [content, setContent] = useState('');
  const [initialContent, setInitialContent] = useState('');
  const [wordCount, setWordCount] = useState(0);
  const [error, setError] = useState<string | null>(null);
  const [submitting, setSubmitting] = useState(false);
  const [noCreditsOpen, setNoCreditsOpen] = useState(false);
  const [insufficientCreditsMessage, setInsufficientCreditsMessage] = useState<string | null>(null);
  // A failed load is a real, recoverable state (Addendum Rev8 §16) — never a
  // permanent "Loading scenario…" with an empty task. Bumping `loadAttempt` re-runs it.
  const [loadError, setLoadError] = useState<{ cause: unknown } | null>(null);
  const [loadAttempt, setLoadAttempt] = useState(0);
  const startedAtRef = useRef<number>(Date.now());
  // The letter text the server is known to hold — set ONLY after a successful save.
  const lastAutosaveContent = useRef<string>('');
  const autosaveInFlightRef = useRef(false);
  // Latest content for autosave + auto-submit (read through refs so typing
  // never restarts their timers).
  const contentRef = useRef('');
  contentRef.current = content;
  const wordCountRef = useRef(0);
  wordCountRef.current = wordCount;
  const scenarioRef = useRef<WritingScenarioDto | null>(null);
  scenarioRef.current = scenario;

  // ── Strict 45-minute exam clock ─────────────────────────────────────────────
  // 5 min forced reading (pad LOCKED) → 40 min writing → hard auto-submit. The
  // deadlines are persisted per-scenario in sessionStorage so a refresh resumes
  // the same clock instead of granting extra time.
  const [phase, setPhase] = useState<'reading' | 'writing' | 'completed'>('reading');
  const [readingDeadlineMs, setReadingDeadlineMs] = useState<number | null>(null);
  const [writingDeadlineMs, setWritingDeadlineMs] = useState<number | null>(null);
  // Yellow highlights, lifted here so they persist from the reading window into
  // the writing view (both render the same Case Notes PDF).
  const [pdfHighlights, setPdfHighlights] = useState<Record<number, Highlight[]>>({});
  // Latest highlights for auto-submit (timer expiry snapshots the current marks).
  const highlightsRef = useRef<Record<number, Highlight[]>>({});
  highlightsRef.current = pdfHighlights;
  // Last value persisted to the server — avoids redundant autosaves. Seeded to an
  // empty map so a scenario with no saved marks doesn't trigger a no-op save.
  const lastSavedHighlightsRef = useRef<string>(serializeHighlights({}));

  const clockKey = `writing-practice-clock:${scenarioId}`;

  // Per-task timing and word guide from the authored task, exam defaults otherwise.
  const windowSeconds = positiveOr(scenario?.readingTimeSeconds, WRITING_READING_WINDOW_SECONDS);
  const writingWindowSeconds = positiveOr(scenario?.writingTimeSeconds, WRITING_WINDOW_SECONDS);
  const wordGuideMin = positiveOr(scenario?.wordGuideMin, 180);
  const wordGuideMax = positiveOr(scenario?.wordGuideMax, 220);
  const wordTarget = wordGuideMax >= wordGuideMin ? { min: wordGuideMin, max: wordGuideMax } : { min: 180, max: 220 };

  // Resolve/initialise the exam clock once the scenario has loaded.
  useEffect(() => {
    if (typeof window === 'undefined' || !scenarioId || !scenario) return;

    const readingSecs = windowSeconds;
    let readingDeadline: number;
    let writingDeadline: number;

    const stored = sessionStorage.getItem(clockKey);
    let parsed: { reading: number; writing: number } | null = null;
    if (stored) {
      try {
        parsed = JSON.parse(stored) as { reading: number; writing: number };
      } catch {
        parsed = null;
      }
    }

    if (parsed && Number.isFinite(parsed.reading) && Number.isFinite(parsed.writing)) {
      readingDeadline = parsed.reading;
      writingDeadline = parsed.writing;
    } else {
      readingDeadline = Date.now() + readingSecs * 1000;
      writingDeadline = readingDeadline + writingWindowSeconds * 1000;
      sessionStorage.setItem(
        clockKey,
        JSON.stringify({ reading: readingDeadline, writing: writingDeadline }),
      );
    }

    setReadingDeadlineMs(readingDeadline);
    setWritingDeadlineMs(writingDeadline);

    if (Date.now() < readingDeadline) {
      setPhase('reading');
    } else {
      setPhase('writing');
      startedAtRef.current = readingDeadline;
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [scenarioId, scenario?.id]);

  // ── Scenario + draft load ───────────────────────────────────────────────────
  // Eligibility is checked FIRST, before any task content is fetched — a
  // learner without enough AI grading credits never sees the case notes or
  // starts the reading/writing clock.
  useEffect(() => {
    if (!scenarioId) return;
    let cancelled = false;
    void checkWritingScenarioEligibility(scenarioId)
      .then((eligibility) => {
        showCreditFeedback(eligibility?.feedbackMessage);
        return Promise.all([
          getWritingScenario(scenarioId),
          // Strict: only a 404 means "no draft". A 5xx or a dropped connection
          // rejects into the Retry state — never "blank editor + overwrite".
          getWritingDraftV2(scenarioId, mode),
          // Saved Case Notes highlights persist per (user, scenario) across attempts.
          getWritingHighlights(scenarioId).catch(() => null),
        ]);
      })
      .then(([sc, draft, hl]) => {
        if (cancelled) return;
        // Same batch as the draft below: the editor mounts on `scenario`, so it
        // is created with the restored text (it reads `initialContent` once).
        setScenario(sc);
        if (draft?.content) {
          setInitialContent(draft.content);
          setContent(draft.content);
          setWordCount(draft.wordCount);
          lastAutosaveContent.current = draft.content;
        }
        if (hl?.highlightsJson) {
          const parsed = parseHighlights(hl.highlightsJson);
          setPdfHighlights(parsed);
          // Record what's already on the server so the autosave effect doesn't
          // immediately echo the just-loaded marks back.
          lastSavedHighlightsRef.current = serializeHighlights(parsed);
        }
      })
      .catch((err) => {
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
  }, [scenarioId, mode, loadAttempt]);

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

  // ── Autosave (writing phase only) ────────────────────────────────────────────
  // The interval reads the letter through refs, so continuous typing can no
  // longer postpone it forever; one save at a time; text counts as saved only
  // once the server has it (a failed save is retried on the next tick).
  useEffect(() => {
    if (phase !== 'writing' || !scenarioId) return;
    const timer = window.setInterval(() => {
      const text = contentRef.current;
      if (autosaveInFlightRef.current || text === lastAutosaveContent.current) return;
      autosaveInFlightRef.current = true;
      const elapsed = Math.round((Date.now() - startedAtRef.current) / 1000);
      void putWritingDraftV2(scenarioId, mode, {
        content: text,
        wordCount: wordCountRef.current,
        timeSpentSeconds: elapsed,
      })
        .then(() => {
          lastAutosaveContent.current = text;
        })
        .catch(() => {
          /* not marked saved — the next tick retries */
        })
        .finally(() => {
          autosaveInFlightRef.current = false;
        });
    }, 5000);
    return () => window.clearInterval(timer);
  }, [phase, scenarioId, mode]);

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
  const canSubmit = phase === 'writing' && !submitting;

  const helperText = t('writing.practice.session.helper.ready');

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
      const elapsed = Math.round((Date.now() - startedAtRef.current) / 1000);
      return createWritingSubmission({
        scenarioId: current.id,
        mode,
        letterContent: contentRef.current,
        wordCount: wordCountRef.current,
        timeSpentSeconds: elapsed,
        inputSource: 'editor',
        caseNoteHighlightsJson: serializeHighlights(highlightsRef.current),
        idempotencyKey,
      });
    },
    [mode],
  );

  const openGrading = useCallback(
    (submissionId: string) => {
      // Clear the clock so a future retake of this scenario starts fresh.
      if (typeof window !== 'undefined') sessionStorage.removeItem(clockKey);
      router.push(`/writing/submissions/${encodeURIComponent(submissionId)}/grading`);
    },
    [clockKey, router],
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
    startedAtRef.current = Date.now();
    setPhase('writing');
  }, []);

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
                    <Badge variant="muted" size="sm">{scenario.letterType}</Badge>
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
          <div className="flex items-center gap-4">
            <WordCounter count={wordCount} target={wordTarget} ariaLabelPrefix="Letter length" />
            <WritingTimerV2
              phase={phase}
              readingSecondsRemaining={readingSeconds}
              writingSecondsRemaining={writingSeconds}
              strict
            />
          </div>
        </header>

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
            {scenario ? (
              <WritingEditorV2
                mode={mode}
                initialContent={initialContent}
                disabled={phase !== 'writing'}
                blockPaste
                onChange={(text, words) => {
                  setContent(text);
                  setWordCount(words);
                }}
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
        title="No AI credits remaining"
      >
        <div className="space-y-4">
          <p className="text-sm leading-6 text-muted">
            You have no AI grading credits remaining. AI Credits grade your Writing letters and Speaking
            cards instantly. Purchase a package to continue. Your draft has been saved.
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
