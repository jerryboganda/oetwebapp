import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import type { AuthSession, CurrentUser, OtpChallenge } from '@/lib/types/auth';

const realFetch = global.fetch;

function createCurrentUser(overrides: Partial<CurrentUser> = {}): CurrentUser {
  return {
    userId: 'auth_learner_local_001',
    email: 'learner@oet-prep.dev',
    role: 'learner',
    displayName: 'Learner Local',
    isEmailVerified: true,
    isAuthenticatorEnabled: false,
    requiresEmailVerification: false,
    requiresMfa: false,
    emailVerifiedAt: '2026-03-27T00:00:00.000Z',
    authenticatorEnabledAt: null,
    ...overrides,
  };
}

function createSession(overrides: Partial<AuthSession> = {}): AuthSession {
  return {
    accessToken: 'access-token-1',
    refreshToken: 'refresh-token-1',
    accessTokenExpiresAt: '2099-03-27T00:15:00.000Z',
    refreshTokenExpiresAt: '2099-04-26T00:00:00.000Z',
    currentUser: createCurrentUser(),
    ...overrides,
  };
}

describe('auth-client', () => {
  beforeEach(() => {
    vi.resetModules();
    window.localStorage.clear();
    window.sessionStorage.clear();
    global.fetch = vi.fn();
  });

  afterEach(() => {
    global.fetch = realFetch;
  });

  it('stores a successful sign-in session and returns the access token for API usage', async () => {
    const session = createSession();
    vi.mocked(global.fetch).mockResolvedValueOnce(new Response(JSON.stringify(session), {
      status: 200,
      headers: { 'content-type': 'application/json' },
    }));

    const { signIn, ensureFreshAccessToken } = await import('@/lib/auth-client');
    const { loadStoredSession } = await import('@/lib/auth-storage');

    const result = await signIn({
      email: 'learner@oet-prep.dev',
      password: 'Password123!',
      rememberMe: true,
    });

    expect(result.status).toBe('authenticated');
    expect(loadStoredSession()).toEqual(session);
    await expect(ensureFreshAccessToken()).resolves.toBe('access-token-1');
  });

  it('returns an MFA challenge payload instead of persisting a partial sign-in', async () => {
    vi.mocked(global.fetch).mockResolvedValueOnce(new Response(JSON.stringify({
      code: 'mfa_challenge_required',
      message: 'Authenticator code is required to continue.',
      email: 'expert@oet-prep.dev',
      challengeToken: 'challenge-token-1',
      retryable: false,
    }), {
      status: 403,
      headers: { 'content-type': 'application/json' },
    }));

    const { signIn } = await import('@/lib/auth-client');
    const { loadStoredSession } = await import('@/lib/auth-storage');

    const result = await signIn({
      email: 'expert@oet-prep.dev',
      password: 'Password123!',
      rememberMe: true,
    });

    expect(result).toEqual({
      status: 'mfa_required',
      challenge: {
        email: 'expert@oet-prep.dev',
        challengeToken: 'challenge-token-1',
        rememberMe: true,
      },
    });
    expect(loadStoredSession()).toBeNull();
  });

  it('refreshes an expired access token before returning it to the API layer', async () => {
    const expiredSession = createSession({
      accessToken: 'expired-access-token',
      refreshToken: 'refresh-token-1',
      accessTokenExpiresAt: '2000-03-27T00:00:00.000Z',
    });
    const { saveStoredSession, loadStoredSession } = await import('@/lib/auth-storage');
    saveStoredSession(expiredSession, 'local');

    const refreshedSession = createSession({
      accessToken: 'fresh-access-token',
      refreshToken: 'refresh-token-2',
      accessTokenExpiresAt: '2099-03-27T01:00:00.000Z',
      refreshTokenExpiresAt: '2099-04-27T00:00:00.000Z',
    });

    vi.mocked(global.fetch).mockResolvedValueOnce(new Response(JSON.stringify(refreshedSession), {
      status: 200,
      headers: { 'content-type': 'application/json' },
    }));

    const { ensureFreshAccessToken } = await import('@/lib/auth-client');

    await expect(ensureFreshAccessToken()).resolves.toBe('fresh-access-token');
    expect(loadStoredSession()).toEqual(refreshedSession);
  });

  it('requests a password reset challenge for a public email address', async () => {
    const challenge: OtpChallenge = {
      challengeId: 'reset-challenge-1',
      purpose: 'reset_password',
      deliveryChannel: 'email',
      destinationHint: 'l*****@oet-prep.dev',
      expiresAt: '2026-03-27T00:10:00.000Z',
      retryAfterSeconds: 60,
    };

    vi.mocked(global.fetch).mockResolvedValueOnce(new Response(JSON.stringify(challenge), {
      status: 200,
      headers: { 'content-type': 'application/json' },
    }));

    const { requestPasswordReset } = await import('@/lib/auth-client');

    await expect(requestPasswordReset('learner@oet-prep.dev')).resolves.toEqual(challenge);

    expect(global.fetch).toHaveBeenCalledTimes(1);
    expect(vi.mocked(global.fetch).mock.calls[0]?.[0]).toContain('/v1/auth/forgot-password');
    expect(JSON.parse(String(vi.mocked(global.fetch).mock.calls[0]?.[1]?.body))).toEqual({
      email: 'learner@oet-prep.dev',
    });
  });

  it('can register a learner without persisting the returned session', async () => {
    const session = createSession();
    vi.mocked(global.fetch).mockResolvedValueOnce(new Response(JSON.stringify(session), {
      status: 200,
      headers: { 'content-type': 'application/json' },
    }));

    const { registerLearner } = await import('@/lib/auth-client');
    const { loadStoredSession } = await import('@/lib/auth-storage');

    await expect(registerLearner({
      email: 'learner@oet-prep.dev',
      password: 'Password123!',
      displayName: 'Learner Local',
      firstName: 'Learner',
      lastName: 'Local',
      mobileNumber: '+923001234567',
      examTypeId: 'oet',
      professionId: 'nursing',
      sessionId: 'session-oet-nursing-apr',
      countryTarget: 'Australia',
      agreeToTerms: true,
      agreeToPrivacy: true,
      marketingOptIn: false,
    }, { persistSession: false })).resolves.toEqual(session);

    expect(loadStoredSession()).toBeNull();
  });

  it('submits the reset token and new password to complete a password reset', async () => {
    vi.mocked(global.fetch).mockResolvedValueOnce(new Response(null, { status: 204 }));

    const { resetPassword } = await import('@/lib/auth-client');

    await expect(resetPassword({
      email: 'learner@oet-prep.dev',
      resetToken: '123456',
      newPassword: 'BetterPassword123!',
    })).resolves.toBeUndefined();

    expect(global.fetch).toHaveBeenCalledTimes(1);
    expect(vi.mocked(global.fetch).mock.calls[0]?.[0]).toContain('/v1/auth/reset-password');
    expect(JSON.parse(String(vi.mocked(global.fetch).mock.calls[0]?.[1]?.body))).toEqual({
      email: 'learner@oet-prep.dev',
      resetToken: '123456',
      newPassword: 'BetterPassword123!',
    });
  });
});
