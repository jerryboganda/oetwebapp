import { screen } from '@testing-library/react';
import { renderWithRouter } from '@/tests/test-utils';

vi.mock('@/components/layout/app-shell', () => ({
  AppShell: ({ children, pageTitle }: { children: React.ReactNode; pageTitle?: string }) => (
    <div data-testid="app-shell" data-page-title={pageTitle ?? ''}>
      {children}
    </div>
  ),
}));

vi.mock('@/components/layout/learner-workspace-container', () => ({
  LearnerWorkspaceContainer: ({ children, className }: { children: React.ReactNode; className?: string }) => (
    <div data-testid="learner-workspace-container" className={className}>
      {children}
    </div>
  ),
}));

import { AdminDashboardShell } from '../admin-dashboard-shell';

describe('AdminDashboardShell', () => {
  it('renders the learner-style workspace container inside AppShell and forwards the page title', () => {
    renderWithRouter(
      <AdminDashboardShell pageTitle="Dashboard" workspaceClassName="space-y-8">
        <div>Admin Workspace</div>
      </AdminDashboardShell>,
    );

    expect(screen.getByTestId('app-shell')).toHaveAttribute('data-page-title', 'Dashboard');
    expect(screen.getByTestId('learner-workspace-container')).toHaveClass('space-y-8');
    expect(screen.getByText('Admin Workspace')).toBeInTheDocument();
  });

  it('collapses nested admin dashboard shells to prevent duplicate app chrome', () => {
    renderWithRouter(
      <AdminDashboardShell pageTitle="Outer">
        <div>Before nested shell</div>
        <AdminDashboardShell pageTitle="Inner" workspaceClassName="inner-shell">
          <div>Nested CMS page</div>
        </AdminDashboardShell>
      </AdminDashboardShell>,
    );

    expect(screen.getAllByTestId('app-shell')).toHaveLength(1);
    expect(screen.getAllByTestId('learner-workspace-container')).toHaveLength(1);
    expect(screen.getByTestId('app-shell')).toHaveAttribute('data-page-title', 'Outer');
    expect(screen.getByText('Before nested shell')).toBeInTheDocument();
    expect(screen.getByText('Nested CMS page')).toBeInTheDocument();
  });
});
