import { render, screen } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';

const { mockGet } = vi.hoisted(() => ({
  mockGet: vi.fn(),
}));

vi.mock('next/navigation', () => ({
  useRouter: () => ({
    replace: vi.fn(),
    push: vi.fn(),
  }),
  useSearchParams: () => ({
    get: mockGet,
  }),
}));

import ForgotPasswordVerifyPage from './page';

describe('ForgotPasswordVerifyPage', () => {
  it('renders the reset-code verification step', () => {
    mockGet.mockImplementation((key: string) => {
      if (key === 'email') {
        return 'learner@oet-prep.dev';
      }

      return null;
    });

    render(<ForgotPasswordVerifyPage />);

    expect(screen.getByRole('heading', { name: /check your email/i })).toBeInTheDocument();
    expect(screen.getByText(/learner@oet-prep\.dev/i)).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /verify otp/i })).toBeInTheDocument();
  });
});
