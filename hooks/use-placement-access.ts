'use client';

import { useEffect, useState } from 'react';
import { canAccessPlacement, fetchPlacementStatus } from '@/lib/api/placement';

export const PLACEMENT_NAV_HREF = '/placement-test';

// One status call per page load, shared by every nav surface. A failure is
// not cached, so a transient error doesn't hide the entry for the session.
let pending: Promise<boolean> | null = null;

export function loadPlacementAccess(): Promise<boolean> {
  pending ??= fetchPlacementStatus()
    .then(canAccessPlacement)
    .catch(() => {
      pending = null;
      return false;
    });
  return pending;
}

/**
 * Whether this learner can open the free placement test (flag on, and inside
 * the beta allowlist while a beta is active). The status route answers 404
 * while the test is unavailable to them, which reads as `false`.
 */
export function usePlacementAccess(enabled = true): boolean {
  const [access, setAccess] = useState(false);
  useEffect(() => {
    if (!enabled) return;
    let cancelled = false;
    void loadPlacementAccess().then((value) => {
      if (!cancelled) setAccess(value);
    });
    return () => {
      cancelled = true;
    };
  }, [enabled]);
  return access;
}
