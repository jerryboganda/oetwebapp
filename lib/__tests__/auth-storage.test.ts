import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest';

const {
  mockHydrateWebStorageKeys,
  mockIsNativeMobilePlatform,
  mockPersistWebStorageKey,
  mockRemoveWebStorageKey,
  mockGetStoredAuthTokens,
  mockStoreAuthTokens,
  mockClearAuthTokens,
} = vi.hoisted(() => ({
  mockHydrateWebStorageKeys: vi.fn(async () => undefined),
  mockIsNativeMobilePlatform: vi.fn(() => false),
  mockPersistWebStorageKey: vi.fn(),
  mockRemoveWebStorageKey: vi.fn(),
  mockGetStoredAuthTokens: vi.fn(async () => ({
    accessToken: null,
    refreshToken: null,
    expiresAt: null,
  })),
  mockStoreAuthTokens: vi.fn(async () => undefined),
  mockClearAuthTokens: vi.fn(async () => undefined),
}));

vi.mock('../mobile/native-storage', () => ({
  hydrateWebStorageKeys: mockHydrateWebStorageKeys,
  isNativeMobilePlatform: mockIsNativeMobilePlatform,
  persistWebStorageKey: mockPersistWebStorageKey,
  removeWebStorageKey: mockRemoveWebStorageKey,
}));

vi.mock('../mobile/secure-storage', () => ({
  getStoredAuthTokens: mockGetStoredAuthTokens,
  storeAuthTokens: mockStoreAuthTokens,
  clearAuthTokens: mockClearAuthTokens,
}));

import {
  AUTH_INDICATOR_COOKIE,
  setAuthIndicatorCookie,
  clearAuthIndicatorCookie,
  saveStoredSession,
  loadStoredSession,
  loadStoredSessionRecord,
  updateStoredUser,
  clearStoredSession,
  savePendingMfaChallenge,
  loadPendingMfaChallenge,
  clearPendingMfaChallenge,
  hydrateAuthStorage,
} from '../auth-storage';
import type { AuthSession, CurrentUser } from '../types/auth';

const sampleUser: CurrentUser = {
  userId: 'u-1',
  email: 'a@b.test',
  displayName: 'Tester',
  isEmailVerified: true,
  role: 'learner',
  permissions: [],
} as unknown as CurrentUser;

const sampleSession: AuthSession = {
  accessToken: 'tok-access',
  refreshToken: 'tok-refresh',
  accessTokenExpiresAt: '2030-01-01T00:00:00Z',
  refreshTokenExpiresAt: '2030-02-01T00:00:00Z',
  currentUser: sampleUser,
};

function clearCookies() {
  document.cookie.split(';').forEach((c) => {
    const name = c.split('=')[0]?.trim();
    if (name) document.cookie = `${name}=; path=/; max-age=0`;
  });
}

describe('auth-storage', () => {
  beforeEach(() => {
    [
      mockHydrateWebStorageKeys,
      mockIsNativeMobilePlatform,
      mockPersistWebStorageKey,
      mockRemoveWebStorageKey,
      mockGetStoredAuthTokens,
      mockStoreAuthTokens,
      mockClearAuthTokens,
    ].forEach((m) => m.mockClear());
    mockIsNativeMobilePlatform.mockReturnValue(false);
    window.localStorage.clear();
    window.sessionStorage.clear();
    clearCookies();
    // Reset the volatile module-level cache by clearing any previously saved
    // session via the public API.
    clearStoredSession();
    [mockClearAuthTokens, mockRemoveWebStorageKey].forEach((m) => m.mockClear());
  });

  afterEach(() => {
    clearCookies();
  });

  describe('cookie helpers', () => {
    it('setAuthIndicatorCookie sets the indicator cookie with SameSite=Lax', () => {
      setAuthIndicatorCookie();
      expect(document.cookie).toContain(`${AUTH_INDICATOR_COOKIE}=1`);
    });

    it('clearAuthIndicatorCookie removes it', () => {
      setAuthIndicatorCookie();
      clearAuthIndicatorCookie();
      expect(document.cookie).not.toContain(`${AUTH_INDICATOR_COOKIE}=1`);
    });
  });

  describe('saveStoredSession + loadStoredSession (volatile)', () => {
    it('persists then returns the saved session including access token (volatile)', () => {
      saveStoredSession(sampleSession, 'local');
      const loaded = loadStoredSession();
      expect(loaded).not.toBeNull();
      expect(loaded?.accessToken).toBe('tok-access');
      expect(loaded?.currentUser.userId).toBe('u-1');
    });

    it('persistWebStorageKey is called WITHOUT the access/refresh token in the snapshot', () => {
      saveStoredSession(sampleSession, 'local');
      expect(mockPersistWebStorageKey).toHaveBeenCalledTimes(1);
      const [, payload] = mockPersistWebStorageKey.mock.calls[0]!;
      const parsed = JSON.parse(payload as string);
      expect(parsed).not.toHaveProperty('accessToken');
      expect(parsed).not.toHaveProperty('refreshToken');
      expect(parsed.currentUser.userId).toBe('u-1');
    });

    it('removes the OTHER persistence key when saving', () => {
      saveStoredSession(sampleSession, 'local');
      expect(mockRemoveWebStorageKey).toHaveBeenCalledWith('oet.auth.session.session');

      mockRemoveWebStorageKey.mockClear();
      saveStoredSession(sampleSession, 'session');
      expect(mockRemoveWebStorageKey).toHaveBeenCalledWith('oet.auth.session.local');
    });

    it('sets the indicator cookie on save', () => {
      saveStoredSession(sampleSession, 'local');
      expect(document.cookie).toContain(AUTH_INDICATOR_COOKIE);
    });

    it('only stores native tokens when isNativeMobilePlatform()', () => {
      saveStoredSession(sampleSession, 'local');
      expect(mockStoreAuthTokens).not.toHaveBeenCalled();

      clearStoredSession();
      mockIsNativeMobilePlatform.mockReturnValue(true);
      saveStoredSession(sampleSession, 'local');
      expect(mockStoreAuthTokens).toHaveBeenCalledWith(
        'tok-access',
        'tok-refresh',
        '2030-01-01T00:00:00Z',
      );
    });
  });

  describe('loadStoredSessionRecord (cold path / no volatile)', () => {
    it('returns null when nothing has been saved', () => {
      expect(loadStoredSession()).toBeNull();
    });

    it('reads back from sessionStorage on a cold load', () => {
      // Simulate a prior save that populated only the persisted snapshot.
      const snapshot = JSON.stringify({
        accessTokenExpiresAt: sampleSession.accessTokenExpiresAt,
        refreshTokenExpiresAt: sampleSession.refreshTokenExpiresAt,
        currentUser: sampleSession.currentUser,
      });
      window.sessionStorage.setItem('oet.auth.session.session', snapshot);
      // Volatile cache is empty after clearStoredSession() in beforeEach.
      const record = loadStoredSessionRecord();
      expect(record?.persistence).toBe('session');
      // Cold load has no access token (it was never persisted).
      expect(record?.session.accessToken).toBe('');
      expect(record?.session.currentUser.userId).toBe('u-1');
    });

    it('prefers session-storage over local-storage when both exist', () => {
      window.sessionStorage.setItem(
        'oet.auth.session.session',
        JSON.stringify({
          accessTokenExpiresAt: 'session-exp',
          refreshTokenExpiresAt: null,
          currentUser: { ...sampleUser, userId: 'session-user' },
        }),
      );
      window.localStorage.setItem(
        'oet.auth.session.local',
        JSON.stringify({
          accessTokenExpiresAt: 'local-exp',
          refreshTokenExpiresAt: null,
          currentUser: { ...sampleUser, userId: 'local-user' },
        }),
      );
      const r = loadStoredSessionRecord();
      expect(r?.persistence).toBe('session');
      expect(r?.session.currentUser.userId).toBe('session-user');
    });

    it('returns null when stored snapshot is invalid JSON', () => {
      window.localStorage.setItem('oet.auth.session.local', '{not-json');
      expect(loadStoredSession()).toBeNull();
    });
  });

  describe('updateStoredUser', () => {
    it('returns null when no session exists', () => {
      expect(updateStoredUser(sampleUser)).toBeNull();
    });

    it('replaces the currentUser while preserving everything else', () => {
      saveStoredSession(sampleSession, 'local');
      const newUser = { ...sampleUser, displayName: 'Renamed' };
      const updated = updateStoredUser(newUser as CurrentUser);
      expect(updated?.currentUser.displayName).toBe('Renamed');
      expect(updated?.accessToken).toBe('tok-access');
    });
  });

  describe('clearStoredSession', () => {
    it('removes both persistence keys, clears native tokens, and clears the cookie', () => {
      saveStoredSession(sampleSession, 'local');
      mockRemoveWebStorageKey.mockClear();
      mockClearAuthTokens.mockClear();

      clearStoredSession();

      expect(mockRemoveWebStorageKey).toHaveBeenCalledWith('oet.auth.session.local');
      expect(mockRemoveWebStorageKey).toHaveBeenCalledWith('oet.auth.session.session');
      expect(mockClearAuthTokens).toHaveBeenCalled();
      expect(document.cookie).not.toContain(`${AUTH_INDICATOR_COOKIE}=1`);
      expect(loadStoredSession()).toBeNull();
    });
  });

  describe('pending MFA challenge', () => {
    const challenge = {
      challengeId: 'c1',
      method: 'totp' as const,
      expiresAt: '2030-01-01T00:00:00Z',
    };

    it('saves to session storage', () => {
      savePendingMfaChallenge(challenge as never);
      expect(mockPersistWebStorageKey).toHaveBeenCalledWith(
        'oet.auth.challenge.mfa',
        JSON.stringify(challenge),
        'session',
      );
    });

    it('reads back and JSON-parses the challenge', () => {
      window.sessionStorage.setItem('oet.auth.challenge.mfa', JSON.stringify(challenge));
      expect(loadPendingMfaChallenge()).toEqual(challenge);
    });

    it('falls back to localStorage when not in sessionStorage', () => {
      window.localStorage.setItem('oet.auth.challenge.mfa', JSON.stringify(challenge));
      expect(loadPendingMfaChallenge()).toEqual(challenge);
    });

    it('returns null on invalid JSON', () => {
      window.sessionStorage.setItem('oet.auth.challenge.mfa', '{nope');
      expect(loadPendingMfaChallenge()).toBeNull();
    });

    it('clearPendingMfaChallenge removes the key', () => {
      clearPendingMfaChallenge();
      expect(mockRemoveWebStorageKey).toHaveBeenCalledWith('oet.auth.challenge.mfa');
    });
  });

  describe('hydrateAuthStorage', () => {
    it('hydrates web storage keys then exits early when no record', async () => {
      await hydrateAuthStorage();
      expect(mockHydrateWebStorageKeys).toHaveBeenCalledWith([
        'oet.auth.session.local',
        'oet.auth.session.session',
        'oet.auth.challenge.mfa',
      ]);
      expect(mockGetStoredAuthTokens).not.toHaveBeenCalled();
    });

    it('does not touch native tokens when not on a native mobile platform', async () => {
      window.localStorage.setItem(
        'oet.auth.session.local',
        JSON.stringify({
          accessTokenExpiresAt: 'x',
          refreshTokenExpiresAt: null,
          currentUser: sampleUser,
        }),
      );
      await hydrateAuthStorage();
      expect(mockGetStoredAuthTokens).not.toHaveBeenCalled();
    });

    it('rehydrates the access token from secure storage when on native', async () => {
      window.localStorage.setItem(
        'oet.auth.session.local',
        JSON.stringify({
          accessTokenExpiresAt: 'old-exp',
          refreshTokenExpiresAt: null,
          currentUser: sampleUser,
        }),
      );
      mockIsNativeMobilePlatform.mockReturnValue(true);
      mockGetStoredAuthTokens.mockResolvedValue({
        accessToken: 'native-access',
        refreshToken: 'native-refresh',
        expiresAt: 'new-exp',
      });

      await hydrateAuthStorage();

      const session = loadStoredSession();
      expect(session?.accessToken).toBe('native-access');
      expect(session?.refreshToken).toBe('native-refresh');
      expect(session?.accessTokenExpiresAt).toBe('new-exp');
    });

    it('does NOT overwrite the session when secure storage has no access token', async () => {
      window.localStorage.setItem(
        'oet.auth.session.local',
        JSON.stringify({
          accessTokenExpiresAt: 'old-exp',
          refreshTokenExpiresAt: null,
          currentUser: sampleUser,
        }),
      );
      mockIsNativeMobilePlatform.mockReturnValue(true);
      mockGetStoredAuthTokens.mockResolvedValue({
        accessToken: null,
        refreshToken: null,
        expiresAt: null,
      });

      await hydrateAuthStorage();
      const session = loadStoredSession();
      // Cold-load reconstruction has empty access token but original expiry preserved.
      expect(session?.accessToken).toBe('');
      expect(session?.accessTokenExpiresAt).toBe('old-exp');
    });
  });
});
