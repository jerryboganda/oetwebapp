import { AlertCircle, Clock, Eye, Loader2, Play, RotateCcw, Save, Send, Settings, ZoomIn, ZoomOut } from 'lucide-react';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { cn } from '@/lib/utils';
import type { ReadingPartCode } from '@/lib/reading-authoring-api';
import { type SaveState, type ActiveAttempt, formatCountdown } from '../reading-paper-helpers';

export function AttemptToolbar({
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
  showSubmitPartA,
  lockingPartA,
  onZoomChange,
  onSubmit,
  onSubmitPartA,
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
  /** Owner request 2026-08-29 — early "Submit Part A" affordance. */
  showSubmitPartA: boolean;
  lockingPartA: boolean;
  onZoomChange: (next: number) => void;
  onSubmit: () => void;
  onSubmitPartA: () => void;
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
          {attempt.isUntimed ? (
            <div className="flex items-center gap-2 rounded-xl bg-background-light px-3 py-2">
              <Clock className="h-4 w-4 text-primary" aria-hidden="true" />
              <span className="eyebrow text-muted">Untimed Practice — no timer</span>
            </div>
          ) : (
            <div
              className="flex items-center gap-2 rounded-xl bg-background-light px-3 py-2"
              role="timer"
              aria-live="polite"
              aria-atomic="true"
              aria-label={`${timerLabel}, ${formatCountdown(secondsLeft)} remaining`}
            >
              <Clock className="h-4 w-4 text-primary" aria-hidden="true" />
              <span className="eyebrow text-muted">{timerLabel}</span>
              <span className="font-mono text-base font-bold text-navy">{formatCountdown(secondsLeft)}</span>
            </div>
          )}
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
          {showSubmitPartA ? (
            <Button
              variant="secondary"
              onClick={onSubmitPartA}
              loading={lockingPartA}
              disabled={lockingPartA || submitting}
              aria-label="Submit Part A and start the break"
              data-testid="reading-submit-part-a"
            >
              <Send className="h-4 w-4" aria-hidden="true" />
              Submit Part A
            </Button>
          ) : null}
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

export function ReadingZoomControls({ zoomLevel, onZoomChange }: { zoomLevel: number; onZoomChange: (next: number) => void }) {
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
export function ReadingA11ySettings({
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
            <p className="eyebrow text-muted">Font size</p>
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
                      ? 'border-primary bg-primary text-white dark:bg-primary-700'
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
        <p className="text-3xs text-muted">
          <Eye className="mr-1 inline h-3 w-3" aria-hidden />
          Settings are saved per paper. Changes here only affect this paper.
        </p>
      </div>
    </details>
  );
}

export function ReadingBreakScreen({ attempt, nowMs, onResume }: { attempt: ActiveAttempt; nowMs: number; onResume: () => void }) {
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

export function ReadingPartTransitionScreen({ minutes, onContinue }: { minutes: number; onContinue: () => void }) {
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

export function SaveStatus({ state }: { state: SaveState }) {
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
      className={cn(
        'inline-flex items-center gap-2 text-sm font-semibold',
        state === 'error' || state === 'conflict' ? 'text-danger-strong' : 'text-muted',
      )}
      role="status"
      aria-live="polite"
    >
      <Icon className={cn('h-4 w-4', state === 'saving' && 'motion-safe:animate-spin')} aria-hidden="true" />
      {label}
    </span>
  );
}
