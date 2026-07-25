'use client';

import { Capacitor } from '@capacitor/core';
import { getSecureItem, setSecureItem } from '@/lib/mobile/secure-storage';

/**
 * Client device identity (Course Platform Security Requirements §3.2).
 * Generated once and persisted for the app's lifetime — web in
 * `localStorage` (clearable, which only forces re-verification on next
 * sign-in — safe-by-default, never a security hole), native via the OS
 * keychain/keystore (lib/mobile/secure-storage.ts, key 'device_id').
 * Sent as the X-OET-Device-Id header on every auth request
 * (lib/auth-client.ts); the backend treats an absent header as "old client,
 * skip enforcement" so shipping this ahead of enabling
 * SecurityTrustedDeviceRequired is always safe.
 */

const WEB_STORAGE_KEY = 'oet_device_id';

let cachedDeviceId: string | null = null;
let nativeInitStarted = false;

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
    // simply omitted and the backend fails open for this request.
  }
}

/**
 * Best-effort synchronous read. On web this resolves (and persists) inline
 * on first call. On native, secure storage is async — the first call(s)
 * before initialization completes return null (header omitted, backend
 * fails open); once `initNativeDeviceId` resolves, subsequent calls return
 * the cached id.
 */
export function getDeviceId(): string | null {
  if (cachedDeviceId) return cachedDeviceId;
  if (typeof window === 'undefined') return null;

  if (!Capacitor.isNativePlatform()) {
    let id = window.localStorage.getItem(WEB_STORAGE_KEY);
    if (!id) {
      id = generateId();
      try {
        window.localStorage.setItem(WEB_STORAGE_KEY, id);
      } catch {
        // Storage unavailable (private browsing quirks) — still usable for
        // this page load, just not persisted across sessions.
      }
    }
    cachedDeviceId = id;
    return cachedDeviceId;
  }

  if (!nativeInitStarted) {
    nativeInitStarted = true;
    void initNativeDeviceId();
  }
  return null;
}
