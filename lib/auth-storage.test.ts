import { describe, it, expect, beforeEach, vi } from 'vitest';
import {
  AUTH_HINT_COOKIE,
  AUTH_INDICATOR_COOKIE,
  setAuthIndicatorCookie,
  clearAuthIndicatorCookie,
  saveStoredSession,
  clearStoredSession,
  syncAuthHintCookie,
} from './auth-storage';
import { sharedWebCookieDomain } from './device-id';
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

// jsdom runs on localhost, so the real helper returns '' (no shared domain). Each
// hint test opts in to the shared domain explicitly.
vi.mock('./device-id', () => ({
  sharedWebCookieDomain: vi.fn(() => ''),
}));

const SHARED_DOMAIN_ATTRIBUTE = '; Domain=.oetwithdrhesham.co.uk';

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

// Records every document.cookie assignment instead of storing it, so the shared
// Domain attribute can be asserted even though jsdom (localhost) would reject it.
function captureCookieWrites(): { writes: string[]; restore: () => void } {
  const writes: string[] = [];
  const spy = vi.spyOn(document, 'cookie', 'set').mockImplementation((value: string) => {
    writes.push(value);
  });
  return { writes, restore: () => spy.mockRestore() };
}

function hintWrites(writes: string[]): string[] {
  return writes.filter((write) => write.startsWith(`${AUTH_HINT_COOKIE}=`));
}

const HINT_SET_PATTERN = /^oet_signed_in=1; path=\/; max-age=2592000; SameSite=Lax(; Secure)?; Domain=\.oetwithdrhesham\.co\.uk$/;
const HINT_CLEAR_PATTERN = /^oet_signed_in=; path=\/; max-age=0; SameSite=Lax(; Secure)?; Domain=\.oetwithdrhesham\.co\.uk$/;

describe('AUTH_HINT_COOKIE signed-in hint (presentation only, read by the marketing site)', () => {
  beforeEach(() => {
    vi.mocked(sharedWebCookieDomain).mockReturnValue('');
    clearStoredSession();
    clearAllCookies();
  });

  it('equals "oet_signed_in"', () => {
    expect(AUTH_HINT_COOKIE).toBe('oet_signed_in');
  });

  it('is never written off the shared hosts (localhost, previews, Capacitor)', () => {
    const { writes, restore } = captureCookieWrites();
    try {
      setAuthIndicatorCookie();
      clearAuthIndicatorCookie();
    } finally {
      restore();
    }
    expect(hintWrites(writes)).toEqual([]);
  });

  it('is set with the constant value 1 on the shared domain, leaving oet_auth host-only', () => {
    vi.mocked(sharedWebCookieDomain).mockReturnValue(SHARED_DOMAIN_ATTRIBUTE);
    const { writes, restore } = captureCookieWrites();
    try {
      setAuthIndicatorCookie();
    } finally {
      restore();
    }
    const hint = hintWrites(writes);
    expect(hint).toHaveLength(1);
    expect(hint[0]).toMatch(HINT_SET_PATTERN);
    const indicator = writes.filter((write) => write.startsWith('oet_auth='));
    expect(indicator).toHaveLength(1);
    expect(indicator[0]).not.toContain('Domain=');
  });

  it('is cleared with the same domain and path when the indicator is cleared', () => {
    vi.mocked(sharedWebCookieDomain).mockReturnValue(SHARED_DOMAIN_ATTRIBUTE);
    const { writes, restore } = captureCookieWrites();
    try {
      clearAuthIndicatorCookie();
    } finally {
      restore();
    }
    const hint = hintWrites(writes);
    expect(hint).toHaveLength(1);
    expect(hint[0]).toMatch(HINT_CLEAR_PATTERN);
  });

  it('syncAuthHintCookie sets the hint for a session that was stored before the hint existed', () => {
    vi.mocked(sharedWebCookieDomain).mockReturnValue(SHARED_DOMAIN_ATTRIBUTE);
    saveStoredSession(fakeSession, 'local');
    clearAllCookies();
    const { writes, restore } = captureCookieWrites();
    try {
      syncAuthHintCookie();
    } finally {
      restore();
    }
    const hint = hintWrites(writes);
    expect(hint).toHaveLength(1);
    expect(hint[0]).toMatch(HINT_SET_PATTERN);
  });

  it('syncAuthHintCookie is idempotent: no write when the hint already matches', () => {
    vi.mocked(sharedWebCookieDomain).mockReturnValue(SHARED_DOMAIN_ATTRIBUTE);
    saveStoredSession(fakeSession, 'local');
    document.cookie = 'oet_signed_in=1; path=/';
    const { writes, restore } = captureCookieWrites();
    try {
      syncAuthHintCookie();
      syncAuthHintCookie();
    } finally {
      restore();
    }
    expect(hintWrites(writes)).toEqual([]);
  });

  it('syncAuthHintCookie drops a stale hint when no session is stored', () => {
    vi.mocked(sharedWebCookieDomain).mockReturnValue(SHARED_DOMAIN_ATTRIBUTE);
    document.cookie = 'oet_signed_in=1; path=/';
    const { writes, restore } = captureCookieWrites();
    try {
      syncAuthHintCookie();
    } finally {
      restore();
    }
    const hint = hintWrites(writes);
    expect(hint).toHaveLength(1);
    expect(hint[0]).toMatch(HINT_CLEAR_PATTERN);
  });

  it('syncAuthHintCookie does nothing when signed out and no hint exists', () => {
    vi.mocked(sharedWebCookieDomain).mockReturnValue(SHARED_DOMAIN_ATTRIBUTE);
    const { writes, restore } = captureCookieWrites();
    try {
      syncAuthHintCookie();
    } finally {
      restore();
    }
    expect(hintWrites(writes)).toEqual([]);
  });
});
