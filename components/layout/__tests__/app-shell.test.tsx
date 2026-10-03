import { createContext } from 'react';
import { fireEvent, render, screen } from '@testing-library/react';
const authProviderSpy = vi.fn(({ children }: { children: React.ReactNode }) => <div data-testid="auth-provider">{children}</div>);
const authGuardSpy = vi.fn(({ children }: { children: React.ReactNode }) => <div data-testid="auth-guard">{children}</div>);
const topNavSpy = vi.fn(({ children }: { children?: React.ReactNode }) => <div data-testid="top-nav">{children}</div>);
type PaletteProps = { open: boolean; onOpenChange: (open: boolean) => void; workspaceRole: string; sections: unknown };
const globalSearchSpy = vi.fn(({ open, onOpenChange }: PaletteProps) => (
  <button type="button" data-testid="global-search" data-open={String(open)} onClick={() => onOpenChange(true)} />
));
const useAuthSpy = vi.fn(() => ({
  isAuthenticated: false,
  loading: false,
}));
const focusExitProviderSpy = vi.fn(({ homeHref, children }: { homeHref: string; children?: React.ReactNode }) => (
  <div data-testid="focus-exit-provider" data-home={homeHref}>
    {children}
  </div>
));

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
  getWorkspaceHomeHref: (role?: string) => (role === 'expert' ? '/expert' : role === 'admin' ? '/admin' : '/'),
}));

vi.mock('@/components/layout/focus-exit', () => ({
  FocusExitProvider: (props: { homeHref: string; children?: React.ReactNode }) => focusExitProviderSpy(props),
  FocusExitControl: () => <div data-testid="focus-exit-control" />,
}));

vi.mock('@/components/layout/bottom-nav', () => ({
  BottomNav: () => <div data-testid="bottom-nav" />,
}));

vi.mock('@/components/layout/global-search', () => ({
  GlobalSearch: (props: PaletteProps) => globalSearchSpy(props),
}));

import { AuthContext, type AuthContextValue } from '@/contexts/auth-context';
import { AppShell, type AppShellProps } from '../app-shell';
import { NextRouterProvider, renderWithRouter } from '@/tests/test-utils';

const SIGNED_IN = { isAuthenticated: true } as AuthContextValue;

/** Renders one persistent AppShell and returns a client-navigation helper. */
function renderShellAt(firstPath: string, props: Omit<AppShellProps, 'children'>, auth: AuthContextValue | null = null) {
  const ui = (pathname: string) => (
    <AuthContext.Provider value={auth}>
      <NextRouterProvider pathname={pathname}>
        <AppShell {...props}>
          <div>Page</div>
        </AppShell>
      </NextRouterProvider>
    </AuthContext.Provider>
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

  // Launch handoff UI-2: the last content and any scrolled-to control clear the
  // fixed mobile bottom nav (by 1rem) in both the learner and the staff shells.
  it.each(['learner', 'admin'] as const)('pads the %s main content clear of the bottom nav', (role) => {
    renderShellAt(`/${role}`, { requiredRole: role, workspaceRole: role });
    const main = screen.getByRole('main');
    expect(main).toHaveClass(
      'pb-[calc(var(--bottom-nav-height)+var(--safe-area-inset-bottom)+1rem)]',
      'scroll-pb-[calc(var(--bottom-nav-height)+var(--safe-area-inset-bottom)+1rem)]',
      'lg:pb-6',
      'lg:scroll-pb-6',
    );
  });

  describe('route entrance', () => {
    it.each([
      ['admin', '/admin/users'],
      ['expert', '/expert/queue'],
      ['learner', '/listening'],
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

    it('never animates a learner exam or live route', () => {
      const navigate = renderShellAt('/reading', { requiredRole: 'learner', workspaceRole: 'learner' });
      navigate('/listening/paper/p1');
      expect(screen.getByRole('main')).not.toHaveClass('page-enter');

      navigate('/listening');
      expect(screen.getByRole('main')).toHaveClass('page-enter');
    });
  });

  describe('command palette', () => {
    const learner = { requiredRole: 'learner', workspaceRole: 'learner' } as const;
    /** The onOpenSearch every TopNav got on the latest render. */
    const lastTopNavSearch = (topNavCount: number) =>
      topNavSpy.mock.calls.slice(-topNavCount).map(([props]: [Record<string, unknown>]) => props.onOpenSearch);

    beforeEach(() => {
      topNavSpy.mockClear();
      globalSearchSpy.mockClear();
    });

    it('mounts once for a signed-in learner and hands the header its opener', () => {
      renderShellAt('/reading', learner, SIGNED_IN);

      expect(screen.getAllByTestId('global-search')).toHaveLength(1);
      expect(globalSearchSpy.mock.lastCall?.[0]).toEqual(expect.objectContaining({ workspaceRole: 'learner' }));
      expect(lastTopNavSearch(1)).toEqual([expect.any(Function)]);
    });

    it.each([
      ['admin', '/admin'],
      ['expert', '/expert'],
    ] as const)('mounts once for a signed-in %s and hands both headers its opener', (role, path) => {
      renderShellAt(path, { requiredRole: role, workspaceRole: role }, SIGNED_IN);

      expect(screen.getAllByTestId('global-search')).toHaveLength(1);
      expect(lastTopNavSearch(2)).toEqual([expect.any(Function), expect.any(Function)]);
    });

    const queue = { href: '/expert/queue', label: 'Queue', icon: <span /> };
    it.each<[string, Omit<AppShellProps, 'children'>, string]>([
      ['navGroups', { navGroups: [{ label: 'Workspace', items: [queue] }], mobileMenuSections: [{ label: 'Review', items: [queue] }], navItems: [queue] }, 'Workspace'],
      ['mobileMenuSections', { mobileMenuSections: [{ label: 'Review', items: [queue] }], navItems: [queue] }, 'Review'],
      ['navItems', { navItems: [queue] }, 'Go to'],
    ])('hands the palette the shell nav from %s', (_source, nav, label) => {
      renderShellAt('/expert', { requiredRole: 'expert', workspaceRole: 'expert', ...nav }, SIGNED_IN);

      expect(globalSearchSpy.mock.lastCall?.[0].sections).toEqual([{ label, items: [queue] }]);
    });

    it.each([
      ['signed out', '/reading', learner, null],
      ['distraction-free', '/reading', { ...learner, distractionFree: true }, SIGNED_IN],
      ['on an exam paper', '/reading/paper/p1', learner, SIGNED_IN],
    ] as const)('is not mounted %s, and the header gets no trigger', (_case, path, props, auth) => {
      renderShellAt(path, props, auth);

      expect(screen.queryByTestId('global-search')).not.toBeInTheDocument();
      expect(lastTopNavSearch(1)).toEqual([undefined]);
    });

    it('is not mounted in a staff live room', () => {
      renderShellAt('/expert/speaking/live-room/s1', { requiredRole: 'expert', workspaceRole: 'expert' }, SIGNED_IN);

      expect(screen.queryByTestId('global-search')).not.toBeInTheDocument();
      expect(lastTopNavSearch(2)).toEqual([undefined, undefined]);
    });

    it('closes when the route changes under the mounted shell', () => {
      const navigate = renderShellAt('/reading', learner, SIGNED_IN);

      fireEvent.click(screen.getByTestId('global-search'));
      expect(screen.getByTestId('global-search')).toHaveAttribute('data-open', 'true');

      navigate('/listening');
      expect(screen.getByTestId('global-search')).toHaveAttribute('data-open', 'false');
    });
  });

  // Focus chrome has no brand, sidebar or bottom nav, and its only other menu
  // trigger is the hamburger, which is `lg:hidden`. Without an exit the header
  // was a dead end on a desktop-width exam.
  describe('focus chrome exit', () => {
    /** The exitControl AppShell handed the header on the latest render. */
    const lastTopNavExit = () =>
      (topNavSpy.mock.lastCall?.[0] as Record<string, unknown> | undefined)?.exitControl;

    beforeEach(() => {
      topNavSpy.mockClear();
      focusExitProviderSpy.mockClear();
    });

    it.each([
      ['learner', { requiredRole: 'learner', workspaceRole: 'learner' }, '/'],
      ['expert', { requiredRole: 'expert', workspaceRole: 'expert' }, '/expert'],
      ['admin', { requiredRole: 'admin', workspaceRole: 'admin' }, '/admin'],
    ] as const)('gives the %s distraction-free header a way out, scoped to its own workspace', (role, roleProps, home) => {
      renderShellAt(`/${role}`, { ...roleProps, distractionFree: true }, SIGNED_IN);

      expect(lastTopNavExit()).toBeTruthy();
      expect(focusExitProviderSpy.mock.lastCall?.[0]).toEqual(expect.objectContaining({ homeHref: home }));
    });

    it('keeps the workspace shell as it was', () => {
      renderShellAt('/reading', { requiredRole: 'learner', workspaceRole: 'learner' }, SIGNED_IN);

      expect(screen.queryByTestId('focus-exit-provider')).not.toBeInTheDocument();
      expect(lastTopNavExit()).toBeUndefined();
      expect(screen.getByTestId('sidebar')).toBeInTheDocument();
    });
  });
});
