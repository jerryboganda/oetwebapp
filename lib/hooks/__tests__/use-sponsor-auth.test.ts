import { describe, it, expect, vi, beforeEach } from 'vitest';
import { renderHook } from '@testing-library/react';

const { mockReplace, mockUseCurrentUser } = vi.hoisted(() => ({
  mockReplace: vi.fn(),
  mockUseCurrentUser: vi.fn(),
}));

vi.mock('next/navigation', () => ({
  useRouter: () => ({ replace: mockReplace, push: vi.fn() }),
}));

vi.mock('../use-current-user', () => ({
  useCurrentUser: mockUseCurrentUser,
}));

import { useSponsorAuth } from '../use-sponsor-auth';

describe('useSponsorAuth', () => {
  beforeEach(() => {
    mockReplace.mockReset();
    mockUseCurrentUser.mockReset();
  });

  it('does nothing while loading', () => {
    mockUseCurrentUser.mockReturnValue({ role: null, isAuthenticated: false, isLoading: true });
    renderHook(() => useSponsorAuth());
    expect(mockReplace).not.toHaveBeenCalled();
  });

  it('redirects to sign-in with the sponsor next param when unauthenticated', () => {
    mockUseCurrentUser.mockReturnValue({ role: null, isAuthenticated: false, isLoading: false });
    renderHook(() => useSponsorAuth());
    expect(mockReplace).toHaveBeenCalledWith('/sign-in?next=/sponsor');
  });

  it('redirects a learner to "/"', () => {
    mockUseCurrentUser.mockReturnValue({ role: 'learner', isAuthenticated: true, isLoading: false });
    renderHook(() => useSponsorAuth());
    expect(mockReplace).toHaveBeenCalledWith('/');
  });

  it('redirects an admin to /admin', () => {
    mockUseCurrentUser.mockReturnValue({ role: 'admin', isAuthenticated: true, isLoading: false });
    renderHook(() => useSponsorAuth());
    expect(mockReplace).toHaveBeenCalledWith('/admin');
  });

  it('does not redirect when the user is a sponsor', () => {
    mockUseCurrentUser.mockReturnValue({ role: 'sponsor', isAuthenticated: true, isLoading: false });
    const { result } = renderHook(() => useSponsorAuth());
    expect(mockReplace).not.toHaveBeenCalled();
    expect(result.current.role).toBe('sponsor');
  });

  it('returns the resolved role and auth state', () => {
    mockUseCurrentUser.mockReturnValue({ role: 'sponsor', isAuthenticated: true, isLoading: false });
    const { result } = renderHook(() => useSponsorAuth());
    expect(result.current).toEqual({
      role: 'sponsor',
      isAuthenticated: true,
      isLoading: false,
    });
  });
});
