import { render, screen } from '@testing-library/react';
import type { ComponentProps } from 'react';
import { describe, expect, it, vi } from 'vitest';
import GetAppPage from './page';
import {
  ANDROID_INSTALL_URL,
  IOS_DOWNLOAD_URL,
  MAC_DOWNLOAD_URL,
  WINDOWS_DOWNLOAD_URL,
} from '@/lib/app-downloads';

vi.mock('next/image', () => ({
  default: (props: ComponentProps<'img'>) => <img alt="" {...props} />,
}));

/**
 * The badge's height comes from PlatformDownloadBadge's sizeClassName, which is
 * responsive (`h-14` below sm:, `sm:h-20` above it) — the 8 Sep 2026 mobile
 * brief replaced the old unconditional `h-20`. Assert the tokens the component
 * actually renders so this keeps checking "every badge is the same size"
 * instead of a class that no longer exists.
 */
const BADGE_SIZE_CLASSES = ['h-14', 'sm:h-20', 'rounded-2xl', 'w-full', 'max-w-[220px]'];

const IOS_BADGE_NAME = 'Download the OET app on the App Store';

describe('GetAppPage download badges', () => {
  it('keeps the desktop and Android badges aligned on their one-click installers', () => {
    render(<GetAppPage />);

    // One-click: each badge resolves straight to that platform's signed
    // installer, never to a chooser that asks which build to pick.
    const badges: Array<[string, string]> = [
      ['Download the OET app for Windows', WINDOWS_DOWNLOAD_URL],
      ['Download the OET app for Mac', MAC_DOWNLOAD_URL],
      ['Get the OET app on Google Play', ANDROID_INSTALL_URL],
    ];

    for (const [name, href] of badges) {
      const badge = screen.getByRole('link', { name });
      expect(badge).toHaveClass(...BADGE_SIZE_CLASSES);
      expect(badge).toHaveAttribute('href', href);
    }
  });

  it('only exposes iOS as a link once an Apple-approved channel exists', () => {
    render(<GetAppPage />);

    if (IOS_DOWNLOAD_URL) {
      const ios = screen.getByRole('link', { name: IOS_BADGE_NAME });
      expect(ios).toHaveAttribute('href', IOS_DOWNLOAD_URL);
      expect(ios).toHaveClass(...BADGE_SIZE_CLASSES);
      return;
    }

    // No App Store / TestFlight URL configured yet. The badge must stay inert
    // rather than offer a download that cannot install — the raw
    // /api/download/ios link is deliberately gone.
    const ios = screen.getByLabelText(new RegExp(IOS_BADGE_NAME));
    expect(ios.tagName).toBe('SPAN');
    expect(ios).toHaveAttribute('aria-disabled', 'true');
    expect(screen.queryByRole('link', { name: IOS_BADGE_NAME })).toBeNull();
  });

  it('disables the Mac download and points candidates at the Web App while the handover kill-switch is armed', () => {
    process.env.NEXT_PUBLIC_MAC_DOWNLOAD_DISABLED = '1';
    try {
      render(<GetAppPage />);

      // No Mac link anywhere: the badge renders inert with the redirect note.
      expect(screen.queryByRole('link', { name: 'Download the OET app for Mac' })).toBeNull();
      const mac = screen.getByLabelText(/Download the OET app for Mac — Use Web App/);
      expect(mac.tagName).toBe('SPAN');
      expect(mac).toHaveAttribute('aria-disabled', 'true');
      expect(screen.getAllByText(/Temporarily unavailable/).length).toBeGreaterThan(0);

      // Other platforms stay untouched.
      expect(screen.getByRole('link', { name: 'Download the OET app for Windows' })).toHaveAttribute(
        'href',
        WINDOWS_DOWNLOAD_URL,
      );
    } finally {
      delete process.env.NEXT_PUBLIC_MAC_DOWNLOAD_DISABLED;
    }
  });
});
