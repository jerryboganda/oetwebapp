'use client';

import { useEffect, useState } from 'react';
import { useAuth } from '@/contexts/auth-context';
import { fetchMyEntitlementSnapshot } from '@/lib/api';

/**
 * Canonical PascalCase module keys stored in a plan's DashboardModulesJson and surfaced as
 * `MyEntitlementSnapshot.enabledModules`. Keep in sync with the backend
 * `OetLearner.Api.Services.Entitlements.ModuleKeys` and the admin editor dropdowns in
 * `components/admin/billing/plan-catalog-editor.tsx`.
 */
export const MODULE_KEYS = {
  recalls: 'Recalls',
  materials: 'MaterialsLibrary',
  videos: 'VideoLibrary',
  mocks: 'Mocks',
} as const;

// A first-load network/auth blip must not look identical to "this plan has no
// modules configured" — that conflation used to permanently hide Recalls/Mocks
// (and, transitively, any nav item behind it) for the rest of the page's life,
// recoverable only by a full reload landing on a lucky retry. Retry a couple of
// times with backoff before giving up.
const LOAD_RETRY_DELAYS_MS = [400, 1200];

function delay(ms: number): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, ms));
}

// Single-flight cache scoped to the signed-in identity so the sidebar, bottom
// nav and skill switcher share ONE snapshot fetch instead of each hitting
// /v1/me/entitlement-snapshot, and so a same-tab account switch (sign-out then
// sign-in as a different learner, no full reload) never leaks one learner's
// modules to another — mirroring hooks/use-feature-flag-map.ts's identity
// scoping. `cachedModules` is deliberately left `null` when every attempt
// fails: a failure is NOT the same fact as "the plan legitimately has no
// modules", so it must not be cached as one. The backend is the authoritative
// gate regardless; a stale/failed client cache only ever affects what's shown.
let activeIdentity: string | null = null;
let cachedModules: string[] | null = null;
let inflight: Promise<string[] | null> | null = null;

function resetCache(identity: string | null) {
  activeIdentity = identity;
  cachedModules = null;
  inflight = null;
}

async function fetchModulesOnce(): Promise<string[]> {
  const snapshot = await fetchMyEntitlementSnapshot();
  return Array.isArray(snapshot.enabledModules) ? snapshot.enabledModules : [];
}

function loadEnabledModules(identity: string | null): Promise<string[] | null> {
  if (identity !== activeIdentity) {
    resetCache(identity);
  }
  if (cachedModules) return Promise.resolve(cachedModules);
  if (inflight) return inflight;

  const attempt = (async () => {
    for (let retry = 0; retry <= LOAD_RETRY_DELAYS_MS.length; retry += 1) {
      try {
        const modules = await fetchModulesOnce();
        if (identity === activeIdentity) {
          cachedModules = modules;
        }
        return modules;
      } catch {
        if (retry < LOAD_RETRY_DELAYS_MS.length) {
          await delay(LOAD_RETRY_DELAYS_MS[retry]);
        }
      }
    }
    // Every attempt failed. Fail fully open (null => unresolved) instead of
    // caching `[]`: the caller treats an unresolved snapshot as "show
    // everything" (see isModuleEnabled below), and leaving the module-level
    // cache empty lets the very next mount/call retry instead of being stuck
    // until a full page reload.
    return null;
  })().finally(() => {
    if (inflight === attempt) {
      inflight = null;
    }
  });

  inflight = attempt;
  return attempt;
}

export interface EnabledModulesGate {
  loaded: boolean;
  modules: string[];
  /**
   * Whether a nav item / tile with the given module key should be shown. FAIL-OPEN: items with no
   * key, an unresolved snapshot (still loading OR every fetch attempt failed), or a plan carrying
   * no explicit module list all read as enabled, so this only ever hides a module an admin has
   * explicitly disabled on the learner's plan.
   */
  isModuleEnabled: (moduleKey?: string | null) => boolean;
}

export function useEnabledModules(active = true): EnabledModulesGate {
  const { user } = useAuth();
  const identity = user?.userId ?? null;
  const [modules, setModules] = useState<string[] | null>(
    identity === activeIdentity ? cachedModules : null,
  );

  useEffect(() => {
    if (!active) return;
    if (identity === activeIdentity && cachedModules) {
      setModules(cachedModules);
      return;
    }
    let cancelled = false;
    void loadEnabledModules(identity).then((resolved) => {
      if (!cancelled) setModules(resolved);
    });
    return () => {
      cancelled = true;
    };
  }, [active, identity]);

  const loaded = modules !== null;
  const lowered = (modules ?? []).map((key) => key.toLowerCase());

  const isModuleEnabled = (moduleKey?: string | null): boolean => {
    if (!moduleKey) return true;
    if (!active || !loaded) return true;
    if (lowered.length === 0) {
      return moduleKey.toLowerCase() !== MODULE_KEYS.mocks.toLowerCase()
        && moduleKey.toLowerCase() !== MODULE_KEYS.recalls.toLowerCase();
    }
    return lowered.includes(moduleKey.toLowerCase());
  };

  return { loaded, modules: modules ?? [], isModuleEnabled };
}
