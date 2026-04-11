import { screen } from '@testing-library/react';
import { act } from 'react';

import ResetPasswordSuccessPage from './page';
import { renderWithRouter } from '@/tests/test-utils';

describe('ResetPasswordSuccessPage', () => {
  const mockReplace = vi.fn();

  beforeEach(() => {
    vi.clearAllMocks();
    vi.useFakeTimers();
  });

  it('shows the password reset success state and redirects to sign in after 3 seconds', () => {
    renderWithRouter(<ResetPasswordSuccessPage />, {
      router: { replace: mockReplace },
      searchParams: new URLSearchParams({ email: 'learner@oet-prep.dev' }),
    });

    expect(screen.getByRole('heading', { name: /reset complete/i })).toBeInTheDocument();

    act(() => {
      vi.advanceTimersByTime(3000);
    });

    expect(mockReplace).toHaveBeenCalledWith('/sign-in?email=learner%40oet-prep.dev');
  });
});
