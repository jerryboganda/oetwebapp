'use client';

import { useEffect, useState } from 'react';

const LAST_SEEN_STREAK_KEY = 'oet_last_seen_streak';

/**
 * True when the learner's real streak has grown since they last saw it on this
 * device. The previous value is read once per mount and the current one is
 * recorded, so a first visit (nothing stored) never claims an increase.
 */
export function useStreakIncrease(current: number | null | undefined) {
  const [previous] = useState<number | null>(() => {
    try {
      const raw = window.localStorage.getItem(LAST_SEEN_STREAK_KEY);
      return raw === null ? null : Number(raw);
    } catch {
      return null; // Server render or storage disabled.
    }
  });

  useEffect(() => {
    if (typeof current !== 'number') return;
    try {
      window.localStorage.setItem(LAST_SEEN_STREAK_KEY, String(current));
    } catch {
      // Storage disabled: the next visit simply makes no claim.
    }
  }, [current]);

  return typeof current === 'number' && previous !== null && Number.isFinite(previous) && current > previous;
}
