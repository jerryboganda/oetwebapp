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
});