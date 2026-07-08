'use client';

import { createContext, useContext, useEffect, useRef, useState, type ReactNode } from 'react';
import { getAppRuntimeKind } from '@/lib/runtime-signals';
import { resolveClientIdentity } from '@/lib/client-version';
import { fetchAppReleasePolicy } from '@/lib/api';
import { compareVersions } from '@/lib/mobile/forced-update';

export interface GatePolicy {
  minVersion?: string;
  storeUrl?: string | null;
  updateFeedUrl?: string | null;
}

interface GateValue {
  blocked: boolean;
  policy: GatePolicy | null;
}

const AppVersionGateContext = createContext<GateValue>({ blocked: false, policy: null });

export function useAppVersionGate(): GateValue {
  return useContext(AppVersionGateContext);
}

/**
 * Boot-time forced-update gate. On mount (once), it resolves the shell's
 * installed version and asks the backend for the release policy; if the shell
 * is below the minimum — or ForceUpdate is set — it flips `blocked`, which the
 * ForcedUpdateOverlay renders on top of everything. It also listens for the
 * `oet:upgrade-required` event that lib/api.ts raises on a 426 response, so an
 * outdated shell is blocked mid-session too.
 *
 * Fails OPEN everywhere: plain web browsers are never gated, an unknown shell
 * version is never gated, and a failed policy fetch never blocks.
 */
export function AppVersionGateProvider({ children }: { children: ReactNode }) {
  const [blocked, setBlocked] = useState(false);
  const [policy, setPolicy] = useState<GatePolicy | null>(null);
  const ranRef = useRef(false);

  useEffect(() => {
    if (ranRef.current) return;
    ranRef.current = true;
    let cancelled = false;

    const onUpgradeRequired = (event: Event) => {
      const detail = ((event as CustomEvent).detail ?? {}) as GatePolicy;
      setPolicy({ minVersion: detail.minVersion, storeUrl: detail.storeUrl, updateFeedUrl: detail.updateFeedUrl });
      setBlocked(true);
    };
    window.addEventListener('oet:upgrade-required', onUpgradeRequired);

    void (async () => {
      if (getAppRuntimeKind() === 'web') return; // the website is never gated

      const identity = await resolveClientIdentity();
      if (!identity.version) return; // unknown version → fail open

      try {
        const platform =
          identity.platform === 'ios' ? 'ios' : identity.platform === 'android' ? 'android' : 'desktop';
        const releasePolicy = await fetchAppReleasePolicy(platform);
        const outdated =
          releasePolicy.forceUpdate ||
          (typeof releasePolicy.minVersion === 'string'
            && compareVersions(identity.version!, releasePolicy.minVersion) < 0);
        if (!cancelled && outdated) {
          setPolicy({
            minVersion: releasePolicy.minVersion,
            storeUrl: releasePolicy.storeUrl,
            updateFeedUrl: releasePolicy.updateFeedUrl,
          });
          setBlocked(true);
        }
      } catch {
        // Fail open — a failed policy fetch must never block the user.
      }
    })();

    return () => {
      cancelled = true;
      window.removeEventListener('oet:upgrade-required', onUpgradeRequired);
    };
  }, []);

  return (
    <AppVersionGateContext.Provider value={{ blocked, policy }}>
      {children}
    </AppVersionGateContext.Provider>
  );
}
