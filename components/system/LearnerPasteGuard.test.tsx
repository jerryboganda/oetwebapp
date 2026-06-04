import { render, cleanup } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';

const { mockUsePathname } = vi.hoisted(() => ({
  mockUsePathname: vi.fn<() => string>(),
}));

vi.mock('next/navigation', () => ({
  usePathname: () => mockUsePathname(),
}));

import { LearnerPasteGuard } from './LearnerPasteGuard';

/**
 * Dispatch a real, cancelable `paste` event at the document and report whether
 * a listener called preventDefault (i.e. it was blocked). jsdom does not
 * implement `ClipboardEvent`, so we synthesise a generic cancelable Event with
 * the `paste` type — the guard only cares about the event type + preventDefault.
 */
function dispatchPaste(): boolean {
  const event = new Event('paste', { bubbles: true, cancelable: true });
  document.dispatchEvent(event);
  return event.defaultPrevented;
}

describe('LearnerPasteGuard', () => {
  afterEach(() => {
    cleanup();
    mockUsePathname.mockReset();
  });

  it('prevents paste on a learner path', () => {
    mockUsePathname.mockReturnValue('/dashboard');
    render(<LearnerPasteGuard />);

    expect(dispatchPaste()).toBe(true);
  });

  it('does NOT prevent paste on an /admin path', () => {
    mockUsePathname.mockReturnValue('/admin/content/new');
    render(<LearnerPasteGuard />);

    expect(dispatchPaste()).toBe(false);
  });

  it('does NOT prevent paste on auth/OTP routes', () => {
    mockUsePathname.mockReturnValue('/sign-in');
    render(<LearnerPasteGuard />);

    expect(dispatchPaste()).toBe(false);
  });

  it('blocks copy, cut, dragstart and drop on a learner path', () => {
    mockUsePathname.mockReturnValue('/practice');
    render(<LearnerPasteGuard />);

    for (const type of ['copy', 'cut', 'dragstart', 'drop'] as const) {
      const event = new Event(type, { bubbles: true, cancelable: true });
      document.dispatchEvent(event);
      expect(event.defaultPrevented, `${type} should be prevented`).toBe(true);
    }
  });

  it('removes its listeners on unmount (no leak across routes)', () => {
    mockUsePathname.mockReturnValue('/dashboard');
    const { unmount } = render(<LearnerPasteGuard />);
    expect(dispatchPaste()).toBe(true);

    unmount();
    // After unmount the cleanup must have detached the capture listener.
    expect(dispatchPaste()).toBe(false);
  });
});
