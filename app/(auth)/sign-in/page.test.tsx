import { waitFor } from '@testing-library/react';
const { mockReplace, mockUseAuth } = vi.hoisted(() => ({
  mockReplace: vi.fn(),
  mockUseAuth: vi.fn(),
}));

vi.mock('@/components/auth/sign-in-form', () => ({
  SignInForm: () => <div>Sign in form</div>,
}));

vi.mock('@/contexts/auth-context', () => ({
  useAuth: () => mockUseAuth(),
}));

import SignInPage from './page';
import { renderWithRouter } from '@/tests/test-utils';

describe('SignInPage', () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it('routes privileged users without an authenticator to their console', async () => {
    mockUseAuth.mockReturnValue({
      loading: false,
      isAuthenticated: true,
      pendingMfaChallenge: null,
      user: {
        role: 'expert',
        requiresMfa: true,
        isAuthenticatorEnabled: false,
      },
    });

    renderWithRouter(<SignInPage />, { router: { replace: mockReplace } });

    await waitFor(() => {
      expect(mockReplace).toHaveBeenCalledWith('/expert');
    });
  });
});
