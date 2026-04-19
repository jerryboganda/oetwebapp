'use client';

import { useEffect, useMemo, useState } from 'react';
import { fetchLearnerFeatureFlag } from '@/lib/api';

export type FeatureFlagMap = Record<string, boolean>;

type FeatureFlaggedItem = {
  featureFlag?: string;
};

export function collectFeatureFlagKeys(items: readonly FeatureFlaggedItem[]): string[] {
  return Array.from(new Set(items.map((item) => item.featureFlag).filter((key): key is string => Boolean(key)))).sort();
}

export function isFeatureFlaggedItemVisible(item: FeatureFlaggedItem, flags: FeatureFlagMap, filteringEnabled: boolean): boolean {
  if (!item.featureFlag || !filteringEnabled) return true;
  return flags[item.featureFlag] === true;
}

export function useFeatureFlagMap(keys: readonly string[], active: boolean): FeatureFlagMap {
  const signature = useMemo(() => Array.from(new Set(keys)).sort().join('|'), [keys]);
  const stableKeys = useMemo(() => (signature ? signature.split('|') : []), [signature]);
  const [flags, setFlags] = useState<FeatureFlagMap>({});

  useEffect(() => {
    if (!active || stableKeys.length === 0) {
      return;
    }

    let cancelled = false;

    Promise.all(
      stableKeys.map(async (key) => {
        try {
          const flag = await fetchLearnerFeatureFlag(key);
          return [key, flag.enabled] as const;
        } catch {
          return [key, false] as const;
        }
      }),
    ).then((entries) => {
      if (!cancelled) {
        setFlags(Object.fromEntries(entries));
      }
    });

    return () => {
      cancelled = true;
    };
  }, [active, stableKeys]);

  return flags;
}
