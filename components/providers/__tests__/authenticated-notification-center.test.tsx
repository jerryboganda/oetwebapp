import React, { type ReactNode } from 'react';
import { render, screen } from '@testing-library/react';

const lifecycle = vi.hoisted(() => ({
  auth: { isAuthenticated: false },
  pathname: '/sign-in',
  chunkLoad: vi.fn(),
  providerStart: vi.fn(),
  providerStop: vi.fn(),
}));

vi.mock('@/contexts/auth-context', () => ({
  useAuth: () => lifecycle.auth,
}));

vi.mock('next/navigation', () => ({
  usePathname: () => lifecycle.pathname,
}));

vi.mock('next/dynamic', () => ({
  default: () => {
    function MockDynamicNotificationProvider({ children }: { children: ReactNode }) {
      React.useEffect(() => {
        lifecycle.chunkLoad();
        lifecycle.providerStart();
        return () => lifecycle.providerStop();
      }, []);

      return <div data-testid="notification-provider">{children}</div>;
    }

    return MockDynamicNotificationProvider;
  },
}));

import { AuthenticatedNotificationCenter } from '../authenticated-notification-center';

function Subject({ page }: { page: string }) {
  return (
    <AuthenticatedNotificationCenter>
      <div>{page}</div>
    </AuthenticatedNotificationCenter>
  );
}

describe('AuthenticatedNotificationCenter', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    lifecycle.auth.isAuthenticated = false;
    lifecycle.pathname = '/sign-in';
  });

  it('does not request or start notification code for unauthenticated or auth-route renders', () => {
    const { rerender } = render(<Subject page="Sign in" />);

    expect(screen.queryByTestId('notification-provider')).not.toBeInTheDocument();
    expect(lifecycle.chunkLoad).not.toHaveBeenCalled();
    expect(lifecycle.providerStart).not.toHaveBeenCalled();

    lifecycle.auth.isAuthenticated = true;
    rerender(<Subject page="Authenticated sign in" />);

    expect(screen.queryByTestId('notification-provider')).not.toBeInTheDocument();
    expect(lifecycle.chunkLoad).not.toHaveBeenCalled();
    expect(lifecycle.providerStart).not.toHaveBeenCalled();
  });

  it('keeps one provider bootstrap across workspace navigation and tears it down on logout', () => {
    lifecycle.auth.isAuthenticated = true;
    lifecycle.pathname = '/dashboard';
    const { rerender } = render(<Subject page="Dashboard" />);

    expect(screen.getByTestId('notification-provider')).toBeInTheDocument();
    expect(lifecycle.chunkLoad).toHaveBeenCalledTimes(1);
    expect(lifecycle.providerStart).toHaveBeenCalledTimes(1);
    expect(lifecycle.providerStop).not.toHaveBeenCalled();

    lifecycle.pathname = '/reading';
    rerender(<Subject page="Reading" />);

    expect(screen.getByText('Reading')).toBeInTheDocument();
    expect(lifecycle.chunkLoad).toHaveBeenCalledTimes(1);
    expect(lifecycle.providerStart).toHaveBeenCalledTimes(1);
    expect(lifecycle.providerStop).not.toHaveBeenCalled();

    lifecycle.auth.isAuthenticated = false;
    rerender(<Subject page="Signed out" />);

    expect(screen.queryByTestId('notification-provider')).not.toBeInTheDocument();
    expect(lifecycle.providerStop).toHaveBeenCalledTimes(1);
  });
});
