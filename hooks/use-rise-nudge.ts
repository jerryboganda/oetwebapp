'use client';

import { useState } from 'react';

/**
 * A counter that increments each time `count` rises after its first settled
 * value (`ready`), so a badge can replay a one-shot nudge for new items but not
 * for the initial load. Adjusts state during render, so there is no effect.
 */
export function useRiseNudge(count: number, ready: boolean) {
  const [baseline, setBaseline] = useState<number | null>(null);
  const [nudge, setNudge] = useState(0);

  if (baseline === null) {
    if (ready) setBaseline(count);
  } else if (count !== baseline) {
    setBaseline(count);
    if (count > baseline) setNudge((current) => current + 1);
  }

  return nudge;
}
