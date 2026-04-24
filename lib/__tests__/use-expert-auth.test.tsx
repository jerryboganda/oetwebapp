import { waitFor } from '@testing-library/react';
const { mockReplace, mockUseCurrentUser, mockFetchExpertMe } = vi.hoisted(() => ({
  mockReplace: vi.fn(),
  mockUseCurrentUser: vi.fn(),
  mockFetchExpertMe: vi.fn(),
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
import { renderWithRouter } from '@/tests/test-utils';

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

    renderWithRouter(<TestComponent />, { router: { replace: mockReplace } });

    await waitFor(() => {
      expect(mockReplace).toHaveBeenCalledWith('/');
    });
  });
});