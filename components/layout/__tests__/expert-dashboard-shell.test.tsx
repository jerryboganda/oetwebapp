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
    <div
      data-testid="learner-workspace-container"
      className={`w-full max-w-[1200px] mx-auto px-4 sm:px-6 lg:px-8 py-6 ${className ?? ''}`.trim()}
    >
      {children}
    </div>
  ),
}));

import { ExpertDashboardShell } from '../expert-dashboard-shell';

describe('ExpertDashboardShell', () => {
  it('renders the learner-style workspace container inside AppShell and forwards the page title', () => {
    renderWithRouter(
      <ExpertDashboardShell pageTitle="Dashboard" workspaceClassName="space-y-8">
        <div>Expert Workspace</div>
      </ExpertDashboardShell>,
    );

    expect(screen.getByTestId('app-shell')).toBeInTheDocument();
    expect(screen.getByTestId('app-shell')).toHaveAttribute('data-page-title', 'Dashboard');

    const container = screen.getByTestId('learner-workspace-container');
    expect(container).toHaveClass('w-full');
    expect(container).toHaveClass('max-w-[1200px]');
    expect(container).toHaveClass('mx-auto');
    expect(container).toHaveClass('px-4');
    expect(container).toHaveClass('sm:px-6');
    expect(container).toHaveClass('lg:px-8');
    expect(container).toHaveClass('py-6');
    expect(container).toHaveClass('space-y-8');
  });
});
