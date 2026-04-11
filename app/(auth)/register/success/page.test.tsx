import { screen } from '@testing-library/react';
import { act } from 'react';

import RegisterSuccessPage from './page';
import { renderWithRouter } from '@/tests/test-utils';

describe('RegisterSuccessPage', () => {
  const mockReplace = vi.fn();

  beforeEach(() => {
    vi.clearAllMocks();
    vi.useFakeTimers();
  });

  it('shows the success message and redirects to sign in after 3 seconds', () => {
    renderWithRouter(<RegisterSuccessPage />, {
      router: { replace: mockReplace },
      searchParams: new URLSearchParams({ email: 'learner@oet-prep.dev' }),
    });

    expect(screen.getByRole('heading', { name: /account created successfully/i })).toBeInTheDocument();

    act(() => {
      vi.advanceTimersByTime(3000);
    });

    expect(mockReplace).toHaveBeenCalledWith('/sign-in?email=learner%40oet-prep.dev');
  });
});
