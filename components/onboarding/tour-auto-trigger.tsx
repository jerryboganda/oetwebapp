'use client';

import { useEffect, useRef } from 'react';
import { usePathname } from 'next/navigation';
import type { UserRole } from '@/lib/types/auth';
import { allTours } from '@/lib/onboarding/tour-registry';
import { TOUR_VERSION, type TourRole } from '@/lib/onboarding/tour-types';
import { useTourSafe } from './tour-provider';

function toTourRole(role: UserRole | undefined): TourRole | null {
  if (role === 'learner' || role === 'expert' || role === 'admin') return role;
  return null; // sponsor (and any future role) gets no auto-tour
}

/**
 * Exact-route match (after normalising `/` → `/dashboard`). Intentionally NOT a
 * prefix match so a module tour fires on the module hub (e.g. `/listening`) but
 * never inside a live exam player (`/listening/player/123`).
 */
function matchesTrigger(pathname: string, route: string): boolean {
  const base = route !== '/' && route.endsWith('/') ? route.slice(0, -1) : route;
  return pathname === base;
}

const AUTOSTART_DELAY_MS = 1000;

/**
 * Starts the relevant first-run tour once per surface. Mounted inside the
 * authenticated AppShell. Never repeats: gated on persisted completion/skip flags
 * plus a per-session fired set, and re-shows a completed tour only after a major
 * content bump (persisted lastSeenTourVersion < TOUR_VERSION). Never blocks the UI.
 */
export function TourAutoTrigger({ workspaceRole }: { workspaceRole?: UserRole }) {
  const rawPath = usePathname() ?? '/';
  const pathname = rawPath === '/' ? '/dashboard' : rawPath;
  const tour = useTourSafe();
  const { state, loading, isAuthenticated, isCompleted, isSkipped, startTour } = tour ?? {
    state: undefined, loading: true, isAuthenticated: false,
    isCompleted: () => false, isSkipped: () => false, startTour: () => Promise.resolve(),
  };
  const firedRef = useRef<Set<string>>(new Set());

  useEffect(() => {
    if (!isAuthenticated || loading || !state) return;
    const tourRole = toTourRole(workspaceRole);
    if (!tourRole) return;

    const candidate = allTours().find((tour) => {
      const roleMatch = tour.role === tourRole || (tourRole === 'expert' && tour.role === 'tutor');
      if (!roleMatch || !tour.triggerRoute) return false;
      if (!matchesTrigger(pathname, tour.triggerRoute)) return false;
      if (firedRef.current.has(tour.id)) return false;
      if (isSkipped(tour.id)) return false;
      // Completed tours only re-appear after a major content bump.
      if (isCompleted(tour.completionKey) && state.lastSeenTourVersion >= TOUR_VERSION) return false;
      return true;
    });
    if (!candidate) return;

    firedRef.current.add(candidate.id);
    const timer = window.setTimeout(() => {
      void startTour(candidate.id);
    }, AUTOSTART_DELAY_MS);
    return () => window.clearTimeout(timer);
  }, [pathname, isAuthenticated, loading, state, workspaceRole, isCompleted, isSkipped, startTour]);

  return null;
}
