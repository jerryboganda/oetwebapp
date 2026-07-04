'use client';

import {
  useCallback,
  useEffect,
  useMemo,
  useRef,
  useState,
  type ReactNode,
} from 'react';
import { useTranslations } from 'next-intl';
import Link from 'next/link';
import { Lock } from 'lucide-react';
import { cn } from '@/lib/utils';
import { InlineAlert } from '@/components/ui/alert';
import {
  WritingTimerV2,
  type WritingTimerPhase as WritingPhase,
} from '@/components/domain/writing/WritingTimerV2';
import { WordCounter } from '@/components/domain/writing/WordCounter';
import { SubmitBar } from '@/components/domain/writing/SubmitBar';
import { WritingStimulusViewer, type Highlight } from '@/components/domain/writing/WritingStimulusViewer';
import { recordWritingAttemptEvent } from '@/lib/writing/exam-api';
import type { WritingAttemptEventType } from '@/lib/writing/types';

export type { WritingPhase };

// ─────────────────────────────────────────────────────────────────────────────
// Public contract
// ─────────────────────────────────────────────────────────────────────────────

/**
 * Booklet content the simulation renders. Built from the richest source the
 * page can resolve: an enriched authored `WritingTaskDto` (preferred) or a
 * plain `WritingScenarioDto`. The case-notes/question paper itself is supplied
 * separately as a stimulus PDF (see {@link PaperBookletSimulationProps.stimulus}).
 */
export interface PaperBookletContent {
  title: string;
  professionLabel: string;
  /** Candidate role line ("You are …"); optional. */
  writerRole: string | null;
  /** "Today's date" stamp shown on the booklet cover; optional. */
  todayDate: string | null;
  /** The Writing Task instruction paragraph (prompt). */
  taskPromptMarkdown: string | null;
  /** Fixed instruction bullet lines printed under the task. */
  fixedInstructions: string[];
  wordGuideMin: number;
  wordGuideMax: number;
}

export interface PaperBookletSimulationProps {
  /** Stable identifier for the attempt (mock-session id or scenario id). */
  attemptId: string;
  /**
   * Scenario id used for autosave drafts. When the launch param is a mock
   * session this is the resolved `session.scenarioId`; when launched directly
   * from a scenario it equals {@link attemptId}.
   */
  scenarioId: string | null;
  /** Submission id once known (mock session may surface it on transition). */
  submissionId?: string | null;
  content: PaperBookletContent | null;
  /**
   * Real exam question-paper PDF for the booklet's case-notes page. When
   * `downloadPath` is a non-empty string the booklet shows the authenticated
   * PDF (via {@link WritingStimulusViewer}); when absent/falsy no case-notes
   * page is shown. The separate Writing-Task instructions page is unaffected
   * either way.
   */
  stimulus?: { downloadPath: string | null };
  /** Lifecycle phase, owned by the page (mirrors the mock session). */
  phase: WritingPhase;
  readingSecondsRemaining: number;
  writingSecondsRemaining: number;
  /** Disable the textarea + submit while the page resolves the session. */
  loading?: boolean;
  /** Error surfaced from the page (load/submit failures). */
  error?: string | null;
  /** Frozen, read-only view after a successful submit. */
  submitted?: boolean;
  /** True while the submit request is in flight. */
  submitting?: boolean;
  /** Min words before submit is allowed (mirrors mock session = 100). */
  minWords?: number;
  /** Where the locked post-submit booklet links the learner. */
  resultsHref: string;
  onContentChange: (text: string, wordCount: number) => void;
  onSubmit: () => void;
  /**
   * Fired ~5s while writing when the text changed. The page performs the
   * actual `putWritingDraftV2` call (it owns the elapsed-time bookkeeping);
   * the component only schedules the cadence and emits the `auto_saved`
   * attempt event.
   */
  onAutosave: (text: string, wordCount: number) => void;
  /** Controlled Case Notes highlights, lifted to the page so they persist + save. */
  highlights?: Record<number, Highlight[]>;
  onHighlightsChange?: (next: Record<number, Highlight[]>) => void;
}

const AUTOSAVE_INTERVAL_MS = 5000;

// ─────────────────────────────────────────────────────────────────────────────
// Word counting — identical heuristic to WritingEditorV2 (whitespace split).
// ─────────────────────────────────────────────────────────────────────────────

function countWords(text: string): number {
  const trimmed = text.trim();
  if (!trimmed) return 0;
  return trimmed.split(/\s+/).length;
}

// ─────────────────────────────────────────────────────────────────────────────
// Booklet building blocks (presentational)
// ─────────────────────────────────────────────────────────────────────────────

function BookletPage({
  children,
  ariaLabel,
}: {
  children: ReactNode;
  ariaLabel?: string;
}) {
  return (
    <section
      aria-label={ariaLabel}
      className="rounded-sm border border-amber-200/80 bg-[#fdfbf4] p-6 shadow-[0_1px_2px_rgba(120,53,15,0.08)] sm:p-8"
    >
      {children}
    </section>
  );
}

// ─────────────────────────────────────────────────────────────────────────────
// Main component
// ─────────────────────────────────────────────────────────────────────────────

export function PaperBookletSimulation({
  attemptId,
  scenarioId,
  submissionId,
  content,
  stimulus,
  phase,
  readingSecondsRemaining,
  writingSecondsRemaining,
  loading = false,
  error = null,
  submitted = false,
  submitting = false,
  minWords = 100,
  resultsHref,
  onContentChange,
  onSubmit,
  onAutosave,
  highlights,
  onHighlightsChange,
}: PaperBookletSimulationProps) {
  const t = useTranslations();
  const [text, setText] = useState('');
  const textareaRef = useRef<HTMLTextAreaElement | null>(null);

  const wordCount = useMemo(() => countWords(text), [text]);
  const locked = phase !== 'writing' || submitted;

  // Keep latest text/words in refs so the autosave interval and event emitters
  // don't need to re-subscribe on every keystroke.
  const textRef = useRef(text);
  const wordCountRef = useRef(wordCount);
  textRef.current = text;
  wordCountRef.current = wordCount;

  // ── Attempt-event helper (fire-and-forget, mode:'paper') ──────────────────
  const emit = useCallback(
    (eventType: WritingAttemptEventType, payload?: Record<string, unknown>) => {
      void recordWritingAttemptEvent({
        eventType,
        timestamp: new Date().toISOString(),
        mode: 'paper',
        sessionId: attemptId,
        scenarioId: scenarioId ?? null,
        submissionId: submissionId ?? null,
        ...(payload ? { payload } : {}),
      }).catch(() => {
        /* best-effort telemetry */
      });
    },
    [attemptId, scenarioId, submissionId],
  );

  // attempt_started — once per mount (the route IS a fresh paper attempt).
  const startedRef = useRef(false);
  useEffect(() => {
    if (startedRef.current) return;
    startedRef.current = true;
    emit('attempt_started');
  }, [emit]);

  // reading_started / reading_ended → writing_started — on phase transitions.
  const lastPhaseRef = useRef<WritingPhase | null>(null);
  useEffect(() => {
    if (lastPhaseRef.current === phase) return;
    const prev = lastPhaseRef.current;
    lastPhaseRef.current = phase;
    if (phase === 'reading') {
      emit('reading_started');
    } else if (phase === 'writing') {
      // Transitioning out of reading: the reading window has ended.
      if (prev === 'reading') emit('reading_ended');
      emit('writing_started');
    }
  }, [phase, emit]);

  // timer_expired + attempt_locked — when the writing window hits zero.
  const prevWritingSecondsRef = useRef(writingSecondsRemaining);
  useEffect(() => {
    const prev = prevWritingSecondsRef.current;
    prevWritingSecondsRef.current = writingSecondsRemaining;
    if (phase === 'writing' && prev > 0 && writingSecondsRemaining <= 0) {
      emit('timer_expired');
      emit('attempt_locked', { reason: 'timer_expired' });
    }
  }, [writingSecondsRemaining, phase, emit]);

  // attempt_locked — also when a successful submit freezes the booklet.
  const lockedEmittedRef = useRef(false);
  useEffect(() => {
    if (submitted && !lockedEmittedRef.current) {
      lockedEmittedRef.current = true;
      emit('attempt_locked', { reason: 'submitted' });
    }
  }, [submitted, emit]);

  // focus_lost — strict invigilation: window blur during the writing window.
  useEffect(() => {
    if (phase !== 'writing' || submitted) return;
    const handleBlur = () => emit('focus_lost');
    window.addEventListener('blur', handleBlur);
    return () => window.removeEventListener('blur', handleBlur);
  }, [phase, submitted, emit]);

  // Autosave cadence — only while writing; skip when unchanged. The page does
  // the network write so it can stamp elapsed time consistently with the
  // computer-mode session.
  const lastSavedRef = useRef('');
  useEffect(() => {
    if (phase !== 'writing' || submitted || !scenarioId) return;
    const id = window.setInterval(() => {
      if (textRef.current === lastSavedRef.current) return;
      lastSavedRef.current = textRef.current;
      onAutosave(textRef.current, wordCountRef.current);
      emit('auto_saved', { wordCount: wordCountRef.current });
    }, AUTOSAVE_INTERVAL_MS);
    return () => window.clearInterval(id);
  }, [phase, submitted, scenarioId, onAutosave, emit]);

  const handleChange = useCallback(
    (e: React.ChangeEvent<HTMLTextAreaElement>) => {
      if (locked) return;
      const next = e.target.value;
      setText(next);
      onContentChange(next, countWords(next));
    },
    [locked, onContentChange],
  );

  // Block paste — strict editor rule. Emit a `paste` event for invigilation.
  const handlePaste = useCallback(
    (e: React.ClipboardEvent<HTMLTextAreaElement>) => {
      e.preventDefault();
      emit('paste', { blocked: true });
    },
    [emit],
  );

  // Also block drop, an escape hatch that bypasses onPaste.
  const handleDrop = useCallback((e: React.DragEvent<HTMLTextAreaElement>) => {
    e.preventDefault();
  }, []);

  const handleSubmitClick = useCallback(() => {
    emit('submit_clicked', { wordCount: wordCountRef.current });
    onSubmit();
  }, [emit, onSubmit]);

  const canSubmit = phase === 'writing' && !submitted && !submitting && wordCount >= minWords;

  const helperText = useMemo(() => {
    if (submitted) return t('writing.paper.helper.submitted');
    if (phase !== 'writing') return t('writing.paper.helper.reading');
    if (wordCount < minWords) return t('writing.paper.helper.tooShort', { min: minWords });
    return t('writing.paper.helper.ready');
  }, [submitted, phase, wordCount, minWords, t]);

  // Show the real exam question-paper PDF for the case-notes page when one is
  // available; otherwise no case-notes page is rendered.
  const stimulusDownloadPath =
    typeof stimulus?.downloadPath === 'string' && stimulus.downloadPath.length > 0
      ? stimulus.downloadPath
      : null;

  return (
    <div className="space-y-4 pb-32" aria-busy={loading}>
      {/* Control bar — timer + word count (brand-accented). In-flow (not sticky)
          so it never floats over the letter content as an overlay on scroll. */}
      <header
        className="flex flex-wrap items-center justify-between gap-3 rounded-2xl border border-border bg-surface p-4 shadow-sm"
        aria-label={t('writing.paper.controlsLabel')}
      >
        <div className="min-w-0">
          <p className="text-xs font-bold uppercase tracking-wider text-muted">
            {t('writing.paper.eyebrow')}
          </p>
          {/* Booklet title is OET-authored English content (spec §32). */}
          <h1 className="truncate text-base font-bold text-navy" dir="ltr">
            {content?.title ?? t('writing.paper.loading')}
          </h1>
        </div>
        <div className="flex items-center gap-4">
          <WordCounter
            count={wordCount}
            target={{ min: content?.wordGuideMin ?? 180, max: content?.wordGuideMax ?? 200 }}
            ariaLabelPrefix={t('writing.paper.wordCountLabel')}
          />
          {/* Display-only: the page drives reading→writing→completed transitions
              via deadline-anchored countdowns + the reading-window overlay, so
              the timer must NOT also fire them (would double-drive). */}
          <WritingTimerV2
            phase={phase}
            readingSecondsRemaining={readingSecondsRemaining}
            writingSecondsRemaining={writingSecondsRemaining}
            strict
          />
        </div>
      </header>

      {error ? <InlineAlert variant="error">{error}</InlineAlert> : null}

      {/* Reading-phase lock announcement (assertive for screen readers). */}
      {phase === 'reading' && !submitted ? (
        <div
          className="flex items-center gap-2 rounded-xl border border-amber-300 bg-amber-50 px-4 py-2.5 text-sm font-medium text-amber-900"
          role="status"
          aria-live="assertive"
        >
          <Lock className="h-4 w-4 shrink-0" aria-hidden="true" />
          <span>{t('writing.paper.readingLock')}</span>
        </div>
      ) : null}

      {/* Two-column booklet on large screens; stacked on small. LTR: the
          booklet + answer are English exam content (spec §32). */}
      <div className="grid gap-6 lg:grid-cols-2" dir="ltr">
        {/* ── Question Booklet ────────────────────────────────────────────── */}
        <div
          className="space-y-5"
          aria-label={t('writing.paper.questionBookletLabel')}
          role="region"
        >
          {/* Cover */}
          <BookletPage ariaLabel={t('writing.paper.coverLabel')}>
            <div className="text-center">
              <p className="font-serif text-xs font-semibold uppercase tracking-[0.2em] text-amber-800">
                {t('writing.paper.cover.testName')}
              </p>
              <h2 className="mt-2 font-serif text-2xl font-bold text-amber-950">
                {t('writing.paper.cover.subTest')}
              </h2>
              {content ? (
                <p className="mt-1 font-serif text-sm text-stone-700">
                  {content.professionLabel}
                </p>
              ) : null}
              <div className="mx-auto mt-4 h-px w-16 bg-amber-300" aria-hidden="true" />
              {content?.todayDate ? (
                <p className="mt-4 text-xs text-stone-600">
                  {t('writing.paper.cover.dateStamp', { date: content.todayDate })}
                </p>
              ) : null}
            </div>

            <div className="mt-6 rounded-sm border border-amber-200 bg-amber-50/60 p-4">
              <h3 className="font-serif text-xs font-bold uppercase tracking-wide text-amber-900">
                {t('writing.paper.cover.instructionsHeading')}
              </h3>
              <ul className="mt-2 space-y-1 text-xs leading-relaxed text-stone-700">
                <li>{t('writing.paper.cover.instruction1')}</li>
                <li>{t('writing.paper.cover.instruction2')}</li>
                <li>{t('writing.paper.cover.instruction3')}</li>
              </ul>
            </div>
          </BookletPage>

          {/* Case notes — real question-paper PDF when available; otherwise no
              case-notes page is shown (prompt + instructions only). */}
          {stimulusDownloadPath ? (
            <BookletPage ariaLabel={t('writing.paper.caseNotesLabel')}>
              {/* Fixed-height PDF surface framed inside the booklet page; the
                  viewer is internally hardened (copy/drag/print blocked). */}
              <div className="h-[70vh] overflow-hidden rounded-sm border border-amber-200">
                <WritingStimulusViewer
                  downloadPath={stimulusDownloadPath}
                  title="Question paper"
                  className="h-full"
                  highlights={highlights}
                  onHighlightsChange={onHighlightsChange}
                />
              </div>
            </BookletPage>
          ) : null}

          {/* Writing Task — clearly separated section. */}
          <BookletPage ariaLabel={t('writing.paper.writingTaskLabel')}>
            <div className="mb-4 flex items-center gap-3">
              <span aria-hidden="true" className="h-px flex-1 bg-amber-300" />
              <h2 className="font-serif text-lg font-bold uppercase tracking-wide text-amber-950">
                {t('writing.paper.writingTaskHeading')}
              </h2>
              <span aria-hidden="true" className="h-px flex-1 bg-amber-300" />
            </div>

            {content?.writerRole ? (
              <p className="mb-3 font-serif text-sm font-semibold text-amber-950">
                {content.writerRole}
              </p>
            ) : null}

            {content?.taskPromptMarkdown ? (
              <p className="whitespace-pre-wrap font-serif text-sm leading-relaxed text-stone-800">
                {content.taskPromptMarkdown}
              </p>
            ) : null}

            {content && content.fixedInstructions.length > 0 ? (
              <ul className="mt-4 space-y-1.5 font-serif text-sm leading-relaxed text-stone-800">
                {content.fixedInstructions.map((line, i) => (
                  <li key={i} className="flex gap-2">
                    <span aria-hidden="true" className="select-none pt-0.5 text-amber-700">
                      &bull;
                    </span>
                    <span>{line}</span>
                  </li>
                ))}
              </ul>
            ) : null}

            <p className="mt-4 font-serif text-sm font-semibold text-amber-950">
              {t('writing.paper.wordGuide', {
                min: content?.wordGuideMin ?? 180,
                max: content?.wordGuideMax ?? 200,
              })}
            </p>
          </BookletPage>
        </div>

        {/* ── Answer Booklet — lined, typed ───────────────────────────────── */}
        <div
          className="lg:sticky lg:top-24 lg:self-start"
          aria-label={t('writing.paper.answerBookletLabel')}
          role="region"
        >
          <div className="overflow-hidden rounded-sm border border-amber-200/80 bg-[#fdfbf4] shadow-[0_1px_2px_rgba(120,53,15,0.08)]">
            <div className="flex items-center justify-between border-b border-amber-200 bg-amber-50/70 px-5 py-2.5">
              <h2 className="font-serif text-sm font-bold uppercase tracking-wide text-amber-950">
                {t('writing.paper.answerBookletHeading')}
              </h2>
              {locked ? (
                <span className="inline-flex items-center gap-1 text-xs font-semibold text-amber-800">
                  <Lock className="h-3.5 w-3.5" aria-hidden="true" />
                  {submitted
                    ? t('writing.paper.answerLockedSubmitted')
                    : t('writing.paper.answerLockedReading')}
                </span>
              ) : null}
            </div>

            <div className="relative">
              <label htmlFor="paper-answer" className="sr-only">
                {t('writing.paper.answerBookletLabel')}
              </label>
              <textarea
                id="paper-answer"
                ref={textareaRef}
                value={text}
                onChange={handleChange}
                onPaste={handlePaste}
                onDrop={handleDrop}
                disabled={locked || loading}
                readOnly={submitted}
                spellCheck={false}
                autoCorrect="off"
                autoCapitalize="off"
                autoComplete="off"
                data-gramm="false"
                data-gramm_editor="false"
                data-enable-grammarly="false"
                aria-label={t('writing.paper.answerBookletLabel')}
                aria-readonly={locked}
                placeholder={locked ? undefined : t('writing.paper.answerPlaceholder')}
                rows={24}
                className={cn(
                  // Lined-paper effect: a repeating gradient sized to the line
                  // height; transparent background so the rules show through.
                  'block w-full resize-none border-0 bg-transparent px-5 font-serif text-[15px] leading-[32px] text-stone-900',
                  'bg-[repeating-linear-gradient(transparent,transparent_31px,rgba(120,53,15,0.18)_31px,rgba(120,53,15,0.18)_32px)]',
                  '[background-attachment:local]',
                  'placeholder:text-stone-400 focus:outline-none focus:ring-0',
                  locked && 'cursor-not-allowed text-stone-500',
                )}
                style={{ paddingTop: 0, paddingBottom: '32px' }}
              />
            </div>
          </div>

          {/* Post-submit frozen state — do NOT navigate; offer a link. */}
          {submitted ? (
            <div
              className="mt-4 rounded-xl border border-brand/30 bg-brand/5 p-4"
              role="status"
              aria-live="polite"
            >
              <p className="text-sm font-bold text-navy">{t('writing.paper.submittedTitle')}</p>
              <p className="mt-1 text-sm text-muted">{t('writing.paper.submittedBody')}</p>
              <Link
                href={resultsHref}
                className="mt-3 inline-flex items-center gap-1 text-sm font-semibold text-brand underline-offset-2 hover:underline"
              >
                {t('writing.paper.viewResults')}
              </Link>
            </div>
          ) : null}
        </div>
      </div>

      {/* Submit bar — hidden once frozen (read-only state replaces it). */}
      {!submitted ? (
        <SubmitBar
          canSubmit={canSubmit}
          submitLabel={t('writing.paper.submit')}
          onSubmit={handleSubmitClick}
          loading={submitting}
          helperText={helperText}
        />
      ) : null}
    </div>
  );
}
