import { render, screen, within } from '@testing-library/react';
import { AuthScreenShell } from '../auth-screen-shell';

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
    expect(screen.getByRole('link', { name: /download the OET app for Windows/i })).toHaveAttribute('href', '/get-app');
    expect(screen.getByRole('link', { name: /download the OET app for Mac/i })).toHaveAttribute('href', '/get-app');
    expect(screen.getByRole('link', { name: /Get the OET app on Google Play/i })).toHaveAttribute(
      'href',
      '/get-app/android-install',
    );
    expect(screen.getByRole('link', { name: /Download the OET app on the App Store/i })).toHaveAttribute(
      'href',
      '/api/download/ios',
    );
    expect(within(strip).getAllByRole('link')).toHaveLength(4);
  });
});
