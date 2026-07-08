import { getAppRuntimeKind } from './runtime-signals';

/**
 * The app "shell" a request originates from. `web` is a plain browser (never
 * gated); the others are native shells that report a real installed version.
 */
export type ClientPlatform = 'android' | 'ios' | 'desktop' | 'web';

export interface ClientIdentity {
  platform: ClientPlatform;
  /** Installed shell version (semver), or null when unknown/web. */
  version: string | null;
}

let cached: ClientIdentity | null = null;
let inflight: Promise<ClientIdentity> | null = null;

/**
 * Synchronous best-effort read for hot paths like request headers. Returns
 * null until {@link resolveClientIdentity} has run once at boot. When null,
 * no version headers are attached and the request is treated as un-gated.
 */
export function getClientIdentitySnapshot(): ClientIdentity | null {
  return cached;
}

/**
 * Resolves (and memoizes) the shell platform + installed version. Safe to call
 * repeatedly — the async work runs at most once. On the server, or in a plain
 * browser, resolves to `{ platform: 'web', version: null }`.
 */
export function resolveClientIdentity(): Promise<ClientIdentity> {
  if (cached) return Promise.resolve(cached);
  if (inflight) return inflight;

  inflight = computeIdentity()
    .then((identity) => {
      cached = identity;
      inflight = null;
      return identity;
    })
    .catch(() => {
      inflight = null;
      const fallback: ClientIdentity = { platform: 'web', version: null };
      cached = fallback;
      return fallback;
    });

  return inflight;
}

async function computeIdentity(): Promise<ClientIdentity> {
  if (typeof window === 'undefined') {
    return { platform: 'web', version: null };
  }

  const kind = getAppRuntimeKind();

  if (kind === 'desktop') {
    try {
      const info = await window.desktopBridge?.runtime?.info?.();
      const version = info && typeof info.appVersion === 'string' && info.appVersion.length > 0
        ? info.appVersion
        : null;
      return { platform: 'desktop', version };
    } catch {
      return { platform: 'desktop', version: null };
    }
  }

  if (kind === 'capacitor-native') {
    try {
      const { getAppVersion } = await import('./mobile/forced-update');
      const info = await getAppVersion();
      const platform: ClientPlatform = info?.platform === 'ios' ? 'ios' : 'android';
      return { platform, version: info?.currentVersion ?? null };
    } catch {
      return { platform: 'android', version: null };
    }
  }

  return { platform: 'web', version: null };
}
