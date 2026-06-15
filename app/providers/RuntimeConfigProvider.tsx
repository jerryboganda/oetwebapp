'use client';

/**
 * Wave 5 (frontend runtime-config) — React provider that fetches the secret-free
 * public runtime config on mount and exposes it via context + `useRuntimeConfig()`.
 *
 * The initial/fallback value is built from build-time `NEXT_PUBLIC_*` so there is
 * NO flash or boot failure if the runtime fetch is slow or unavailable — the tree
 * always has a usable config from first paint, then upgrades in place once the
 * `/v1/public/runtime-config` fetch resolves.
 *
 * SENTRY (do not break it):
 *   - The boot Sentry init stays in `sentry.client.config.ts` / `instrumentation.ts`
 *     driven by build-time `NEXT_PUBLIC_SENTRY_DSN`. That is the early-boot capture
 *     path and is left untouched.
 *   - Here we ONLY add a *lazy* init: if NO build-time DSN was baked in
 *     (readSentryDsn() === null) but the runtime config provides one, we initialise
 *     Sentry from the runtime DSN after the fetch. This lets an admin drive the DSN
 *     from the DB when nothing was baked at build time, WITHOUT ever re-initialising
 *     (and thus risking dropping early-boot capture) when a build-time DSN already
 *     exists. When a build-time DSN is present we leave Sentry exactly as the boot
 *     config wired it. This is the least-risky option that still supports DB-driven DSN.
 */

import {
  createContext,
  useContext,
  useEffect,
  useRef,
  useState,
  type ReactNode,
} from 'react';
import {
  buildFallbackRuntimeConfig,
  fetchRuntimeConfig,
  type RuntimeConfig,
} from '@/lib/runtime-config';
import {
  readSentryDsn,
  readSentryEnvironment,
  readSentryRelease,
  scrubPii,
} from '@/lib/observability/sentry-shared';

const RuntimeConfigContext = createContext<RuntimeConfig>(buildFallbackRuntimeConfig());

export function useRuntimeConfig(): RuntimeConfig {
  return useContext(RuntimeConfigContext);
}

/**
 * Lazily initialise Sentry from the runtime DSN — ONLY when no build-time DSN was
 * baked in (so we never re-init over an already-initialised SDK and drop early
 * capture). Best-effort: any failure is swallowed so boot can never break.
 */
async function maybeLazyInitSentryFromRuntime(dsn: string | null): Promise<void> {
  if (!dsn) return;
  // A build-time DSN means boot Sentry is already wired — leave it alone.
  if (readSentryDsn()) return;

  try {
    const Sentry = await import('@sentry/nextjs');
    // If a client is already bound (e.g. another code path initialised it),
    // don't double-init.
    const getClient = (Sentry as { getClient?: () => unknown }).getClient;
    if (typeof getClient === 'function' && getClient()) return;

    Sentry.init({
      dsn,
      environment: readSentryEnvironment(),
      release: readSentryRelease(),
      // Privacy pins mirror sentry.client.config.ts — hard-coded so they cannot
      // be flipped by runtime config.
      sendDefaultPii: false,
      beforeSend: scrubPii,
      // Performance/replay default to 0 — the runtime DSN path only enables
      // error capture; sampling stays opt-in via the existing build-time path.
      tracesSampleRate: 0,
    });
  } catch {
    // Sentry not installed / SDK surface differs — lazy DSN wiring is best-effort.
  }
}

export function RuntimeConfigProvider({ children }: { children: ReactNode }) {
  const [config, setConfig] = useState<RuntimeConfig>(buildFallbackRuntimeConfig);
  const didFetch = useRef(false);

  useEffect(() => {
    if (didFetch.current) return;
    didFetch.current = true;

    const controller = new AbortController();
    let active = true;

    fetchRuntimeConfig(controller.signal)
      .then((next) => {
        if (!active) return;
        setConfig(next);
        void maybeLazyInitSentryFromRuntime(next.sentry.dsn);
      })
      .catch(() => {
        // fetchRuntimeConfig already falls back internally; nothing to do.
      });

    return () => {
      active = false;
      controller.abort();
    };
  }, []);

  return (
    <RuntimeConfigContext.Provider value={config}>{children}</RuntimeConfigContext.Provider>
  );
}
