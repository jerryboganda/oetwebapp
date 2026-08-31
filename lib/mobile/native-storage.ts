'use client';

import { Capacitor } from '@capacitor/core';

type PreferencesModule = typeof import('@capacitor/preferences');

let preferencesModulePromise: Promise<PreferencesModule> | null = null;
const nativeMutationQueues = new Map<string, Promise<unknown>>();

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

export async function setNativePreference(key: string, value: string): Promise<boolean> {
  try {
    const preferencesApi = await loadPreferencesModule();
    if (!preferencesApi) {
      return false;
    }

    await preferencesApi.Preferences.set({ key, value });
    return true;
  } catch {
    return false;
  }
}

export async function removeNativePreference(key: string): Promise<boolean> {
  try {
    const preferencesApi = await loadPreferencesModule();
    if (!preferencesApi) {
      return false;
    }

    await preferencesApi.Preferences.remove({ key });
    return true;
  } catch {
    return false;
  }
}

function enqueueNativeMutation<T>(key: string, mutation: () => Promise<T>): Promise<T> {
  const previous = nativeMutationQueues.get(key) ?? Promise.resolve();
  const current = previous.catch(() => undefined).then(mutation);
  nativeMutationQueues.set(key, current);
  void current.then(
    () => {
      if (nativeMutationQueues.get(key) === current) {
        nativeMutationQueues.delete(key);
      }
    },
    () => {
      if (nativeMutationQueues.get(key) === current) {
        nativeMutationQueues.delete(key);
      }
    },
  );
  return current;
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

export function persistWebStorageKey(key: string, value: string | null, persistence: 'local' | 'session' = 'local'): Promise<boolean> {
  if (!isBrowser()) {
    return Promise.resolve(false);
  }

  const storage = persistence === 'local' ? window.localStorage : window.sessionStorage;
  if (value === null) {
    storage.removeItem(key);
  } else {
    storage.setItem(key, value);
  }

  if (!isNativePlatform()) {
    return Promise.resolve(true);
  }

  return enqueueNativeMutation(key, async () => {
    if (value === null) {
      return removeNativePreference(key);
    }

    return setNativePreference(key, value);
  });
}

export function removeWebStorageKey(key: string): Promise<boolean> {
  if (!isBrowser()) {
    return Promise.resolve(false);
  }

  window.localStorage.removeItem(key);
  window.sessionStorage.removeItem(key);

  if (!isNativePlatform()) {
    return Promise.resolve(true);
  }

  return enqueueNativeMutation(key, () => removeNativePreference(key));
}

export function isNativeMobilePlatform(): boolean {
  return isNativePlatform();
}
