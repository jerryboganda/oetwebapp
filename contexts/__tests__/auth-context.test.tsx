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
  sendEmailVerificationOtp: vi.fn(),
  verifyEmailOtp: vi.fn(),
  beginAuthenticatorSetup: vi.fn(),
  confirmAuthenticatorSetup: vi.fn(),
  completeMfaChallenge: vi.fn(),
  completeRecoveryChallenge: vi.fn(),
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
      email: 'learner@oet-with-dr-hesham.dev',
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
  });

  it('hydrates the current user from the stored backend auth session', async () => {
    authClientMock.restoreSession.mockResolvedValue(createSession({
      currentUser: {
        ...createSession().currentUser,
        role: 'expert',
        email: 'expert@oet-with-dr-hesham.dev',
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
    expect(screen.getByTestId('email')).toHaveTextContent('expert@oet-with-dr-hesham.dev');
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
      expect(screen.getByTestId('email')).toHaveTextContent('learner@oet-with-dr-hesham.dev');
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
      expect(screen.getByTestId('email')).toHaveTextContent('learner@oet-with-dr-hesham.dev');
    });

    fireEvent.click(screen.getByRole('button', { name: 'Sign out' }));

    await waitFor(() => {
      expect(screen.getByTestId('role')).toHaveTextContent('anonymous');
    });

    // FE-001: both the TanStack Query cache and the persisted expert store are wiped.
    expect(getQueryClient().getQueryCache().getAll()).toHaveLength(0);
    expect(useExpertStore.getState().reviewDrafts).toEqual({});
  });
});
