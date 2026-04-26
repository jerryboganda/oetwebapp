import { describe, it, expect, vi, beforeEach } from 'vitest';
import { renderHook } from '@testing-library/react';

const { mockTrack } = vi.hoisted(() => ({ mockTrack: vi.fn() }));

vi.mock('@/lib/analytics', () => ({
  analytics: { track: mockTrack },
}));

import { useAnalytics } from '../use-analytics';

describe('useAnalytics', () => {
  beforeEach(() => {
    mockTrack.mockReset();
  });

  it('returns a stable track function across re-renders', () => {
    const { result, rerender } = renderHook(() => useAnalytics());
    const firstTrack = result.current.track;
    rerender();
    expect(result.current.track).toBe(firstTrack);
  });

  it('forwards event + properties to analytics.track', () => {
    const { result } = renderHook(() => useAnalytics());
    result.current.track('signin_succeeded' as never, { foo: 'bar' });
    expect(mockTrack).toHaveBeenCalledTimes(1);
    expect(mockTrack).toHaveBeenCalledWith('signin_succeeded', { foo: 'bar' });
  });

  it('forwards events without properties', () => {
    const { result } = renderHook(() => useAnalytics());
    result.current.track('signout_clicked' as never);
    expect(mockTrack).toHaveBeenCalledWith('signout_clicked', undefined);
  });
});
