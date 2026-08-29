import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { AuthProvider, useAuth } from '@/contexts/auth-context';
import { getQueryClient } from '@/components/providers/query-provider';
import { useExpertStore } from '@/lib/stores/expert-store';
import type { AuthSession } from '@/lib/types/auth';

const authClientMock = vi.hoisted(() => ({
  restoreSession: vi.fn(),
  signIn: vi.fn(),
  registerLearner: vi.fn(),
  signOut: vi.fn(),
  getPendingMfaChallenge: vi.fn(() => null),
  getPendingDeviceChallenge: vi.fn(() => null),
  sendEmailVerificationOtp: vi.fn(),
  verifyEmailOtp: vi.fn(),
  reissueSessionAfterVerification: vi.fn(),
  beginAuthenticatorSetup: vi.fn(),
  confirmAuthenticatorSetup: vi.fn(),
  completeMfaChallenge: vi.fn(),
  completeRecoveryChallenge: vi.fn(),
  completeDeviceVerification: vi.fn(),
}));

vi.mock('@/lib/auth-client', () => authClientMock);

function createSession(overrides: Partial<AuthSession> = {}): AuthSession {
  return {
    accessToken: 'access-token-1',
    refreshToken: 'refresh-token-1',
    accessTokenExpiresAt: '2099-03-27T00:15:00.000Z',
    refreshTokenExpiresAt: '2099-04-26T00:00:00.000Z',
    currentUser: {
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
    },
    ...overrides,
  };
}

function unverifiedSession(): AuthSession {
  const base = createSession();
  return createSession({
    accessToken: 'stale-jwt-email-unverified',
    currentUser: {
      ...base.currentUser,
      isEmailVerified: false,
      requiresEmailVerification: true,
      emailVerifiedAt: null,
    },
  });
}

function VerifyConsumer() {
  const { user, session, verifyEmailOtp } = useAuth();

  return (
    <div>
      <div data-testid="verified">{user?.isEmailVerified ? 'verified' : 'unverified'}</div>
      <div data-testid="access-token">{session?.accessToken ?? 'none'}</div>
      <div data-testid="email">{user?.email ?? 'none'}</div>
      <button type="button" onClick={() => void verifyEmailOtp('123456')}>
        Verify
      </button>
    </div>
  );
}

function AuthConsumer() {
  const { user, loading, signOut } = useAuth();

  return (
    <div>
      <div data-testid="loading">{loading ? 'loading' : 'ready'}</div>
      <div data-testid="role">{user?.role ?? 'anonymous'}</div>
      <div data-testid="email">{user?.email ?? 'none'}</div>
      <button type="button" onClick={() => void signOut()}>
        Sign out
      </button>
    </div>
  );
}

describe('AuthProvider', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    authClientMock.getPendingMfaChallenge.mockReturnValue(null);
    authClientMock.getPendingDeviceChallenge.mockReturnValue(null);
  });

  it('hydrates the current user from the stored backend auth session', async () => {
    authClientMock.restoreSession.mockResolvedValue(createSession({
      currentUser: {
        ...createSession().currentUser,
        role: 'expert',
        email: 'expert@oet-prep.dev',
      },
    }));

    render(
      <AuthProvider>
        <AuthConsumer />
      </AuthProvider>,
    );

    await waitFor(() => {
      expect(screen.getByTestId('loading')).toHaveTextContent('ready');
    });

    expect(screen.getByTestId('role')).toHaveTextContent('expert');
    expect(screen.getByTestId('email')).toHaveTextContent('expert@oet-prep.dev');
  });

  it('clears the current user after sign-out', async () => {
    authClientMock.restoreSession.mockResolvedValue(createSession());
    authClientMock.signOut.mockResolvedValue(undefined);

    render(
      <AuthProvider>
        <AuthConsumer />
      </AuthProvider>,
    );

    await waitFor(() => {
      expect(screen.getByTestId('email')).toHaveTextContent('learner@oet-prep.dev');
    });

    fireEvent.click(screen.getByRole('button', { name: 'Sign out' }));

    await waitFor(() => {
      expect(screen.getByTestId('role')).toHaveTextContent('anonymous');
    });
  });

  it('clears the query cache and persisted Zustand stores on sign-out (FE-001)', async () => {
    authClientMock.restoreSession.mockResolvedValue(createSession());
    authClientMock.signOut.mockResolvedValue(undefined);

    // Seed user-scoped client state that must NOT survive logout.
    getQueryClient().setQueryData(['dashboard', 'home'], { hello: 'world' });
    useExpertStore.getState().upsertReviewDraft('review-1', {
      scores: { C1: 5 },
      criterionComments: {},
      finalComment: 'draft',
      anchoredComments: [],
      timestampComments: [],
      scratchpad: '',
      checklistItems: [],
    });

    render(
      <AuthProvider>
        <AuthConsumer />
      </AuthProvider>,
    );

    await waitFor(() => {
      expect(screen.getByTestId('email')).toHaveTextContent('learner@oet-prep.dev');
    });

    fireEvent.click(screen.getByRole('button', { name: 'Sign out' }));

    await waitFor(() => {
      expect(screen.getByTestId('role')).toHaveTextContent('anonymous');
    });

    // FE-001: both the TanStack Query cache and the persisted expert store are wiped.
    expect(getQueryClient().getQueryCache().getAll()).toHaveLength(0);
    expect(useExpertStore.getState().reviewDrafts).toEqual({});
  });

  it('re-issues the session after email verification so the learner is not bounced back to /verify-email', async () => {
    // POST /v1/auth/email/verify-otp returns CurrentUserResponse only — no
    // tokens. Without an explicit re-issue the JWT still carries
    // email_verified=false, the first learner API call 403s on the backend
    // EmailVerifiedGate, and lib/api.ts hard-navigates the learner straight
    // back onto the OTP screen.
    const stale = unverifiedSession();
    authClientMock.restoreSession.mockResolvedValue(stale);
    authClientMock.verifyEmailOtp.mockResolvedValue({
      ...unverifiedSession().currentUser,
      isEmailVerified: true,
      requiresEmailVerification: false,
      emailVerifiedAt: '2026-08-30T00:00:00.000Z',
    });
    authClientMock.reissueSessionAfterVerification.mockResolvedValue(
      createSession({ accessToken: 'fresh-jwt-email-verified' }),
    );

    render(
      <AuthProvider>
        <VerifyConsumer />
      </AuthProvider>,
    );

    await waitFor(() => {
      expect(screen.getByTestId('access-token')).toHaveTextContent('stale-jwt-email-unverified');
    });

    fireEvent.click(screen.getByRole('button', { name: 'Verify' }));

    await waitFor(() => {
      expect(screen.getByTestId('access-token')).toHaveTextContent('fresh-jwt-email-verified');
    });
    expect(authClientMock.reissueSessionAfterVerification).toHaveBeenCalledTimes(1);
    expect(screen.getByTestId('verified')).toHaveTextContent('verified');
  });

  it('keeps the verified learner signed in when the post-verification re-issue fails', async () => {
    // The OTP is already burned by this point, so a flaky refresh must NOT read
    // as an OTP error and must NOT sign the learner out.
    const stale = unverifiedSession();
    authClientMock.restoreSession.mockResolvedValue(stale);
    authClientMock.verifyEmailOtp.mockResolvedValue({
      ...unverifiedSession().currentUser,
      isEmailVerified: true,
      requiresEmailVerification: false,
      emailVerifiedAt: '2026-08-30T00:00:00.000Z',
    });
    authClientMock.reissueSessionAfterVerification.mockResolvedValue(null);

    render(
      <AuthProvider>
        <VerifyConsumer />
      </AuthProvider>,
    );

    await waitFor(() => {
      expect(screen.getByTestId('verified')).toHaveTextContent('unverified');
    });

    fireEvent.click(screen.getByRole('button', { name: 'Verify' }));

    await waitFor(() => {
      expect(screen.getByTestId('verified')).toHaveTextContent('verified');
    });
    // Session preserved, learner still signed in — no wipe, no throw.
    expect(screen.getByTestId('access-token')).toHaveTextContent('stale-jwt-email-unverified');
    expect(screen.getByTestId('email')).toHaveTextContent('learner@oet-prep.dev');
  });
});
