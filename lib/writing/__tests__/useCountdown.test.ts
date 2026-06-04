import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { renderHook, act } from '@testing-library/react';
import { useDeadlineCountdown } from '../useCountdown';

describe('useDeadlineCountdown', () => {
  beforeEach(() => {
    vi.useFakeTimers();
  });

  afterEach(() => {
    vi.useRealTimers();
  });

  it('returns the correct initial remaining seconds on mount', () => {
    const now = 1_000_000;
    vi.setSystemTime(now);
    const deadline = now + 10_000; // 10 seconds out

    const { result } = renderHook(() => useDeadlineCountdown(deadline));
    expect(result.current).toBe(10);
  });

  it('counts down as the interval fires', () => {
    const now = 1_000_000;
    vi.setSystemTime(now);
    const deadline = now + 10_000;

    const { result } = renderHook(() => useDeadlineCountdown(deadline));
    expect(result.current).toBe(10);

    act(() => {
      vi.advanceTimersByTime(3_000);
    });

    expect(result.current).toBe(7);
  });

  it('recomputes from the deadline (not naive decrement) after visibilitychange', () => {
    const now = 1_000_000;
    vi.setSystemTime(now);
    const deadline = now + 10_000; // 10 seconds out

    const { result } = renderHook(() => useDeadlineCountdown(deadline));
    expect(result.current).toBe(10);

    // Advance system time by 6 seconds WITHOUT firing the interval (simulates
    // the tab being backgrounded — the JS timer pauses but wall-clock advances).
    vi.setSystemTime(now + 6_000);

    // Fire a visibilitychange event to trigger the recompute.
    act(() => {
      Object.defineProperty(document, 'visibilityState', {
        value: 'visible',
        writable: true,
        configurable: true,
      });
      document.dispatchEvent(new Event('visibilitychange'));
    });

    // Should be 4, computed from the deadline, not from naive "10 - 3".
    expect(result.current).toBe(4);
  });

  it('calls onZero exactly once when the deadline passes', () => {
    const now = 1_000_000;
    vi.setSystemTime(now);
    const deadline = now + 3_000; // 3 seconds out

    const onZero = vi.fn();
    renderHook(() => useDeadlineCountdown(deadline, { onZero }));

    expect(onZero).not.toHaveBeenCalled();

    act(() => { vi.advanceTimersByTime(3_000); });

    expect(onZero).toHaveBeenCalledTimes(1);

    // Additional ticks should not re-fire onZero.
    act(() => { vi.advanceTimersByTime(5_000); });

    expect(onZero).toHaveBeenCalledTimes(1);
  });

  it('returns 0 immediately when deadline is null', () => {
    const { result } = renderHook(() => useDeadlineCountdown(null));
    expect(result.current).toBe(0);
  });

  it('returns 0 when the deadline is already in the past', () => {
    const now = 1_000_000;
    vi.setSystemTime(now);
    const deadline = now - 5_000; // 5 seconds in the past

    const { result } = renderHook(() => useDeadlineCountdown(deadline));
    expect(result.current).toBe(0);
  });

  it('resets the guard and fires onZero again when the deadline changes', () => {
    const now = 1_000_000;
    vi.setSystemTime(now);

    const onZero = vi.fn();
    let deadline = now + 2_000;

    const { rerender } = renderHook(
      (dl: number) => useDeadlineCountdown(dl, { onZero }),
      { initialProps: deadline },
    );

    act(() => { vi.advanceTimersByTime(2_000); });
    expect(onZero).toHaveBeenCalledTimes(1);

    // Change the deadline to a new future value.
    deadline = now + 3_000;
    rerender(deadline);

    act(() => { vi.advanceTimersByTime(3_000); });

    // onZero should fire again for the new deadline.
    expect(onZero).toHaveBeenCalledTimes(2);
  });
});
