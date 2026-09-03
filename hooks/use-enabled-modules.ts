'use client';

import { useAuth } from '@/contexts/auth-context';
import { useEntitlementSnapshot } from '@/lib/query/hooks';

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

/**
 * Backed by the shared QueryClient (see lib/query/hooks.ts useEntitlementSnapshot)
 * instead of a hand-rolled module-level single-flight cache. Sharing the
 * queryKeys.dashboard.entitlement(userId) key with the Dashboard's own
 * entitlement query means:
 *  - the sidebar, bottom nav, skill switcher, and dashboard hero collapse
 *    into ONE /v1/me/entitlement-snapshot request instead of two
 *    independent caches that both hit the endpoint;
 *  - the purchase-success invalidation in app/page.tsx
 *    (queryClient.invalidateQueries({ queryKey: queryKeys.dashboard.entitlement(...) }))
 *    now also refreshes nav/module visibility, instead of only the hero;
 *  - the query key already includes the userId, so switching identity
 *    (sign-out then sign-in as a different learner, no full reload) reads
 *    from a different cache entry automatically — no manual reset needed.
 */
export function useEnabledModules(active = true): EnabledModulesGate {
  const { user } = useAuth();
  const identity = user?.userId ?? '';

  const { data: snapshot, isSuccess } = useEntitlementSnapshot(identity, {
    enabled: active && Boolean(identity),
  });

  const modules = isSuccess && Array.isArray(snapshot?.enabledModules) ? snapshot.enabledModules : null;
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
