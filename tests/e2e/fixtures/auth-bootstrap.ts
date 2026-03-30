import { mkdir, readFile, writeFile } from 'node:fs/promises';
import { dirname, join } from 'node:path';
import { createHmac } from 'node:crypto';
import type { APIRequestContext, Page } from '@playwright/test';
import { authStatePaths, seededAccounts, type SeededRole } from './auth';

const apiBaseURL = (process.env.NEXT_PUBLIC_API_BASE_URL ?? 'http://localhost:5198').replace(/\/$/, '');
const localSessionKey = 'oet.auth.session.local';
const sessionSessionKey = 'oet.auth.session.session';
const mfaChallengeKey = 'oet.auth.challenge.mfa';

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

function mfaStatePathForRole(role: Extract<SeededRole, 'expert' | 'admin'>) {
  return join(dirname(authStatePaths[role]), `${role}.mfa.json`);
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

async function signInRaw(request: APIRequestContext, role: SeededRole) {
  const account = seededAccounts[role];
  return request.post(`${apiBaseURL}/v1/auth/sign-in`, {
    headers: { 'Content-Type': 'application/json' },
    data: {
      email: account.email,
      password: account.password,
      rememberMe: true,
    },
  });
}

async function beginAuthenticatorSetup(request: APIRequestContext, accessToken: string) {
  const response = await request.post(`${apiBaseURL}/v1/auth/mfa/authenticator/begin`, {
    headers: {
      Authorization: `Bearer ${accessToken}`,
      'Content-Type': 'application/json',
    },
    data: {},
  });

  return expectJsonOk<AuthenticatorSetupResponse>(response, 'Expected authenticator setup bootstrap to succeed.');
}

async function confirmAuthenticatorSetup(request: APIRequestContext, accessToken: string, code: string) {
  const response = await request.post(`${apiBaseURL}/v1/auth/mfa/authenticator/confirm`, {
    headers: {
      Authorization: `Bearer ${accessToken}`,
      'Content-Type': 'application/json',
    },
    data: { code },
  });

  return expectJsonOk<CurrentUser>(response, 'Expected authenticator confirmation to succeed.');
}

async function completeMfaChallenge(
  request: APIRequestContext,
  role: Extract<SeededRole, 'expert' | 'admin'>,
  challengeToken: string,
  secretKey: string,
) {
  const account = seededAccounts[role];
  const response = await request.post(`${apiBaseURL}/v1/auth/mfa/challenge`, {
    headers: { 'Content-Type': 'application/json' },
    data: {
      email: account.email,
      code: generateTotp(secretKey),
      challengeToken,
      recoveryCode: null,
    },
  });

  return expectJsonOk<AuthSessionResponse>(response, `Expected MFA challenge completion to succeed for ${role}.`);
}

async function resolvePrivilegedSession(
  request: APIRequestContext,
  role: Extract<SeededRole, 'expert' | 'admin'>,
) {
  const initialSignIn = await signInRaw(request, role);

  if (initialSignIn.ok()) {
    const initialSession = await initialSignIn.json() as AuthSessionResponse;
    const bootstrap = await beginAuthenticatorSetup(request, initialSession.accessToken);
    await writeMfaBootstrapState(role, {
      secretKey: bootstrap.secretKey,
      recoveryCodes: bootstrap.recoveryCodes,
      updatedAt: new Date().toISOString(),
    });

    await confirmAuthenticatorSetup(request, initialSession.accessToken, generateTotp(bootstrap.secretKey));

    const challengedSignIn = await signInRaw(request, role);
    if (challengedSignIn.status() !== 403) {
      const body = await readResponseBody(challengedSignIn);
      throw new Error(`Expected MFA challenge sign-in for ${role} after setup.\nStatus: ${challengedSignIn.status()}\nBody: ${body}`);
    }

    const challenge = await challengedSignIn.json() as MfaChallengePayload;
    return completeMfaChallenge(request, role, challenge.challengeToken, bootstrap.secretKey);
  }

  if (initialSignIn.status() === 403) {
    const challenge = await initialSignIn.json() as MfaChallengePayload;
    const bootstrapState = await readMfaBootstrapState(role);
    if (!bootstrapState?.secretKey) {
      const body = await readResponseBody(initialSignIn);
      throw new Error(`MFA is already required for ${role}, but no stored TOTP secret is available.\nStatus: ${initialSignIn.status()}\nBody: ${body}`);
    }

    return completeMfaChallenge(request, role, challenge.challengeToken, bootstrapState.secretKey);
  }

  const body = await readResponseBody(initialSignIn);
  throw new Error(`Expected sign-in bootstrap to succeed for ${role}.\nStatus: ${initialSignIn.status()}\nBody: ${body}`);
}

export async function bootstrapSessionForRole(
  request: APIRequestContext,
  role: SeededRole,
): Promise<AuthSessionResponse> {
  if (role === 'learner') {
    const response = await signInRaw(request, role);
    return expectJsonOk<AuthSessionResponse>(response, 'Expected learner sign-in bootstrap to succeed.');
  }

  return resolvePrivilegedSession(request, role);
}

export async function persistSessionToStorageState(
  page: Page,
  role: SeededRole,
  session: AuthSessionResponse,
) {
  await page.goto('/sign-in');
  await page.evaluate(
    ({ sessionRecord, localKey, sessionKey, challengeKey }) => {
      window.localStorage.setItem(localKey, JSON.stringify(sessionRecord));
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

  await page.context().storageState({ path: authStatePaths[role] });
}
