import { renderHook } from '@testing-library/react';
import { useTimer } from '../useTimer';

describe('useTimer', () => {
  afterEach(() => {
    sessionStorage.clear();
  });

  // Reload right at or after 00:00 restores an already-expired countdown from
  // sessionStorage. startTimer() is gated on `!isExpired`, so the interval
  // that would normally call onExpire never starts — without this, the
  // Listening sub-section (and anything else built on this hook) would be
  // stranded with no way to auto-advance short of a manual tap.
  it('fires onExpire once when mounted already past an expired countdown', () => {
    sessionStorage.setItem('reading_timer_elapsed_listening-exam:attempt-1:0', '10');
    const onExpire = vi.fn();

    renderHook(() => useTimer(5, 'down', onExpire, 'listening-exam:attempt-1:0'));

    expect(onExpire).toHaveBeenCalledTimes(1);
  });

  it('does not fire onExpire for a countdown restored still in progress', () => {
    sessionStorage.setItem('reading_timer_elapsed_listening-exam:attempt-2:0', '2');
    const onExpire = vi.fn();

    renderHook(() => useTimer(5, 'down', onExpire, 'listening-exam:attempt-2:0'));

    expect(onExpire).not.toHaveBeenCalled();
  });
});
