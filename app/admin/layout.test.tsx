import type { ReactNode } from 'react';
import { render, screen } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';

const state = vi.hoisted(() => ({
  pathname: '/admin',
}));

vi.mock('next/navigation', () => ({
  usePathname: () => state.pathname,
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

describe('AdminLayout', () => {
  beforeEach(() => {
    state.pathname = '/admin';
  });

  it('routes standard admin pages through the learner-style admin shell', () => {
    state.pathname = '/admin/users/user-1';

    render(
      <AdminLayout>
        <div>User detail workspace</div>
      </AdminLayout>,
    );

    expect(screen.getByTestId('admin-dashboard-shell')).toHaveAttribute('data-page-title', 'User Ops');
    expect(screen.queryByTestId('app-shell')).not.toBeInTheDocument();
    expect(screen.getByTestId('mfa-banner')).toBeInTheDocument();
  });

  it('keeps content editor routes on the distraction-free app shell', () => {
    state.pathname = '/admin/content/content-1';

    render(
      <AdminLayout>
        <div>Content editor workspace</div>
      </AdminLayout>,
    );

    expect(screen.getByTestId('app-shell')).toHaveAttribute('data-page-title', 'Content Workspace');
    expect(screen.getByTestId('app-shell')).toHaveAttribute('data-distraction-free', 'true');
    expect(screen.queryByTestId('admin-dashboard-shell')).not.toBeInTheDocument();
    expect(screen.getByTestId('learner-workspace')).toBeInTheDocument();
  });

  it('maps revision history routes back to the learner-style admin shell title', () => {
    state.pathname = '/admin/content/content-1/revisions';

    render(
      <AdminLayout>
        <div>Revision history workspace</div>
      </AdminLayout>,
    );

    expect(screen.getByTestId('admin-dashboard-shell')).toHaveAttribute('data-page-title', 'Revision History');
    expect(screen.queryByTestId('app-shell')).not.toBeInTheDocument();
  });
});
