import { useCallback, useEffect, useRef, useState } from 'react';

const STORAGE_KEY_PREFIX = 'reading_timer_elapsed_';

export function useTimer(
  initialSeconds: number,
  direction: 'up' | 'down' = 'up',
  onExpire?: () => void,
  storageKey?: string,
) {
  const key = storageKey ? `${STORAGE_KEY_PREFIX}${storageKey}` : null;

  const [elapsed, setElapsed] = useState<number>(() => {
    if (key && typeof sessionStorage !== 'undefined') {
      const stored = sessionStorage.getItem(key);
      if (stored) {
        const n = parseInt(stored, 10);
        if (!isNaN(n)) return n;
      }
    }
    return 0;
  });

  const [isPaused, setIsPaused] = useState(false);
  const intervalRef = useRef<ReturnType<typeof setInterval> | null>(null);
  const elapsedRef = useRef(elapsed);

  // keep ref in sync
  useEffect(() => {
    elapsedRef.current = elapsed;
  }, [elapsed]);

  const isExpired = direction === 'down' && elapsed >= initialSeconds;
  const remaining = Math.max(0, initialSeconds - elapsed);

  const clearTimer = useCallback(() => {
    if (intervalRef.current) {
      clearInterval(intervalRef.current);
      intervalRef.current = null;
    }
  }, []);

  const startTimer = useCallback(() => {
    clearTimer();
    intervalRef.current = setInterval(() => {
      setElapsed((prev) => {
        const next = prev + 1;
        if (key && typeof sessionStorage !== 'undefined') {
          sessionStorage.setItem(key, String(next));
        }
        if (direction === 'down' && next >= initialSeconds) {
          clearTimer();
          onExpire?.();
        }
        return next;
      });
    }, 1000);
  }, [clearTimer, direction, initialSeconds, key, onExpire]);

  useEffect(() => {
    if (!isPaused && !isExpired) {
      startTimer();
    }
    return clearTimer;
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [isPaused, isExpired]);

  const pause = useCallback(() => {
    setIsPaused(true);
    clearTimer();
  }, [clearTimer]);

  const resume = useCallback(() => {
    setIsPaused(false);
  }, []);

  const reset = useCallback(() => {
    clearTimer();
    setElapsed(0);
    if (key && typeof sessionStorage !== 'undefined') {
      sessionStorage.removeItem(key);
    }
    setIsPaused(false);
  }, [clearTimer, key]);

  return { elapsed, remaining, isPaused, pause, resume, reset, isExpired };
}
