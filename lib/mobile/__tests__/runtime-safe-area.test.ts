import { describe, expect, it, vi, beforeEach, afterEach } from 'vitest';

// Root-cause regression guard for the "10 Sep 2026 item 2" cold-launch bug:
// header pushed under the status bar / bottom nav too low on the FIRST
// render, self-correcting only after backgrounding and returning.
//
// app/globals.css's :root default for --safe-area-inset-top/bottom is
// `env(safe-area-inset-*)`, which Android's WebView never populates (see
// MainActivity's insets bridge comment) — the real value depends entirely on
// MainActivity's async native push. setSafeAreaInsets() is the JS mirror
// that runs synchronously in initializeMobileRuntime()'s prelude; on a cold
// launch it can run before that native push lands (or, per the bug report,
// before it lands at all during the session). This test locks in that
// setSafeAreaInsets() now applies a non-zero Android placeholder instead of
// silently leaving the CSS vars at their hard-0 default in that window, and
// that it never overwrites a real native-pushed value when one is present.
// Pixel-perfect correctness on a real device (Samsung S24 Ultra specifically)
// still needs a device/emulator QA pass — see docs referenced in
// components/layout/__tests__/top-nav.test.tsx for the same caveat.

const { getPlatformMock } = vi.hoisted(() => ({
  getPlatformMock: vi.fn(() => 'web' as 'web' | 'ios' | 'android'),
}));

vi.mock('@capacitor/core', () => ({
  Capacitor: {
    getPlatform: getPlatformMock,
    isNativePlatform: vi.fn(() => true),
  },
}));

import { setSafeAreaInsets } from '../runtime';

function getInsetVar(name: string): string {
  return document.documentElement.style.getPropertyValue(name);
}

describe('setSafeAreaInsets', () => {
  beforeEach(() => {
    getPlatformMock.mockReturnValue('web');
    delete (window as unknown as { __oetSafeAreaInsets?: unknown }).__oetSafeAreaInsets;
    document.documentElement.style.removeProperty('--safe-area-inset-top');
    document.documentElement.style.removeProperty('--safe-area-inset-right');
    document.documentElement.style.removeProperty('--safe-area-inset-bottom');
    document.documentElement.style.removeProperty('--safe-area-inset-left');
  });

  afterEach(() => {
    vi.clearAllMocks();
  });

  it('applies a non-zero Android placeholder when the native push has not landed yet', () => {
    getPlatformMock.mockReturnValue('android');

    setSafeAreaInsets();

    // Not 0px/empty: this is the exact "header under the status bar" bug —
    // padding-top computing to 0 while MainActivity's real push is still
    // in flight (or never lands during this session).
    expect(getInsetVar('--safe-area-inset-top')).toBe('24px');
    expect(getInsetVar('--safe-area-inset-bottom')).toBe('16px');
    // Portrait left/right stay 0 — no cutout on those edges in practice.
    expect(getInsetVar('--safe-area-inset-left')).toBe('0px');
    expect(getInsetVar('--safe-area-inset-right')).toBe('0px');
  });

  it('never applies the Android placeholder on iOS or web — env() already works there', () => {
    getPlatformMock.mockReturnValue('ios');

    setSafeAreaInsets();

    // No native push and not Android: leave the CSS default (env()) alone
    // rather than guessing — nothing gets written.
    expect(getInsetVar('--safe-area-inset-top')).toBe('');
    expect(getInsetVar('--safe-area-inset-bottom')).toBe('');
  });

  it('prefers the real native-pushed value over the placeholder when both are available', () => {
    getPlatformMock.mockReturnValue('android');
    (window as unknown as { __oetSafeAreaInsets?: unknown }).__oetSafeAreaInsets = {
      top: 51,
      right: 0,
      bottom: 34,
      left: 0,
    };

    setSafeAreaInsets();

    expect(getInsetVar('--safe-area-inset-top')).toBe('51px');
    expect(getInsetVar('--safe-area-inset-bottom')).toBe('34px');
  });

  it('lets a later native push overwrite an earlier placeholder (cold-launch race resolves correctly)', () => {
    getPlatformMock.mockReturnValue('android');

    // T0: React mounts, native hasn't pushed yet.
    setSafeAreaInsets();
    expect(getInsetVar('--safe-area-inset-top')).toBe('24px');

    // T1: MainActivity's real push lands (mirrors onto window.__oetSafeAreaInsets,
    // and — in the real app — also sets the inline style directly). Simulate the
    // JS-side re-application (e.g. via the appStateChange resume path) picking up
    // the real value once it exists.
    (window as unknown as { __oetSafeAreaInsets?: unknown }).__oetSafeAreaInsets = {
      top: 59,
      right: 0,
      bottom: 42,
      left: 0,
    };
    setSafeAreaInsets();

    expect(getInsetVar('--safe-area-inset-top')).toBe('59px');
    expect(getInsetVar('--safe-area-inset-bottom')).toBe('42px');
  });
});
