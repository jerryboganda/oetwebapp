import { screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

const authClientMock = vi.hoisted(() => ({
  sendEmailVerificationOtp: vi.fn(),
  verifyEmailOtp: vi.fn(),
}));

const authContextMock = vi.hoisted(() => ({
  user: null as { email: string } | null,
  verifyEmailOtp: vi.fn(),
}));

vi.mock('@/lib/auth-client', () => authClientMock);
vi.mock('@/contexts/auth-context', () => ({
  useAuth: () => authContextMock,
}));

import VerifyEmailPage from './page';
import { renderWithRouter } from '@/tests/test-utils';

describe('VerifyEmailPage', () => {
  beforeEach(() => {
    window.sessionStorage.clear();
    authClientMock.sendEmailVerificationOtp.mockReset();
    authClientMock.verifyEmailOtp.mockReset();
    authContextMock.verifyEmailOtp.mockReset();
    authContextMock.user = null;
    authClientMock.sendEmailVerificationOtp.mockResolvedValue({
      challengeId: 'challenge-1',
      purpose: 'verify_email',
      deliveryChannel: 'email',
      destinationHint: 'h*****@yahoo.com',
      expiresAt: '2099-08-21T17:56:14.000Z',
      retryAfterSeconds: 60,
    });
  });

  it('sends one OTP on load and does not rotate it on remount', async () => {
    const { unmount } = renderWithRouter(<VerifyEmailPage />, {
      pathname: '/verify-email',
      searchParams: new URLSearchParams({ email: 'husam_20052@yahoo.com' }),
    });

    await waitFor(() => {
      expect(authClientMock.sendEmailVerificationOtp).toHaveBeenCalledTimes(1);
    });
    expect(authClientMock.sendEmailVerificationOtp).toHaveBeenCalledWith('husam_20052@yahoo.com');

    unmount();
    renderWithRouter(<VerifyEmailPage />, {
      pathname: '/verify-email',
      searchParams: new URLSearchParams({ email: 'husam_20052@yahoo.com' }),
    });

    await screen.findByText(/h\*{5}@yahoo\.com/i);
    expect(authClientMock.sendEmailVerificationOtp).toHaveBeenCalledTimes(1);
  });

  it('issues a new code only when the learner clicks Resend', async () => {
    const user = userEvent.setup();
    renderWithRouter(<VerifyEmailPage />, {
      pathname: '/verify-email',
      searchParams: new URLSearchParams({ email: 'husam_20052@yahoo.com' }),
    });

    await waitFor(() => {
      expect(authClientMock.sendEmailVerificationOtp).toHaveBeenCalledTimes(1);
    });

    await user.click(screen.getByRole('button', { name: /resend it/i }));

    await waitFor(() => {
      expect(authClientMock.sendEmailVerificationOtp).toHaveBeenCalledTimes(2);
    });
    expect(authClientMock.sendEmailVerificationOtp).toHaveBeenLastCalledWith(
      'husam_20052@yahoo.com',
      { forceNew: true },
    );
  });
});
