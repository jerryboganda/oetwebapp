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

function toPersistedSnapshot(session: AuthSession): PersistedSessionSnapshot {
  return {
    accessTokenExpiresAt: session.accessTokenExpiresAt,
    refreshTokenExpiresAt: session.refreshTokenExpiresAt,
    currentUser: session.currentUser,
  };
}

function fromPersistedSnapshot(snapshot: PersistedSessionSnapshot): AuthSession {
  return {
    accessToken: '',
    refreshToken: null,
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
