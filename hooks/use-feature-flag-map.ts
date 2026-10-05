'use client';

import { useEffect, useMemo, useState } from 'react';
import { useAuth } from '@/contexts/auth-context';
import { fetchLearnerFeatureFlags } from '@/lib/api';

export type FeatureFlagMap = Record<string, boolean>;

type FeatureFlaggedItem = {
  featureFlag?: string;
};

/** What one coalesced network round trip produced. It never rejects. */
type FlagBatchOutcome = {
  succeeded: boolean;
  flags: Record<string, boolean>;
};

const EMPTY_FLAGS: FeatureFlagMap = {};
let activeIdentity: string | null = null;
let cacheGeneration = 0;
const cachedFlagMaps = new Map<string, FeatureFlagMap>();
const inflightFlagMaps = new Map<string, Promise<FeatureFlagMap>>();

// A single transient network/auth blip on first load must not permanently
// read as "flag disabled" — retry a couple of times with backoff before
// falling back to fail-closed for this render (see hooks/use-enabled-modules.ts
// for the analogous fix on the module-gating side).
const FLAG_RETRY_DELAYS_MS = [400, 1200];

// Every hook that mounts inside this window shares ONE request. The shell reads
// flags from several places (top nav, sidebar/search nav, streak badges, the
// companion mount), each with its own key set; they used to cost one request per
// flag per key set on every cold load.
const FLAG_BATCH_WINDOW_MS = 10;

function delay(ms: number): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, ms));
}

async function fetchFlagBatchWithRetry(keys: readonly string[]): Promise<FlagBatchOutcome> {
  for (let attempt = 0; attempt <= FLAG_RETRY_DELAYS_MS.length; attempt += 1) {
    try {
      return { succeeded: true, flags: await fetchLearnerFeatureFlags(keys) };
    } catch {
      if (attempt < FLAG_RETRY_DELAYS_MS.length) {
        await delay(FLAG_RETRY_DELAYS_MS[attempt]);
      }
    }
  }
  return { succeeded: false, flags: {} };
}

type PendingFlagBatch = {
  keys: Set<string>;
  waiters: Array<(outcome: FlagBatchOutcome) => void>;
};

let pendingBatch: PendingFlagBatch | null = null;

function requestFlags(keys: readonly string[]): Promise<FlagBatchOutcome> {
  return new Promise((resolve) => {
    let batch = pendingBatch;
    if (!batch) {
      const opened: PendingFlagBatch = { keys: new Set(), waiters: [] };
      batch = opened;
      pendingBatch = opened;
      setTimeout(() => {
        if (pendingBatch === opened) pendingBatch = null;
        void fetchFlagBatchWithRetry(Array.from(opened.keys).sort()).then((outcome) => {
          for (const waiter of opened.waiters) waiter(outcome);
        });
      }, FLAG_BATCH_WINDOW_MS);
    }
    for (const key of keys) batch.keys.add(key);
    batch.waiters.push(resolve);
  });
}

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
  const request = requestFlags(keys)
    .then((outcome) => {
      // A failed request leaves every flag fail-closed for this render but does
      // not poison the session cache; the next mount can retry the complete key set.
      const flags: FeatureFlagMap = Object.fromEntries(
        keys.map((key) => [key, outcome.succeeded && outcome.flags[key] === true]),
      );

      if (
        outcome.succeeded
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
