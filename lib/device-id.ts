'use client';

import { Capacitor } from '@capacitor/core';
import { getSecureItem, setSecureItem } from '@/lib/mobile/secure-storage';

/**
 * Client device identity (Course Platform Security Requirements §3.2).
 * Generated once and persisted for the app's lifetime — web in
 * `localStorage` (clearable, which only forces re-verification on next
 * sign-in — safe-by-default, never a security hole), native via the OS
 * keychain/keystore (lib/mobile/secure-storage.ts, key 'device_id').
 * Sent as the X-OET-Device-Id header on auth requests and shared API calls
 * (`lib/auth-client.ts`, `lib/api.ts`) so refresh and protected playback can
 * enforce the same device boundary.
 */

const WEB_STORAGE_KEY = 'oet_device_id';

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
      await setSecureItem('device_id', id);
    }
    cachedDeviceId = id;
  } catch {
    // Secure storage unavailable — leave cachedDeviceId null; the header is
    // simply omitted; enforced sign-in fails closed for this request.
  }
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
    try {
      let id = window.localStorage.getItem(WEB_STORAGE_KEY);
      if (!id) {
        id = generateId();
        window.localStorage.setItem(WEB_STORAGE_KEY, id);
      }
      cachedDeviceId = id;
      return cachedDeviceId;
    } catch {
      // Storage or entropy unavailable: return null so the enforced backend
      // policy rejects the request instead of creating a fresh identity on
      // every page load.
      return null;
    }
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
