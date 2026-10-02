'use client';

import { useEffect, useRef, useState } from 'react';

/**
 * useDeadlineCountdown — counts down to a UTC epoch millisecond deadline.
 *
 * Returns the whole seconds remaining (never negative). Recomputes:
 *   - Immediately on mount and whenever `deadlineMs` changes.
 *   - Every 1 000 ms via setInterval.
 *   - On `document` visibilitychange so drift while the tab was backgrounded
 *     is corrected the moment the learner refocuses.
 *
 * `opts.onZero` fires exactly once when remaining first touches 0, regardless
 * of how many subsequent ticks pass. The latest `onZero` identity is captured
 * in a ref so callers can pass an inline arrow without triggering re-mount.
 */
export function useDeadlineCountdown(
  deadlineMs: number | null,
  opts?: { onZero?: () => void },
): number {
  const compute = () =>
    deadlineMs == null
      ? 0
      : Math.max(0, Math.ceil((deadlineMs - Date.now()) / 1000));

  // Remember which deadline the stored value belongs to: when a deadline is
  // first set (e.g. a restored clock), the render that receives it must not
  // show the previous value (0) until the effect ticks — a strict timer would
  // flash 00:00 and beep.
  const [state, setState] = useState(() => ({ deadlineMs, remaining: compute() }));
  const remaining = state.deadlineMs === deadlineMs ? state.remaining : compute();
  const setRemaining = (next: number) => setState({ deadlineMs, remaining: next });

  // Stable ref for the latest onZero callback — avoids resetting the guard
  // or thrashing effects when the caller passes a new function identity.
  const onZeroRef = useRef(opts?.onZero);
  useEffect(() => { onZeroRef.current = opts?.onZero; });

  // Guard: fire onZero exactly once per deadline.
  const firedRef = useRef(false);

  useEffect(() => {
    // Reset the "fired" guard whenever the deadline changes so a new deadline
    // gets its own fresh onZero notification.
    firedRef.current = false;

    const tick = () => {
      const next = compute();
      setRemaining(next);
      // Only fire onZero for a real elapsed deadline — never when there simply
      // is no deadline (deadlineMs == null also computes 0).
      if (deadlineMs != null && next === 0 && !firedRef.current) {
        firedRef.current = true;
        onZeroRef.current?.();
      }
    };

    // Compute immediately (covers refresh / resume scenarios).
    tick();

    const interval = setInterval(tick, 1000);

    // Recompute on tab refocus to eliminate drift while backgrounded.
    const onVisibility = () => {
      if (typeof document !== 'undefined' && document.visibilityState === 'visible') {
        tick();
      }
    };

    if (typeof document !== 'undefined') {
      document.addEventListener('visibilitychange', onVisibility);
    }

    return () => {
      clearInterval(interval);
      if (typeof document !== 'undefined') {
        document.removeEventListener('visibilitychange', onVisibility);
      }
    };
    // `compute` is intentionally omitted from the deps: it closes over
    // `deadlineMs` from the same render, so a changed deadline already yields a
    // fresh `compute` when this effect re-runs. Including it would loop.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [deadlineMs]);

  return remaining;
}
