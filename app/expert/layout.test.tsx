import type { ReactNode } from 'react';
import { screen } from '@testing-library/react';
const state = vi.hoisted(() => ({
  pathname: '/expert',
}));

vi.mock('@/contexts/auth-context', () => ({
  AuthProvider: ({ children }: { children: ReactNode }) => <>{children}</>,
}));

vi.mock('@/lib/hooks/use-expert-auth', () => ({
  useExpertAuth: () => ({
    expert: {
      displayName: 'Tutor Console',
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
import { renderWithRouter } from '@/tests/test-utils';

describe('ExpertLayout', () => {
  beforeEach(() => {
    state.pathname = '/expert';
  });

  it('routes non-review expert pages through the learner-style expert shell', () => {
    renderWithRouter(
      <ExpertLayout>
        <div>Queue workspace</div>
      </ExpertLayout>,
      { pathname: '/expert/queue' },
    );

    expect(screen.getByTestId('expert-dashboard-shell')).toHaveAttribute('data-page-title', 'Review Queue');
    expect(screen.queryByTestId('app-shell')).not.toBeInTheDocument();
  });

  it('keeps tutor review workspaces on the distraction-free app shell', () => {
    renderWithRouter(
      <ExpertLayout>
        <div>Review workspace</div>
      </ExpertLayout>,
      { pathname: '/expert/review/writing/rev-1' },
    );

    expect(screen.getByTestId('app-shell')).toHaveAttribute('data-page-title', 'Review Workspace');
    expect(screen.getByTestId('app-shell')).toHaveAttribute('data-distraction-free', 'true');
    expect(screen.queryByTestId('expert-dashboard-shell')).not.toBeInTheDocument();
  });
});
