'use client';

import { Capacitor } from '@capacitor/core';
import { getSecureItem, setSecureItem } from '@/lib/mobile/secure-storage';

/**
 * Client device identity (Course Platform Security Requirements §3.2).
 * Generated once and persisted for the app's lifetime — web in `localStorage`
 * plus a shared first-party cookie (the cookie can survive browser implementations
 * that clear transient in-app-browser localStorage and remains available when
 * the learner moves between the app's own web subdomains), native via the OS
 * keychain/keystore (lib/mobile/secure-storage.ts, key 'device_id'). Clearing
 * all site data still intentionally forces re-verification on the next sign-in.
 * Sent as the X-OET-Device-Id header on auth requests and shared API calls
 * (`lib/auth-client.ts`, `lib/api.ts`) so refresh and protected playback can
 * enforce the same device boundary.
 */

const WEB_STORAGE_KEY = 'oet_device_id';
const WEB_COOKIE_KEY = 'oet_device_id';
const WEB_COOKIE_MAX_AGE_SECONDS = 60 * 60 * 24 * 365;
const SHARED_WEB_COOKIE_DOMAIN = 'oetwithdrhesham.co.uk';
const SHARED_WEB_COOKIE_HOSTS = new Set([
  SHARED_WEB_COOKIE_DOMAIN,
  `www.${SHARED_WEB_COOKIE_DOMAIN}`,
  `app.${SHARED_WEB_COOKIE_DOMAIN}`,
]);

let cachedDeviceId: string | null = null;
let nativeInitPromise: Promise<void> | null = null;

function generateId(): string {
  if (typeof globalThis.crypto?.randomUUID === 'function') {
    return globalThis.crypto.randomUUID();
  }
  const bytes = new Uint8Array(16);
  globalThis.crypto.getRandomValues(bytes);
  return Array.from(bytes, (byte) => byte.toString(16).padStart(2, '0')).join('');
}

async function initNativeDeviceId(): Promise<void> {
  try {
    let id = await getSecureItem('device_id');
    if (!id) {
      id = generateId();
      const persisted = await setSecureItem('device_id', id);
      if (!persisted) {
        return;
      }
    }
    cachedDeviceId = id;
  } catch {
    // Secure storage unavailable — leave cachedDeviceId null; the header is
    // simply omitted; enforced sign-in fails closed for this request.
  }
}

function readWebDeviceCookie(): string | null {
  if (typeof document === 'undefined') return null;

  const match = document.cookie.match(new RegExp(`(?:^|;\\s*)${WEB_COOKIE_KEY}=([^;]+)`));
  if (!match) return null;

  try {
    return decodeURIComponent(match[1]) || null;
  } catch {
    return null;
  }
}

function sharedWebCookieDomain(): string {
  if (typeof window === 'undefined') return '';

  const hostname = window.location.hostname.toLowerCase();
  if (SHARED_WEB_COOKIE_HOSTS.has(hostname)) {
    return `; Domain=.${SHARED_WEB_COOKIE_DOMAIN}`;
  }

  return '';
}

function persistWebDeviceId(id: string): boolean {
  let persisted = false;
  try {
    window.localStorage.setItem(WEB_STORAGE_KEY, id);
    persisted = window.localStorage.getItem(WEB_STORAGE_KEY) === id;
  } catch {
    // The first-party cookie below is the durable fallback for browsers that
    // expose a transient or unavailable localStorage implementation.
  }

  try {
    const secure = window.location.protocol === 'https:' ? '; Secure' : '';
    const domain = sharedWebCookieDomain();
    // Remove a host-only cookie left by older builds before writing the shared
    // domain cookie. Without this migration, browsers can return two values
    // for the same name and the identity would appear to change randomly.
    if (domain) {
      document.cookie = `${WEB_COOKIE_KEY}=; Max-Age=0; Path=/`;
    }
    document.cookie = `${WEB_COOKIE_KEY}=${encodeURIComponent(id)}; Path=/; Max-Age=${WEB_COOKIE_MAX_AGE_SECONDS}; SameSite=Lax${secure}${domain}`;
    persisted ||= readWebDeviceCookie() === id;
  } catch {
    // Continue with the localStorage result, if one was available.
  }

  return persisted;
}

/**
 * Best-effort synchronous read. On web this resolves (and persists) inline
 * on first call. On native, secure storage is async — the first call(s)
 * before initialization completes return null; request-safe callers await the
 * initialization and enforced sign-in fails closed if storage cannot provide
 * an identity.
 */
export function getDeviceId(): string | null {
  if (cachedDeviceId) return cachedDeviceId;
  if (typeof window === 'undefined') return null;

  if (!Capacitor.isNativePlatform()) {
    let id: string | null = null;
    try {
      id = window.localStorage.getItem(WEB_STORAGE_KEY);
    } catch {
      // Continue to the first-party cookie fallback below.
    }

    id ??= readWebDeviceCookie();
    const hadPersistedId = Boolean(id);
    if (!id) {
      try {
        id = generateId();
      } catch {
        // Storage or entropy unavailable: return null so the enforced backend
        // policy rejects the request instead of creating a fresh identity on
        // every page load.
        return null;
      }
    }

    if (!persistWebDeviceId(id) && !hadPersistedId) {
      // Do not bootstrap a new server-side identity if this browser cannot
      // retain it across requests. The next attempt must fail closed instead
      // of creating a fresh trusted-device row on every page load.
      return null;
    }
    cachedDeviceId = id;
    return cachedDeviceId;
  }

  nativeInitPromise ??= initNativeDeviceId();
  return null;
}

/**
 * Request-safe device identity. Native secure storage is asynchronous, so
 * security-boundary calls await the first keychain/keystore lookup instead of
 * silently omitting the header during app startup.
 */
export async function getDeviceIdForRequest(): Promise<string | null> {
  const immediate = getDeviceId();
  if (immediate || typeof window === 'undefined' || !Capacitor.isNativePlatform()) {
    return immediate;
  }

  nativeInitPromise ??= initNativeDeviceId();
  await nativeInitPromise;
  return cachedDeviceId;
}
