'use client';

import { useEffect, useState } from 'react';
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

// Single-flight, session-scoped cache so the sidebar, bottom nav and skill switcher share ONE
// snapshot fetch instead of each hitting /v1/me/entitlement-snapshot. Staleness after a mid-session
// plan change resolves on the next reload; the backend is the authoritative gate regardless.
let cachedModules: string[] | null = null;
let inflight: Promise<string[]> | null = null;

function loadEnabledModules(): Promise<string[]> {
  if (cachedModules) return Promise.resolve(cachedModules);
  if (inflight) return inflight;
  inflight = fetchMyEntitlementSnapshot()
    .then((snapshot) => {
      cachedModules = Array.isArray(snapshot.enabledModules) ? snapshot.enabledModules : [];
      return cachedModules;
    })
    .catch(() => {
      // Fail-open on error: treat as "no explicit list" → everything enabled.
      cachedModules = [];
      return cachedModules;
    })
    .finally(() => {
      inflight = null;
    });
  return inflight;
}

export interface EnabledModulesGate {
  loaded: boolean;
  modules: string[];
  /**
   * Whether a nav item / tile with the given module key should be shown. FAIL-OPEN: items with no
   * key, an unresolved snapshot, or a plan carrying no explicit module list all read as enabled, so
   * this only ever hides a module an admin has explicitly disabled on the learner's plan.
   */
  isModuleEnabled: (moduleKey?: string | null) => boolean;
}

export function useEnabledModules(active = true): EnabledModulesGate {
  const [modules, setModules] = useState<string[] | null>(cachedModules);

  useEffect(() => {
    if (!active) return;
    if (cachedModules) {
      setModules(cachedModules);
      return;
    }
    let cancelled = false;
    void loadEnabledModules().then((resolved) => {
      if (!cancelled) setModules(resolved);
    });
    return () => {
      cancelled = true;
    };
  }, [active]);

  const loaded = modules !== null;
  const lowered = (modules ?? []).map((key) => key.toLowerCase());

  const isModuleEnabled = (moduleKey?: string | null): boolean => {
    if (!moduleKey) return true;
    if (!active || !loaded) return true;
    if (lowered.length === 0) return true;
    return lowered.includes(moduleKey.toLowerCase());
  };

  return { loaded, modules: modules ?? [], isModuleEnabled };
}
