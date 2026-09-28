import { createContext } from 'react';
import { render, screen } from '@testing-library/react';
const authProviderSpy = vi.fn(({ children }: { children: React.ReactNode }) => <div data-testid="auth-provider">{children}</div>);
const authGuardSpy = vi.fn(({ children }: { children: React.ReactNode }) => <div data-testid="auth-guard">{children}</div>);
const topNavSpy = vi.fn(({ children }: { children?: React.ReactNode }) => <div data-testid="top-nav">{children}</div>);
const useAuthSpy = vi.fn(() => ({
  isAuthenticated: false,
  loading: false,
}));

vi.mock('@/contexts/auth-context', () => ({
  AuthContext: createContext(null),
  AuthProvider: (props: { children: React.ReactNode }) => authProviderSpy(props),
  useAuth: () => useAuthSpy(),
}));

vi.mock('@/components/auth/auth-guard', () => ({
  AuthGuard: (props: { children: React.ReactNode; requiredRole?: 'learner' | 'expert' | 'admin' }) => authGuardSpy(props),
}));

vi.mock('@/components/layout/top-nav', () => ({
  TopNav: (props: { children?: React.ReactNode }) => topNavSpy(props),
}));

vi.mock('@/components/layout/sidebar', () => ({
  Sidebar: () => <div data-testid="sidebar" />,
}));

vi.mock('@/components/layout/bottom-nav', () => ({
  BottomNav: () => <div data-testid="bottom-nav" />,
}));

import { AppShell, type AppShellProps } from '../app-shell';
import { NextRouterProvider, renderWithRouter } from '@/tests/test-utils';

/** Renders one persistent AppShell and returns a client-navigation helper. */
function renderShellAt(firstPath: string, props: Omit<AppShellProps, 'children'>) {
  const ui = (pathname: string) => (
    <NextRouterProvider pathname={pathname}>
      <AppShell {...props}>
        <div>Page</div>
      </AppShell>
    </NextRouterProvider>
  );
  const view = render(ui(firstPath));
  return (pathname: string) => view.rerender(ui(pathname));
}

describe('AppShell', () => {
  it('wraps protected shells in AuthProvider and forwards requiredRole to AuthGuard', () => {
    renderWithRouter(
      <AppShell
        requiredRole="admin"
        mobileMenuSections={[
          {
            label: 'Practice',
            items: [{ href: '/', label: 'Dashboard', icon: <span aria-hidden="true" /> }],
          },
        ]}
      >
        <div>Admin console</div>
      </AppShell>,
    );

    expect(screen.getByTestId('auth-provider')).toBeInTheDocument();
    expect(screen.getByTestId('auth-guard')).toBeInTheDocument();
    expect(authProviderSpy).toHaveBeenCalled();
    expect(authGuardSpy).toHaveBeenCalled();
    expect(authGuardSpy.mock.calls[0]?.[0]).toEqual(expect.objectContaining({ requiredRole: 'admin' }));
    expect(topNavSpy.mock.calls.some(([props]: [Record<string, unknown>]) => Array.isArray(props.sectionedItems) && (props.sectionedItems as unknown[]).length === 1)).toBe(true);
  });

  describe('route entrance', () => {
    it.each([
      ['admin', '/admin/users'],
      ['expert', '/expert/queue'],
    ] as const)('skips first paint, then adds page-enter after a %s client navigation', (role, nextPath) => {
      const navigate = renderShellAt(`/${role}`, { requiredRole: role, workspaceRole: role });
      expect(screen.getByRole('main')).not.toHaveClass('page-enter');

      navigate(nextPath);
      expect(screen.getByRole('main')).toHaveClass('page-enter');
    });

    it('never animates a distraction-free shell', () => {
      const navigate = renderShellAt('/expert/review/r1', { distractionFree: true, requiredRole: 'expert', workspaceRole: 'expert' });
      navigate('/expert/review/r2');
      expect(screen.getByRole('main')).not.toHaveClass('page-enter');
    });

    it('never animates an exam or live route', () => {
      const navigate = renderShellAt('/expert/speaking', { requiredRole: 'expert', workspaceRole: 'expert' });
      navigate('/expert/speaking/live-room/s1');
      expect(screen.getByRole('main')).not.toHaveClass('page-enter');

      navigate('/expert/queue');
      expect(screen.getByRole('main')).toHaveClass('page-enter');
    });

    it('never animates the learner shell', () => {
      const navigate = renderShellAt('/reading', { requiredRole: 'learner', workspaceRole: 'learner' });
      navigate('/listening');
      expect(screen.getByRole('main')).not.toHaveClass('page-enter');
    });
  });
});
