import { describe, it, expect, beforeEach, vi } from 'vitest';
import {
  AUTH_INDICATOR_COOKIE,
  setAuthIndicatorCookie,
  clearAuthIndicatorCookie,
  saveStoredSession,
  clearStoredSession,
} from './auth-storage';
import { persistWebStorageKey } from './mobile/native-storage';
import type { AuthSession } from './types/auth';

vi.mock('./mobile/native-storage', () => ({
  hydrateWebStorageKeys: vi.fn(),
  isNativeMobilePlatform: vi.fn(() => false),
  persistWebStorageKey: vi.fn(),
  removeWebStorageKey: vi.fn(),
}));

vi.mock('./mobile/secure-storage', () => ({
  clearAuthTokens: vi.fn(async () => undefined),
  getStoredAuthTokens: vi.fn(),
  storeAuthTokens: vi.fn(),
}));

function getCookie(name: string): string | undefined {
  const match = document.cookie.match(new RegExp(`(?:^|; )${name}=([^;]*)`));
  return match ? match[1] : undefined;
}

function clearAllCookies() {
  document.cookie.split(';').forEach((c) => {
    const name = c.trim().split('=')[0];
    document.cookie = `${name}=; max-age=0; path=/`;
  });
}

const fakeSession: AuthSession = {
  accessToken: 'tok',
  refreshToken: 'ref',
  accessTokenExpiresAt: new Date(Date.now() + 60_000).toISOString(),
  refreshTokenExpiresAt: new Date(Date.now() + 600_000).toISOString(),
  currentUser: {
    userId: '1',
    email: 'a@b.com',
    displayName: 'A B',
    role: 'learner',
    isEmailVerified: true,
    isAuthenticatorEnabled: false,
    requiresEmailVerification: false,
    requiresMfa: false,
    emailVerifiedAt: null,
    authenticatorEnabledAt: null,
  },
};

describe('AUTH_INDICATOR_COOKIE constant', () => {
  it('equals "oet_auth"', () => {
    expect(AUTH_INDICATOR_COOKIE).toBe('oet_auth');
  });
});

describe('setAuthIndicatorCookie', () => {
  beforeEach(clearAllCookies);

  it('sets oet_auth=1 cookie', () => {
    setAuthIndicatorCookie();
    expect(getCookie('oet_auth')).toBe('1');
  });
});

describe('clearAuthIndicatorCookie', () => {
  beforeEach(clearAllCookies);

  it('removes the oet_auth cookie', () => {
    setAuthIndicatorCookie();
    expect(getCookie('oet_auth')).toBe('1');
    clearAuthIndicatorCookie();
    expect(getCookie('oet_auth')).toBeUndefined();
  });
});

describe('saveStoredSession sets cookie', () => {
  beforeEach(() => {
    clearAllCookies();
    vi.mocked(persistWebStorageKey).mockClear();
  });

  it('sets auth indicator cookie when saving session', () => {
    saveStoredSession(fakeSession, 'local');
    expect(getCookie('oet_auth')).toBe('1');
  });

  it('persists only a sanitized session snapshot to web storage', () => {
    saveStoredSession(fakeSession, 'local');

    const persistedPayload = JSON.parse(String(vi.mocked(persistWebStorageKey).mock.calls[0]?.[1]));
    expect(persistedPayload).not.toHaveProperty('accessToken');
    expect(persistedPayload).not.toHaveProperty('refreshToken');
    expect(persistedPayload.currentUser.email).toBe(fakeSession.currentUser.email);
  });
});

describe('clearStoredSession clears cookie', () => {
  beforeEach(clearAllCookies);

  it('clears auth indicator cookie when clearing session', () => {
    setAuthIndicatorCookie();
    expect(getCookie('oet_auth')).toBe('1');
    clearStoredSession();
    expect(getCookie('oet_auth')).toBeUndefined();
  });
});
