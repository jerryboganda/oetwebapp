import type { ReactNode } from 'react';
import { screen } from '@testing-library/react';
import { AdminPermission } from '@/lib/admin-permissions';
const state = vi.hoisted(() => ({
  pathname: '/admin',
}));

vi.mock('@/contexts/auth-context', () => ({
  useAuth: () => ({
    user: {
      userId: 'admin-1',
      email: 'admin@test.com',
      role: 'admin' as const,
      displayName: 'Admin',
      isEmailVerified: true,
      isAuthenticatorEnabled: false,
      requiresEmailVerification: false,
      requiresMfa: false,
      emailVerifiedAt: null,
      authenticatorEnabledAt: null,
      adminPermissions: Object.values(AdminPermission),
    },
  }),
}));

vi.mock('@/components/auth/privileged-mfa-banner', () => ({
  PrivilegedMfaBanner: () => <div data-testid="mfa-banner">MFA banner</div>,
}));

vi.mock('@/components/layout', () => ({
  AppShell: ({
    children,
    pageTitle,
    distractionFree,
  }: {
    children: ReactNode;
    pageTitle?: string;
    distractionFree?: boolean;
  }) => (
    <div
      data-testid="app-shell"
      data-page-title={pageTitle ?? ''}
      data-distraction-free={String(Boolean(distractionFree))}
    >
      {children}
    </div>
  ),
  AdminDashboardShell: ({
    children,
    pageTitle,
  }: {
    children: ReactNode;
    pageTitle?: string;
  }) => (
    <div data-testid="admin-dashboard-shell" data-page-title={pageTitle ?? ''}>
      {children}
    </div>
  ),
  LearnerWorkspaceContainer: ({ children }: { children: ReactNode }) => (
    <div data-testid="learner-workspace">{children}</div>
  ),
}));

import AdminLayout from './layout';
import { renderWithRouter } from '@/tests/test-utils';

describe('AdminLayout', () => {
  beforeEach(() => {
    state.pathname = '/admin';
  });

  it('routes standard admin pages through the learner-style admin shell', () => {
    renderWithRouter(
      <AdminLayout>
        <div>User detail workspace</div>
      </AdminLayout>,
      { pathname: '/admin/users/user-1' },
    );

    expect(screen.getByTestId('admin-dashboard-shell')).toHaveAttribute('data-page-title', 'User Ops');
    expect(screen.queryByTestId('app-shell')).not.toBeInTheDocument();
    expect(screen.getByTestId('mfa-banner')).toBeInTheDocument();
  });

  it('keeps content editor routes on the distraction-free app shell', () => {
    renderWithRouter(
      <AdminLayout>
        <div>Content editor workspace</div>
      </AdminLayout>,
      { pathname: '/admin/content/content-1' },
    );

    expect(screen.getByTestId('app-shell')).toHaveAttribute('data-page-title', 'Content Workspace');
    expect(screen.getByTestId('app-shell')).toHaveAttribute('data-distraction-free', 'true');
    expect(screen.queryByTestId('admin-dashboard-shell')).not.toBeInTheDocument();
    expect(screen.getByTestId('learner-workspace')).toBeInTheDocument();
  });

  it('keeps the new content workspace route on the distraction-free app shell', () => {
    renderWithRouter(
      <AdminLayout>
        <div>New content workspace</div>
      </AdminLayout>,
      { pathname: '/admin/content/new' },
    );

    expect(screen.getByTestId('app-shell')).toHaveAttribute('data-page-title', 'Content Workspace');
    expect(screen.getByTestId('app-shell')).toHaveAttribute('data-distraction-free', 'true');
    expect(screen.queryByTestId('admin-dashboard-shell')).not.toBeInTheDocument();
    expect(screen.getByTestId('learner-workspace')).toBeInTheDocument();
  });

  it('maps revision history routes back to the learner-style admin shell title', () => {
    renderWithRouter(
      <AdminLayout>
        <div>Revision history workspace</div>
      </AdminLayout>,
      { pathname: '/admin/content/content-1/revisions' },
    );

    expect(screen.getByTestId('admin-dashboard-shell')).toHaveAttribute('data-page-title', 'Revision History');
    expect(screen.queryByTestId('app-shell')).not.toBeInTheDocument();
  });
});
