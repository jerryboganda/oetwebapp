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

// The page keeps a module-scoped "already auto-sent for this email" guard that
// survives remounts (that is the fix). Resetting the module registry between
// tests simulates a fresh WebView session per test, while unmount+render
// within a test exercises the guard.
import VerifyEmailPage from './page';
import { clearAutoSendRequest } from './auto-send-guard';
import { renderWithRouter } from '@/tests/test-utils';

const CHALLENGE_KEY = 'oet.verify-email.challenge:husam_20052@yahoo.com';

/**
 * Some Node/jsdom combinations expose a broken Storage shim whose methods are
 * missing (Node ≥22 ships a global `localStorage` that can shadow jsdom's).
 * Install a working in-memory fallback so the suite can exercise the
 * persistence semantics deterministically.
 */
function ensureWorkingLocalStorage() {
  if (typeof window.localStorage?.setItem === 'function') return;
  const store = new Map<string, string>();
  Object.defineProperty(window, 'localStorage', {
    configurable: true,
    value: {
      getItem: (key: string) => store.get(key) ?? null,
      setItem: (key: string, value: string) => void store.set(key, String(value)),
      removeItem: (key: string) => void store.delete(key),
      clear: () => store.clear(),
    },
  });
}

describe('VerifyEmailPage', () => {
  beforeEach(() => {
    ensureWorkingLocalStorage();
    window.sessionStorage.clear();
    // Targeted removals rather than localStorage.clear(): some CI/node
    // combinations expose a partial Storage shim without clear().
    window.localStorage.removeItem(CHALLENGE_KEY);
    authClientMock.sendEmailVerificationOtp.mockReset();
    authClientMock.verifyEmailOtp.mockReset();
    authContextMock.verifyEmailOtp.mockReset();
    authContextMock.user = null;
    // Fresh WebView session per test: reset the module-scoped auto-send guard.
    clearAutoSendRequest();
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

  it('keeps the OTP session active when the mobile WebView is killed and reloaded mid-registration', async () => {
    // Simulates the Android/iOS flow: request OTP → background app to check
    // Gmail → OS kills the WebView → Capacitor cold-reloads the page on
    // return. The persisted challenge must survive (localStorage, not
    // sessionStorage) so NO second OTP is auto-requested.
    const { unmount } = renderWithRouter(<VerifyEmailPage />, {
      pathname: '/verify-email',
      searchParams: new URLSearchParams({ email: 'husam_20052@yahoo.com' }),
    });

    await waitFor(() => {
      expect(authClientMock.sendEmailVerificationOtp).toHaveBeenCalledTimes(1);
    });

    // The challenge must be persisted in localStorage (survives WebView
    // process death), never sessionStorage.
    expect(window.localStorage.getItem(CHALLENGE_KEY)).toBeTruthy();

    unmount();
    // Cold reload: fresh React tree, fresh refs — only storage carries over.
    renderWithRouter(<VerifyEmailPage />, {
      pathname: '/verify-email',
      searchParams: new URLSearchParams({ email: 'husam_20052@yahoo.com' }),
    });

    await screen.findByText(/h\*{5}@yahoo\.com/i);
    expect(authClientMock.sendEmailVerificationOtp).toHaveBeenCalledTimes(1);
  });

  it('does not silently rotate the OTP when the stored challenge is lost mid-session', async () => {
    const { unmount } = renderWithRouter(<VerifyEmailPage />, {
      pathname: '/verify-email',
      searchParams: new URLSearchParams({ email: 'husam_20052@yahoo.com' }),
    });

    await waitFor(() => {
      expect(authClientMock.sendEmailVerificationOtp).toHaveBeenCalledTimes(1);
    });

    unmount();
    // Storage record gone (private mode / blocked storage) but the same
    // WebView session continues — must NOT fire another send request.
    window.localStorage.removeItem(CHALLENGE_KEY);
    renderWithRouter(<VerifyEmailPage />, {
      pathname: '/verify-email',
      searchParams: new URLSearchParams({ email: 'husam_20052@yahoo.com' }),
    });

    // Exact-match the field hint (the subtitle also starts with the same
    // words, so a loose regex would match two nodes).
    await screen.findByText(
      'Enter the 6 digit verification code sent to husam_20052@yahoo.com.',
      { exact: true },
    );
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
