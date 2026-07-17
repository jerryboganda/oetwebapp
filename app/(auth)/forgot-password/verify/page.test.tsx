import { screen } from '@testing-library/react';

import ForgotPasswordVerifyPage from './page';
import { renderWithRouter } from '@/tests/test-utils';

describe('ForgotPasswordVerifyPage', () => {
  it('renders the reset-code verification step', () => {
    renderWithRouter(<ForgotPasswordVerifyPage />, {
      searchParams: new URLSearchParams({ email: 'learner@oet-with-dr-hesham.dev' }),
    });

    expect(screen.getByRole('heading', { name: /check your email/i })).toBeInTheDocument();
    expect(screen.getByText(/learner@oet-with-dr-hesham\.dev/i)).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /verify otp/i })).toBeInTheDocument();
  });
});
