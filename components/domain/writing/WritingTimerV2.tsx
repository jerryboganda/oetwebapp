'use client';

import { useEffect, useRef } from 'react';
import { cn } from '@/lib/utils';

export type WritingTimerPhase = 'reading' | 'writing' | 'completed';

export interface WritingTimerV2Props {
  phase: WritingTimerPhase;
  readingSecondsRemaining: number;
  writingSecondsRemaining: number;
  onPhaseChange?: (newPhase: WritingTimerPhase) => void;
  strict?: boolean;
  className?: string;
}

function formatHms(totalSeconds: number): string {
  const safe = Math.max(0, Math.floor(totalSeconds));
  const m = Math.floor(safe / 60).toString().padStart(2, '0');
  const s = (safe % 60).toString().padStart(2, '0');
  return `${m}:${s}`;
}

const BEEP_MARKS: ReadonlySet<number> = new Set([300, 60, 0]); // 5 min, 1 min, 0 min

function beep(): void {
  if (typeof window === 'undefined') return;
  try {
    const AudioCtx: typeof AudioContext | undefined =
      (window as unknown as { AudioContext?: typeof AudioContext; webkitAudioContext?: typeof AudioContext }).AudioContext
      ?? (window as unknown as { webkitAudioContext?: typeof AudioContext }).webkitAudioContext;
    if (!AudioCtx) return;
    const ctx = new AudioCtx();
    const osc = ctx.createOscillator();
    const gain = ctx.createGain();
    osc.connect(gain);
    gain.connect(ctx.destination);
    osc.frequency.value = 880;
    gain.gain.setValueAtTime(0.0001, ctx.currentTime);
    gain.gain.exponentialRampToValueAtTime(0.2, ctx.currentTime + 0.02);
    gain.gain.exponentialRampToValueAtTime(0.0001, ctx.currentTime + 0.25);
    osc.start();
    osc.stop(ctx.currentTime + 0.3);
    osc.onended = () => void ctx.close();
  } catch {
    /* swallow audio errors */
  }
}

/**
 * Phase-aware countdown timer for the writing editor.
 *
 * - In `reading` phase: shows the reading-window remaining seconds.
 *   When it reaches 0, emits `onPhaseChange('writing')`.
 * - In `writing` phase: shows writing-window remaining seconds. When 0,
 *   emits `onPhaseChange('completed')`.
 * - `strict` mode beeps at 5 min, 1 min, 0 (used in mock + diagnostic).
 *
 * The component is fully presentational — it does NOT decrement the
 * counter itself. The parent is responsible for ticking the seconds
 * down (typically via `requestAnimationFrame` or `setInterval`) and
 * passing fresh prop values each tick. This keeps the timer testable
 * and avoids races between local + server time sources.
 */
export function WritingTimerV2({
  phase,
  readingSecondsRemaining,
  writingSecondsRemaining,
  onPhaseChange,
  strict = false,
  className,
}: WritingTimerV2Props) {
  const lastBeepedReading = useRef<number | null>(null);
  const lastBeepedWriting = useRef<number | null>(null);
  const transitionedToWriting = useRef(false);
  const transitionedToCompleted = useRef(false);

  // Phase transition: reading -> writing
  useEffect(() => {
    if (
      phase === 'reading'
      && readingSecondsRemaining <= 0
      && !transitionedToWriting.current
      && onPhaseChange
    ) {
      transitionedToWriting.current = true;
      onPhaseChange('writing');
    }
  }, [phase, readingSecondsRemaining, onPhaseChange]);

  // Phase transition: writing -> completed
  useEffect(() => {
    if (
      phase === 'writing'
      && writingSecondsRemaining <= 0
      && !transitionedToCompleted.current
      && onPhaseChange
    ) {
      transitionedToCompleted.current = true;
      onPhaseChange('completed');
    }
  }, [phase, writingSecondsRemaining, onPhaseChange]);

  // Strict-mode beeps
  useEffect(() => {
    if (!strict) return;
    if (phase === 'reading' && BEEP_MARKS.has(readingSecondsRemaining)) {
      if (lastBeepedReading.current !== readingSecondsRemaining) {
        lastBeepedReading.current = readingSecondsRemaining;
        beep();
      }
    }
    if (phase === 'writing' && BEEP_MARKS.has(writingSecondsRemaining)) {
      if (lastBeepedWriting.current !== writingSecondsRemaining) {
        lastBeepedWriting.current = writingSecondsRemaining;
        beep();
      }
    }
  }, [strict, phase, readingSecondsRemaining, writingSecondsRemaining]);

  const seconds = phase === 'reading' ? readingSecondsRemaining : writingSecondsRemaining;
  const tone =
    phase === 'completed'
      ? 'text-slate-500'
      : seconds <= 60
        ? 'text-red-600 dark:text-red-400'
        : seconds <= 300
          ? 'text-amber-600 dark:text-amber-400'
          : 'text-navy dark:text-white';

  const phaseLabel =
    phase === 'reading' ? 'Reading window' : phase === 'writing' ? 'Writing window' : 'Completed';

  return (
    <div
      className={cn('flex items-center gap-3 select-none', className)}
      role="timer"
      aria-live="polite"
      aria-atomic="true"
      aria-label={`${phaseLabel}: ${formatHms(seconds)} remaining`}
    >
      <span className="text-xs uppercase tracking-wider font-bold text-muted">{phaseLabel}</span>
      <span className={cn('font-bold tabular-nums text-2xl', tone)}>
        {phase === 'completed' ? '--:--' : formatHms(seconds)}
      </span>
    </div>
  );
}
