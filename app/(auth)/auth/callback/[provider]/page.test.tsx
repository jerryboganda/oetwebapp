import { waitFor } from '@testing-library/react';
const { mockReplace, mockExchangeExternalAuth } = vi.hoisted(() => ({
  mockReplace: vi.fn(),
  mockExchangeExternalAuth: vi.fn(),
}));

vi.mock('@/lib/auth-client', () => ({
  exchangeExternalAuth: (...args: unknown[]) => mockExchangeExternalAuth(...args),
}));

vi.mock('@/components/auth/auth-screen-shell', () => ({
  AuthScreenShell: ({ children }: { children: React.ReactNode }) => <div>{children}</div>,
}));

import ExternalAuthCallbackPage from './page';
import { renderWithRouter } from '@/tests/test-utils';

describe('ExternalAuthCallbackPage', () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it('returns authenticated users to their requested destination after social sign-in', async () => {
    mockExchangeExternalAuth.mockResolvedValueOnce({
      status: 'authenticated',
      session: {
        accessToken: 'access-token-1',
        refreshToken: 'refresh-token-1',
        accessTokenExpiresAt: '2099-03-27T00:15:00.000Z',
        refreshTokenExpiresAt: '2099-04-26T00:00:00.000Z',
        currentUser: {
          userId: 'expert-001',
          email: 'expert@oet-prep.dev',
          role: 'expert',
          displayName: 'Expert One',
          isEmailVerified: true,
          isAuthenticatorEnabled: true,
          requiresEmailVerification: false,
          requiresMfa: false,
          emailVerifiedAt: '2026-03-27T00:00:00.000Z',
          authenticatorEnabledAt: '2026-03-27T00:00:00.000Z',
        },
      },
      registration: null,
    });

    renderWithRouter(<ExternalAuthCallbackPage />, {
      router: { replace: mockReplace },
      params: { provider: 'google' },
      searchParams: new URLSearchParams({ token: 'oauth-exchange-token', next: '/expert/queue?assignment=assigned' }),
    });

    await waitFor(() => {
      expect(mockReplace).toHaveBeenCalledWith('/expert/queue?assignment=assigned');
    });
  });

  it('reads the exchange token from the URL fragment and strips it before exchange (H11)', async () => {
    mockExchangeExternalAuth.mockResolvedValueOnce({
      status: 'authenticated',
      session: {
        accessToken: 'access-token-2',
        refreshToken: 'refresh-token-2',
        accessTokenExpiresAt: '2099-03-27T00:15:00.000Z',
        refreshTokenExpiresAt: '2099-04-26T00:00:00.000Z',
        currentUser: {
          userId: 'learner-001',
          email: 'learner@oet-prep.dev',
          role: 'learner',
          displayName: 'Learner One',
          isEmailVerified: true,
          isAuthenticatorEnabled: false,
          requiresEmailVerification: false,
          requiresMfa: false,
          emailVerifiedAt: '2026-03-27T00:00:00.000Z',
          authenticatorEnabledAt: null,
        },
      },
      registration: null,
    });

    // Simulate the backend redirect: token lives in the fragment, ?next= in the query.
    window.history.replaceState(null, '', '/auth/callback/google?next=%2Fdashboard#token=fragment-token');

    renderWithRouter(<ExternalAuthCallbackPage />, {
      router: { replace: mockReplace },
      params: { provider: 'google' },
      searchParams: new URLSearchParams({ next: '/dashboard' }),
    });

    await waitFor(() => {
      expect(mockExchangeExternalAuth).toHaveBeenCalledWith('google', 'fragment-token');
    });

    // Fragment and any ?token= must be stripped from the visible URL immediately.
    expect(window.location.hash).toBe('');
    expect(new URL(window.location.href).searchParams.has('token')).toBe(false);

    await waitFor(() => {
      expect(mockReplace).toHaveBeenCalledWith('/dashboard');
    });
  });
});