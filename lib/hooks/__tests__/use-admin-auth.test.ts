import { describe, it, expect, vi, beforeEach } from 'vitest';
import { renderHook } from '@testing-library/react';

const { mockReplace, mockPush, mockUseCurrentUser } = vi.hoisted(() => ({
  mockReplace: vi.fn(),
  mockPush: vi.fn(),
  mockUseCurrentUser: vi.fn(),
}));

vi.mock('next/navigation', () => ({
  useRouter: () => ({ replace: mockReplace, push: mockPush }),
}));

vi.mock('../use-current-user', () => ({
  useCurrentUser: mockUseCurrentUser,
}));

import { useAdminAuth } from '../use-admin-auth';

describe('useAdminAuth', () => {
  beforeEach(() => {
    mockReplace.mockReset();
    mockPush.mockReset();
    mockUseCurrentUser.mockReset();
  });

  it('does nothing while loading', () => {
    mockUseCurrentUser.mockReturnValue({ role: null, isAuthenticated: false, isLoading: true });
    const { result } = renderHook(() => useAdminAuth());
    expect(mockReplace).not.toHaveBeenCalled();
    expect(result.current.isLoading).toBe(true);
  });

  it('redirects to sign-in with the admin next param when unauthenticated', () => {
    mockUseCurrentUser.mockReturnValue({ role: null, isAuthenticated: false, isLoading: false });
    renderHook(() => useAdminAuth());
    expect(mockReplace).toHaveBeenCalledWith('/sign-in?next=/admin');
  });

  it('redirects a learner to the learner default route', () => {
    mockUseCurrentUser.mockReturnValue({ role: 'learner', isAuthenticated: true, isLoading: false });
    renderHook(() => useAdminAuth());
    expect(mockReplace).toHaveBeenCalledWith('/');
  });

  it('redirects an expert to /expert', () => {
    mockUseCurrentUser.mockReturnValue({ role: 'expert', isAuthenticated: true, isLoading: false });
    renderHook(() => useAdminAuth());
    expect(mockReplace).toHaveBeenCalledWith('/expert');
  });

  it('redirects a sponsor to /sponsor', () => {
    mockUseCurrentUser.mockReturnValue({ role: 'sponsor', isAuthenticated: true, isLoading: false });
    renderHook(() => useAdminAuth());
    expect(mockReplace).toHaveBeenCalledWith('/sponsor');
  });

  it('does not redirect when the user is an admin', () => {
    mockUseCurrentUser.mockReturnValue({ role: 'admin', isAuthenticated: true, isLoading: false });
    const { result } = renderHook(() => useAdminAuth());
    expect(mockReplace).not.toHaveBeenCalled();
    expect(result.current.role).toBe('admin');
    expect(result.current.isAuthenticated).toBe(true);
  });

  it('coerces a null role to "learner" before deciding', () => {
    mockUseCurrentUser.mockReturnValue({ role: null, isAuthenticated: true, isLoading: false });
    const { result } = renderHook(() => useAdminAuth());
    expect(mockReplace).toHaveBeenCalledWith('/');
    expect(result.current.role).toBe('learner');
  });

  describe('requireAdminAccess()', () => {
    it('does nothing while loading', () => {
      mockUseCurrentUser.mockReturnValue({ role: 'admin', isAuthenticated: false, isLoading: true });
      const { result } = renderHook(() => useAdminAuth());
      result.current.requireAdminAccess();
      expect(mockPush).not.toHaveBeenCalled();
    });

    it('pushes to sign-in when unauthenticated', () => {
      mockUseCurrentUser.mockReturnValue({ role: null, isAuthenticated: false, isLoading: false });
      mockReplace.mockClear(); // ignore the effect-driven replace
      const { result } = renderHook(() => useAdminAuth());
      mockPush.mockClear();
      result.current.requireAdminAccess();
      expect(mockPush).toHaveBeenCalledWith('/sign-in?next=/admin');
    });

    it('pushes a non-admin user to their default route', () => {
      mockUseCurrentUser.mockReturnValue({ role: 'learner', isAuthenticated: true, isLoading: false });
      const { result } = renderHook(() => useAdminAuth());
      mockPush.mockClear();
      result.current.requireAdminAccess();
      expect(mockPush).toHaveBeenCalledWith('/');
    });

    it('does nothing for an admin user', () => {
      mockUseCurrentUser.mockReturnValue({ role: 'admin', isAuthenticated: true, isLoading: false });
      const { result } = renderHook(() => useAdminAuth());
      mockPush.mockClear();
      result.current.requireAdminAccess();
      expect(mockPush).not.toHaveBeenCalled();
    });
  });
});
