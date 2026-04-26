import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest';
import { renderHook, act } from '@testing-library/react';
import { useIsMobile } from '../use-mobile';

type MqlListener = (e: MediaQueryListEvent) => void;

function installMatchMedia(initialWidth: number) {
  const listeners: { event: string; cb: MqlListener }[] = [];
  const mql: MediaQueryList = {
    matches: initialWidth < 768,
    media: '(max-width: 767px)',
    onchange: null,
    addEventListener: vi.fn((event: string, cb: MqlListener) => {
      listeners.push({ event, cb });
    }),
    removeEventListener: vi.fn((event: string, cb: MqlListener) => {
      const i = listeners.findIndex((l) => l.event === event && l.cb === cb);
      if (i >= 0) listeners.splice(i, 1);
    }),
    dispatchEvent: vi.fn(),
    addListener: vi.fn(),
    removeListener: vi.fn(),
  } as unknown as MediaQueryList;

  Object.defineProperty(window, 'matchMedia', {
    configurable: true,
    writable: true,
    value: vi.fn().mockReturnValue(mql),
  });

  Object.defineProperty(window, 'innerWidth', {
    configurable: true,
    writable: true,
    value: initialWidth,
  });

  return {
    mql,
    fireChange(newWidth: number) {
      Object.defineProperty(window, 'innerWidth', { configurable: true, value: newWidth });
      for (const l of listeners) {
        if (l.event === 'change') l.cb({} as MediaQueryListEvent);
      }
    },
  };
}

describe('useIsMobile', () => {
  let originalMatchMedia: unknown;
  let originalInnerWidth: number;

  beforeEach(() => {
    originalMatchMedia = window.matchMedia;
    originalInnerWidth = window.innerWidth;
  });

  afterEach(() => {
    if (originalMatchMedia === undefined) {
      delete (window as Record<string, unknown>).matchMedia;
    } else {
      Object.defineProperty(window, 'matchMedia', {
        configurable: true,
        writable: true,
        value: originalMatchMedia,
      });
    }
    Object.defineProperty(window, 'innerWidth', {
      configurable: true,
      writable: true,
      value: originalInnerWidth,
    });
  });

  it('returns true when the viewport is below the mobile breakpoint', () => {
    installMatchMedia(500);
    const { result } = renderHook(() => useIsMobile());
    expect(result.current).toBe(true);
  });

  it('returns false when the viewport is at or above 768px', () => {
    installMatchMedia(1024);
    const { result } = renderHook(() => useIsMobile());
    expect(result.current).toBe(false);
  });

  it('updates when the media query reports a change', () => {
    const { fireChange } = installMatchMedia(1024);
    const { result } = renderHook(() => useIsMobile());
    expect(result.current).toBe(false);

    act(() => fireChange(500));
    expect(result.current).toBe(true);
  });

  it('removes the listener on unmount', () => {
    const { mql } = installMatchMedia(500);
    const { unmount } = renderHook(() => useIsMobile());
    unmount();
    expect(mql.removeEventListener).toHaveBeenCalledWith('change', expect.any(Function));
  });
});
