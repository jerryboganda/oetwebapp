import { renderHook } from '@testing-library/react';
import { useRiseNudge } from '../use-rise-nudge';

describe('useRiseNudge', () => {
  it('takes the first settled value as the baseline, so the initial load never nudges', () => {
    const { result, rerender } = renderHook(({ count, ready }) => useRiseNudge(count, ready), {
      initialProps: { count: 0, ready: false },
    });
    rerender({ count: 12, ready: true });

    expect(result.current).toBe(0);
  });

  it('nudges once per rise after loading, and not when the count falls', () => {
    const { result, rerender } = renderHook(({ count, ready }) => useRiseNudge(count, ready), {
      initialProps: { count: 3, ready: true },
    });

    rerender({ count: 4, ready: true });
    expect(result.current).toBe(1);
    rerender({ count: 2, ready: true });
    expect(result.current).toBe(1);
    rerender({ count: 5, ready: true });
    expect(result.current).toBe(2);
  });
});
