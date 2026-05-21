/**
 * Vitest spec for `PrepCountdown`.
 *
 * Verifies three load-bearing behaviours called out in plan C.3:
 *   1. The initial mm:ss display reflects the requested duration.
 *   2. `onComplete` fires exactly once when the timer reaches zero.
 *   3. A warning data-attribute flips on at <30s remaining (drives the
 *      red colour treatment + pulse animation).
 *
 * Uses `vi.useFakeTimers({ now })` so we can fast-forward real
 * wall-clock time without relying on `setInterval` resolution — the
 * component reads `Date.now()` directly, so fake timers must control
 * BOTH `Date.now` and `setInterval`.
 */
import { act, render, screen } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import { PrepCountdown } from '../PrepCountdown';

describe('PrepCountdown', () => {
  beforeEach(() => {
    vi.useFakeTimers({ now: 0, toFake: ['Date', 'setInterval', 'clearInterval', 'setTimeout', 'clearTimeout'] });
  });

  afterEach(() => {
    vi.useRealTimers();
  });

  it('renders the initial mm:ss display from durationSeconds', () => {
    render(<PrepCountdown durationSeconds={180} onComplete={() => undefined} />);

    const display = screen.getByTestId('prep-countdown-display');
    expect(display.textContent).toBe('03:00');
    expect(display.getAttribute('data-warning')).toBe('false');
  });

  it('fires onComplete exactly once when the countdown reaches zero', () => {
    const onComplete = vi.fn();
    render(<PrepCountdown durationSeconds={5} onComplete={onComplete} />);

    expect(onComplete).not.toHaveBeenCalled();

    // Advance well past the full duration; we expect the callback once.
    act(() => {
      vi.advanceTimersByTime(6_000);
    });

    expect(onComplete).toHaveBeenCalledTimes(1);

    // Further ticks must NOT re-fire the callback.
    act(() => {
      vi.advanceTimersByTime(2_000);
    });
    expect(onComplete).toHaveBeenCalledTimes(1);
  });

  it('flips the warning state on at <30 seconds remaining', () => {
    render(<PrepCountdown durationSeconds={60} onComplete={() => undefined} />);

    expect(screen.getByTestId('prep-countdown-display').getAttribute('data-warning')).toBe('false');

    // Advance to 29 seconds elapsed → 31 remaining → still NOT warning.
    act(() => {
      vi.advanceTimersByTime(29_000);
    });
    expect(screen.getByTestId('prep-countdown-display').getAttribute('data-warning')).toBe('false');

    // Cross the 30-second threshold (31s elapsed → 29 remaining).
    act(() => {
      vi.advanceTimersByTime(2_000);
    });
    const display = screen.getByTestId('prep-countdown-display');
    expect(display.getAttribute('data-warning')).toBe('true');
    expect(display.textContent).toBe('00:29');
  });
});
