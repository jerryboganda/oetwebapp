import { render, screen, within } from '@testing-library/react';
import { AuthScreenShell } from '../auth-screen-shell';
import {
  ANDROID_INSTALL_URL,
  IOS_DOWNLOAD_URL,
  MAC_DOWNLOAD_URL,
  WINDOWS_DOWNLOAD_URL,
} from '@/lib/app-downloads';

describe('AuthScreenShell', () => {
  it('exposes the primary auth content as a main landmark', () => {
    render(
      <AuthScreenShell title="Welcome back">
        <form aria-label="Sign in form">
          <button type="submit">Continue</button>
        </form>
      </AuthScreenShell>,
    );

    expect(screen.getByRole('main')).toContainElement(
      screen.getByRole('form', { name: 'Sign in form' }),
    );
  });

  it('places the responsive app download strip outside the auth card', () => {
    render(
      <AuthScreenShell title="Welcome back">
        <form aria-label="Sign in form">
          <button type="submit">Continue</button>
        </form>
      </AuthScreenShell>,
    );

    // The banner's accessible name comes from its heading, which now reads
    // "Study anywhere with the official Candidates App".
    const strip = screen.getByRole('region', { name: /official Candidates App/i });
    expect(strip).toBeInTheDocument();
    expect(strip).not.toBe(screen.getByRole('main').querySelector('[class*="card"]'));

    // Desktop downloads are one-click: the badge resolves straight to the
    // signed installer for the visitor's OS. It must NOT send users to the
    // /get-app chooser, which asked them to pick a DMG/Intel/Apple-Silicon
    // build — that choice is gone (brief: macOS one-click, no build picker).
    expect(screen.getByRole('link', { name: /download the OET app for Windows/i })).toHaveAttribute(
      'href',
      WINDOWS_DOWNLOAD_URL,
    );
    expect(screen.getByRole('link', { name: /download the OET app for Mac/i })).toHaveAttribute(
      'href',
      MAC_DOWNLOAD_URL,
    );
    expect(screen.getByRole('link', { name: /Get the OET app on Google Play/i })).toHaveAttribute(
      'href',
      ANDROID_INSTALL_URL,
    );

    // iOS is only a link once an Apple-approved channel (App Store or
    // TestFlight) is configured. Otherwise the badge is inert so candidates
    // are never offered a download that cannot install — the old raw
    // /api/download/ios link is deliberately gone.
    if (IOS_DOWNLOAD_URL) {
      expect(
        screen.getByRole('link', { name: /Download the OET app on the App Store/i }),
      ).toHaveAttribute('href', IOS_DOWNLOAD_URL);
      expect(within(strip).getAllByRole('link')).toHaveLength(4);
    } else {
      const iosBadge = screen.getByLabelText(/Download the OET app on the App Store/i);
      expect(iosBadge.tagName).toBe('SPAN');
      expect(iosBadge).toHaveAttribute('aria-disabled', 'true');
      expect(within(strip).getAllByRole('link')).toHaveLength(3);
    }
  });
});
