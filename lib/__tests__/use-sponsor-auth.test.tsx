import { screen, waitFor } from '@testing-library/react';

const { mockReplace, mockUseCurrentUser } = vi.hoisted(() => ({
  mockReplace: vi.fn(),
  mockUseCurrentUser: vi.fn(),
}));

vi.mock('@/lib/hooks/use-current-user', () => ({
  useCurrentUser: () => mockUseCurrentUser(),
}));

import { SponsorAuthGate } from '@/app/sponsor/sponsor-auth-gate';
import { renderWithRouter } from '@/tests/test-utils';

describe('SponsorAuthGate', () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it('does not render sponsor content for authenticated non-sponsor users', async () => {
    mockUseCurrentUser.mockReturnValue({
      role: 'learner',
      isAuthenticated: true,
      isLoading: false,
    });

    renderWithRouter(
      <SponsorAuthGate>
        <div>Sponsor-only content</div>
      </SponsorAuthGate>,
      { router: { replace: mockReplace } },
    );

    expect(screen.queryByText('Sponsor-only content')).toBeNull();
    await waitFor(() => {
      expect(mockReplace).toHaveBeenCalled();
    });
  });
});
