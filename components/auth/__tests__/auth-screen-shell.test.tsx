import { render, screen } from '@testing-library/react';
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

    const strip = screen.getByRole('region', { name: /official OET apps/i });
    expect(strip).toBeInTheDocument();
    expect(strip).not.toBe(screen.getByRole('main').querySelector('[class*="card"]'));
    expect(screen.getByRole('link', { name: /download the OET app for Windows & Mac/i })).toHaveAttribute('href', '/get-app');
    expect(screen.getByRole('link', { name: /Google Play/i })).toHaveAttribute('href', '/get-app/android-install');
    expect(screen.getByRole('link', { name: /iPhone and iPad/i })).toHaveAttribute('href', '/get-app');
  });
});
