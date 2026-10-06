'use client';

import { useEffect, useRef } from 'react';

export interface UseRevalidateOnResumeOptions {
  /** Minimum gap between two revalidations. Defaults to 15 seconds. */
  minIntervalMs?: number;
}

const DEFAULT_MIN_INTERVAL_MS = 15_000;

/**
 * Runs `callback` when the page becomes visible again (tab switch, app resume in
 * a long-lived mobile WebView) or is restored from the back/forward cache
 * (`pageshow` with `persisted`). Calls are throttled so quick tab flips do not
 * spam the API; the mount itself counts as a fresh load, so a callback never
 * fires right after the consumer's own initial fetch. The latest `callback` is
 * always the one invoked, so callers can pass an inline function.
 */
export function useRevalidateOnResume(
  callback: () => void,
  options?: UseRevalidateOnResumeOptions,
): void {
  const callbackRef = useRef(callback);
  const minIntervalMs = options?.minIntervalMs ?? DEFAULT_MIN_INTERVAL_MS;

  useEffect(() => {
    callbackRef.current = callback;
  }, [callback]);

  useEffect(() => {
    if (typeof window === 'undefined' || typeof document === 'undefined') return undefined;

    let lastRunAt = Date.now();
    const run = () => {
      const now = Date.now();
      if (now - lastRunAt < minIntervalMs) return;
      lastRunAt = now;
      callbackRef.current();
    };
    const onVisibilityChange = () => {
      if (document.visibilityState === 'visible') run();
    };
    const onPageShow = (event: PageTransitionEvent) => {
      if (event.persisted) run();
    };

    document.addEventListener('visibilitychange', onVisibilityChange);
    window.addEventListener('pageshow', onPageShow);
    return () => {
      document.removeEventListener('visibilitychange', onVisibilityChange);
      window.removeEventListener('pageshow', onPageShow);
    };
  }, [minIntervalMs]);
}
