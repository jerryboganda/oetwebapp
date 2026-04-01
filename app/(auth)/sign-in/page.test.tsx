import { render, waitFor } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';

const { mockReplace, mockUseAuth } = vi.hoisted(() => ({
  mockReplace: vi.fn(),
  mockUseAuth: vi.fn(),
}));

vi.mock('next/navigation', () => ({
  useRouter: () => ({
    replace: mockReplace,
  }),
  useSearchParams: () => ({
    get: (_key: string) => null,
  }),
}));

vi.mock('@/components/auth/sign-in-form', () => ({
  SignInForm: () => <div>Sign in form</div>,
}));

vi.mock('@/contexts/auth-context', () => ({
  useAuth: () => mockUseAuth(),
}));

import SignInPage from './page';

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

    render(<SignInPage />);

    await waitFor(() => {
      expect(mockReplace).toHaveBeenCalledWith('/expert');
    });
  });
});
