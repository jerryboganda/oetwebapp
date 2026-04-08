import { render, screen } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';

const appShellSpy = vi.fn(({ children }: { children: React.ReactNode }) => <div data-testid="app-shell">{children}</div>);

vi.mock('../app-shell', () => ({
  AppShell: (props: { children: React.ReactNode }) => appShellSpy(props),
}));

import { LearnerDashboardShell } from '../learner-dashboard-shell';

describe('LearnerDashboardShell', () => {
  it('renders the dashboard gutter container inside the learner shell', () => {
    render(
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
    expect(container).toHaveClass('py-6');
    expect(container).toHaveClass('space-y-8');
    expect(appShellSpy.mock.calls[0]?.[0].mobileMenuSections?.map((section: { label: string }) => section.label)).toEqual([
      'Practice',
      'Learn',
      'Community',
    ]);
  });
});
