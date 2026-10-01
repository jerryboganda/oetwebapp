import { screen, within } from '@testing-library/react';
import { renderWithRouter } from '@/tests/test-utils';

type ShellProps = Record<string, unknown> & { children: React.ReactNode; navActions?: React.ReactNode };

// Stands in for TopNav's actions row and #main-content so the slot and the
// shell count are observable without the real chrome's data hooks.
const appShellSpy = vi.fn(({ children, navActions }: ShellProps) => (
  <div data-testid="app-shell">
    <header data-testid="top-nav-actions">{navActions}</header>
    <main id="main-content">{children}</main>
  </div>
));

vi.mock('@/components/layout/app-shell', () => ({
  AppShell: (props: ShellProps) => appShellSpy(props),
}));

import { LearnerNavActions, LearnerShellLayout } from '../learner-dashboard-shell';

function lastShellProps() {
  return appShellSpy.mock.calls.at(-1)?.[0] as ShellProps;
}

describe('LearnerShellLayout', () => {
  beforeEach(() => {
    appShellSpy.mockClear();
  });

  it('renders one learner shell around a workspace page', () => {
    renderWithRouter(
      <LearnerShellLayout>
        <div>Progress page</div>
      </LearnerShellLayout>,
      { pathname: '/progress' },
    );

    expect(screen.getByText('Progress page')).toBeInTheDocument();
    expect(screen.getAllByTestId('app-shell')).toHaveLength(1);
    expect(screen.getAllByTestId('learner-workspace-container')).toHaveLength(1);
    expect(screen.getAllByRole('navigation', { name: /breadcrumb/i })).toHaveLength(1);
    expect(lastShellProps()).toMatchObject({ requiredRole: 'learner', workspaceRole: 'learner', requireAuth: true });
    expect(lastShellProps().distractionFree).toBe(false);
  });

  it('renders focus routes distraction-free, titled from the page copy or i18n key', () => {
    const { unmount } = renderWithRouter(
      <LearnerShellLayout>
        <div>Onboarding</div>
      </LearnerShellLayout>,
      { pathname: '/onboarding' },
    );
    expect(lastShellProps()).toMatchObject({ distractionFree: true, requireAuth: true, pageTitle: 'Getting Started' });
    expect(screen.queryByRole('navigation', { name: /breadcrumb/i })).not.toBeInTheDocument();
    unmount();

    // The global next-intl mock returns the key, as the production fallback does.
    renderWithRouter(
      <LearnerShellLayout>
        <div>Paper</div>
      </LearnerShellLayout>,
      { pathname: '/writing/paper/session/s1' },
    );
    expect(lastShellProps()).toMatchObject({ distractionFree: true, pageTitle: 'writing.paper.pageTitle' });
  });

  it('keeps public learner routes outside the auth gate', () => {
    renderWithRouter(
      <LearnerShellLayout>
        <div>Criteria</div>
      </LearnerShellLayout>,
      { pathname: '/speaking/assessment-criteria' },
    );

    expect(lastShellProps()).toMatchObject({ distractionFree: false, requireAuth: false });
  });

  it('renders self-chromed routes bare, leaving their own shell in charge', () => {
    renderWithRouter(
      <LearnerShellLayout>
        <div>Player</div>
      </LearnerShellLayout>,
      { pathname: '/listening/player/a1' },
    );
    expect(screen.getByText('Player')).toBeInTheDocument();
    expect(appShellSpy).not.toHaveBeenCalled();
  });

  it.each([
    ['an in-shell exam route', '/listening/paper/p1'],
    ['a self-chromed player', '/listening/player/a1'],
  ])('holds %s still (DESIGN.md §5) and leaves workspace pages animated', (_label, examPath) => {
    const { unmount } = renderWithRouter(
      <LearnerShellLayout>
        <div>Exam page</div>
      </LearnerShellLayout>,
      { pathname: examPath },
    );
    expect(screen.getByText('Exam page').closest('[data-motion="still"]')).not.toBeNull();
    unmount();

    renderWithRouter(
      <LearnerShellLayout>
        <div>Workspace page</div>
      </LearnerShellLayout>,
      { pathname: '/progress' },
    );
    expect(screen.getByText('Workspace page').closest('[data-motion="still"]')).toBeNull();
  });

  it('portals page nav actions into the layout TopNav actions slot', async () => {
    renderWithRouter(
      <LearnerShellLayout>
        <LearnerNavActions>
          <button type="button">Cart</button>
        </LearnerNavActions>
        <div>Plans</div>
        <LearnerNavActions>
          <span>Segment 1 of 3</span>
        </LearnerNavActions>
      </LearnerShellLayout>,
      { pathname: '/subscriptions' },
    );

    const actions = screen.getByTestId('top-nav-actions');
    expect(await within(actions).findByRole('button', { name: 'Cart' })).toBeInTheDocument();
    expect(within(actions).getByText('Segment 1 of 3')).toBeInTheDocument();
    expect(screen.getAllByRole('button', { name: 'Cart' })).toHaveLength(1);
  });

  it('renders LearnerNavActions as nothing outside the learner layout', () => {
    renderWithRouter(
      <LearnerNavActions>
        <span>Orphan action</span>
      </LearnerNavActions>,
    );

    expect(screen.queryByText('Orphan action')).not.toBeInTheDocument();
  });
});
