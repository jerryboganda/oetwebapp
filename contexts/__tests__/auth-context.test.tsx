import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';

import { AuthProvider, useAuth } from '@/contexts/auth-context';
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
});
