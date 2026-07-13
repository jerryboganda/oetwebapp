'use client';

import { useEffect, useMemo, useState } from 'react';
import { useAuth } from '@/contexts/auth-context';
import { fetchLearnerFeatureFlag } from '@/lib/api';

export type FeatureFlagMap = Record<string, boolean>;

type FeatureFlaggedItem = {
  featureFlag?: string;
};

type FeatureFlagLoadResult = {
  key: string;
  enabled: boolean;
  succeeded: boolean;
};

const EMPTY_FLAGS: FeatureFlagMap = {};
let activeIdentity: string | null = null;
let cacheGeneration = 0;
const cachedFlagMaps = new Map<string, FeatureFlagMap>();
const inflightFlagMaps = new Map<string, Promise<FeatureFlagMap>>();

function synchronizeIdentity(identity: string | null) {
  if (identity === activeIdentity) return;

  activeIdentity = identity;
  cacheGeneration += 1;
  cachedFlagMaps.clear();
  inflightFlagMaps.clear();
}

function loadFeatureFlagMap(
  identity: string,
  cacheKey: string,
  keys: readonly string[],
): Promise<FeatureFlagMap> {
  const cached = cachedFlagMaps.get(cacheKey);
  if (cached) return Promise.resolve(cached);

  const inflight = inflightFlagMaps.get(cacheKey);
  if (inflight) return inflight;

  const requestGeneration = cacheGeneration;
  const request = Promise.all(
    keys.map(async (key): Promise<FeatureFlagLoadResult> => {
      try {
        const flag = await fetchLearnerFeatureFlag(key);
        return { key, enabled: flag.enabled, succeeded: true };
      } catch {
        return { key, enabled: false, succeeded: false };
      }
    }),
  )
    .then((results) => {
      const flags = Object.fromEntries(
        results.map(({ key, enabled }) => [key, enabled]),
      );

      // A rejected flag remains fail-closed for this render, but does not poison
      // the session cache; the next mount can retry the complete key set.
      if (
        results.every(({ succeeded }) => succeeded)
        && activeIdentity === identity
        && cacheGeneration === requestGeneration
      ) {
        cachedFlagMaps.set(cacheKey, flags);
      }

      return flags;
    })
    .finally(() => {
      if (inflightFlagMaps.get(cacheKey) === request) {
        inflightFlagMaps.delete(cacheKey);
      }
    });

  inflightFlagMaps.set(cacheKey, request);
  return request;
}

export function collectFeatureFlagKeys(items: readonly FeatureFlaggedItem[]): string[] {
  return Array.from(new Set(items.map((item) => item.featureFlag).filter((key): key is string => Boolean(key)))).sort();
}

export function isFeatureFlaggedItemVisible(item: FeatureFlaggedItem, flags: FeatureFlagMap, filteringEnabled: boolean): boolean {
  if (!item.featureFlag || !filteringEnabled) return true;
  return flags[item.featureFlag] === true;
}

export function useFeatureFlagMap(keys: readonly string[], active: boolean): FeatureFlagMap {
  const { user } = useAuth();
  const identity = user?.userId ?? null;
  const signature = Array.from(new Set(keys)).sort().join('|');
  const stableKeys = useMemo(() => (signature ? signature.split('|') : []), [signature]);

  const requestKey = active && identity && stableKeys.length > 0
    ? `${identity}:${signature}`
    : null;
  const cachedFlags = requestKey && activeIdentity === identity
    ? cachedFlagMaps.get(requestKey)
    : undefined;
  const [resolved, setResolved] = useState<{
    requestKey: string | null;
    flags: FeatureFlagMap;
  }>(() => ({
    requestKey,
    flags: cachedFlags ?? EMPTY_FLAGS,
  }));

  useEffect(() => {
    synchronizeIdentity(identity);
  }, [identity]);

  useEffect(() => {
    if (!requestKey || !identity) {
      return;
    }

    const cached = cachedFlagMaps.get(requestKey);
    if (cached) {
      return;
    }

    let cancelled = false;
    void loadFeatureFlagMap(identity, requestKey, stableKeys).then((flags) => {
      if (!cancelled) {
        setResolved({ requestKey, flags });
      }
    });

    return () => {
      cancelled = true;
    };
  }, [identity, requestKey, stableKeys]);

  if (activeIdentity === identity && resolved.requestKey === requestKey) {
    return resolved.flags;
  }

  return cachedFlags ?? EMPTY_FLAGS;
}
