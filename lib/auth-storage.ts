import type { AuthSession, CurrentUser, PendingMfaChallenge } from './types/auth';
import {
  hydrateWebStorageKeys,
  isNativeMobilePlatform,
  persistWebStorageKey,
  removeWebStorageKey,
} from './mobile/native-storage';
import { clearAuthTokens, getStoredAuthTokens, storeAuthTokens } from './mobile/secure-storage';

export type AuthPersistence = 'local' | 'session';

const LOCAL_SESSION_KEY = 'oet.auth.session.local';
const SESSION_SESSION_KEY = 'oet.auth.session.session';
const MFA_CHALLENGE_KEY = 'oet.auth.challenge.mfa';

export const AUTH_INDICATOR_COOKIE = 'oet_auth';

export function setAuthIndicatorCookie(): void {
  if (typeof document === 'undefined') return;
  const secure = typeof location !== 'undefined' && location.protocol === 'https:' ? '; Secure' : '';
  document.cookie = `${AUTH_INDICATOR_COOKIE}=1; path=/; max-age=2592000; SameSite=Lax${secure}`;
}

export function clearAuthIndicatorCookie(): void {
  if (typeof document === 'undefined') return;
  document.cookie = `${AUTH_INDICATOR_COOKIE}=; path=/; max-age=0; SameSite=Lax`;
}

interface StoredSessionRecord {
  persistence: AuthPersistence;
  session: AuthSession;
}

type PersistedSessionSnapshot = Omit<AuthSession, 'accessToken' | 'refreshToken'>;

let volatileSessionRecord: StoredSessionRecord | null = null;

function isBrowser() {
  return typeof window !== 'undefined';
}

function parseJson<T>(value: string | null): T | null {
  if (!value) {
    return null;
  }

  try {
    return JSON.parse(value) as T;
  } catch {
    return null;
  }
}

function getStorage(persistence: AuthPersistence): Storage | null {
  if (!isBrowser()) {
    return null;
  }

  return persistence === 'local' ? window.localStorage : window.sessionStorage;
}

const E2E_KEEP_TOKENS_KEY = 'oet.e2e.keep-tokens';

function shouldKeepTokensForE2E(): boolean {
  // Only true when the Playwright bootstrap explicitly seeds this flag into
  // localStorage. Production sign-in flows never set this key, so production
  // behavior (XSS hardening: tokens never persisted to web storage) is
  // unchanged. The flag exists so a single shared `storageState` file can
  // serve many test contexts: without it, the first cold load triggers a
  // single-use refresh-token rotation that invalidates every other test that
  // shares the same persisted `oet_rt`.
  if (!isBrowser()) return false;
  try {
    return window.localStorage.getItem(E2E_KEEP_TOKENS_KEY) === '1';
  } catch {
    return false;
  }
}

function toPersistedSnapshot(session: AuthSession): PersistedSessionSnapshot {
  const base: PersistedSessionSnapshot = {
    accessTokenExpiresAt: session.accessTokenExpiresAt,
    refreshTokenExpiresAt: session.refreshTokenExpiresAt,
    currentUser: session.currentUser,
  };
  if (shouldKeepTokensForE2E()) {
    return {
      ...base,
      accessToken: session.accessToken,
      refreshToken: session.refreshToken,
    } as PersistedSessionSnapshot & { accessToken?: string; refreshToken?: string | null };
  }
  return base;
}

function fromPersistedSnapshot(snapshot: PersistedSessionSnapshot): AuthSession {
  // Production paths persist via `toPersistedSnapshot`, which intentionally
  // strips access/refresh tokens (browser-side XSS hardening). Test bootstraps
  // (Playwright storageState) write the full session payload directly into
  // localStorage. When those extra fields are present at runtime we honor
  // them so the auth manager can avoid an immediate /v1/auth/refresh round
  // trip — important because the backend rotates refresh tokens single-use,
  // so a single shared storageState cannot survive multiple test contexts
  // that each refresh on cold load. Reading the persisted access token costs
  // nothing in production where the field is simply absent.
  const extras = snapshot as PersistedSessionSnapshot & {
    accessToken?: unknown;
    refreshToken?: unknown;
  };
  const persistedAccessToken = typeof extras.accessToken === 'string' ? extras.accessToken : '';
  const persistedRefreshToken = typeof extras.refreshToken === 'string' ? extras.refreshToken : null;

  return {
    accessToken: persistedAccessToken,
    refreshToken: persistedRefreshToken,
    accessTokenExpiresAt: snapshot.accessTokenExpiresAt,
    refreshTokenExpiresAt: snapshot.refreshTokenExpiresAt,
    currentUser: snapshot.currentUser,
  };
}

export function saveStoredSession(session: AuthSession, persistence: AuthPersistence): void {
  const key = persistence === 'local' ? LOCAL_SESSION_KEY : SESSION_SESSION_KEY;
  volatileSessionRecord = { persistence, session };
  persistWebStorageKey(key, JSON.stringify(toPersistedSnapshot(session)), persistence);
  setAuthIndicatorCookie();
  if (isNativeMobilePlatform() && session.accessToken) {
    void storeAuthTokens(session.accessToken, session.refreshToken, session.accessTokenExpiresAt).catch(() => undefined);
  }

  if (persistence === 'local') {
    removeWebStorageKey(SESSION_SESSION_KEY);
  } else {
    removeWebStorageKey(LOCAL_SESSION_KEY);
  }
}

export function loadStoredSessionRecord(): StoredSessionRecord | null {
  if (volatileSessionRecord) {
    return volatileSessionRecord;
  }

  const sessionSession = parseJson<PersistedSessionSnapshot>(getStorage('session')?.getItem(SESSION_SESSION_KEY) ?? null);
  if (sessionSession) {
    return { persistence: 'session', session: fromPersistedSnapshot(sessionSession) };
  }

  const sessionSessionFallback = parseJson<PersistedSessionSnapshot>(getStorage('local')?.getItem(SESSION_SESSION_KEY) ?? null);
  if (sessionSessionFallback) {
    return { persistence: 'session', session: fromPersistedSnapshot(sessionSessionFallback) };
  }

  const localSession = parseJson<PersistedSessionSnapshot>(getStorage('local')?.getItem(LOCAL_SESSION_KEY) ?? null);
  if (localSession) {
    return { persistence: 'local', session: fromPersistedSnapshot(localSession) };
  }

  return null;
}

export function loadStoredSession(): AuthSession | null {
  return loadStoredSessionRecord()?.session ?? null;
}

export function updateStoredUser(currentUser: CurrentUser): AuthSession | null {
  const record = loadStoredSessionRecord();
  if (!record) {
    return null;
  }

  const session = { ...record.session, currentUser };
  saveStoredSession(session, record.persistence);
  return session;
}

export function clearStoredSession(): void {
  volatileSessionRecord = null;
  removeWebStorageKey(LOCAL_SESSION_KEY);
  removeWebStorageKey(SESSION_SESSION_KEY);
  void clearAuthTokens().catch(() => undefined);
  clearAuthIndicatorCookie();
}

export function savePendingMfaChallenge(challenge: PendingMfaChallenge): void {
  persistWebStorageKey(MFA_CHALLENGE_KEY, JSON.stringify(challenge), 'session');
}

export function loadPendingMfaChallenge(): PendingMfaChallenge | null {
  return parseJson<PendingMfaChallenge>(getStorage('session')?.getItem(MFA_CHALLENGE_KEY) ?? getStorage('local')?.getItem(MFA_CHALLENGE_KEY) ?? null);
}

export function clearPendingMfaChallenge(): void {
  removeWebStorageKey(MFA_CHALLENGE_KEY);
}

export async function hydrateAuthStorage(): Promise<void> {
  await hydrateWebStorageKeys([LOCAL_SESSION_KEY, SESSION_SESSION_KEY, MFA_CHALLENGE_KEY]);

  const record = loadStoredSessionRecord();
  if (!record || !isNativeMobilePlatform()) {
    return;
  }

  const tokens = await getStoredAuthTokens();
  if (!tokens.accessToken) {
    return;
  }

  volatileSessionRecord = {
    persistence: record.persistence,
    session: {
      ...record.session,
      accessToken: tokens.accessToken,
      refreshToken: tokens.refreshToken,
      accessTokenExpiresAt: tokens.expiresAt ?? record.session.accessTokenExpiresAt,
    },
  };
}
