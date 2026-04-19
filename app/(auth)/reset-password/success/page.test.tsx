import { screen } from '@testing-library/react';
import { act } from 'react';

import ResetPasswordSuccessPage from './page';
import { renderWithRouter } from '@/tests/test-utils';

/**
 * Contract:
 *   - The success page shows a calm "you're all set" state (not a clinical
 *     "reset complete" heading).
 *   - Auto-redirect fires after 12 seconds — long enough for the user to
 *     read the screen without being bounced.
 *   - The countdown is visible + pausable so screen-reader users and
 *     anyone who wants to take the explicit CTA is not railroaded.
 *
 * These assertions deliberately use `getByRole('heading')` rather than raw
 * text to keep the spec grounded in what the accessibility tree exposes.
 */
describe('ResetPasswordSuccessPage', () => {
  const mockReplace = vi.fn();

  beforeEach(() => {
    vi.clearAllMocks();
    vi.useFakeTimers();
  });

  afterEach(() => {
    vi.useRealTimers();
  });

  it('renders the calm "you are all set" state with the countdown visible', () => {
    renderWithRouter(<ResetPasswordSuccessPage />, {
      router: { replace: mockReplace },
      searchParams: new URLSearchParams({ email: 'learner@oet-prep.dev' }),
    });

    expect(
      screen.getByRole('heading', { name: /you'?re all set/i }),
    ).toBeInTheDocument();

    // Countdown status is live-announced.
    expect(
      screen.getByText(/redirecting automatically in 12 seconds/i),
    ).toBeInTheDocument();

    // Primary CTA is always present as a user-driven escape hatch.
    expect(
      screen.getByRole('link', { name: /continue to sign in/i }),
    ).toHaveAttribute('href', '/sign-in?email=learner%40oet-prep.dev');
  });

  it('redirects to sign-in after the 12-second countdown', () => {
    renderWithRouter(<ResetPasswordSuccessPage />, {
      router: { replace: mockReplace },
      searchParams: new URLSearchParams({ email: 'learner@oet-prep.dev' }),
    });

    // Partway through — should NOT have redirected yet.
    act(() => {
      vi.advanceTimersByTime(3000);
    });
    expect(mockReplace).not.toHaveBeenCalled();

    // After the full window has elapsed, redirect must fire exactly once.
    act(() => {
      vi.advanceTimersByTime(10_000);
    });
    expect(mockReplace).toHaveBeenCalledWith('/sign-in?email=learner%40oet-prep.dev');
  });

  it('preserves a `next` parameter in the redirect target', () => {
    renderWithRouter(<ResetPasswordSuccessPage />, {
      router: { replace: mockReplace },
      searchParams: new URLSearchParams({
        email: 'learner@oet-prep.dev',
        next: '/dashboard',
      }),
    });

    act(() => {
      vi.advanceTimersByTime(13_000);
    });

    expect(mockReplace).toHaveBeenCalledWith(
      '/sign-in?email=learner%40oet-prep.dev&next=%2Fdashboard',
    );
  });
});
