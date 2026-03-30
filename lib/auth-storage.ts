import type { AuthSession, CurrentUser, PendingMfaChallenge } from './types/auth';

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
  const target = getStorage(persistence);
  const secondary = getStorage(persistence === 'local' ? 'session' : 'local');

  target?.setItem(persistence === 'local' ? LOCAL_SESSION_KEY : SESSION_SESSION_KEY, JSON.stringify(session));
  secondary?.removeItem(persistence === 'local' ? SESSION_SESSION_KEY : LOCAL_SESSION_KEY);
}

export function loadStoredSessionRecord(): StoredSessionRecord | null {
  const sessionSession = parseJson<AuthSession>(getStorage('session')?.getItem(SESSION_SESSION_KEY) ?? null);
  if (sessionSession) {
    return { persistence: 'session', session: sessionSession };
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
  getStorage('local')?.removeItem(LOCAL_SESSION_KEY);
  getStorage('session')?.removeItem(SESSION_SESSION_KEY);
}

export function savePendingMfaChallenge(challenge: PendingMfaChallenge): void {
  getStorage('session')?.setItem(MFA_CHALLENGE_KEY, JSON.stringify(challenge));
}

export function loadPendingMfaChallenge(): PendingMfaChallenge | null {
  return parseJson<PendingMfaChallenge>(getStorage('session')?.getItem(MFA_CHALLENGE_KEY) ?? null);
}

export function clearPendingMfaChallenge(): void {
  getStorage('session')?.removeItem(MFA_CHALLENGE_KEY);
}
