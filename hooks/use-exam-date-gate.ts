'use client';

import { useEffect, useState } from 'react';
import { fetchOnboardingState } from '@/lib/api';

let cachedPromise: Promise<boolean> | null = null;

function loadExamDateRequired(): Promise<boolean> {
  if (!cachedPromise) {
    cachedPromise = fetchOnboardingState()
      .then((state) => state.examDateRequired)
      .catch(() => false);
  }
  return cachedPromise;
}

/** Call after a successful sign-out or a successful /goals exam-date save so
 * the next check re-fetches instead of replaying a stale cached value. */
export function resetExamDateGateCache() {
  cachedPromise = null;
}

/** True once we know the signed-in learner has no confirmed target exam
 * date. Fetched once per session (module-level cache) rather than once per
 * guarded page, since AuthGuard wraps ~250 pages. */
export function useExamDateGate(enabled: boolean): boolean | null {
  const [required, setRequired] = useState<boolean | null>(null);

  useEffect(() => {
    if (!enabled) {
      setRequired(null);
      return;
    }

    let cancelled = false;
    loadExamDateRequired().then((value) => {
      if (!cancelled) setRequired(value);
    });
    return () => {
      cancelled = true;
    };
  }, [enabled]);

  return required;
}
