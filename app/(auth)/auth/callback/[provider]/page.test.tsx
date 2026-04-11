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
});