import type { AuthSession, CurrentUser, PendingMfaChallenge } from './types/auth';
import { hydrateWebStorageKeys, persistWebStorageKey, removeWebStorageKey } from './mobile/native-storage';

export type AuthPersistence = 'local' | 'session';

const LOCAL_SESSION_KEY = 'oet.auth.session.local';
const SESSION_SESSION_KEY = 'oet.auth.session.session';
const MFA_CHALLENGE_KEY = 'oet.auth.challenge.mfa';

interface StoredSessionRecord {
  persistence: AuthPersistence;
  session: AuthSession;
}

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

export function saveStoredSession(session: AuthSession, persistence: AuthPersistence): void {
  const key = persistence === 'local' ? LOCAL_SESSION_KEY : SESSION_SESSION_KEY;
  persistWebStorageKey(key, JSON.stringify(session), persistence);

  if (persistence === 'local') {
    removeWebStorageKey(SESSION_SESSION_KEY);
  } else {
    removeWebStorageKey(LOCAL_SESSION_KEY);
  }
}

export function loadStoredSessionRecord(): StoredSessionRecord | null {
  const sessionSession = parseJson<AuthSession>(getStorage('session')?.getItem(SESSION_SESSION_KEY) ?? null);
  if (sessionSession) {
    return { persistence: 'session', session: sessionSession };
  }

  const sessionSessionFallback = parseJson<AuthSession>(getStorage('local')?.getItem(SESSION_SESSION_KEY) ?? null);
  if (sessionSessionFallback) {
    return { persistence: 'session', session: sessionSessionFallback };
  }

  const localSession = parseJson<AuthSession>(getStorage('local')?.getItem(LOCAL_SESSION_KEY) ?? null);
  if (localSession) {
    return { persistence: 'local', session: localSession };
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
  removeWebStorageKey(LOCAL_SESSION_KEY);
  removeWebStorageKey(SESSION_SESSION_KEY);
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
}
