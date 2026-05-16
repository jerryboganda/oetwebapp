import type { ReactNode } from 'react';
import { screen } from '@testing-library/react';
import { AdminPermission } from '@/lib/admin-permissions';
const state = vi.hoisted(() => ({
  pathname: '/admin',
}));
const mockUseAuth = vi.hoisted(() => vi.fn());

vi.mock('@/contexts/auth-context', () => ({
  useAuth: mockUseAuth,
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
    mockUseAuth.mockReturnValue({
      loading: false,
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
    });
  });

  it('routes standard admin pages through the learner-style admin shell', () => {
    renderWithRouter(
      <AdminLayout>
        <div>User detail workspace</div>
      </AdminLayout>,
      { pathname: '/admin/users/user-1' },
    );

    expect(screen.getByTestId('admin-dashboard-shell')).toHaveAttribute('data-page-title', 'User Operations');
    expect(screen.queryByTestId('app-shell')).not.toBeInTheDocument();
    expect(screen.getByTestId('mfa-banner')).toBeInTheDocument();
  });

  it('keeps content editor routes inside the admin dashboard shell so the sidebar stays visible', () => {
    renderWithRouter(
      <AdminLayout>
        <div>Content editor workspace</div>
      </AdminLayout>,
      { pathname: '/admin/content/content-1' },
    );

    expect(screen.getByTestId('admin-dashboard-shell')).toHaveAttribute('data-page-title', 'Content Workspace');
    expect(screen.queryByTestId('app-shell')).not.toBeInTheDocument();
    expect(screen.queryByTestId('learner-workspace')).not.toBeInTheDocument();
  });

  it('keeps the new content workspace route inside the admin dashboard shell', () => {
    renderWithRouter(
      <AdminLayout>
        <div>New content workspace</div>
      </AdminLayout>,
      { pathname: '/admin/content/new' },
    );

    expect(screen.getByTestId('admin-dashboard-shell')).toHaveAttribute('data-page-title', 'Content Workspace');
    expect(screen.queryByTestId('app-shell')).not.toBeInTheDocument();
    expect(screen.queryByTestId('learner-workspace')).not.toBeInTheDocument();
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

  it('blocks deep-linked pages when the admin lacks the page permission', async () => {
    mockUseAuth.mockReturnValue({
      loading: false,
      user: {
        userId: 'admin-2',
        email: 'limited@test.com',
        role: 'admin',
        displayName: 'Limited Admin',
        isEmailVerified: true,
        isAuthenticatorEnabled: false,
        requiresEmailVerification: false,
        requiresMfa: false,
        emailVerifiedAt: null,
        authenticatorEnabledAt: null,
        adminPermissions: [AdminPermission.UsersRead],
      },
    });

    renderWithRouter(
      <AdminLayout>
        <div>Billing workspace</div>
      </AdminLayout>,
      { pathname: '/admin/billing' },
    );

    expect(screen.getByText('Admin permission required')).toBeInTheDocument();
    expect(screen.queryByText('Billing workspace')).not.toBeInTheDocument();
  });
});
