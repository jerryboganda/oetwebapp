import {
  clearPendingDeviceChallenge,
  clearPendingMfaChallenge,
  clearStoredSession,
  hydrateAuthStorage,
  loadPendingDeviceChallenge,
  loadPendingMfaChallenge,
  loadStoredSessionRecord,
  savePendingDeviceChallenge,
  savePendingMfaChallenge,
  saveStoredSession,
  updateStoredUser,
} from './auth-storage';
import { env } from './env';
import { getDeviceIdForRequest } from './device-id';
import type {
  AuthenticatorSetup,
  AuthSession,
  CurrentUser,
  ExternalAuthExchangeResult,
  ExternalAuthProvider,
  MfaCompletionResult,
  OtpChallenge,
  PendingDeviceChallenge,
  PendingMfaChallenge,
  RegisterLearnerInput,
  SignupCatalog,
  SignInResult,
} from './types/auth';
import { fetchWithTimeout } from './network/fetch-with-timeout';

const API_BASE_URL = env.apiBaseUrl;

interface AuthErrorPayload {
  code?: string;
  error?: string;
  message?: string;
  retryable?: boolean;
  email?: string;
  challengeToken?: string;
}

interface DevicePairingRedeemResponse {
  handoffToken: string;
  expiresAt: string;
}

export interface DevicePairingInitiateResponse {
  code: string;
  expiresAt: string;
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

async function buildHeaders(contentType?: string, accessToken?: string | null): Promise<Headers> {
  const headers = new Headers();
  if (contentType) {
    headers.set('Content-Type', contentType);
  }
  headers.set('X-OET-Client-Platform', resolveClientPlatform());
  const csrfToken = readCookie('oet_csrf');
  if (csrfToken) {
    headers.set('x-csrf-token', csrfToken);
  }
  // Security spec §3.2: device identity, sent ahead of enabling
  // SecurityTrustedDeviceRequired server-side — see lib/device-id.ts.
  const deviceId = await getDeviceIdForRequest();
  if (deviceId) {
    headers.set('X-OET-Device-Id', deviceId);
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
      payload.code ?? payload.error ?? 'auth_request_failed',
      payload.message ?? payload.error ?? `Auth request failed with status ${response.status}.`,
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
  const headers = await buildHeaders('application/json', accessToken);
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
  const headers = await buildHeaders(undefined, accessToken);
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
  '/privacy',
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
function redirectToSignInAfterSessionLoss(reason?: string): void {
  if (typeof window === 'undefined') return;
  const currentPath = window.location.pathname;
  if (isOnPublicAuthPath(currentPath)) return;
  const next = encodeURIComponent(currentPath + window.location.search);
  const reasonParam = reason ? `&reason=${encodeURIComponent(reason)}` : '';
  // Hard navigation so the Next.js middleware sees the cleared auth cookie and
  // any in-flight React state is discarded.
  window.location.replace(`/sign-in?next=${next}${reasonParam}`);
}

/**
 * Security spec §3.1: called when a `session_revoked` push arrives over
 * SignalR (see contexts/notification-center-context.tsx) — another sign-in
 * elsewhere, an admin revoke, or a self-serve revoke of the CURRENT session.
 * Clears local session state and hard-navigates to /sign-in with a reason
 * code the form renders as a banner (`signed_out_elsewhere` | `session_revoked`).
 */
export function forceSignOutAndRedirect(reason: 'signed_out_elsewhere' | 'session_revoked'): void {
  clearStoredSession();
  redirectToSignInAfterSessionLoss(reason);
}

// Dedupe concurrent refresh attempts. Refresh tokens are single-use (rotated
// on every successful refresh), so two parallel callers each issuing their
// own POST /v1/auth/refresh would race: whichever lands second sees the now-
// revoked token and gets 403. We share one in-flight promise across all
// concurrent callers and reset it once it settles.
let inflightRefresh: Promise<AuthSession> | null = null;

function refreshSessionDeduped(refreshToken: string | null | undefined): Promise<AuthSession> {
  if (inflightRefresh) return inflightRefresh;
  const promise = refreshSessionInternal(refreshToken).finally(() => {
    if (inflightRefresh === promise) {
      inflightRefresh = null;
    }
  });
  inflightRefresh = promise;
  return promise;
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
      session = await refreshSessionDeduped(session.refreshToken);
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

/** Security spec §3.2: `DeviceVerificationRequiredException` (backend) uses the
 * identical 403 JSON shape as the MFA challenge (`email` + `challengeToken`),
 * so every call site that can hit it builds the pending challenge the same way. */
function buildPendingDeviceChallenge(
  error: AuthClientError,
  fallbackEmail: string,
  rememberMe: boolean,
): PendingDeviceChallenge {
  return {
    email: error.details?.email ?? fallbackEmail,
    challengeToken: error.details?.challengeToken ?? '',
    rememberMe,
  };
}

export async function signIn(input: { email: string; password: string; rememberMe: boolean }): Promise<SignInResult> {
  try {
    const session = await postJson<AuthSession>('/v1/auth/sign-in', input);
    saveStoredSession(session, input.rememberMe ? 'local' : 'session');
    clearPendingMfaChallenge();
    clearPendingDeviceChallenge();
    return { status: 'authenticated', session };
  } catch (error) {
    if (error instanceof AuthClientError && error.status === 400 && error.code === 'auth_request_failed') {
      throw new AuthClientError(400, 'invalid_credentials', 'Invalid email or password.', false);
    }

    if (error instanceof AuthClientError && error.code === 'mfa_challenge_required') {
      const challenge = {
        email: error.details?.email ?? input.email,
        challengeToken: error.details?.challengeToken ?? '',
        rememberMe: input.rememberMe,
      } satisfies PendingMfaChallenge;
      savePendingMfaChallenge(challenge);
      return { status: 'mfa_required', challenge };
    }

    if (error instanceof AuthClientError && error.code === 'device_verification_required') {
      const challenge = buildPendingDeviceChallenge(error, input.email, input.rememberMe);
      savePendingDeviceChallenge(challenge);
      return { status: 'device_verification_required', challenge };
    }

    throw error;
  }
}

function createDeviceChallenge(): string {
  const bytes = new Uint8Array(32);
  globalThis.crypto.getRandomValues(bytes);
  return Array.from(bytes, (byte) => byte.toString(16).padStart(2, '0')).join('');
}

export async function completeDevicePairing(code: string): Promise<AuthSession> {
  const normalizedCode = code.trim().toUpperCase();
  if (!/^[A-Z0-9]{6,8}$/.test(normalizedCode)) {
    throw new AuthClientError(400, 'invalid_pairing_code', 'Invalid pairing code.');
  }

  const deviceChallenge = createDeviceChallenge();
  const redeemed = await postJson<DevicePairingRedeemResponse>('/v1/device-pairing/redeem', {
    code: normalizedCode,
    deviceChallenge,
  });

  const session = await postJson<AuthSession>('/v1/device-pairing/exchange', {
    handoffToken: redeemed.handoffToken,
    deviceChallenge,
  });
  saveStoredSession(session, 'local');
  clearPendingMfaChallenge();
  return session;
}

export async function initiateDevicePairing(accessToken?: string | null): Promise<DevicePairingInitiateResponse> {
  return postJson<DevicePairingInitiateResponse>('/v1/device-pairing/initiate', {}, accessToken);
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
    countryTarget: input.countryTarget,
    targetExamDate: input.targetExamDate,
    agreeToTerms: input.agreeToTerms,
    agreeToPrivacy: input.agreeToPrivacy,
    marketingOptIn: input.marketingOptIn,
    externalRegistrationToken: input.externalRegistrationToken ?? null,
    utmSource: input.utmSource ?? null,
    utmMedium: input.utmMedium ?? null,
    utmCampaign: input.utmCampaign ?? null,
    utmTerm: input.utmTerm ?? null,
    utmContent: input.utmContent ?? null,
    referrerUrl: input.referrerUrl ?? null,
    landingPath: input.landingPath ?? null,
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

export function getPendingDeviceChallenge(): PendingDeviceChallenge | null {
  return loadPendingDeviceChallenge();
}

/** Completing the MFA step can itself land on a NEW device the account
 * hasn't trusted yet — `CreateSessionCoreAsync` (backend) runs the device
 * check only after MFA passes, so this endpoint can also 403
 * `device_verification_required`. Returning `SignInResult` (not a bare
 * `AuthSession`) lets callers route to the device-verify step instead of
 * hitting a dead end. */
export async function completeMfaChallenge(code: string): Promise<MfaCompletionResult> {
  const challenge = loadPendingMfaChallenge();
  if (!challenge) {
    throw new AuthClientError(400, 'missing_mfa_challenge', 'No MFA challenge is available.');
  }

  try {
    const session = await postJson<AuthSession>('/v1/auth/mfa/challenge', {
      email: challenge.email,
      code,
      challengeToken: challenge.challengeToken,
      recoveryCode: null,
    });

    saveStoredSession(session, challenge.rememberMe ? 'local' : 'session');
    clearPendingMfaChallenge();
    return { status: 'authenticated', session };
  } catch (error) {
    if (error instanceof AuthClientError && error.code === 'device_verification_required') {
      const deviceChallenge = buildPendingDeviceChallenge(error, challenge.email, challenge.rememberMe);
      clearPendingMfaChallenge();
      savePendingDeviceChallenge(deviceChallenge);
      return { status: 'device_verification_required', challenge: deviceChallenge };
    }

    throw error;
  }
}

export async function completeRecoveryChallenge(recoveryCode: string): Promise<MfaCompletionResult> {
  const challenge = loadPendingMfaChallenge();
  if (!challenge) {
    throw new AuthClientError(400, 'missing_mfa_challenge', 'No MFA challenge is available.');
  }

  try {
    const session = await postJson<AuthSession>('/v1/auth/mfa/recovery', {
      email: challenge.email,
      code: '',
      challengeToken: challenge.challengeToken,
      recoveryCode,
    });

    saveStoredSession(session, challenge.rememberMe ? 'local' : 'session');
    clearPendingMfaChallenge();
    return { status: 'authenticated', session };
  } catch (error) {
    if (error instanceof AuthClientError && error.code === 'device_verification_required') {
      const deviceChallenge = buildPendingDeviceChallenge(error, challenge.email, challenge.rememberMe);
      clearPendingMfaChallenge();
      savePendingDeviceChallenge(deviceChallenge);
      return { status: 'device_verification_required', challenge: deviceChallenge };
    }

    throw error;
  }
}

/** Security spec §3.2: re-send the device-approval email code for the
 * pending challenge (mirrors `sendEmailVerificationOtp`). No auth state
 * changes here, so this is a plain client call rather than a context method —
 * same reasoning as the email-verification send during registration. */
export async function sendDeviceVerificationOtp(): Promise<OtpChallenge> {
  const challenge = loadPendingDeviceChallenge();
  if (!challenge) {
    throw new AuthClientError(400, 'missing_device_challenge', 'No device verification challenge is available.');
  }

  return postJson<OtpChallenge>('/v1/auth/device/send-otp', {
    challengeToken: challenge.challengeToken,
  });
}

export async function completeDeviceVerification(code: string): Promise<AuthSession> {
  const challenge = loadPendingDeviceChallenge();
  if (!challenge) {
    throw new AuthClientError(400, 'missing_device_challenge', 'No device verification challenge is available.');
  }

  const session = await postJson<AuthSession>('/v1/auth/device/verify', {
    challengeToken: challenge.challengeToken,
    code,
  });

  saveStoredSession(session, challenge.rememberMe ? 'local' : 'session');
  clearPendingDeviceChallenge();
  clearPendingMfaChallenge();
  return session;
}
