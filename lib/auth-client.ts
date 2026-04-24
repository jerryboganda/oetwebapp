import {
  clearPendingMfaChallenge,
  clearStoredSession,
  hydrateAuthStorage,
  loadPendingMfaChallenge,
  loadStoredSessionRecord,
  savePendingMfaChallenge,
  saveStoredSession,
  updateStoredUser,
} from './auth-storage';
import type {
  AuthenticatorSetup,
  AuthSession,
  CurrentUser,
  ExternalAuthExchangeResult,
  ExternalAuthProvider,
  OtpChallenge,
  PendingMfaChallenge,
  RegisterLearnerInput,
  SignupCatalog,
  SignInResult,
} from './types/auth';
import { fetchWithTimeout } from './network/fetch-with-timeout';

const API_BASE_URL = (process.env.NEXT_PUBLIC_API_BASE_URL ?? '/api/backend').replace(/\/$/, '');

interface AuthErrorPayload {
  code?: string;
  message?: string;
  retryable?: boolean;
  email?: string;
  challengeToken?: string;
}

export class AuthClientError extends Error {
  constructor(
    public readonly status: number,
    public readonly code: string,
    message: string,
    public readonly retryable = false,
    public readonly details?: AuthErrorPayload,
  ) {
    super(message);
    this.name = 'AuthClientError';
  }
}

function resolveUrl(path: string): string {
  return `${API_BASE_URL}${path.startsWith('/') ? path : `/${path}`}`;
}

function buildPathWithQuery(
  path: string,
  values: Record<string, string | undefined | null>
): string {
  const params = new URLSearchParams();

  Object.entries(values).forEach(([key, value]) => {
    if (value) {
      params.set(key, value);
    }
  });

  const query = params.toString();
  return query ? `${path}?${query}` : path;
}

function withAuthHeader(headers: Headers, accessToken?: string | null): Headers {
  if (accessToken) {
    headers.set('Authorization', `Bearer ${accessToken}`);
  }

  return headers;
}

function resolveClientPlatform(): 'web' | 'desktop' | 'capacitor' {
  if (typeof window === 'undefined') {
    return 'web';
  }

  const w = window as unknown as Record<string, unknown> & {
    Capacitor?: { isNativePlatform?: () => boolean };
    desktopBridge?: unknown;
  };

  if (w.desktopBridge) {
    return 'desktop';
  }

  if (typeof w.Capacitor?.isNativePlatform === 'function' && w.Capacitor.isNativePlatform()) {
    return 'capacitor';
  }

  return 'web';
}

function buildHeaders(contentType?: string, accessToken?: string | null): Headers {
  const headers = new Headers();
  if (contentType) {
    headers.set('Content-Type', contentType);
  }
  headers.set('X-OET-Client-Platform', resolveClientPlatform());
  const csrfToken = readCookie('oet_csrf');
  if (csrfToken) {
    headers.set('x-csrf-token', csrfToken);
  }
  return withAuthHeader(headers, accessToken);
}

function readCookie(name: string): string | null {
  if (typeof document === 'undefined') {
    return null;
  }

  const match = document.cookie.match(new RegExp(`(?:^|;\\s*)${name}=([^;]+)`));
  return match ? decodeURIComponent(match[1]) : null;
}

function canSendBodyRefreshToken(): boolean {
  const platform = resolveClientPlatform();
  return platform === 'capacitor' || platform === 'desktop';
}

async function parseResponse<T>(response: Response): Promise<T> {
  if (!response.ok) {
    let payload: AuthErrorPayload = {};

    try {
      payload = await response.json();
    } catch {
      payload = {};
    }

    throw new AuthClientError(
      response.status,
      payload.code ?? 'auth_request_failed',
      payload.message ?? `Auth request failed with status ${response.status}.`,
      payload.retryable ?? false,
      payload,
    );
  }

  if (response.status === 204) {
    return undefined as T;
  }

  return response.json() as Promise<T>;
}

function isAbortError(error: unknown): boolean {
  return error instanceof DOMException
    ? error.name === 'AbortError'
    : error instanceof Error && error.name === 'AbortError';
}

async function postJson<TResponse>(path: string, body: unknown, accessToken?: string | null): Promise<TResponse> {
  const headers = buildHeaders('application/json', accessToken);
  try {
    const response = await fetchWithTimeout(resolveUrl(path), {
      method: 'POST',
      headers,
      body: JSON.stringify(body),
      credentials: 'include',
    });

    return parseResponse<TResponse>(response);
  } catch (error) {
    if (isAbortError(error)) {
      throw new AuthClientError(408, 'auth_request_timeout', 'Authentication request timed out.', true);
    }

    throw error;
  }
}

async function getJson<TResponse>(path: string, accessToken?: string | null): Promise<TResponse> {
  const headers = buildHeaders(undefined, accessToken);
  try {
    const response = await fetchWithTimeout(resolveUrl(path), {
      method: 'GET',
      headers,
      credentials: 'include',
    });

    return parseResponse<TResponse>(response);
  } catch (error) {
    if (isAbortError(error)) {
      throw new AuthClientError(408, 'auth_request_timeout', 'Authentication request timed out.', true);
    }

    throw error;
  }
}

function isExpiredOrCloseToExpiry(isoDate: string, skewSeconds = 30): boolean {
  const expiresAt = Date.parse(isoDate);
  if (Number.isNaN(expiresAt)) {
    return true;
  }

  return expiresAt <= Date.now() + skewSeconds * 1000;
}

async function refreshSessionInternal(refreshToken?: string | null): Promise<AuthSession> {
  return postJson<AuthSession>(
    '/v1/auth/refresh',
    canSendBodyRefreshToken() && refreshToken ? { refreshToken } : {},
  );
}

/**
 * Public paths where an expired session must NOT trigger a redirect. These are
 * pages the user is *supposed* to see signed-out. Keep in sync with the
 * PUBLIC_PATHS set in middleware.ts.
 */
const PUBLIC_PATHS_NO_REDIRECT = new Set<string>([
  '/sign-in',
  '/register',
  '/register/success',
  '/terms',
  '/forgot-password',
  '/forgot-password/verify',
  '/reset-password',
  '/reset-password/success',
  '/verify-email',
  '/mfa/challenge',
  '/mfa/setup',
  '/mfa/recovery',
  '/auth/callback',
]);

function isOnPublicAuthPath(pathname: string): boolean {
  if (PUBLIC_PATHS_NO_REDIRECT.has(pathname)) return true;
  if (pathname.startsWith('/auth/callback/')) return true;
  return false;
}

/**
 * When a background refresh fails (expired/invalid/missing refresh token) we
 * actively bounce the user to the sign-in page. Without this, the SPA stays on
 * a protected route, fires API calls without a bearer, and renders confusing
 * "Request failed: 400" error cards — which is what the dashboard was showing
 * after the SameSite cookie migration left stale Strict cookies in browsers.
 */
function redirectToSignInAfterSessionLoss(): void {
  if (typeof window === 'undefined') return;
  const currentPath = window.location.pathname;
  if (isOnPublicAuthPath(currentPath)) return;
  const next = encodeURIComponent(currentPath + window.location.search);
  // Hard navigation so the Next.js middleware sees the cleared auth cookie and
  // any in-flight React state is discarded.
  window.location.replace(`/sign-in?next=${next}`);
}

export async function ensureFreshSession(): Promise<AuthSession | null> {
  await hydrateAuthStorage();
  const record = loadStoredSessionRecord();
  if (!record) {
    redirectToSignInAfterSessionLoss();
    return null;
  }

  let session = record.session;

  if (!session.accessToken || isExpiredOrCloseToExpiry(session.accessTokenExpiresAt)) {
    try {
      session = await refreshSessionInternal(session.refreshToken);
      saveStoredSession(session, record.persistence);
    } catch {
      clearStoredSession();
      redirectToSignInAfterSessionLoss();
      return null;
    }
  }

  return session;
}

export async function ensureFreshAccessToken(): Promise<string | null> {
  const session = await ensureFreshSession();
  return session?.accessToken ?? null;
}

export async function restoreSession(): Promise<AuthSession | null> {
  await hydrateAuthStorage();
  const record = loadStoredSessionRecord();
  if (!record) {
    return null;
  }

  const session = await ensureFreshSession();
  if (!session) {
    return null;
  }

  if (session.currentUser) {
    saveStoredSession(session, record.persistence);
    return session;
  }

  clearStoredSession();
  return null;
}

export async function signIn(input: { email: string; password: string; rememberMe: boolean }): Promise<SignInResult> {
  try {
    const session = await postJson<AuthSession>('/v1/auth/sign-in', input);
    saveStoredSession(session, input.rememberMe ? 'local' : 'session');
    clearPendingMfaChallenge();
    return { status: 'authenticated', session };
  } catch (error) {
    if (error instanceof AuthClientError && error.code === 'mfa_challenge_required') {
      const challenge = {
        email: error.details?.email ?? input.email,
        challengeToken: error.details?.challengeToken ?? '',
        rememberMe: input.rememberMe,
      } satisfies PendingMfaChallenge;
      savePendingMfaChallenge(challenge);
      return { status: 'mfa_required', challenge };
    }

    throw error;
  }
}

export async function registerLearner(
  input: RegisterLearnerInput,
  options?: { persistSession?: boolean },
): Promise<AuthSession> {
  const session = await postJson<AuthSession>('/v1/auth/register', {
    email: input.email,
    password: input.password,
    role: 'learner',
    displayName: input.displayName ?? `${input.firstName} ${input.lastName}`.trim(),
    firstName: input.firstName,
    lastName: input.lastName,
    mobileNumber: input.mobileNumber,
    examTypeId: input.examTypeId,
    professionId: input.professionId,
    sessionId: input.sessionId,
    countryTarget: input.countryTarget,
    agreeToTerms: input.agreeToTerms,
    agreeToPrivacy: input.agreeToPrivacy,
    marketingOptIn: input.marketingOptIn,
    externalRegistrationToken: input.externalRegistrationToken ?? null,
  });
  if (options?.persistSession !== false) {
    saveStoredSession(session, 'local');
  }
  clearPendingMfaChallenge();
  return session;
}

export async function fetchSignupCatalog(): Promise<SignupCatalog> {
  return getJson<SignupCatalog>('/v1/auth/catalog/signup');
}

export function buildExternalAuthStartHref(provider: ExternalAuthProvider, nextPath?: string | null): string {
  let platform: string | undefined;
  if (typeof window !== 'undefined') {
    const w = window as unknown as Record<string, unknown>;
    if (w.desktopBridge) {
      platform = 'desktop';
    } else if (w.Capacitor && typeof (w as Record<string, unknown> & { Capacitor?: { isNativePlatform?: () => boolean } }).Capacitor?.isNativePlatform === 'function' && (w as Record<string, unknown> & { Capacitor?: { isNativePlatform?: () => boolean } }).Capacitor!.isNativePlatform!()) {
      platform = 'capacitor';
    }
  }
  return resolveUrl(
    buildPathWithQuery(`/v1/auth/external/${provider}/start`, {
      next: nextPath ?? undefined,
      platform,
    })
  );
}

export async function exchangeExternalAuth(
  provider: ExternalAuthProvider,
  exchangeToken: string,
  options?: { persistSession?: boolean }
): Promise<ExternalAuthExchangeResult> {
  const result = await postJson<ExternalAuthExchangeResult>(`/v1/auth/external/${provider}/exchange`, {
    exchangeToken,
  });

  if (result.status === 'authenticated' && result.session && options?.persistSession !== false) {
    saveStoredSession(result.session, 'local');
    clearPendingMfaChallenge();
  }

  return result;
}

export async function signOut(): Promise<void> {
  const session = loadStoredSessionRecord()?.session;

  try {
    await postJson<void>(
      '/v1/auth/sign-out',
      canSendBodyRefreshToken() && session?.refreshToken ? { refreshToken: session.refreshToken } : {},
      session?.accessToken,
    );
  } finally {
    clearStoredSession();
    clearPendingMfaChallenge();
  }
}

export async function deleteAccount(password: string, reason?: string): Promise<void> {
  const accessToken = await ensureFreshAccessToken();
  if (!accessToken) {
    throw new AuthClientError(401, 'not_authenticated', 'A valid session is required.');
  }

  await postJson<void>('/v1/auth/account/delete', { password, reason }, accessToken);
}

export async function sendEmailVerificationOtp(email: string): Promise<OtpChallenge> {
  return postJson<OtpChallenge>('/v1/auth/email/send-verification-otp', {
    email,
    purpose: 'verify_email',
  });
}

export async function verifyEmailOtp(email: string, code: string): Promise<CurrentUser> {
  const currentUser = await postJson<CurrentUser>('/v1/auth/email/verify-otp', {
    email,
    purpose: 'verify_email',
    code,
  });
  updateStoredUser(currentUser);
  return currentUser;
}

export async function requestPasswordReset(email: string): Promise<OtpChallenge> {
  return postJson<OtpChallenge>('/v1/auth/forgot-password', {
    email,
  });
}

export async function resetPassword(input: { email: string; resetToken: string; newPassword: string }): Promise<void> {
  await postJson<void>('/v1/auth/reset-password', input);
}

export async function beginAuthenticatorSetup(): Promise<AuthenticatorSetup> {
  const accessToken = await ensureFreshAccessToken();
  if (!accessToken) {
    throw new AuthClientError(401, 'not_authenticated', 'A valid session is required.');
  }

  return postJson<AuthenticatorSetup>('/v1/auth/mfa/authenticator/begin', {}, accessToken);
}

export async function confirmAuthenticatorSetup(code: string): Promise<CurrentUser> {
  const accessToken = await ensureFreshAccessToken();
  if (!accessToken) {
    throw new AuthClientError(401, 'not_authenticated', 'A valid session is required.');
  }

  const currentUser = await postJson<CurrentUser>('/v1/auth/mfa/authenticator/confirm', { code }, accessToken);
  updateStoredUser(currentUser);
  return currentUser;
}

export function getPendingMfaChallenge(): PendingMfaChallenge | null {
  return loadPendingMfaChallenge();
}

export async function completeMfaChallenge(code: string): Promise<AuthSession> {
  const challenge = loadPendingMfaChallenge();
  if (!challenge) {
    throw new AuthClientError(400, 'missing_mfa_challenge', 'No MFA challenge is available.');
  }

  const session = await postJson<AuthSession>('/v1/auth/mfa/challenge', {
    email: challenge.email,
    code,
    challengeToken: challenge.challengeToken,
    recoveryCode: null,
  });

  saveStoredSession(session, challenge.rememberMe ? 'local' : 'session');
  clearPendingMfaChallenge();
  return session;
}

export async function completeRecoveryChallenge(recoveryCode: string): Promise<AuthSession> {
  const challenge = loadPendingMfaChallenge();
  if (!challenge) {
    throw new AuthClientError(400, 'missing_mfa_challenge', 'No MFA challenge is available.');
  }

  const session = await postJson<AuthSession>('/v1/auth/mfa/recovery', {
    email: challenge.email,
    code: '',
    challengeToken: challenge.challengeToken,
    recoveryCode,
  });

  saveStoredSession(session, challenge.rememberMe ? 'local' : 'session');
  clearPendingMfaChallenge();
  return session;
}
