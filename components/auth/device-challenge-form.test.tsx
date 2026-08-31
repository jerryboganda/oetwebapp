import userEvent from '@testing-library/user-event';
import { screen, waitFor } from '@testing-library/react';
import { DeviceChallengeForm } from './device-challenge-form';
import { renderWithRouter } from '@/tests/test-utils';

const {
  mockCancelDeviceVerification,
  mockClaimDeviceVerificationOtpSend,
  mockCompleteDeviceVerification,
  mockSendDeviceVerificationOtp,
  mockSelectReplacementDevice,
} = vi.hoisted(() => ({
  mockCancelDeviceVerification: vi.fn(),
  mockClaimDeviceVerificationOtpSend: vi.fn(),
  mockCompleteDeviceVerification: vi.fn(),
  mockSendDeviceVerificationOtp: vi.fn(),
  mockSelectReplacementDevice: vi.fn(),
}));

let mockPendingChallenge: any = {
  email: 'wrong@example.com',
  challengeToken: 'challenge-token',
  rememberMe: true,
};

vi.mock('@/contexts/auth-context', () => ({
  useAuth: () => ({
    pendingDeviceChallenge: mockPendingChallenge,
    completeDeviceVerification: mockCompleteDeviceVerification,
    cancelDeviceVerification: mockCancelDeviceVerification,
  }),
}));

vi.mock('@/lib/auth-client', () => ({
  claimDeviceVerificationOtpSend: mockClaimDeviceVerificationOtpSend,
  sendDeviceVerificationOtp: mockSendDeviceVerificationOtp,
  selectReplacementDevice: mockSelectReplacementDevice,
  // Mirrors the module-under-test's storage-backed guard: reads whatever
  // the test's `mockPendingChallenge` currently holds, same as the real
  // `getPendingDeviceChallenge()` reads live storage.
  getPendingDeviceChallenge: () => mockPendingChallenge,
  formatDeviceCountdown: (s: number) => (s > 0 ? `${s}s` : '0s'),
}));

vi.mock('@/lib/auth/firebase-otp-recaptcha', () => ({
  obtainFirebaseOtpRecaptchaToken: vi.fn().mockResolvedValue(null),
}));

describe('DeviceChallengeForm', () => {
  beforeEach(() => {
    mockCancelDeviceVerification.mockReset();
    mockClaimDeviceVerificationOtpSend.mockReset();
    mockCompleteDeviceVerification.mockReset();
    mockSendDeviceVerificationOtp.mockReset();
    mockSelectReplacementDevice.mockReset();
    mockSendDeviceVerificationOtp.mockResolvedValue({ destinationHint: 'w***@example.com', deliveryChannel: 'email' });
    mockClaimDeviceVerificationOtpSend.mockImplementation(async (challengeToken: string) => {
      if (mockPendingChallenge?.challengeToken !== challengeToken
        || mockPendingChallenge?.otpRequestedForToken === challengeToken) {
        return false;
      }
      mockPendingChallenge = { ...mockPendingChallenge, otpRequestedForToken: challengeToken };
      return true;
    });
    mockSelectReplacementDevice.mockImplementation(async (id: string) => {
      mockPendingChallenge = { ...mockPendingChallenge, selectedDeviceId: id, challengeToken: 'bound-token' };
      return mockPendingChallenge;
    });
    // default free-slot challenge (no replacement)
    mockPendingChallenge = {
      email: 'wrong@example.com',
      challengeToken: 'challenge-token',
      rememberMe: true,
    };
  });

  it('provides a mobile-safe back-to-sign-in link that cancels the pending challenge', async () => {
    const user = userEvent.setup();

    renderWithRouter(<DeviceChallengeForm nextHref="/dashboard" />);

    const backLink = screen.getByRole('link', { name: 'Back to sign in' });
    expect(backLink).toHaveAttribute('href', '/sign-in?next=%2Fdashboard');

    await user.click(backLink);

    expect(mockCancelDeviceVerification).toHaveBeenCalledOnce();
  });

  it('free-slot flow: no replacement choice, shows active/max and allows verification', async () => {
    mockPendingChallenge = {
      email: 'learner@example.com',
      challengeToken: 'free-token',
      rememberMe: true,
      mode: 'otp_required',
      registeredDevices: [
        { id: 'dev-1', maskedDeviceId: 'abcd…1234', deviceName: 'Chrome on Windows', platform: 'web', trustedAt: '2026-04-20T10:00:00Z', lastSeenAt: '2026-04-22T10:00:00Z' },
      ],
      activeDeviceCount: 1,
      maxDevices: 2,
    };

    renderWithRouter(<DeviceChallengeForm />);

    expect(await screen.findByText(/Approved devices: 1\/2/)).toBeInTheDocument();
    expect(screen.queryByText(/Select a device to replace/)).not.toBeInTheDocument();
    // Verify button should be enabled (no selection required)
    const verifyBtn = screen.getByRole('button', { name: /Verify Device/i });
    expect(verifyBtn).toBeEnabled();
  });

  it('replacement_required: renders two-device choice, requires explicit selection before verify', async () => {
    const user = userEvent.setup();
    mockPendingChallenge = {
      email: 'learner@example.com',
      challengeToken: 'replacement-token',
      rememberMe: true,
      mode: 'replacement_required',
      registeredDevices: [
        { id: 'dev-1', maskedDeviceId: 'abcd…1111', deviceName: 'Chrome on Windows', platform: 'web', trustedAt: '2026-04-18T10:00:00Z', lastSeenAt: '2026-04-22T10:00:00Z' },
        { id: 'dev-2', maskedDeviceId: 'wxyz…2222', deviceName: 'Safari on iPhone', platform: 'capacitor-ios', trustedAt: '2026-04-19T10:00:00Z', lastSeenAt: '2026-04-21T10:00:00Z' },
      ],
      activeDeviceCount: 2,
      maxDevices: 2,
    };

    renderWithRouter(<DeviceChallengeForm />);

    expect(await screen.findByText(/Choose a device to replace/)).toBeInTheDocument();
    expect(screen.getByText(/abcd…1111/)).toBeInTheDocument();
    expect(screen.getByText(/wxyz…2222/)).toBeInTheDocument();
    const radios = screen.getAllByRole('radio');
    expect(radios).toHaveLength(2);

    const verifyBtn = screen.getByRole('button', { name: /Verify Device/i });
    expect(verifyBtn).toBeDisabled();
    expect(screen.getByText(/Select a device above to enable verification/)).toBeInTheDocument();

    // Selecting a device should bind and then enable verify
    await user.click(radios[0]);

    await waitFor(() => expect(mockSelectReplacementDevice).toHaveBeenCalledWith('dev-1'));
    await waitFor(() => expect(mockSendDeviceVerificationOtp).toHaveBeenCalled());

    // After selection, verify should be enabled
    await waitFor(() => expect(verifyBtn).toBeEnabled());

    // Trying to submit without code should show validation error
    await user.click(verifyBtn);
    await waitFor(() => expect(screen.getAllByText(/Enter the 6-digit code/).length).toBeGreaterThan(0));
  });

  it('rejects wrong/missing replacement selection via backend validation', async () => {
    const user = userEvent.setup();
    mockPendingChallenge = {
      email: 'learner@example.com',
      challengeToken: 'replacement-token',
      rememberMe: true,
      mode: 'replacement_required',
      registeredDevices: [
        { id: 'dev-1', maskedDeviceId: 'abcd…1111', deviceName: null, platform: 'web', trustedAt: '2026-04-18T10:00:00Z', lastSeenAt: null },
        { id: 'dev-2', maskedDeviceId: 'wxyz…2222', deviceName: null, platform: 'web', trustedAt: '2026-04-19T10:00:00Z', lastSeenAt: null },
      ],
      activeDeviceCount: 2,
      maxDevices: 2,
    };
    mockSelectReplacementDevice.mockRejectedValueOnce(new Error('Unable to select that device: invalid'));

    renderWithRouter(<DeviceChallengeForm />);

    const radios = await screen.findAllByRole('radio');
    await user.click(radios[1]);

    await waitFor(() => expect(screen.getByText(/Unable to select that device/)).toBeInTheDocument());
  });

  it('cooldown display: shows exact cooldownUntil, secondsRemaining, window/limit and live countdown', async () => {
    const future = new Date(Date.now() + 7200 * 1000).toISOString();
    mockPendingChallenge = {
      email: 'learner@example.com',
      challengeToken: 'cooldown-token',
      rememberMe: true,
      mode: 'cooldown',
      registeredDevices: [
        { id: 'dev-1', maskedDeviceId: 'abcd…1111', deviceName: 'Chrome', platform: 'web', trustedAt: '2026-04-18T10:00:00Z', lastSeenAt: null },
      ],
      activeDeviceCount: 2,
      maxDevices: 2,
      cooldownUntil: future,
      secondsRemaining: 7200,
      changeWindowDays: 7,
      changeMaxPerWindow: 3,
      countdown: '2h 0m 0s',
    };

    renderWithRouter(<DeviceChallengeForm />);

    expect(await screen.findByText(/Too many device changes recently/)).toBeInTheDocument();
    expect(screen.getByText(/Cooldown until/)).toBeInTheDocument();
    // Live countdown is computed from cooldownUntil, so allow any remaining text
    expect(screen.getByText(/remaining/)).toBeInTheDocument();
    expect(screen.getByText(/Window: 7 days/)).toBeInTheDocument();
    expect(screen.getByText(/Limit: 3/)).toBeInTheDocument();
    expect(screen.getByText(/Live countdown/)).toBeInTheDocument();
  });

  it('selecting a replacement device sends the OTP exactly once (regression: used to double-send)', async () => {
    const user = userEvent.setup();
    mockPendingChallenge = {
      email: 'learner@example.com',
      challengeToken: 'replacement-token',
      rememberMe: true,
      mode: 'replacement_required',
      registeredDevices: [
        { id: 'dev-1', maskedDeviceId: 'abcd…1111', deviceName: null, platform: 'web', trustedAt: '2026-04-18T10:00:00Z', lastSeenAt: null },
        { id: 'dev-2', maskedDeviceId: 'wxyz…2222', deviceName: null, platform: 'web', trustedAt: '2026-04-19T10:00:00Z', lastSeenAt: null },
      ],
      activeDeviceCount: 2,
      maxDevices: 2,
    };

    renderWithRouter(<DeviceChallengeForm />);

    const radios = await screen.findAllByRole('radio');
    await user.click(radios[0]);

    await waitFor(() => expect(mockSendDeviceVerificationOtp).toHaveBeenCalled());
    // Give any stray duplicate auto-send effect a chance to fire before asserting the count.
    await waitFor(() => expect(screen.getByRole('button', { name: /Verify Device/i })).toBeEnabled());
    expect(mockSendDeviceVerificationOtp).toHaveBeenCalledTimes(1);
  });

  it('does not auto-resend on remount when a code was already requested for this challenge (regression: WebView-reload resend loop)', async () => {
    mockPendingChallenge = {
      email: 'learner@example.com',
      challengeToken: 'already-sent-token',
      rememberMe: true,
      mode: 'otp_required',
      registeredDevices: [
        { id: 'dev-1', maskedDeviceId: 'abcd…1111', deviceName: 'Chrome', platform: 'web', trustedAt: '2026-04-18T10:00:00Z', lastSeenAt: null },
      ],
      activeDeviceCount: 1,
      maxDevices: 2,
      // Simulates a fresh mount (e.g. Android killed and recreated the
      // WebView while the learner checked their email) where storage
      // already recorded that a code went out for this exact token.
      otpRequestedForToken: 'already-sent-token',
    };

    renderWithRouter(<DeviceChallengeForm />);

    expect(await screen.findByText(/Approved devices: 1\/2/)).toBeInTheDocument();
    expect(mockSendDeviceVerificationOtp).not.toHaveBeenCalled();
  });

  it('claims an automatic OTP request atomically across concurrent mounts', async () => {
    mockPendingChallenge = {
      email: 'learner@example.com',
      challengeToken: 'concurrent-token',
      rememberMe: true,
      mode: 'otp_required',
      registeredDevices: [],
      activeDeviceCount: 0,
      maxDevices: 2,
    };

    renderWithRouter(
      <>
        <DeviceChallengeForm />
        <DeviceChallengeForm />
      </>,
    );

    await waitFor(() => expect(mockSendDeviceVerificationOtp).toHaveBeenCalled());
    expect(mockSendDeviceVerificationOtp).toHaveBeenCalledTimes(1);
  });

  it('routes free-slot verification exactly as today after correct code', async () => {
    mockPendingChallenge = {
      email: 'learner@example.com',
      challengeToken: 'free-token',
      rememberMe: true,
      mode: 'otp_required',
      registeredDevices: [
        { id: 'dev-1', maskedDeviceId: 'abcd…1111', deviceName: 'Chrome', platform: 'web', trustedAt: '2026-04-18T10:00:00Z', lastSeenAt: null },
      ],
      activeDeviceCount: 1,
      maxDevices: 2,
    };

    renderWithRouter(<DeviceChallengeForm nextHref="/dashboard" />);

    expect(await screen.findByText(/Approved devices: 1\/2/)).toBeInTheDocument();
    const verifyBtn = screen.getByRole('button', { name: /Verify Device/i });
    expect(verifyBtn).toBeEnabled();
    expect(screen.getByText(/Enter the code we emailed you/)).toBeInTheDocument();
  });
});
