import { render, screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { WebNotAllowedNotice } from './web-not-allowed-notice';
import { PLATFORM_ARIA_LABELS, PLATFORM_ORDER } from '@/components/marketing/store-badges';

/**
 * Regression coverage for the mobile "App Required for Video Playback" gate
 * (Final Developer Modification Brief, 8 Sep 2026, item 2). jsdom does no
 * real layout, so this asserts on the Tailwind classes the fix actually
 * changed rather than pixel geometry: a bare `min-h-[360px]`/`py-12`/`h-20`
 * (no `sm:` guard) is exactly what let the panel outgrow a 360-430px phone
 * viewport with nothing to fall back on but an ancestor `overflow-hidden`.
 */
describe('WebNotAllowedNotice on a real mobile viewport', () => {
  it('is scrollable in its own box, so tall content is never hard-clipped regardless of breakpoint math', () => {
    const { container } = render(<WebNotAllowedNotice />);
    const panel = container.firstElementChild as HTMLElement;
    const classes = panel.className.split(/\s+/);

    expect(classes).toContain('overflow-y-auto');
  });

  it('shrinks height/padding below the sm: breakpoint instead of forcing ~360px+py-12 on every viewport', () => {
    const { container } = render(<WebNotAllowedNotice />);
    const panel = container.firstElementChild as HTMLElement;
    const classes = panel.className.split(/\s+/);

    expect(classes).toContain('min-h-[240px]');
    expect(classes).toContain('py-8');
    expect(classes).toContain('sm:min-h-[360px]');
    expect(classes).toContain('sm:py-12');
    // The old unconditional (non-responsive) values must not reappear.
    expect(classes).not.toContain('min-h-[360px]');
    expect(classes).not.toContain('py-12');
  });

  it('renders every platform download button, visible and tappable', () => {
    render(<WebNotAllowedNotice />);
    for (const platform of PLATFORM_ORDER) {
      expect(screen.getByRole('link', { name: PLATFORM_ARIA_LABELS[platform] })).toBeInTheDocument();
    }
  });

  it('shrinks each download button below sm: via a responsive height variant, not a bare fixed height', () => {
    render(<WebNotAllowedNotice />);
    const classes = screen.getByRole('link', { name: PLATFORM_ARIA_LABELS.windows }).className.split(/\s+/);

    expect(classes).toContain('h-14');
    expect(classes).toContain('sm:h-20');
    expect(classes).not.toContain('h-20');
  });
});
