import type { ReactNode } from 'react';
import { render, screen } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';

const state = vi.hoisted(() => ({
  pathname: '/expert',
}));

vi.mock('next/navigation', () => ({
  usePathname: () => state.pathname,
}));

vi.mock('@/contexts/auth-context', () => ({
  AuthProvider: ({ children }: { children: ReactNode }) => <>{children}</>,
}));

vi.mock('@/lib/hooks/use-expert-auth', () => ({
  useExpertAuth: () => ({
    expert: {
      displayName: 'Expert Console',
      email: 'expert@example.com',
    },
  }),
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
  ExpertDashboardShell: ({
    children,
    pageTitle,
  }: {
    children: ReactNode;
    pageTitle?: string;
  }) => (
    <div data-testid="expert-dashboard-shell" data-page-title={pageTitle ?? ''}>
      {children}
    </div>
  ),
}));

import ExpertLayout from './layout';

describe('ExpertLayout', () => {
  beforeEach(() => {
    state.pathname = '/expert';
  });

  it('routes non-review expert pages through the learner-style expert shell', () => {
    state.pathname = '/expert/queue';

    render(
      <ExpertLayout>
        <div>Queue workspace</div>
      </ExpertLayout>,
    );

    expect(screen.getByTestId('expert-dashboard-shell')).toHaveAttribute('data-page-title', 'Review Queue');
    expect(screen.queryByTestId('app-shell')).not.toBeInTheDocument();
  });

  it('keeps expert review workspaces on the distraction-free app shell', () => {
    state.pathname = '/expert/review/writing/rev-1';

    render(
      <ExpertLayout>
        <div>Review workspace</div>
      </ExpertLayout>,
    );

    expect(screen.getByTestId('app-shell')).toHaveAttribute('data-page-title', 'Review Workspace');
    expect(screen.getByTestId('app-shell')).toHaveAttribute('data-distraction-free', 'true');
    expect(screen.queryByTestId('expert-dashboard-shell')).not.toBeInTheDocument();
  });
});
