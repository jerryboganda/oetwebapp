import { describe, it, expect, vi } from 'vitest';
import { renderHook } from '@testing-library/react';

const { mockUseAuth } = vi.hoisted(() => ({ mockUseAuth: vi.fn() }));

vi.mock('@/contexts/auth-context', () => ({
  useAuth: mockUseAuth,
}));

import { useCurrentUser } from '../use-current-user';

describe('useCurrentUser', () => {
  it('renames loading -> isLoading and forwards the rest', () => {
    const user = { userId: 'u-1', displayName: 'Jane', isEmailVerified: true };
    mockUseAuth.mockReturnValue({
      user,
      role: 'learner',
      isAuthenticated: true,
      loading: false,
      pendingMfaChallenge: null,
    });

    const { result } = renderHook(() => useCurrentUser());
    expect(result.current).toEqual({
      user,
      role: 'learner',
      isAuthenticated: true,
      isLoading: false,
      pendingMfaChallenge: null,
    });
  });

  it('forwards the loading flag while auth is initialising', () => {
    mockUseAuth.mockReturnValue({
      user: null,
      role: null,
      isAuthenticated: false,
      loading: true,
      pendingMfaChallenge: null,
    });
    const { result } = renderHook(() => useCurrentUser());
    expect(result.current.isLoading).toBe(true);
    expect(result.current.user).toBeNull();
    expect(result.current.role).toBeNull();
  });

  it('exposes a pending MFA challenge unchanged', () => {
    const challenge = { challengeId: 'mfa-1', methods: ['authenticator'] };
    mockUseAuth.mockReturnValue({
      user: null,
      role: null,
      isAuthenticated: false,
      loading: false,
      pendingMfaChallenge: challenge,
    });
    const { result } = renderHook(() => useCurrentUser());
    expect(result.current.pendingMfaChallenge).toBe(challenge);
  });
});
