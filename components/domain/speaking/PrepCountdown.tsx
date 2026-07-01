'use client';

/**
 * Prep-phase countdown for the OET Speaking session (plan C.3).
 *
 * Used only by the interlocutor-trainee practice route
 * (`app/speaking/sessions/[id]/prep`), not the candidate exam flow — see
 * `app/speaking/exam/[id]/page.tsx` for the current candidate-facing prep
 * screen.
 *
 * Renders a large mm:ss display with a circular SVG progress ring.
 * The ring fills counter-clockwise as time runs out. Below ~30 s
 * remaining the digits flip red and a subtle pulse engages to give
 * the learner a clear urgency cue.
 *
 * The timer is purely visual + a callback fence — it doesn't touch
 * any backend timing. The parent route is responsible for calling
 * `startRolePlay()` once `onComplete` fires. The component:
 *   • cleans up its interval on unmount,
 *   • respects reduced motion (no pulse),
 *   • does not start a new interval if `durationSeconds` is <=0,
 *   • exposes `aria-live` so screen-readers hear the countdown
 *     transition into the warning window without spamming updates
 *     every second (polite, with controlled refresh cadence).
 */
import { useEffect, useId, useMemo, useRef, useState } from 'react';
import { useReducedMotion, motion } from 'motion/react';
import { cn } from '@/lib/utils';
import { Button } from '@/components/ui/button';

const WARNING_THRESHOLD_SECONDS = 30;

export interface PrepCountdownProps {
  /** Total prep duration in seconds. Defaults to 180 (3 minutes). */
  durationSeconds?: number;
  /** Fired once when the countdown reaches zero. */
  onComplete: () => void;
  /** Optional cancel hook — wires a small text-link "Skip prep" button. */
  onCancel?: () => void;
  /** Optional className for the outer container. */
  className?: string;
  /** Smaller display variant for dense layouts (e.g. sidebars). */
  size?: 'md' | 'lg';
}

function formatTime(secondsLeft: number): string {
  const safe = Math.max(0, secondsLeft);
  const m = Math.floor(safe / 60);
  const s = safe % 60;
  return `${m.toString().padStart(2, '0')}:${s.toString().padStart(2, '0')}`;
}

export function PrepCountdown({
  durationSeconds = 180,
  onComplete,
  onCancel,
  className,
  size = 'lg',
}: PrepCountdownProps) {
  const total = Math.max(0, Math.floor(durationSeconds));
  const [startTs, setStartTs] = useState(0);
  const completedRef = useRef(false);
  const onCompleteRef = useRef(onComplete);
  const labelId = useId();
  const reducedMotion = useReducedMotion();

  // Use a tick reducer rather than storing the count in state — this
  // way we re-derive the remaining seconds from the start timestamp
  // every render, which keeps things accurate even after tab-throttle.
  const [nowMs, setNowMs] = useState(0);

  useEffect(() => {
    onCompleteRef.current = onComplete;
  }, [onComplete]);

  useEffect(() => {
    if (total <= 0) {
      if (!completedRef.current) {
        completedRef.current = true;
        onCompleteRef.current();
      }
      return undefined;
    }

    const start = Date.now();
    window.queueMicrotask(() => {
      setStartTs(start);
      setNowMs(start);
    });
    completedRef.current = false;

    const interval = window.setInterval(() => {
      const elapsed = Math.floor((Date.now() - start) / 1000);
      const remaining = total - elapsed;
      if (remaining <= 0 && !completedRef.current) {
        completedRef.current = true;
        window.clearInterval(interval);
        setNowMs(Date.now());
        onCompleteRef.current();
        return;
      }
      setNowMs(Date.now());
    }, 250);

    return () => {
      window.clearInterval(interval);
    };
  }, [total]);

  const elapsed = Math.min(total, Math.floor((nowMs - startTs) / 1000));
  const remaining = Math.max(0, total - elapsed);
  const isWarning = remaining > 0 && remaining <= WARNING_THRESHOLD_SECONDS;
  const fractionRemaining = total > 0 ? remaining / total : 0;

  // SVG ring geometry
  const ringSize = size === 'lg' ? 240 : 168;
  const strokeWidth = size === 'lg' ? 14 : 10;
  const radius = (ringSize - strokeWidth) / 2;
  const circumference = 2 * Math.PI * radius;
  const dashOffset = circumference * (1 - fractionRemaining);

  const digitClass = useMemo(
    () =>
      cn(
        'tabular-nums font-bold tracking-tight transition-colors duration-300',
        size === 'lg' ? 'text-6xl md:text-7xl' : 'text-4xl md:text-5xl',
        isWarning ? 'text-rose-600' : 'text-foreground',
      ),
    [isWarning, size],
  );

  // Polite aria-live: only announce on the warning threshold flip and
  // every 30s. Avoids spamming.
  const shouldAnnounce =
    remaining === total ||
    remaining === WARNING_THRESHOLD_SECONDS ||
    (remaining > 0 && remaining % 30 === 0);

  return (
    <div
      className={cn(
        'flex flex-col items-center gap-4 rounded-2xl border border-border bg-surface p-6 shadow-sm',
        className,
      )}
      data-testid="prep-countdown"
    >
      <p id={labelId} className="text-sm font-medium uppercase tracking-wider text-muted">
        Preparation time
      </p>

      <div
        className="relative inline-flex items-center justify-center"
        role="timer"
        aria-labelledby={labelId}
        aria-live={shouldAnnounce ? 'polite' : 'off'}
        style={{ width: ringSize, height: ringSize }}
      >
        <svg
          width={ringSize}
          height={ringSize}
          viewBox={`0 0 ${ringSize} ${ringSize}`}
          aria-hidden
          className="-rotate-90"
        >
          <circle
            cx={ringSize / 2}
            cy={ringSize / 2}
            r={radius}
            stroke="currentColor"
            strokeWidth={strokeWidth}
            fill="none"
            className="text-border"
          />
          <circle
            cx={ringSize / 2}
            cy={ringSize / 2}
            r={radius}
            stroke="currentColor"
            strokeWidth={strokeWidth}
            strokeLinecap="round"
            fill="none"
            strokeDasharray={circumference}
            strokeDashoffset={dashOffset}
            className={cn(
              'transition-[stroke-dashoffset,color] duration-500 ease-linear',
              isWarning ? 'text-rose-500' : 'text-emerald-500',
            )}
          />
        </svg>

        <motion.div
          className="absolute inset-0 flex flex-col items-center justify-center"
          animate={
            isWarning && !reducedMotion
              ? { scale: [1, 1.04, 1] }
              : { scale: 1 }
          }
          transition={
            isWarning && !reducedMotion
              ? { duration: 1, repeat: Infinity, ease: 'easeInOut' }
              : { duration: 0 }
          }
        >
          <span
            data-testid="prep-countdown-display"
            data-warning={isWarning ? 'true' : 'false'}
            className={digitClass}
          >
            {formatTime(remaining)}
          </span>
          <span className="mt-1 text-xs uppercase tracking-wide text-muted">
            {isWarning ? 'Wrap up' : 'minutes:seconds'}
          </span>
        </motion.div>
      </div>

      {onCancel ? (
        <Button
          type="button"
          variant="ghost"
          size="sm"
          onClick={onCancel}
          className="text-muted hover:text-foreground"
        >
          Skip prep
        </Button>
      ) : null}
    </div>
  );
}

export default PrepCountdown;
