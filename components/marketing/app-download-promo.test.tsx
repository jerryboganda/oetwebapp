import { render, screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { AppDownloadPromo } from './app-download-promo';
import {
  ANDROID_INSTALL_URL,
  IOS_DOWNLOAD_URL,
  MAC_DOWNLOAD_URL,
  WINDOWS_DOWNLOAD_URL,
} from '@/lib/app-downloads';

const IOS_BADGE_NAME = 'Download the OET app on the App Store';

// Windows / Mac / Android resolve straight to their own installer (one-click).
// Asserted against the canonical constants so this tracks the single source of
// truth rather than freezing URLs that were themselves the defect.
const RESOLVED_BADGES: Array<[string, string]> = [
  ['Download the OET app for Windows', WINDOWS_DOWNLOAD_URL],
  ['Download the OET app for Mac', MAC_DOWNLOAD_URL],
  ['Get the OET app on Google Play', ANDROID_INSTALL_URL],
];

describe('AppDownloadPromo', () => {
  it.each(['banner', 'card', 'modal'] as const)(
    'renders equal platform badges in the %s variant',
    (variant) => {
      render(
        <AppDownloadPromo variant={variant} onClose={variant === 'modal' ? () => undefined : undefined} />,
      );

      for (const [name, href] of RESOLVED_BADGES) {
        const link = screen.getByRole('link', { name });
        expect(link).toHaveAttribute('href', href);
        // The banner renders the compact badge (fixed h-16); the card and modal
        // use the responsive size (h-14, sm:h-20).
        if (variant === 'banner') {
          expect(link).toHaveClass('h-16', 'w-full', 'rounded-2xl');
        } else {
          expect(link).toHaveClass('h-14', 'sm:h-20', 'w-full', 'rounded-2xl');
        }
      }

      // iOS is a link only once an Apple-approved channel exists; otherwise the
      // badge is inert, so a candidate is never offered an install that cannot
      // complete.
      if (IOS_DOWNLOAD_URL) {
        expect(screen.getByRole('link', { name: IOS_BADGE_NAME })).toHaveAttribute(
          'href',
          IOS_DOWNLOAD_URL,
        );
        expect(screen.getAllByRole('link')).toHaveLength(4);
      } else {
        expect(screen.getByLabelText(new RegExp(IOS_BADGE_NAME))).toHaveAttribute(
          'aria-disabled',
          'true',
        );
        expect(screen.getAllByRole('link')).toHaveLength(3);
      }

      if (variant === 'modal') {
        expect(screen.getByRole('dialog')).toHaveAttribute('aria-labelledby', 'app-download-modal-title');
        expect(screen.getByRole('button', { name: 'Close' })).toBeInTheDocument();
      }
      expect(screen.queryByText('Download for')).not.toBeInTheDocument();
      expect(screen.queryByText('Download directly')).not.toBeInTheDocument();
      expect(screen.queryByText('iOS app')).not.toBeInTheDocument();
      expect(screen.queryByText('Windows & Mac')).not.toBeInTheDocument();
    },
  );
});
