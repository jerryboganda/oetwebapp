import { render, screen } from '@testing-library/react';
import { act } from 'react';
import { beforeEach, describe, expect, it, vi } from 'vitest';

const { mockReplace, mockGet } = vi.hoisted(() => ({
  mockReplace: vi.fn(),
  mockGet: vi.fn(),
}));

vi.mock('next/navigation', () => ({
  useRouter: () => ({
    replace: mockReplace,
  }),
  useSearchParams: () => ({
    get: mockGet,
  }),
}));

import ResetPasswordSuccessPage from './page';

describe('ResetPasswordSuccessPage', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    vi.useFakeTimers();
    mockGet.mockImplementation((key: string) => {
      if (key === 'email') {
        return 'learner@oet-prep.dev';
      }

      return null;
    });
  });

  it('shows the password reset success state and redirects to sign in after 3 seconds', () => {
    render(<ResetPasswordSuccessPage />);

    expect(screen.getByRole('heading', { name: /reset complete/i })).toBeInTheDocument();

    act(() => {
      vi.advanceTimersByTime(3000);
    });

    expect(mockReplace).toHaveBeenCalledWith('/sign-in?email=learner%40oet-prep.dev');
  });
});
