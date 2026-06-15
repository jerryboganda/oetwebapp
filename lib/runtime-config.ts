/**
 * Wave 5 (frontend runtime-config) — typed client fetcher for the secret-free
 * public runtime config served by the backend at
 * `GET /v1/public/runtime-config` (proxied same-origin via `/api/backend`).
 *
 * The browser used to depend on build-time `NEXT_PUBLIC_*` values baked into the
 * bundle. This module lets the app read those boot values at RUNTIME from the DB
 * (admin-driven RuntimeSettings) while keeping `NEXT_PUBLIC_*` as first-paint /
 * offline fallbacks so there is never a flash-of-broken-boot if the fetch is slow
 * or unavailable.
 *
 * SECURITY: the endpoint exposes ONLY public values (Sentry DSN is a publishable
 * ingest key; Soketi appKey is the public Pusher key; the VAPID key is the public
 * key). No secrets are ever returned, so caching it in memory in the browser is safe.
 */

import { fetchWithTimeout } from './network/fetch-with-timeout';

export interface RuntimeSentryConfig {
  dsn: string | null;
  environment: string | null;
  sampleRate: number | null;
}

export interface RuntimeSoketiConfig {
  host: string | null;
  port: number | null;
  /** Public Pusher-protocol key (NOT the app secret). */
  appKey: string | null;
  useTls: boolean;
  enabled: boolean;
}

export interface RuntimeWebPushConfig {
  /** Public VAPID key (NOT the private key). */
  vapidPublicKey: string | null;
  vapidSubject: string | null;
  enabled: boolean;
}

export interface RuntimePlatformConfig {
  publicWebBaseUrl: string | null;
  publicApiBaseUrl: string | null;
}

export interface RuntimeConfig {
  sentry: RuntimeSentryConfig;
  soketi: RuntimeSoketiConfig;
  webPush: RuntimeWebPushConfig;
  platform: RuntimePlatformConfig;
}

const RUNTIME_CONFIG_PATH = '/api/backend/v1/public/runtime-config';
const FETCH_TIMEOUT_MS = 8000;

function trim(value: string | null | undefined): string | null {
  if (!value) return null;
  const trimmed = value.trim();
  return trimmed.length > 0 ? trimmed : null;
}

function parseSampleRate(value: string | undefined): number | null {
  if (!value) return null;
  const parsed = Number.parseFloat(value);
  if (!Number.isFinite(parsed)) return null;
  if (parsed < 0) return 0;
  if (parsed > 1) return 1;
  return parsed;
}

/**
 * Build the default config purely from build-time `NEXT_PUBLIC_*` values. This is
 * what first paint uses before the runtime fetch resolves, and the permanent
 * fallback if the fetch fails. Every value keeps its `NEXT_PUBLIC_*` fallback so
 * boot can never break just because the runtime endpoint is slow/unreachable.
 */
export function buildFallbackRuntimeConfig(): RuntimeConfig {
  return {
    sentry: {
      dsn: trim(process.env.NEXT_PUBLIC_SENTRY_DSN),
      environment: trim(process.env.NEXT_PUBLIC_SENTRY_ENVIRONMENT),
      sampleRate: parseSampleRate(process.env.NEXT_PUBLIC_SENTRY_TRACES_SAMPLE_RATE),
    },
    soketi: {
      host: trim(process.env.NEXT_PUBLIC_SOKETI_HOST),
      port: (() => {
        const raw = trim(process.env.NEXT_PUBLIC_SOKETI_PORT);
        if (!raw) return null;
        const parsed = Number.parseInt(raw, 10);
        return Number.isFinite(parsed) ? parsed : null;
      })(),
      appKey: trim(process.env.NEXT_PUBLIC_SOKETI_APP_KEY),
      useTls: process.env.NEXT_PUBLIC_SOKETI_USE_TLS === 'true',
      enabled: process.env.NEXT_PUBLIC_SOKETI_ENABLED !== 'false',
    },
    webPush: {
      vapidPublicKey: trim(process.env.NEXT_PUBLIC_WEB_PUSH_PUBLIC_KEY),
      vapidSubject: trim(process.env.NEXT_PUBLIC_WEB_PUSH_SUBJECT),
      enabled: Boolean(trim(process.env.NEXT_PUBLIC_WEB_PUSH_PUBLIC_KEY)),
    },
    platform: {
      // Keep the API base as a minimal same-origin default — the bootstrap fetch
      // itself needs an origin and the browser is already same-origin. Do NOT make
      // the API base depend on this fetch.
      publicWebBaseUrl: trim(process.env.NEXT_PUBLIC_APP_URL) ?? trim(process.env.NEXT_PUBLIC_SITE_URL),
      publicApiBaseUrl: trim(process.env.NEXT_PUBLIC_API_BASE_URL),
    },
  };
}

/** Coerce the raw JSON response (unknown shape) into a typed RuntimeConfig, merging fallbacks. */
function normalizeResponse(raw: unknown, fallback: RuntimeConfig): RuntimeConfig {
  if (!raw || typeof raw !== 'object') return fallback;
  const r = raw as Record<string, unknown>;
  const sentry = (r.sentry ?? {}) as Record<string, unknown>;
  const soketi = (r.soketi ?? {}) as Record<string, unknown>;
  const webPush = (r.webPush ?? {}) as Record<string, unknown>;
  const platform = (r.platform ?? {}) as Record<string, unknown>;

  const str = (v: unknown): string | null =>
    typeof v === 'string' && v.trim().length > 0 ? v.trim() : null;
  const num = (v: unknown): number | null => (typeof v === 'number' && Number.isFinite(v) ? v : null);
  const bool = (v: unknown, dflt: boolean): boolean => (typeof v === 'boolean' ? v : dflt);

  return {
    sentry: {
      dsn: str(sentry.dsn) ?? fallback.sentry.dsn,
      environment: str(sentry.environment) ?? fallback.sentry.environment,
      sampleRate: num(sentry.sampleRate) ?? fallback.sentry.sampleRate,
    },
    soketi: {
      host: str(soketi.host) ?? fallback.soketi.host,
      port: num(soketi.port) ?? fallback.soketi.port,
      appKey: str(soketi.appKey) ?? fallback.soketi.appKey,
      useTls: bool(soketi.useTls, fallback.soketi.useTls),
      enabled: bool(soketi.enabled, fallback.soketi.enabled),
    },
    webPush: {
      vapidPublicKey: str(webPush.vapidPublicKey) ?? fallback.webPush.vapidPublicKey,
      vapidSubject: str(webPush.vapidSubject) ?? fallback.webPush.vapidSubject,
      enabled: bool(webPush.enabled, fallback.webPush.enabled),
    },
    platform: {
      publicWebBaseUrl: str(platform.publicWebBaseUrl) ?? fallback.platform.publicWebBaseUrl,
      publicApiBaseUrl: str(platform.publicApiBaseUrl) ?? fallback.platform.publicApiBaseUrl,
    },
  };
}

// In-memory singleton cache. Seeded with build-time fallbacks so the synchronous
// getter always returns something usable, even before the first fetch resolves.
let cachedConfig: RuntimeConfig = buildFallbackRuntimeConfig();
let inFlight: Promise<RuntimeConfig> | null = null;

/**
 * Synchronous getter — returns the last-fetched runtime config, or the build-time
 * `NEXT_PUBLIC_*` fallbacks before the first fetch resolves. Never throws, never
 * blocks. Safe to call during render / first paint.
 */
export function getRuntimeConfig(): RuntimeConfig {
  return cachedConfig;
}

/**
 * Fetch the runtime config once and cache it in memory. Concurrent callers share
 * the same in-flight promise. On any failure it resolves to the current cached
 * (fallback) value so callers never have to special-case errors.
 */
export async function fetchRuntimeConfig(signal?: AbortSignal): Promise<RuntimeConfig> {
  if (inFlight) return inFlight;

  inFlight = (async () => {
    try {
      const response = await fetchWithTimeout(
        RUNTIME_CONFIG_PATH,
        { signal, headers: { Accept: 'application/json' } },
        FETCH_TIMEOUT_MS,
      );
      if (!response.ok) {
        return cachedConfig;
      }
      const raw: unknown = await response.json();
      cachedConfig = normalizeResponse(raw, buildFallbackRuntimeConfig());
      return cachedConfig;
    } catch {
      // Network/timeout/parse failure — keep the build-time fallback so boot
      // (and Sentry) never breaks because of a slow or unreachable endpoint.
      return cachedConfig;
    } finally {
      inFlight = null;
    }
  })();

  return inFlight;
}
