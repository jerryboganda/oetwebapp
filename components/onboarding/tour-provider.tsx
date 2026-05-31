'use client';

import 'driver.js/dist/driver.css';
import './tour.css';

import { createContext, useCallback, useContext, useMemo, useRef, type ReactNode } from 'react';
import { useReducedMotion } from 'motion/react';
import { useAuth } from '@/contexts/auth-context';
import { getTour } from '@/lib/onboarding/tour-registry';
import { runTour } from '@/lib/onboarding/tour-driver';
import { useMarkTourMutation, useTourStateQuery } from '@/lib/onboarding/tour-storage';
import {
  trackTourCompleted,
  trackTourDismissed,
  trackTourReplayed,
  trackTourSkipped,
  trackTourStarted,
  trackTourStepViewed,
} from '@/lib/onboarding/tour-events';
import type { OnboardingTourState, TourCompletionKey, TourId } from '@/lib/onboarding/tour-types';

interface TourContextValue {
  state: OnboardingTourState | undefined;
  loading: boolean;
  isAuthenticated: boolean;
  /** True once the matching backend completion flag is set. */
  isCompleted: (key: TourCompletionKey) => boolean;
  isSkipped: (id: TourId) => boolean;
  isTipDismissed: (id: string) => boolean;
  /** Run a tour. `replay` only changes which analytics event fires. */
  startTour: (id: TourId, opts?: { replay?: boolean }) => Promise<void>;
  dismissTip: (id: string) => void;
}

const TourContext = createContext<TourContextValue | null>(null);

/**
 * Global product-tour context. Mounted once in app/providers.tsx, immediately
 * inside AuthProvider (so it can read the session) and inside QueryProvider (so
 * tour state is cached). Rendering nothing until a tour is driven keeps it cheap;
 * Driver.js owns its own portal/overlay.
 */
export function TourProvider({ children }: { children: ReactNode }) {
  const { isAuthenticated, role } = useAuth();
  const prefersReducedMotion = useReducedMotion();
  const { data: state, isLoading } = useTourStateQuery(isAuthenticated);
  const markTour = useMarkTourMutation();
  const runningRef = useRef(false);

  const isCompleted = useCallback(
    (key: TourCompletionKey) => Boolean(state?.completed?.[key]),
    [state],
  );
  const isSkipped = useCallback((id: TourId) => Boolean(state?.skippedTours?.includes(id)), [state]);
  const isTipDismissed = useCallback(
    (id: string) => Boolean(state?.dismissedTips?.includes(id)),
    [state],
  );

  const startTour = useCallback(
    async (id: TourId, opts?: { replay?: boolean }) => {
      if (runningRef.current) return;
      const def = getTour(id);
      if (!def) return;
      runningRef.current = true;
      const ctx = {
        tourId: def.id,
        role: role ?? undefined,
        route: typeof window !== 'undefined' ? window.location.pathname : undefined,
      };
      (opts?.replay ? trackTourReplayed : trackTourStarted)(ctx);
      try {
        await runTour(def, {
          reducedMotion: Boolean(prefersReducedMotion),
          onStepViewed: (index, step) =>
            trackTourStepViewed({ ...ctx, stepId: step.target ?? `step-${index}`, stepIndex: index }),
          onComplete: () => {
            trackTourCompleted({ ...ctx, stepCount: def.steps.length });
            markTour.mutate({ tourId: def.completionKey, status: 'completed', role: role ?? undefined });
          },
          onSkip: (index) => {
            trackTourSkipped({ ...ctx, atStepIndex: index });
            markTour.mutate({ tourId: def.id, status: 'skipped', role: role ?? undefined });
          },
        });
      } finally {
        runningRef.current = false;
      }
    },
    [prefersReducedMotion, role, markTour],
  );

  const dismissTip = useCallback(
    (id: string) => {
      trackTourDismissed({ tourId: id, role: role ?? undefined });
      markTour.mutate({ tourId: id, status: 'dismissed', role: role ?? undefined });
    },
    [markTour, role],
  );

  const value = useMemo<TourContextValue>(
    () => ({
      state,
      loading: isLoading,
      isAuthenticated,
      isCompleted,
      isSkipped,
      isTipDismissed,
      startTour,
      dismissTip,
    }),
    [state, isLoading, isAuthenticated, isCompleted, isSkipped, isTipDismissed, startTour, dismissTip],
  );

  return <TourContext.Provider value={value}>{children}</TourContext.Provider>;
}

export function useTour(): TourContextValue {
  const ctx = useContext(TourContext);
  if (!ctx) throw new Error('useTour must be used within TourProvider');
  return ctx;
}

/**
 * Nullable variant — returns `null` when rendered outside TourProvider (e.g.
 * in tests that don't mount the full provider tree). Components that silently
 * degrade (checklist, auto-trigger) use this; components that require the
 * context (Help drawer replay) continue to use useTour() so misconfiguration
 * surfaces at dev time.
 */
export function useTourSafe(): TourContextValue | null {
  return useContext(TourContext);
}
