import { mkdir, readFile, rm, writeFile } from 'node:fs/promises';
import { dirname, join } from 'node:path';
import { createHmac } from 'node:crypto';
import { execFile } from 'node:child_process';
import { promisify } from 'node:util';
import type { APIRequestContext, Page } from '@playwright/test';
import { authStatePaths, seededAccounts, type SeededRole } from './auth';

const defaultApiBaseURL = (process.env.NEXT_PUBLIC_API_BASE_URL ?? 'http://localhost:5198').replace(/\/$/, '');
const defaultAppOrigin = new URL(process.env.PLAYWRIGHT_BASE_URL ?? 'http://localhost:3000').origin;
const localSessionKey = 'oet.auth.session.local';
const sessionSessionKey = 'oet.auth.session.session';
const mfaChallengeKey = 'oet.auth.challenge.mfa';
const authIndicatorCookieName = 'oet_auth';
const desktopComposeFilePath = join(process.cwd(), 'docker-compose.desktop.yml');
const execFileAsync = promisify(execFile);
let dockerPrivilegedAuthResetPromise: Promise<void> | null = null;
const privilegedSessionCache = new Map<string, AuthSessionResponse>();
const privilegedSessionBootstrapPromises = new Map<string, Promise<AuthSessionResponse>>();

export const authStorageKeys = {
  localSessionKey,
  sessionSessionKey,
  mfaChallengeKey,
};

type CurrentUser = {
  userId: string;
  email: string;
  role: string;
  displayName: string | null;
  isEmailVerified: boolean;
  isAuthenticatorEnabled: boolean;
  requiresEmailVerification: boolean;
  requiresMfa: boolean;
  emailVerifiedAt: string | null;
  authenticatorEnabledAt: string | null;
};

type AuthSessionResponse = {
  accessToken: string;
  refreshToken: string;
  accessTokenExpiresAt: string;
  refreshTokenExpiresAt: string;
  currentUser: CurrentUser;
};

type MfaChallengePayload = {
  code: 'mfa_challenge_required';
  message: string;
  email: string;
  challengeToken: string;
};

type AuthenticatorSetupResponse = {
  secretKey: string;
  otpAuthUri: string;
  qrCodeDataUrl: string;
  recoveryCodes: string[];
};

type MfaBootstrapState = {
  secretKey: string;
  recoveryCodes: string[];
  updatedAt: string;
};

type SessionCacheState = {
  apiBaseURL: string;
  session: AuthSessionResponse;
  updatedAt: string;
};

type BootstrapSessionOptions = {
  useDiskCache?: boolean;
  isolateSession?: boolean;
};

function mfaStatePathForRole(role: Extract<SeededRole, 'expert' | 'admin'>) {
  return join(dirname(authStatePaths[role]), `${role}.mfa.json`);
}

function sessionCachePathForRole(role: SeededRole) {
  return join(dirname(authStatePaths[role]), `${role}.session.json`);
}

function privilegedSessionCacheKey(
  role: Extract<SeededRole, 'expert' | 'admin'>,
  apiBaseURL?: string,
) {
  return `${role}::${resolveApiBaseURL(apiBaseURL)}`;
}

function getCachedPrivilegedSession(
  role: Extract<SeededRole, 'expert' | 'admin'>,
  apiBaseURL?: string,
) {
  const key = privilegedSessionCacheKey(role, apiBaseURL);
  const cached = privilegedSessionCache.get(key);

  if (!cached) {
    return null;
  }

  const expiresAt = Date.parse(cached.accessTokenExpiresAt);
  if (Number.isNaN(expiresAt) || expiresAt <= Date.now() + 60_000) {
    privilegedSessionCache.delete(key);
    return null;
  }

  return cached;
}

function cachePrivilegedSession(
  role: Extract<SeededRole, 'expert' | 'admin'>,
  session: AuthSessionResponse,
  apiBaseURL?: string,
) {
  privilegedSessionCache.set(privilegedSessionCacheKey(role, apiBaseURL), session);
}

function clearPrivilegedSessionCache(
  role: Extract<SeededRole, 'expert' | 'admin'>,
  apiBaseURL?: string,
) {
  privilegedSessionCache.delete(privilegedSessionCacheKey(role, apiBaseURL));
  privilegedSessionBootstrapPromises.delete(privilegedSessionCacheKey(role, apiBaseURL));
}

async function readJsonFile<T>(path: string): Promise<T | null> {
  try {
    const raw = await readFile(path, 'utf8');
    return JSON.parse(raw) as T;
  } catch {
    return null;
  }
}

async function writeJsonFile(path: string, value: unknown) {
  await mkdir(dirname(path), { recursive: true });
  await writeFile(path, JSON.stringify(value, null, 2), 'utf8');
}

async function readMfaBootstrapState(role: Extract<SeededRole, 'expert' | 'admin'>) {
  return readJsonFile<MfaBootstrapState>(mfaStatePathForRole(role));
}

async function writeMfaBootstrapState(role: Extract<SeededRole, 'expert' | 'admin'>, state: MfaBootstrapState) {
  await writeJsonFile(mfaStatePathForRole(role), state);
}

async function clearMfaBootstrapState(role: Extract<SeededRole, 'expert' | 'admin'>) {
  await rm(mfaStatePathForRole(role), { force: true });
}

async function readSessionCacheState(role: SeededRole, apiBaseURL?: string) {
  const cached = await readJsonFile<SessionCacheState>(sessionCachePathForRole(role));
  if (!cached) {
    return null;
  }

  if (cached.apiBaseURL !== resolveApiBaseURL(apiBaseURL)) {
    return null;
  }

  const expiresAt = Date.parse(cached.session.accessTokenExpiresAt);
  if (Number.isNaN(expiresAt) || expiresAt <= Date.now() + 60_000) {
    return null;
  }

  return cached.session;
}

async function writeSessionCacheState(role: SeededRole, session: AuthSessionResponse, apiBaseURL?: string) {
  await writeJsonFile(sessionCachePathForRole(role), {
    apiBaseURL: resolveApiBaseURL(apiBaseURL),
    session,
    updatedAt: new Date().toISOString(),
  } satisfies SessionCacheState);
}

async function clearSessionCacheState(role: SeededRole) {
  await rm(sessionCachePathForRole(role), { force: true });
}

async function readResponseBody(response: Awaited<ReturnType<APIRequestContext['post']>>) {
  try {
    return await response.text();
  } catch {
    return '<no response body>';
  }
}

async function expectJsonOk<T>(
  response: Awaited<ReturnType<APIRequestContext['post']>>,
  errorContext: string,
): Promise<T> {
  if (response.ok()) {
    return response.json() as Promise<T>;
  }

  const body = await readResponseBody(response);
  throw new Error(`${errorContext}\nStatus: ${response.status()}\nBody: ${body}`);
}

function decodeBase32(value: string) {
  const cleaned = value.trim().replace(/=+$/u, '').toUpperCase();
  let buffer = 0;
  let bitsLeft = 0;
  const bytes: number[] = [];

  for (const character of cleaned) {
    const digit = (() => {
      if (character >= 'A' && character <= 'Z') {
        return character.charCodeAt(0) - 65;
      }

      if (character >= '2' && character <= '7') {
        return character.charCodeAt(0) - 24;
      }

      throw new Error(`Invalid base32 character: ${character}`);
    })();

    buffer = (buffer << 5) | digit;
    bitsLeft += 5;

    if (bitsLeft < 8) {
      continue;
    }

    bytes.push((buffer >> (bitsLeft - 8)) & 0xff);
    bitsLeft -= 8;
    buffer &= (1 << bitsLeft) - 1;
  }

  return Buffer.from(bytes);
}

function generateTotp(secretKey: string, timestamp = new Date()) {
  const key = decodeBase32(secretKey);
  let timestep = Math.floor(timestamp.getTime() / 1000 / 30);
  const counter = Buffer.alloc(8);

  for (let index = 7; index >= 0; index -= 1) {
    counter[index] = timestep & 0xff;
    timestep >>= 8;
  }

  const hash = createHmac('sha1', key).update(counter).digest();
  const offset = hash[hash.length - 1] & 0x0f;
  const binaryCode =
    ((hash[offset] & 0x7f) << 24)
    | (hash[offset + 1] << 16)
    | (hash[offset + 2] << 8)
    | hash[offset + 3];

  return String(binaryCode % 1_000_000).padStart(6, '0');
}

function generateTotpCandidates(secretKey: string, timestamp = new Date()) {
  const offsets = [-30_000, 0, 30_000];
  const seenCodes = new Set<string>();
  const codes: string[] = [];

  for (const offset of offsets) {
    const code = generateTotp(secretKey, new Date(timestamp.getTime() + offset));
    if (seenCodes.has(code)) {
      continue;
    }

    seenCodes.add(code);
    codes.push(code);
  }

  return codes;
}

function resolveApiBaseURL(apiBaseURL?: string) {
  return (apiBaseURL ?? defaultApiBaseURL).replace(/\/$/, '');
}

function canResetDockerBackedPrivilegedAuth(apiBaseURL?: string) {
  try {
    const url = new URL(resolveApiBaseURL(apiBaseURL));
    return (url.hostname === 'localhost' || url.hostname === '127.0.0.1') && url.port === '5198';
  } catch {
    return false;
  }
}

function isInvalidAuthenticatorCodeError(error: unknown) {
  return error instanceof Error && error.message.includes('"code":"invalid_authenticator_code"');
}

function isMfaNotConfiguredError(error: unknown) {
  return error instanceof Error && error.message.includes('"code":"mfa_not_configured"');
}

async function waitForHealth(url: string, timeoutMs = 120_000) {
  const startTime = Date.now();

  while (Date.now() - startTime < timeoutMs) {
    try {
      const response = await fetch(url);
      if (response.ok) {
        return;
      }
    } catch {
      // The container may still be restarting.
    }

    await new Promise((resolve) => setTimeout(resolve, 2_000));
  }

  throw new Error(`Timed out waiting for Docker-backed auth baseline health at ${url}.`);
}

function sleep(ms: number) {
  return new Promise((resolve) => setTimeout(resolve, ms));
}

function isTransientApiRequestError(error: unknown) {
  const message = error instanceof Error ? error.message : String(error);
  return /\b(ECONNRESET|ECONNREFUSED|EPIPE|ETIMEDOUT)\b|socket hang up|fetch failed/i.test(message);
}

async function waitForApiReadiness(apiBaseURL?: string, timeoutMs = 20_000) {
  const readyUrl = `${resolveApiBaseURL(apiBaseURL)}/health/ready`;
  const startTime = Date.now();

  while (Date.now() - startTime < timeoutMs) {
    try {
      const response = await fetch(readyUrl);
      if (response.ok) {
        return;
      }
    } catch {
      // The API can briefly close sockets while restarting in local smoke runs.
    }

    await sleep(500);
  }
}

async function postJsonWithRetry(
  request: APIRequestContext,
  path: string,
  options: Parameters<APIRequestContext['post']>[1],
  apiBaseURL?: string,
) {
  const url = `${resolveApiBaseURL(apiBaseURL)}${path}`;
  let lastError: unknown = null;

  for (let attempt = 1; attempt <= 3; attempt += 1) {
    try {
      return await request.post(url, options);
    } catch (error) {
      lastError = error;
      if (!isTransientApiRequestError(error) || attempt === 3) {
        throw error;
      }

      await waitForApiReadiness(apiBaseURL);
      await sleep(500 * attempt);
    }
  }

  throw lastError ?? new Error(`Failed to POST ${url}.`);
}

async function resetDockerBackedPrivilegedAuthState(
  role: Extract<SeededRole, 'expert' | 'admin'>,
  apiBaseURL?: string,
) {
  if (!canResetDockerBackedPrivilegedAuth(apiBaseURL)) {
    return;
  }

  await clearMfaBootstrapState(role);
  clearPrivilegedSessionCache(role, apiBaseURL);
  await clearSessionCacheState(role);

  if (!dockerPrivilegedAuthResetPromise) {
    dockerPrivilegedAuthResetPromise = (async () => {
      const dockerCommand = process.platform === 'win32' ? 'docker.exe' : 'docker';
      try {
        await execFileAsync(dockerCommand, ['compose', '-f', desktopComposeFilePath, 'restart', 'learner-api']);
      } catch {
        await execFileAsync(dockerCommand, ['compose', '-f', desktopComposeFilePath, 'up', '-d', 'learner-api']);
      }

      await waitForHealth(`${resolveApiBaseURL(apiBaseURL)}/health/ready`);
    })().finally(() => {
      dockerPrivilegedAuthResetPromise = null;
    });
  }

  await dockerPrivilegedAuthResetPromise;
}

async function signInRaw(request: APIRequestContext, role: SeededRole, apiBaseURL?: string) {
  const account = seededAccounts[role];
  return postJsonWithRetry(request, '/v1/auth/sign-in', {
    headers: { 'Content-Type': 'application/json' },
    data: {
      email: account.email,
      password: account.password,
      rememberMe: true,
    },
  }, apiBaseURL);
}

async function beginAuthenticatorSetup(request: APIRequestContext, accessToken: string, apiBaseURL?: string) {
  const response = await postJsonWithRetry(request, '/v1/auth/mfa/authenticator/begin', {
    headers: {
      Authorization: `Bearer ${accessToken}`,
      'Content-Type': 'application/json',
    },
    data: {},
  }, apiBaseURL);

  return expectJsonOk<AuthenticatorSetupResponse>(response, 'Expected authenticator setup bootstrap to succeed.');
}

async function confirmAuthenticatorSetup(request: APIRequestContext, accessToken: string, code: string, apiBaseURL?: string) {
  const response = await postJsonWithRetry(request, '/v1/auth/mfa/authenticator/confirm', {
    headers: {
      Authorization: `Bearer ${accessToken}`,
      'Content-Type': 'application/json',
    },
    data: { code },
  }, apiBaseURL);

  return expectJsonOk<CurrentUser>(response, 'Expected authenticator confirmation to succeed.');
}

async function confirmAuthenticatorSetupWithRetry(
  request: APIRequestContext,
  accessToken: string,
  secretKey: string,
  apiBaseURL?: string,
) {
  let lastInvalidCodeError: unknown = null;

  for (const code of generateTotpCandidates(secretKey)) {
    try {
      return await confirmAuthenticatorSetup(request, accessToken, code, apiBaseURL);
    } catch (error) {
      if (!isInvalidAuthenticatorCodeError(error)) {
        throw error;
      }

      lastInvalidCodeError = error;
    }
  }

  throw lastInvalidCodeError ?? new Error('Expected authenticator confirmation to succeed.');
}

async function completeMfaChallenge(
  request: APIRequestContext,
  role: Extract<SeededRole, 'expert' | 'admin'>,
  challengeToken: string,
  code: string,
  apiBaseURL?: string,
) {
  const account = seededAccounts[role];
  const response = await postJsonWithRetry(request, '/v1/auth/mfa/challenge', {
    headers: { 'Content-Type': 'application/json' },
    data: {
      email: account.email,
      code,
      challengeToken,
      recoveryCode: null,
    },
  }, apiBaseURL);

  return expectJsonOk<AuthSessionResponse>(response, `Expected MFA challenge completion to succeed for ${role}.`);
}

async function completeMfaChallengeWithRetry(
  request: APIRequestContext,
  role: Extract<SeededRole, 'expert' | 'admin'>,
  challengeToken: string,
  secretKey: string,
  apiBaseURL?: string,
) {
  let lastInvalidCodeError: unknown = null;

  for (const code of generateTotpCandidates(secretKey)) {
    try {
      return await completeMfaChallenge(request, role, challengeToken, code, apiBaseURL);
    } catch (error) {
      if (!isInvalidAuthenticatorCodeError(error)) {
        throw error;
      }

      lastInvalidCodeError = error;
    }
  }

  throw lastInvalidCodeError ?? new Error(`Expected MFA challenge completion to succeed for ${role}.`);
}

async function resolvePrivilegedSession(
  request: APIRequestContext,
  role: Extract<SeededRole, 'expert' | 'admin'>,
  apiBaseURL?: string,
  allowDockerReset = true,
) {
  const initialSignIn = await signInRaw(request, role, apiBaseURL);

  if (initialSignIn.ok()) {
    const initialSession = await initialSignIn.json() as AuthSessionResponse;
    const bootstrap = await beginAuthenticatorSetup(request, initialSession.accessToken, apiBaseURL);
    await writeMfaBootstrapState(role, {
      secretKey: bootstrap.secretKey,
      recoveryCodes: bootstrap.recoveryCodes,
      updatedAt: new Date().toISOString(),
    });

    try {
      await confirmAuthenticatorSetupWithRetry(request, initialSession.accessToken, bootstrap.secretKey, apiBaseURL);
    } catch (error) {
      if (allowDockerReset && canResetDockerBackedPrivilegedAuth(apiBaseURL) && isInvalidAuthenticatorCodeError(error)) {
        await resetDockerBackedPrivilegedAuthState(role, apiBaseURL);
        return resolvePrivilegedSession(request, role, apiBaseURL, false);
      }

      throw error;
    }

    const challengedSignIn = await signInRaw(request, role, apiBaseURL);
    if (challengedSignIn.status() !== 403) {
      const body = await readResponseBody(challengedSignIn);
      throw new Error(`Expected MFA challenge sign-in for ${role} after setup.\nStatus: ${challengedSignIn.status()}\nBody: ${body}`);
    }

    const challenge = await challengedSignIn.json() as MfaChallengePayload;
    try {
      return await completeMfaChallengeWithRetry(request, role, challenge.challengeToken, bootstrap.secretKey, apiBaseURL);
    } catch (error) {
      if (isMfaNotConfiguredError(error)) {
        await clearMfaBootstrapState(role);
        clearPrivilegedSessionCache(role, apiBaseURL);
        await clearSessionCacheState(role);
        return resolvePrivilegedSession(request, role, apiBaseURL, false);
      }

      if (allowDockerReset && canResetDockerBackedPrivilegedAuth(apiBaseURL) && isInvalidAuthenticatorCodeError(error)) {
        await resetDockerBackedPrivilegedAuthState(role, apiBaseURL);
        return resolvePrivilegedSession(request, role, apiBaseURL, false);
      }

      throw error;
    }
  }

  if (initialSignIn.status() === 403) {
    const challenge = await initialSignIn.json() as MfaChallengePayload;
    const bootstrapState = await readMfaBootstrapState(role);
    if (!bootstrapState?.secretKey) {
      if (allowDockerReset && canResetDockerBackedPrivilegedAuth(apiBaseURL)) {
        await resetDockerBackedPrivilegedAuthState(role, apiBaseURL);
        return resolvePrivilegedSession(request, role, apiBaseURL, false);
      }

      const body = await readResponseBody(initialSignIn);
      throw new Error(`MFA is already required for ${role}, but no stored TOTP secret is available.\nStatus: ${initialSignIn.status()}\nBody: ${body}`);
    }

    try {
      return await completeMfaChallengeWithRetry(request, role, challenge.challengeToken, bootstrapState.secretKey, apiBaseURL);
    } catch (error) {
      if (isMfaNotConfiguredError(error)) {
        await clearMfaBootstrapState(role);
        clearPrivilegedSessionCache(role, apiBaseURL);
        await clearSessionCacheState(role);
        return resolvePrivilegedSession(request, role, apiBaseURL, false);
      }

      if (allowDockerReset && canResetDockerBackedPrivilegedAuth(apiBaseURL) && isInvalidAuthenticatorCodeError(error)) {
        await resetDockerBackedPrivilegedAuthState(role, apiBaseURL);
        return resolvePrivilegedSession(request, role, apiBaseURL, false);
      }

      throw error;
    }
  }

  const body = await readResponseBody(initialSignIn);
  throw new Error(`Expected sign-in bootstrap to succeed for ${role}.\nStatus: ${initialSignIn.status()}\nBody: ${body}`);
}

export async function bootstrapSessionForRole(
  request: APIRequestContext,
  role: SeededRole,
  apiBaseURL?: string,
  options?: BootstrapSessionOptions,
): Promise<AuthSessionResponse> {
  const useDiskCache = options?.useDiskCache ?? true;
  const isolateSession = options?.isolateSession ?? false;

  if (useDiskCache && !isolateSession) {
    const cachedDiskSession = await readSessionCacheState(role, apiBaseURL);
    if (cachedDiskSession) {
      if (role !== 'learner') {
        cachePrivilegedSession(role, cachedDiskSession, apiBaseURL);
      }

      return cachedDiskSession;
    }
  }

  if (role === 'learner') {
    const response = await signInRaw(request, role, apiBaseURL);
    const session = await expectJsonOk<AuthSessionResponse>(response, 'Expected learner sign-in bootstrap to succeed.');
    if (!isolateSession) {
      await writeSessionCacheState(role, session, apiBaseURL);
    }
    return session;
  }

  if (!isolateSession) {
    const cachedSession = getCachedPrivilegedSession(role, apiBaseURL);
    if (cachedSession) {
      return cachedSession;
    }

    const key = privilegedSessionCacheKey(role, apiBaseURL);
    const inFlightBootstrap = privilegedSessionBootstrapPromises.get(key);
    if (inFlightBootstrap) {
      return inFlightBootstrap;
    }
  }

  if (isolateSession) {
    return resolvePrivilegedSession(request, role, apiBaseURL);
  }

  const key = privilegedSessionCacheKey(role, apiBaseURL);
  const bootstrapPromise = resolvePrivilegedSession(request, role, apiBaseURL)
    .then(async (session) => {
      cachePrivilegedSession(role, session, apiBaseURL);
      await writeSessionCacheState(role, session, apiBaseURL);
      return session;
    })
    .finally(() => {
      privilegedSessionBootstrapPromises.delete(key);
    });

  privilegedSessionBootstrapPromises.set(key, bootstrapPromise);
  return bootstrapPromise;
}

export async function hydrateSessionStorage(page: Page, session: AuthSessionResponse) {
  await page.evaluate(
    ({ sessionRecord, localKey, sessionKey, challengeKey }) => {
      window.localStorage.setItem(localKey, JSON.stringify(sessionRecord));
      window.localStorage.setItem('oet.e2e.keep-tokens', '1');
      window.sessionStorage.removeItem(sessionKey);
      window.sessionStorage.removeItem(challengeKey);
    },
    {
      sessionRecord: session,
      localKey: localSessionKey,
      sessionKey: sessionSessionKey,
      challengeKey: mfaChallengeKey,
    },
  );
}

export async function recoverBrowserSession(
  page: Page,
  request: APIRequestContext,
  role: SeededRole,
  targetPath: string,
) {
  const session = await bootstrapSessionForRole(request, role, undefined, {
    useDiskCache: false,
    isolateSession: true,
  });
  const frontendCookies = await captureFrontendAuthCookies(request, role);
  const cookies = [buildAuthIndicatorCookie(session), ...frontendCookies];
  await page.context().clearCookies({ name: /^(oet_auth|oet_rt|oet_csrf)$/ });
  await page.context().addCookies(cookies);
  const currentOrigin = (() => {
    try {
      return new URL(page.url()).origin;
    } catch {
      return null;
    }
  })();
  if (currentOrigin !== defaultAppOrigin) {
    await page.goto('/sign-in', { waitUntil: 'domcontentloaded' });
  }
  await hydrateSessionStorage(page, session);
  await page.goto(targetPath, { waitUntil: 'domcontentloaded' });
}

function buildAuthIndicatorCookie(session: AuthSessionResponse) {
  const appOrigin = new URL(defaultAppOrigin);
  const refreshExpiresAt = Date.parse(session.refreshTokenExpiresAt);
  const fallbackExpiresAt = Math.floor(Date.now() / 1000) + 60 * 60 * 24 * 30;

  return {
    name: authIndicatorCookieName,
    value: '1',
    domain: appOrigin.hostname,
    path: '/',
    expires: Number.isNaN(refreshExpiresAt) ? fallbackExpiresAt : Math.floor(refreshExpiresAt / 1000),
    httpOnly: false,
    secure: appOrigin.protocol === 'https:',
    sameSite: 'Lax' as const,
  };
}

function buildStorageState(session: AuthSessionResponse) {
  return {
    cookies: [buildAuthIndicatorCookie(session)],
    origins: [
      {
        origin: defaultAppOrigin,
        localStorage: [
          {
            name: localSessionKey,
            value: JSON.stringify(session),
          },
          {
            // Opt-in flag honored by lib/auth-storage.ts so toPersistedSnapshot
            // keeps access/refresh tokens in localStorage. Required because the
            // backend rotates refresh tokens single-use: a single shared
            // storageState file would otherwise be invalidated as soon as the
            // first test cold-loads and refreshes. Production sign-in never
            // writes this key.
            name: 'oet.e2e.keep-tokens',
            value: '1',
          },
        ],
      },
    ],
  };
}

/**
 * Cookies that the backend's auth flow sets for the browser-facing origin
 * (rewritten by the Next.js `/api/backend/*` proxy). Keep the indicator cookie
 * out of this list — it is built deterministically from the session payload by
 * `buildAuthIndicatorCookie` and may be present on the request context with a
 * shorter expiry than we want to persist.
 */
const FRONTEND_AUTH_COOKIE_NAMES = new Set(['oet_rt', 'oet_csrf']);

type StorageStateCookie = ReturnType<typeof buildAuthIndicatorCookie>;

type StorageStateBlob = {
  cookies: StorageStateCookie[];
  origins: Array<{ origin: string; localStorage: Array<{ name: string; value: string }> }>;
};

/**
 * Sign the role in through the **frontend proxy** at `${defaultAppOrigin}/api/backend/v1/auth/sign-in`
 * using the provided APIRequestContext, then return the cookies the proxy
 * rewrote onto the frontend origin (`oet_rt`, `oet_csrf`). These are required
 * for the persisted storage state to survive the auth provider's first-load
 * `/v1/auth/refresh` call: without `oet_rt` bound to `localhost` the refresh
 * 400s, the auth manager clears the session, and the page redirects to
 * `/sign-in`.
 *
 * For MFA-protected roles (expert/admin) we additionally complete the MFA
 * challenge through the frontend proxy using the TOTP secret previously
 * captured by `resolvePrivilegedSession`. Without this, the persisted
 * storageState only contains the `oet_auth` indicator + localStorage tokens
 * but no HttpOnly `oet_rt` for the frontend origin — so the auth provider's
 * first-load refresh has nothing to send and the privileged page redirects
 * back to `/sign-in` (or hangs in `/mfa/setup`).
 */
async function captureFrontendAuthCookies(
  request: APIRequestContext,
  role: SeededRole,
): Promise<StorageStateCookie[]> {
  const account = seededAccounts[role];
  const frontendHost = new URL(defaultAppOrigin).hostname;

  const extractFrontendCookies = async () => {
    const state = await request.storageState();
    return state.cookies.filter(
      (cookie) =>
        FRONTEND_AUTH_COOKIE_NAMES.has(cookie.name)
        && (cookie.domain === frontendHost || cookie.domain === `.${frontendHost}`),
    ) as StorageStateCookie[];
  };

  try {
    const signInResponse = await request.post(`${defaultAppOrigin}/api/backend/v1/auth/sign-in`, {
      headers: { 'Content-Type': 'application/json' },
      data: {
        email: account.email,
        password: account.password,
        rememberMe: true,
      },
      timeout: 30_000,
    });

    if (role === 'learner') {
      return extractFrontendCookies();
    }

    // Privileged roles (expert/admin): the proxy sign-in returns 403 with an
    // MFA challenge envelope. Complete the challenge via the proxy so the
    // backend's Set-Cookie for `oet_rt`/`oet_csrf` lands on the frontend
    // origin (rewritten by the proxy).
    if (signInResponse.status() !== 403) {
      // Either an unexpected success (no MFA enrolled — should not happen
      // after `bootstrapSessionForRole`) or a hard failure. Fall back to
      // whatever cookies are present so the caller still gets the indicator
      // cookie + localStorage path.
      return extractFrontendCookies();
    }

    let challenge: MfaChallengePayload;
    try {
      challenge = await signInResponse.json() as MfaChallengePayload;
    } catch {
      return extractFrontendCookies();
    }

    const bootstrapState = await readMfaBootstrapState(role as Extract<SeededRole, 'expert' | 'admin'>);
    if (!bootstrapState?.secretKey) {
      return extractFrontendCookies();
    }

    let lastError: unknown = null;
    for (const code of generateTotpCandidates(bootstrapState.secretKey)) {
      const challengeResponse = await request.post(`${defaultAppOrigin}/api/backend/v1/auth/mfa/challenge`, {
        headers: { 'Content-Type': 'application/json' },
        data: {
          email: account.email,
          code,
          challengeToken: challenge.challengeToken,
          recoveryCode: null,
        },
        timeout: 30_000,
      });

      if (challengeResponse.ok()) {
        return extractFrontendCookies();
      }

      lastError = await readResponseBody(challengeResponse);
      if (!String(lastError).includes('"code":"invalid_authenticator_code"')) {
        break;
      }
    }

    return extractFrontendCookies();
  } catch {
    // Best-effort. If the frontend isn't reachable in this scenario, we still
    // return whatever cookies are present and let the caller fall back to the
    // indicator-only storage state.
    return extractFrontendCookies().catch(() => []);
  }
}

export async function persistSessionToStorageState(
  session: AuthSessionResponse,
  storageStatePath: string,
  request?: APIRequestContext,
  role?: SeededRole,
) {
  const base = buildStorageState(session) as StorageStateBlob;

  if (request && role) {
    const captured = await captureFrontendAuthCookies(request, role);
    if (captured.length > 0) {
      const seen = new Set(base.cookies.map((c) => c.name));
      for (const cookie of captured) {
        if (!seen.has(cookie.name)) {
          base.cookies.push(cookie);
          seen.add(cookie.name);
        }
      }
    }
  }

  await writeJsonFile(storageStatePath, base);
}
