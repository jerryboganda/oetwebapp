'use client';
import { useEffect, type DependencyList } from 'react';

/**
 * Runs `effect` after `delayMs` ms of stable deps. Passes a `cancelled` signal
 * the effect can check to avoid setState after unmount or re-run.
 */
export function useDebouncedEffect(
  effect: (signal: { cancelled: boolean }) => void | Promise<void>,
  deps: DependencyList,
  delayMs = 200,
) {
  useEffect(() => {
    const signal = { cancelled: false };
    const handle = window.setTimeout(() => {
      void effect(signal);
    }, delayMs);
    return () => {
      signal.cancelled = true;
      window.clearTimeout(handle);
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, deps);
}
