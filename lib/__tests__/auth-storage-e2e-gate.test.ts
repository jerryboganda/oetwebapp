/**
 * IAM-05 regression gate: the E2E-only token-persistence path must be
 * unavailable in production builds. `process.env.NODE_ENV` is inlined by the
 * bundler at build time, so `lib/auth-storage.ts` folds the persistence branch
 * to `false` in production — this test pins that property directly.
 */
import { beforeEach, describe, expect, it, vi } from 'vitest';

type StorageRecord = {
  persistence: 'local' | 'session';
  session: {
    accessToken: string;
    refreshToken: string | null;
    accessTokenExpiresAt: string;
    refreshTokenExpiresAt: string;
    currentUser: unknown;
  };
};

const LOCAL_SESSION_KEY = 'oet.auth.session.local';

function fakeLocalStorage(): Storage {
  const store = new Map<string, string>();
  return {
    getItem: (key: string) => store.get(key) ?? null,
    setItem: (key: string, value: string) => void store.set(key, value),
    removeItem: (key: string) => void store.delete(key),
    clear: () => void store.clear(),
    key: () => null,
    get length() {
      return store.size;
    },
  };
}

const sessionPayload = {
  accessToken: 'secret-access-token',
  refreshToken: 'secret-refresh-token',
  accessTokenExpiresAt: new Date(Date.now() + 60_000).toISOString(),
  refreshTokenExpiresAt: new Date(Date.now() + 60_000).toISOString(),
  currentUser: { userId: 'u1', displayName: 'Test', role: 'learner' },
};

async function loadAuthStorage(nodeEnv: 'production' | 'test') {
  vi.resetModules();
  const original = process.env.NODE_ENV;
  vi.stubEnv('NODE_ENV', nodeEnv);
  try {
    return await import('@/lib/auth-storage');
  } finally {
    vi.unstubAllEnvs();
    if (original !== undefined) {
      process.env.NODE_ENV = original;
    }
  }
}

describe('auth-storage E2E token persistence (IAM-05)', () => {
  beforeEach(() => {
    window.localStorage = fakeLocalStorage();
    window.sessionStorage = fakeLocalStorage();
  });

  it('production: never persists tokens to web storage, even with the E2E flag set', async () => {
    const storage = await loadAuthStorage('production');

    // Attacker/XSS seeds the E2E flag and tries to make sign-in persist tokens.
    window.localStorage.setItem('oet.e2e.keep-tokens', '1');

    storage.saveStoredSession(sessionPayload as StorageRecord['session'], 'local');

    const persisted = JSON.parse(String(window.localStorage.getItem(LOCAL_SESSION_KEY)));
    expect(persisted.accessToken).toBeUndefined();
    expect(persisted.refreshToken).toBeUndefined();
  });

  it('production: a hand-seeded payload containing tokens is read back token-less', async () => {
    const storage = await loadAuthStorage('production');

    window.localStorage.setItem(
      LOCAL_SESSION_KEY,
      JSON.stringify({ ...sessionPayload, persistence: 'local' }),
    );

    const loaded = storage.loadStoredSession();
    expect(loaded?.accessToken).toBe('');
    expect(loaded?.refreshToken).toBeNull();
  });

  it('test: the Playwright bootstrap flag still keeps tokens (harness contract)', async () => {
    const storage = await loadAuthStorage('test');

    window.localStorage.setItem('oet.e2e.keep-tokens', '1');
    storage.saveStoredSession(sessionPayload as StorageRecord['session'], 'local');

    const persisted = JSON.parse(String(window.localStorage.getItem(LOCAL_SESSION_KEY)));
    expect(persisted.accessToken).toBe('secret-access-token');
  });
});
