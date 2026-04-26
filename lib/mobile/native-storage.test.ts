import { describe, it, expect, vi, beforeEach } from 'vitest';

// ── Mocks ──────────────────────────────────────────────────────────────────

const { isNativePlatformMock, preferencesGet, preferencesSet, preferencesRemove } = vi.hoisted(() => ({
  isNativePlatformMock: vi.fn(() => true),
  preferencesGet: vi.fn(),
  preferencesSet: vi.fn(),
  preferencesRemove: vi.fn(),
}));

vi.mock('@capacitor/core', () => ({
  Capacitor: {
    isNativePlatform: isNativePlatformMock,
    getPlatform: vi.fn(() => 'ios'),
  },
}));

vi.mock('@capacitor/preferences', () => ({
  Preferences: {
    get: preferencesGet,
    set: preferencesSet,
    remove: preferencesRemove,
  },
}));

import {
  getNativePreference,
  setNativePreference,
  removeNativePreference,
  hydrateWebStorageKey,
  hydrateWebStorageKeys,
  persistWebStorageKey,
  removeWebStorageKey,
  isNativeMobilePlatform,
} from './native-storage';

beforeEach(() => {
  isNativePlatformMock.mockReset();
  isNativePlatformMock.mockReturnValue(true);
  preferencesGet.mockReset();
  preferencesSet.mockReset();
  preferencesRemove.mockReset();
  if (typeof localStorage !== 'undefined') localStorage.clear();
  if (typeof sessionStorage !== 'undefined') sessionStorage.clear();
});

// ── isNativeMobilePlatform ─────────────────────────────────────────────────

describe('isNativeMobilePlatform', () => {
  it('returns true when Capacitor reports native', () => {
    isNativePlatformMock.mockReturnValue(true);
    expect(isNativeMobilePlatform()).toBe(true);
  });

  it('returns false when Capacitor reports web', () => {
    isNativePlatformMock.mockReturnValue(false);
    expect(isNativeMobilePlatform()).toBe(false);
  });
});

// ── getNativePreference ────────────────────────────────────────────────────

describe('getNativePreference', () => {
  it('returns null on web (skips Preferences API entirely)', async () => {
    isNativePlatformMock.mockReturnValue(false);
    expect(await getNativePreference('foo')).toBeNull();
    expect(preferencesGet).not.toHaveBeenCalled();
  });

  it('returns the stored value when present', async () => {
    preferencesGet.mockResolvedValue({ value: 'bar' });
    expect(await getNativePreference('foo')).toBe('bar');
    expect(preferencesGet).toHaveBeenCalledWith({ key: 'foo' });
  });

  it('returns null when value is undefined', async () => {
    preferencesGet.mockResolvedValue({ value: undefined });
    expect(await getNativePreference('foo')).toBeNull();
  });

  it('swallows errors and returns null', async () => {
    preferencesGet.mockRejectedValue(new Error('boom'));
    expect(await getNativePreference('foo')).toBeNull();
  });
});

// ── setNativePreference ────────────────────────────────────────────────────

describe('setNativePreference', () => {
  it('is a no-op on web', async () => {
    isNativePlatformMock.mockReturnValue(false);
    await setNativePreference('a', 'b');
    expect(preferencesSet).not.toHaveBeenCalled();
  });

  it('persists key/value on native', async () => {
    preferencesSet.mockResolvedValue(undefined);
    await setNativePreference('a', 'b');
    expect(preferencesSet).toHaveBeenCalledWith({ key: 'a', value: 'b' });
  });

  it('swallows errors', async () => {
    preferencesSet.mockRejectedValue(new Error('boom'));
    await expect(setNativePreference('a', 'b')).resolves.toBeUndefined();
  });
});

// ── removeNativePreference ─────────────────────────────────────────────────

describe('removeNativePreference', () => {
  it('is a no-op on web', async () => {
    isNativePlatformMock.mockReturnValue(false);
    await removeNativePreference('a');
    expect(preferencesRemove).not.toHaveBeenCalled();
  });

  it('removes key on native', async () => {
    preferencesRemove.mockResolvedValue(undefined);
    await removeNativePreference('a');
    expect(preferencesRemove).toHaveBeenCalledWith({ key: 'a' });
  });

  it('swallows errors', async () => {
    preferencesRemove.mockRejectedValue(new Error('boom'));
    await expect(removeNativePreference('a')).resolves.toBeUndefined();
  });
});

// ── hydrateWebStorageKey ───────────────────────────────────────────────────

describe('hydrateWebStorageKey', () => {
  it('returns true and skips native lookup when localStorage already has value', async () => {
    localStorage.setItem('k', 'cached');
    expect(await hydrateWebStorageKey('k')).toBe(true);
    expect(preferencesGet).not.toHaveBeenCalled();
  });

  it('returns true and skips native lookup when sessionStorage already has value', async () => {
    sessionStorage.setItem('k', 'cached');
    expect(await hydrateWebStorageKey('k')).toBe(true);
    expect(preferencesGet).not.toHaveBeenCalled();
  });

  it('returns false and does NOT write web storage when native value is missing', async () => {
    preferencesGet.mockResolvedValue({ value: undefined });
    expect(await hydrateWebStorageKey('k')).toBe(false);
    expect(localStorage.getItem('k')).toBeNull();
  });

  it('hydrates localStorage with native value when present', async () => {
    preferencesGet.mockResolvedValue({ value: 'native-value' });
    expect(await hydrateWebStorageKey('k')).toBe(true);
    expect(localStorage.getItem('k')).toBe('native-value');
  });

  it('returns false when getNativePreference would throw (defensive)', async () => {
    // getNativePreference itself swallows errors; so this just verifies the
    // null-fallthrough path produces a clean false.
    preferencesGet.mockRejectedValue(new Error('boom'));
    expect(await hydrateWebStorageKey('k')).toBe(false);
    expect(localStorage.getItem('k')).toBeNull();
  });
});

// ── hydrateWebStorageKeys ──────────────────────────────────────────────────

describe('hydrateWebStorageKeys', () => {
  it('hydrates each key in parallel', async () => {
    preferencesGet.mockImplementation(({ key }: { key: string }) =>
      Promise.resolve({ value: `v-${key}` }),
    );
    await hydrateWebStorageKeys(['a', 'b', 'c']);
    expect(localStorage.getItem('a')).toBe('v-a');
    expect(localStorage.getItem('b')).toBe('v-b');
    expect(localStorage.getItem('c')).toBe('v-c');
    expect(preferencesGet).toHaveBeenCalledTimes(3);
  });

  it('does not throw on empty list', async () => {
    await expect(hydrateWebStorageKeys([])).resolves.toBeUndefined();
  });
});

// ── persistWebStorageKey ───────────────────────────────────────────────────

describe('persistWebStorageKey', () => {
  it('writes to localStorage by default and mirrors to native', async () => {
    preferencesSet.mockResolvedValue(undefined);
    persistWebStorageKey('k', 'v');
    expect(localStorage.getItem('k')).toBe('v');
    // native mirror is fire-and-forget — flush microtasks
    await new Promise((r) => setTimeout(r, 0));
    expect(preferencesSet).toHaveBeenCalledWith({ key: 'k', value: 'v' });
  });

  it('writes to sessionStorage when persistence="session"', async () => {
    persistWebStorageKey('k', 'v', 'session');
    expect(sessionStorage.getItem('k')).toBe('v');
    expect(localStorage.getItem('k')).toBeNull();
  });

  it('removes from localStorage and native when value is null', async () => {
    localStorage.setItem('k', 'old');
    preferencesRemove.mockResolvedValue(undefined);
    persistWebStorageKey('k', null);
    expect(localStorage.getItem('k')).toBeNull();
    await new Promise((r) => setTimeout(r, 0));
    expect(preferencesRemove).toHaveBeenCalledWith({ key: 'k' });
    expect(preferencesSet).not.toHaveBeenCalled();
  });

  it('removes from sessionStorage when persistence="session" and value is null', () => {
    sessionStorage.setItem('k', 'old');
    persistWebStorageKey('k', null, 'session');
    expect(sessionStorage.getItem('k')).toBeNull();
  });

  it('swallows native mirror errors silently', async () => {
    preferencesSet.mockRejectedValue(new Error('boom'));
    expect(() => persistWebStorageKey('k', 'v')).not.toThrow();
    // Microtask queue flushes the rejected promise without throwing.
    await new Promise((r) => setTimeout(r, 0));
    expect(localStorage.getItem('k')).toBe('v');
  });
});

// ── removeWebStorageKey ────────────────────────────────────────────────────

describe('removeWebStorageKey', () => {
  it('clears both localStorage and sessionStorage entries', async () => {
    localStorage.setItem('k', 'l');
    sessionStorage.setItem('k', 's');
    preferencesRemove.mockResolvedValue(undefined);

    removeWebStorageKey('k');

    expect(localStorage.getItem('k')).toBeNull();
    expect(sessionStorage.getItem('k')).toBeNull();
    await new Promise((r) => setTimeout(r, 0));
    expect(preferencesRemove).toHaveBeenCalledWith({ key: 'k' });
  });

  it('does not throw if both storages are already empty', () => {
    expect(() => removeWebStorageKey('missing')).not.toThrow();
  });
});
