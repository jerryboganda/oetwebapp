import userEvent from '@testing-library/user-event';
import { screen } from '@testing-library/react';
import { DeviceChallengeForm } from './device-challenge-form';
import { renderWithRouter } from '@/tests/test-utils';

const { mockCancelDeviceVerification, mockSendDeviceVerificationOtp } = vi.hoisted(() => ({
  mockCancelDeviceVerification: vi.fn(),
  mockSendDeviceVerificationOtp: vi.fn(),
}));

vi.mock('@/contexts/auth-context', () => ({
  useAuth: () => ({
    pendingDeviceChallenge: {
      email: 'wrong@example.com',
      challengeToken: 'challenge-token',
      rememberMe: true,
    },
    completeDeviceVerification: vi.fn(),
    cancelDeviceVerification: mockCancelDeviceVerification,
  }),
}));

vi.mock('@/lib/auth-client', () => ({
  sendDeviceVerificationOtp: mockSendDeviceVerificationOtp,
}));

describe('DeviceChallengeForm', () => {
  beforeEach(() => {
    mockCancelDeviceVerification.mockReset();
    mockSendDeviceVerificationOtp.mockReset();
    mockSendDeviceVerificationOtp.mockResolvedValue({ destinationHint: 'w***@example.com' });
  });

  it('provides a mobile-safe back-to-sign-in link that cancels the pending challenge', async () => {
    const user = userEvent.setup();

    renderWithRouter(<DeviceChallengeForm nextHref="/dashboard" />);

    const backLink = screen.getByRole('link', { name: 'Back to sign in' });
    expect(backLink).toHaveAttribute('href', '/sign-in?next=%2Fdashboard');

    await user.click(backLink);

    expect(mockCancelDeviceVerification).toHaveBeenCalledOnce();
  });
});
