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

  it('shows the password reset success state and redirects to sign in after the countdown', () => {
    renderWithRouter(<ResetPasswordSuccessPage />, {
      router: { replace: mockReplace },
      searchParams: new URLSearchParams({ email: 'learner@oet-prep.dev' }),
    });

    expect(screen.getByRole('heading', { name: /you're all set/i })).toBeInTheDocument();
    expect(screen.getByRole('status')).toHaveTextContent(/redirecting automatically in 12 seconds/i);

    act(() => {
      vi.advanceTimersByTime(12000);
    });

    expect(mockReplace).toHaveBeenCalledWith('/sign-in?email=learner%40oet-prep.dev');
  });
});
