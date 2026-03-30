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

import RegisterSuccessPage from './page';

describe('RegisterSuccessPage', () => {
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

  it('shows the success message and redirects to sign in after 3 seconds', () => {
    render(<RegisterSuccessPage />);

    expect(screen.getByRole('heading', { name: /account created successfully/i })).toBeInTheDocument();

    act(() => {
      vi.advanceTimersByTime(3000);
    });

    expect(mockReplace).toHaveBeenCalledWith('/sign-in?email=learner%40oet-prep.dev');
  });
});
