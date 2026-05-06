import { screen } from '@testing-library/react';
import { renderWithRouter } from '@/tests/test-utils';
const appShellSpy = vi.fn(({ children }: Record<string, unknown> & { children: React.ReactNode }) => <div data-testid="app-shell">{children}</div>);

vi.mock('@/components/layout/app-shell', () => ({
  AppShell: (props: Record<string, unknown> & { children: React.ReactNode }) => appShellSpy(props),
}));

import { LearnerDashboardShell } from '../learner-dashboard-shell';

describe('LearnerDashboardShell', () => {
  it('renders the dashboard gutter container inside the learner shell', () => {
    renderWithRouter(
      <LearnerDashboardShell pageTitle="Billing" workspaceClassName="space-y-8">
        <div>Billing Workspace</div>
      </LearnerDashboardShell>,
    );

    expect(screen.getByTestId('app-shell')).toBeInTheDocument();

    const container = screen.getByTestId('learner-workspace-container');
    expect(container).toHaveClass('w-full');
    expect(container).toHaveClass('max-w-[1200px]');
    expect(container).toHaveClass('mx-auto');
    expect(container).toHaveClass('px-4');
    expect(container).toHaveClass('sm:px-6');
    expect(container).toHaveClass('lg:px-8');
    expect(container).toHaveClass('py-2');
    expect(container).toHaveClass('sm:py-4');
    expect(container).toHaveClass('lg:py-6');
    expect(container).toHaveClass('space-y-8');
    expect((appShellSpy.mock.calls[0]?.[0].mobileMenuSections as Array<{ label: string }> | undefined)?.map((section) => section.label)).toEqual([
      'Practice',
      'Learn',
    ]);
  });

  it('adds learner breadcrumbs on non-immersive workspace routes', () => {
    renderWithRouter(
      <LearnerDashboardShell pageTitle="Writing result">
        <div>Writing Result</div>
      </LearnerDashboardShell>,
      { pathname: '/writing/result' },
    );

    expect(screen.getByRole('navigation', { name: /breadcrumb/i })).toBeInTheDocument();
    expect(screen.getByText('Result')).toHaveAttribute('aria-current', 'page');
  });

  it('does not render breadcrumbs in distraction-free mode', () => {
    renderWithRouter(
      <LearnerDashboardShell pageTitle="Writing player" distractionFree>
        <div>Writing Player</div>
      </LearnerDashboardShell>,
      { pathname: '/writing/player' },
    );

    expect(screen.queryByRole('navigation', { name: /breadcrumb/i })).not.toBeInTheDocument();
  });
});
