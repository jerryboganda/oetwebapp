import { beforeEach, describe, expect, it, vi } from 'vitest';
import { persistWebStorageKey } from './native-storage';

const { mockPreferenceSet } = vi.hoisted(() => ({
  mockPreferenceSet: vi.fn(),
}));

vi.mock('@capacitor/core', () => ({
  Capacitor: {
    isNativePlatform: vi.fn(() => true),
  },
}));

vi.mock('@capacitor/preferences', () => ({
  Preferences: {
    get: vi.fn(async () => ({ value: null })),
    set: mockPreferenceSet,
    remove: vi.fn(async () => undefined),
  },
}));

describe('native-storage', () => {
  beforeEach(() => {
    window.localStorage.clear();
    window.sessionStorage.clear();
    mockPreferenceSet.mockReset();
  });

  it('serializes writes to the same Preferences key so the newest value wins', async () => {
    const pendingWrites: Array<{ value: string; resolve: () => void }> = [];
    let storedValue: string | null = null;
    mockPreferenceSet.mockImplementation(({ value }: { value: string }) => new Promise<void>((resolve) => {
      pendingWrites.push({
        value,
        resolve: () => {
          storedValue = value;
          resolve();
        },
      });
    }));

    const firstWrite = persistWebStorageKey('otp-challenge', 'old', 'session');
    const secondWrite = persistWebStorageKey('otp-challenge', 'new', 'session');

    await vi.waitFor(() => expect(pendingWrites).toHaveLength(1));
    pendingWrites[0].resolve();
    await vi.waitFor(() => expect(pendingWrites).toHaveLength(2));
    pendingWrites[1].resolve();

    await Promise.all([firstWrite, secondWrite]);
    expect(storedValue).toBe('new');
  });

  it('reports a native Preferences write failure to security-sensitive callers', async () => {
    mockPreferenceSet.mockRejectedValueOnce(new Error('native storage unavailable'));

    await expect(persistWebStorageKey('otp-challenge', 'marked', 'session')).resolves.toBe(false);
  });
});
