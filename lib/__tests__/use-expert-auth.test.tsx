import { render, waitFor } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';

const { mockReplace, mockUseCurrentUser, mockFetchExpertMe } = vi.hoisted(() => ({
  mockReplace: vi.fn(),
  mockUseCurrentUser: vi.fn(),
  mockFetchExpertMe: vi.fn(),
}));

const mockRouter = {
  replace: mockReplace,
};

vi.mock('next/navigation', () => ({
  useRouter: () => mockRouter,
}));

vi.mock('@/lib/hooks/use-current-user', () => ({
  useCurrentUser: () => mockUseCurrentUser(),
}));

vi.mock('@/lib/api', async () => {
  const actual = await vi.importActual<typeof import('@/lib/api')>('@/lib/api');
  return {
    ...actual,
    fetchExpertMe: (...args: unknown[]) => mockFetchExpertMe(...args),
  };
});

import { ApiError } from '@/lib/api';
import { useExpertAuth } from '@/lib/hooks/use-expert-auth';

function TestComponent() {
  useExpertAuth();
  return null;
}

describe('useExpertAuth', () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it('routes forbidden experts back to the public workspace instead of looping on the expert console', async () => {
    mockUseCurrentUser.mockReturnValue({
      role: 'expert',
      isAuthenticated: true,
      isLoading: false,
    });
    mockFetchExpertMe.mockRejectedValueOnce(new ApiError(403, 'forbidden', 'Expert access is not available.', false));

    render(<TestComponent />);

    await waitFor(() => {
      expect(mockReplace).toHaveBeenCalledWith('/');
    });
  });
});