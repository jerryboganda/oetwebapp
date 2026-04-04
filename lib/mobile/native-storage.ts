'use client';

import { Capacitor } from '@capacitor/core';

type PreferencesModule = typeof import('@capacitor/preferences');

let preferencesModulePromise: Promise<PreferencesModule> | null = null;

function isBrowser() {
  return typeof window !== 'undefined';
}

function isNativePlatform() {
  return isBrowser() && Capacitor.isNativePlatform();
}

async function loadPreferencesModule(): Promise<PreferencesModule | null> {
  if (!isNativePlatform()) {
    return null;
  }

  if (!preferencesModulePromise) {
    preferencesModulePromise = import('@capacitor/preferences');
  }

  return preferencesModulePromise;
}

export async function getNativePreference(key: string): Promise<string | null> {
  try {
    const preferencesApi = await loadPreferencesModule();
    if (!preferencesApi) {
      return null;
    }

    const result = await preferencesApi.Preferences.get({ key });
    return result.value ?? null;
  } catch {
    return null;
  }
}

export async function setNativePreference(key: string, value: string): Promise<void> {
  try {
    const preferencesApi = await loadPreferencesModule();
    if (!preferencesApi) {
      return;
    }

    await preferencesApi.Preferences.set({ key, value });
  } catch {
    return;
  }
}

export async function removeNativePreference(key: string): Promise<void> {
  try {
    const preferencesApi = await loadPreferencesModule();
    if (!preferencesApi) {
      return;
    }

    await preferencesApi.Preferences.remove({ key });
  } catch {
    return;
  }
}

export async function hydrateWebStorageKey(key: string): Promise<boolean> {
  if (!isBrowser()) {
    return false;
  }

  if (window.localStorage.getItem(key) !== null || window.sessionStorage.getItem(key) !== null) {
    return true;
  }

  let value: string | null = null;

  try {
    value = await getNativePreference(key);
  } catch {
    return false;
  }

  if (value === null) {
    return false;
  }

  window.localStorage.setItem(key, value);
  return true;
}

export async function hydrateWebStorageKeys(keys: string[]): Promise<void> {
  await Promise.all(keys.map(async (key) => {
    await hydrateWebStorageKey(key);
  }));
}

export function persistWebStorageKey(key: string, value: string | null, persistence: 'local' | 'session' = 'local'): void {
  if (!isBrowser()) {
    return;
  }

  const storage = persistence === 'local' ? window.localStorage : window.sessionStorage;
  if (value === null) {
    storage.removeItem(key);
  } else {
    storage.setItem(key, value);
  }

  void (async () => {
    if (value === null) {
      await removeNativePreference(key);
      return;
    }

    await setNativePreference(key, value);
  })().catch(() => undefined);
}

export function removeWebStorageKey(key: string): void {
  if (!isBrowser()) {
    return;
  }

  window.localStorage.removeItem(key);
  window.sessionStorage.removeItem(key);

  void removeNativePreference(key).catch(() => undefined);
}

export function isNativeMobilePlatform(): boolean {
  return isNativePlatform();
}