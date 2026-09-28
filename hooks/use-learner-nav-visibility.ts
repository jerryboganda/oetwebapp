'use client';

import { useCallback } from 'react';
import { collectFeatureFlagKeys, isFeatureFlaggedItemVisible, useFeatureFlagMap } from '@/hooks/use-feature-flag-map';
import { useEnabledModules } from '@/hooks/use-enabled-modules';
import { PLACEMENT_NAV_HREF, usePlacementAccess } from '@/hooks/use-placement-access';

/** Structural nav item shape, so callers need not import `NavItem` from the sidebar. */
export interface LearnerNavGatedItem {
  href: string;
  featureFlag?: string;
  moduleKey?: string;
}

/**
 * The learner nav gating every navigation surface shares (sidebar, search):
 * feature flags, plan modules (fail-open, see useEnabledModules) and the
 * placement-test entry, which stays hidden until access resolves true. When
 * `active` is false (non-learner workspaces) every item passes and no gating
 * request is made. Returns a predicate for `items.filter(...)`.
 */
export function useLearnerNavVisibility(
  items: readonly LearnerNavGatedItem[],
  active: boolean,
): (item: LearnerNavGatedItem) => boolean {
  const flags = useFeatureFlagMap(collectFeatureFlagKeys(items), active);
  const { isModuleEnabled, modules } = useEnabledModules(active);
  const modulesKey = modules.join('|');
  const placementAccess = usePlacementAccess(active);
  return useCallback(
    (item: LearnerNavGatedItem) => isFeatureFlaggedItemVisible(item, flags, active)
      && isModuleEnabled(item.moduleKey)
      && (item.href !== PLACEMENT_NAV_HREF || !active || placementAccess),
    // isModuleEnabled is recreated every render; modulesKey carries what it reads.
    // eslint-disable-next-line react-hooks/exhaustive-deps
    [active, flags, modulesKey, placementAccess],
  );
}
