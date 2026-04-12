'use client';

import { Capacitor } from '@capacitor/core';

// ── Types ───────────────────────────────────────────────────────

export type SecureStorageKey =
  | 'auth_access_token'
  | 'auth_refresh_token'
  | 'auth_token_expiry'
  | 'device_id';

// ── Lazy Module Loading ─────────────────────────────────────────

type SecureStorageModule = typeof import('capacitor-secure-storage-plugin');
let modulePromise: Promise<SecureStorageModule> | null = null;

function loadModule(): Promise<SecureStorageModule> {
  modulePromise ??= import('capacitor-secure-storage-plugin');
  return modulePromise;
}

// ── Core Operations ─────────────────────────────────────────────

export async function getSecureItem(key: SecureStorageKey): Promise<string | null> {
  if (!Capacitor.isNativePlatform()) {
    return null;
  }

  try {
    const { SecureStoragePlugin } = await loadModule();
    const result = await SecureStoragePlugin.get({ key });
    return result.value;
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
    const { SecureStoragePlugin } = await loadModule();
    await SecureStoragePlugin.set({ key, value });
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
    const { SecureStoragePlugin } = await loadModule();
    await SecureStoragePlugin.remove({ key });
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
    const { SecureStoragePlugin } = await loadModule();
    await SecureStoragePlugin.clear();
    return true;
  } catch {
    return false;
  }
}

// ── Token Helpers ───────────────────────────────────────────────

export async function storeAuthTokens(
  accessToken: string,
  refreshToken: string,
  expiresAt: string,
): Promise<boolean> {
  if (!Capacitor.isNativePlatform()) {
    return false;
  }

  try {
    await Promise.all([
      setSecureItem('auth_access_token', accessToken),
      setSecureItem('auth_refresh_token', refreshToken),
      setSecureItem('auth_token_expiry', expiresAt),
    ]);
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
