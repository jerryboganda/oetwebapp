'use client';

import { useEffect, useId, useRef } from 'react';
import { createPortal } from 'react-dom';
import type { WritingScenarioDto } from '@/lib/writing/types';
import { cn } from '@/lib/utils';
import { WritingStimulus } from './WritingStimulus';
import type { Highlight } from './WritingStimulusViewer';

export interface WritingReadingWindowOverlayProps {
  open: boolean;
  scenario: WritingScenarioDto | null;
  /** Server/deadline-driven remaining seconds; owned by the parent, NOT this component. */
  secondsRemaining: number;
  /** Total window length, used only for the progress indicator. */
  totalSeconds?: number;
  /** When false (strict modes) the skip button is never rendered. */
  allowSkip?: boolean;
  /** Practice-only: called when the learner chooses to start writing early. */
  onSkip?: () => void;
  /**
   * Called EXACTLY once when `secondsRemaining` reaches 0 while open — but only
   * after a real countdown has been observed (a value > 0). A 0 seen before the
   * countdown starts (clock not yet armed) is ignored.
   */
  onAutoClose: () => void;
  title?: string;
  /** Controlled highlights, shared with the writing-view PDF so marks persist. */
  highlights?: Record<number, Highlight[]>;
  onHighlightsChange?: (next: Record<number, Highlight[]>) => void;
}

/** mm:ss formatter — mirrors `formatHms` in WritingTimerV2 (copied, not imported). */
function formatHms(totalSeconds: number): string {
  const safe = Math.max(0, Math.floor(totalSeconds));
  const m = Math.floor(safe / 60)
    .toString()
    .padStart(2, '0');
  const s = (safe % 60).toString().padStart(2, '0');
  return `${m}:${s}`;
}

/**
 * WritingReadingWindowOverlay — the forced, full-screen reading window shown at
 * the start of a writing exam.
 *
 * The learner sees the real stimulus PDF with a large countdown above it and can
 * ONLY scroll the document. There is no way to close, cancel, Escape, or use the
 * browser Back button; download/copy/print are blocked by the hardened
 * `WritingStimulus` viewer. When the countdown hits 0:00 the window auto-closes
 * (via `onAutoClose`) and the parent swaps in the writing view.
 *
 * Time is PRESENTATIONAL here — the component never runs its own interval. The
 * parent re-derives `secondsRemaining` from a server-anchored deadline, which is
 * what lets the window survive a page refresh.
 */
export function WritingReadingWindowOverlay({
  open,
  scenario,
  secondsRemaining,
  totalSeconds = 300,
  allowSkip = false,
  onSkip,
  onAutoClose,
  title,
  highlights,
  onHighlightsChange,
}: WritingReadingWindowOverlayProps) {
  const scrollRef = useRef<HTMLDivElement | null>(null);
  const descriptionId = useId();
  // Guards `onAutoClose` so it fires exactly once per open cycle, even though the
  // effect re-runs on every render where `secondsRemaining` stays <= 0.
  const autoClosedRef = useRef(false);
  // The parent arms its exam clock AFTER the overlay first opens, so it briefly
  // passes secondsRemaining=0 (readingDeadline still null). We must NOT auto-close
  // on that spurious 0 — only after a genuine countdown has been observed ticking
  // (a value > 0). Without this, the early false close poisoned the parent's
  // transition guards and the reading window stuck at 00:00 forever.
  const sawCountdownRef = useRef(false);
  // Hold the latest callback so the lock effects can depend only on `open`.
  const onAutoCloseRef = useRef(onAutoClose);
  useEffect(() => {
    onAutoCloseRef.current = onAutoClose;
  }, [onAutoClose]);

  // ── Auto-close at zero (exactly once, after a real countdown) ──────────────
  useEffect(() => {
    if (!open) return;
    // Mark that the countdown is genuinely running. Until we've seen a positive
    // value, a 0 means "clock not armed yet", not "reading time elapsed".
    if (secondsRemaining > 0) {
      sawCountdownRef.current = true;
      return;
    }
    if (sawCountdownRef.current && !autoClosedRef.current) {
      autoClosedRef.current = true;
      onAutoCloseRef.current();
    }
  }, [open, secondsRemaining]);

  // Reset the guards when the overlay closes so a future open works again.
  useEffect(() => {
    if (!open) {
      autoClosedRef.current = false;
      sawCountdownRef.current = false;
    }
  }, [open]);

  // ── Non-dismissible: swallow Escape + Ctrl/⌘ S/P/C in the capture phase ────
  useEffect(() => {
    if (!open) return;
    if (typeof document === 'undefined') return;

    const onKeyDownCapture = (event: KeyboardEvent) => {
      const key = event.key;
      if (key === 'Escape') {
        event.preventDefault();
        event.stopPropagation();
        return;
      }
      // Block download / print / copy shortcuts (parity with the hardened viewer).
      if ((event.ctrlKey || event.metaKey) && ['s', 'p', 'c'].includes(key.toLowerCase())) {
        event.preventDefault();
        event.stopPropagation();
      }
    };

    document.addEventListener('keydown', onKeyDownCapture, true);
    return () => {
      document.removeEventListener('keydown', onKeyDownCapture, true);
    };
  }, [open]);

  // ── Browser Back trap: push a history entry and re-push on popstate ─────────
  useEffect(() => {
    if (!open) return;
    if (typeof window === 'undefined') return;

    window.history.pushState(null, '', window.location.href);
    const onPopState = () => {
      window.history.pushState(null, '', window.location.href);
    };
    window.addEventListener('popstate', onPopState);
    return () => {
      window.removeEventListener('popstate', onPopState);
    };
  }, [open]);

  // ── Body scroll lock (restore previous value on close/unmount) ─────────────
  useEffect(() => {
    if (!open) return;
    if (typeof document === 'undefined') return;

    const previousOverflow = document.body.style.overflow;
    document.body.style.overflow = 'hidden';
    return () => {
      document.body.style.overflow = previousOverflow;
    };
  }, [open]);

  // ── Focus management: move focus to the scroll container, restore on close ─
  useEffect(() => {
    if (!open) return;
    if (typeof document === 'undefined') return;

    const previouslyFocused =
      document.activeElement instanceof HTMLElement ? document.activeElement : null;
    scrollRef.current?.focus();
    return () => {
      previouslyFocused?.focus();
    };
  }, [open]);

  if (!open) return null;
  if (typeof document === 'undefined') return null;

  const tone =
    secondsRemaining <= 60
      ? 'text-danger'
      : secondsRemaining <= 300
        ? 'text-warning'
        : 'text-white';

  const clampedTotal = Math.max(1, Math.floor(totalSeconds));
  const elapsedFraction = Math.min(
    1,
    Math.max(0, (clampedTotal - Math.max(0, secondsRemaining)) / clampedTotal),
  );

  // Announce remaining time only at minute boundaries and during the final 10s,
  // so a screen reader isn't read a new value every single second.
  const announcement =
    secondsRemaining <= 10 || Math.floor(secondsRemaining) % 60 === 0
      ? `${formatHms(secondsRemaining)} remaining in the reading window`
      : '';

  const overlay = (
    <div
      className="fixed inset-0 z-[100] flex flex-col items-center bg-navy/95 backdrop-blur"
      role="dialog"
      aria-modal="true"
      aria-label={title ? `Reading window — ${title}` : 'Reading window'}
      aria-describedby={descriptionId}
    >
      {/* Header — countdown + instructions */}
      <header className="w-full max-w-4xl px-4 pt-[calc(2rem+env(safe-area-inset-top))] pb-4 text-center select-none">
        <p className="text-xs font-bold uppercase tracking-wider text-white/60">
          Reading time
        </p>
        <div
          className={cn('mt-1 font-bold tabular-nums text-6xl sm:text-7xl', tone)}
          aria-hidden="true"
        >
          {formatHms(secondsRemaining)}
        </div>
        {/* Politely announce the remaining time without spamming a screen reader. */}
        <div aria-live="polite" aria-atomic="true" className="sr-only">
          {announcement}
        </div>
        <p id={descriptionId} className="mt-2 text-sm text-white/80">
          Reading time — review the question paper. You can only scroll.
        </p>
        {/* Slim progress bar */}
        <div
          className="mt-4 h-1.5 w-full overflow-hidden rounded-full bg-white/15"
          role="progressbar"
          aria-valuemin={0}
          aria-valuemax={clampedTotal}
          aria-valuenow={Math.max(0, Math.floor(secondsRemaining))}
          aria-hidden="true"
        >
          <div
            className={cn(
              'h-full rounded-full transition-[width] duration-500 ease-linear motion-reduce:transition-none',
              secondsRemaining <= 60
                ? 'bg-danger'
                : secondsRemaining <= 300
                  ? 'bg-warning'
                  : 'bg-primary',
            )}
            style={{ width: `${elapsedFraction * 100}%` }}
          />
        </div>
      </header>

      {/* Body — the scroll-only stimulus */}
      <div
        ref={scrollRef}
        tabIndex={-1}
        className="min-h-0 w-full max-w-4xl flex-1 overflow-y-auto px-4 pb-6 focus:outline-none"
      >
        <WritingStimulus
          scenario={scenario}
          locked
          title={title}
          className="h-full"
          // Reading window is scroll-only: NO highlighter during the forced
          // 5-minute reading time (owner directive). Any pre-existing marks
          // still render read-only; the tool + per-mark delete are hidden.
          allowHighlight={false}
          highlights={highlights}
          onHighlightsChange={onHighlightsChange}
        />
      </div>

      {/* Skip — practice only; absent (not disabled) in strict modes */}
      {allowSkip ? (
        <footer className="w-full max-w-4xl px-4 pb-[calc(2rem+env(safe-area-inset-bottom))] pt-2 text-center select-none">
          <button
            type="button"
            onClick={() => onSkip?.()}
            className="rounded-xl bg-surface px-5 py-2.5 text-sm font-bold text-navy shadow-lg transition-colors hover:bg-background-light focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary"
          >
            Start writing early
          </button>
        </footer>
      ) : null}
    </div>
  );

  return createPortal(overlay, document.body);
}
