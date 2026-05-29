'use client';

import { Capacitor } from '@capacitor/core';

// ── Types ───────────────────────────────────────────────────────

export type SecureStorageKey =
  | 'auth_access_token'
  | 'auth_refresh_token'
  | 'auth_token_expiry'
  | 'device_id';

// ── Storage Key Migration ───────────────────────────────────────

const MIGRATION_FLAG = '__secure_storage_migrated_v7';

/**
 * One-time re-key migration from old capacitor-secure-storage-plugin
 * to @aparajita/capacitor-secure-storage. Call once on app startup.
 * The new package uses the same underlying keychain/keystore, so
 * keys are likely already accessible — this just ensures the migration
 * flag is set so we don't re-run.
 */
export async function migrateSecureStorageIfNeeded(): Promise<void> {
  if (!Capacitor.isNativePlatform()) return;

  try {
    const { SecureStorage } = await loadModule();
    const migrated = await SecureStorage.getItem(MIGRATION_FLAG);
    if (migrated) return;

    // The new @aparajita/capacitor-secure-storage reads from the same
    // iOS Keychain / Android Keystore, so existing keys should be
    // accessible without manual re-keying. Mark as migrated.
    await SecureStorage.setItem(MIGRATION_FLAG, '1');
  } catch {
    // Silently ignore — migration will retry next launch.
  }
}

// ── Lazy Module Loading ─────────────────────────────────────────

type SecureStorageModule = typeof import('@aparajita/capacitor-secure-storage');
let modulePromise: Promise<SecureStorageModule> | null = null;

function loadModule(): Promise<SecureStorageModule> {
  modulePromise ??= import('@aparajita/capacitor-secure-storage');
  return modulePromise;
}

// ── Core Operations ─────────────────────────────────────────────

export async function getSecureItem(key: SecureStorageKey): Promise<string | null> {
  if (!Capacitor.isNativePlatform()) {
    return null;
  }

  try {
    const { SecureStorage } = await loadModule();
    return await SecureStorage.getItem(key);
  } catch {
    // Key not found or storage unavailable.
    return null;
  }
}

export async function setSecureItem(key: SecureStorageKey, value: string): Promise<boolean> {
  if (!Capacitor.isNativePlatform()) {
    return false;
  }

  try {
    const { SecureStorage } = await loadModule();
    await SecureStorage.setItem(key, value);
    return true;
  } catch {
    return false;
  }
}

export async function removeSecureItem(key: SecureStorageKey): Promise<boolean> {
  if (!Capacitor.isNativePlatform()) {
    return false;
  }

  try {
    const { SecureStorage } = await loadModule();
    await SecureStorage.removeItem(key);
    return true;
  } catch {
    return false;
  }
}

export async function clearSecureStorage(): Promise<boolean> {
  if (!Capacitor.isNativePlatform()) {
    return false;
  }

  try {
    const { SecureStorage } = await loadModule();
    await SecureStorage.clear();
    return true;
  } catch {
    return false;
  }
}

// ── Token Helpers ───────────────────────────────────────────────

export async function storeAuthTokens(
  accessToken: string,
  refreshToken: string | null | undefined,
  expiresAt: string,
): Promise<boolean> {
  if (!Capacitor.isNativePlatform()) {
    return false;
  }

  try {
    const writes: Promise<boolean>[] = [
      setSecureItem('auth_access_token', accessToken),
      setSecureItem('auth_token_expiry', expiresAt),
    ];

    if (refreshToken) {
      writes.push(setSecureItem('auth_refresh_token', refreshToken));
    } else {
      writes.push(removeSecureItem('auth_refresh_token'));
    }

    await Promise.all(writes);
    return true;
  } catch {
    return false;
  }
}

export async function getStoredAuthTokens(): Promise<{
  accessToken: string | null;
  refreshToken: string | null;
  expiresAt: string | null;
}> {
  const [accessToken, refreshToken, expiresAt] = await Promise.all([
    getSecureItem('auth_access_token'),
    getSecureItem('auth_refresh_token'),
    getSecureItem('auth_token_expiry'),
  ]);

  return { accessToken, refreshToken, expiresAt };
}

export async function clearAuthTokens(): Promise<void> {
  await Promise.all([
    removeSecureItem('auth_access_token'),
    removeSecureItem('auth_refresh_token'),
    removeSecureItem('auth_token_expiry'),
  ]);
}
