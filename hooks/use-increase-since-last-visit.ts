'use client';

import { useEffect, useState } from 'react';

// Read once per page session per key, so every consumer (the header chip and the
// dashboard card) compares against the same "last visit" value even after one of
// them has recorded the new one.
const sessionBaselines = new Map<string, number | null>();

function readBaseline(storageKey: string) {
  if (!sessionBaselines.has(storageKey)) {
    let value: number | null = null;
    try {
      const raw = window.localStorage.getItem(storageKey);
      value = raw === null ? null : Number(raw);
    } catch {
      value = null; // Server render or storage disabled.
    }
    sessionBaselines.set(storageKey, value);
  }
  return sessionBaselines.get(storageKey) ?? null;
}

/**
 * True when a real value (streak, level) has grown since this device last saw
 * it. The current value is recorded for the next visit; a first visit (nothing
 * stored) never claims an increase.
 */
export function useIncreaseSinceLastVisit(storageKey: string, current: number | null | undefined) {
  const [previous] = useState(() => readBaseline(storageKey));

  useEffect(() => {
    if (typeof current !== 'number') return;
    try {
      window.localStorage.setItem(storageKey, String(current));
    } catch {
      // Storage disabled: the next visit simply makes no claim.
    }
  }, [storageKey, current]);

  return typeof current === 'number' && previous !== null && Number.isFinite(previous) && current > previous;
}
