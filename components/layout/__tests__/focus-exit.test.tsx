import { fireEvent, render, screen } from '@testing-library/react';
import { renderWithRouter } from '@/tests/test-utils';
import { FocusExitControl, FocusExitProvider, useFocusExitGuard } from '../focus-exit';

// Focus chrome (writing practice/paper/mock sessions, placement test, listening
// player, speaking task, tutor review workspace) drops the brand lockup, the
// sidebar and the bottom nav. The header's only other menu trigger is the
// hamburger, which is `lg:hidden` — so before this control a desktop-width exam
// had no navigation at all. These tests lock in the exit, and the confirmation
// that keeps a live attempt from being abandoned by a stray click.

/** A page whose work is in flight; registers the guard exactly like the exams do. */
function Attempt({ live, description, confirmLabel }: { live: boolean; description?: string; confirmLabel?: string }) {
  useFocusExitGuard({ live, description, confirmLabel });
  return <p>Attempt page</p>;
}

function renderFocusExit(options: {
  live?: boolean;
  homeHref?: string;
  historyLength?: number;
  description?: string;
  confirmLabel?: string;
} = {}) {
  const { live = false, homeHref = '/', historyLength = 1, description, confirmLabel } = options;
  // A cold start (external link, mobile app launch) reports length 1.
  Object.defineProperty(window.history, 'length', { configurable: true, value: historyLength });

  const push = vi.fn();
  const back = vi.fn();
  const tree = (attemptLive: boolean) => (
    <FocusExitProvider homeHref={homeHref}>
      <Attempt live={attemptLive} description={description} confirmLabel={confirmLabel} />
      <FocusExitControl />
    </FocusExitProvider>
  );

  const view = renderWithRouter(tree(live), {
    router: { push, back },
    pathname: '/writing/practice/session/s1',
  });

  return { push, back, setLive: (next: boolean) => view.rerender(tree(next)) };
}

const dashboardLink = () => screen.getByRole('link', { name: /dashboard/i });

afterEach(() => {
  // Drop the shadowing own property so jsdom's real `history.length` getter applies again.
  delete (window.history as unknown as Record<string, unknown>).length;
});

describe('FocusExitControl', () => {
  it('offers Back and Dashboard, with the workspace home as the Dashboard target', () => {
    renderFocusExit({ homeHref: '/expert' });

    expect(screen.getByRole('button', { name: 'Go back' })).toBeInTheDocument();
    expect(screen.getByText('Dashboard')).toBeInTheDocument();
    expect(dashboardLink()).toHaveAttribute('href', '/expert');
  });

  it('leaves straight away when no attempt is registered', () => {
    const { push, back } = renderFocusExit({ historyLength: 5 });

    fireEvent.click(dashboardLink());
    expect(push).toHaveBeenCalledWith('/');
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();

    fireEvent.click(screen.getByRole('button', { name: 'Go back' }));
    expect(back).toHaveBeenCalledTimes(1);
  });

  it('falls back to the workspace home for Back when there is no in-app history', () => {
    const { push, back } = renderFocusExit({ homeHref: '/expert', historyLength: 1 });

    fireEvent.click(screen.getByRole('button', { name: 'Go back' }));
    expect(back).not.toHaveBeenCalled();
    expect(push).toHaveBeenCalledWith('/expert');
  });

  it('confirms before abandoning a live attempt, and Stay keeps the route', () => {
    const { push, back } = renderFocusExit({ live: true });

    fireEvent.click(dashboardLink());
    expect(screen.getByRole('dialog')).toBeInTheDocument();
    expect(screen.getByText('Leave this attempt?')).toBeInTheDocument();

    fireEvent.click(screen.getByRole('button', { name: 'Stay' }));
    expect(push).not.toHaveBeenCalled();
    expect(back).not.toHaveBeenCalled();
  });

  it('leaves through the confirmed route when the attempt is live', () => {
    const { back } = renderFocusExit({ live: true, historyLength: 5 });

    fireEvent.click(screen.getByRole('button', { name: 'Go back' }));
    fireEvent.click(screen.getByRole('button', { name: 'Leave attempt' }));

    expect(back).toHaveBeenCalledTimes(1);
  });

  it("uses the attempt's own confirm copy and label", () => {
    const { push } = renderFocusExit({
      live: true,
      description: 'The audio does not restart.',
      confirmLabel: 'Leave paper',
    });

    fireEvent.click(dashboardLink());
    expect(screen.getByText('The audio does not restart.')).toBeInTheDocument();

    fireEvent.click(screen.getByRole('button', { name: 'Leave paper' }));
    expect(push).toHaveBeenCalledWith('/');
  });

  it('drops the guard when the attempt is no longer live', () => {
    const { push, setLive } = renderFocusExit({ live: true });

    setLive(false);
    fireEvent.click(dashboardLink());

    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    expect(push).toHaveBeenCalledWith('/');
  });

  it('is inert without a FocusExitProvider', () => {
    render(<Attempt live />);

    expect(screen.queryByRole('button', { name: 'Go back' })).not.toBeInTheDocument();
    expect(screen.queryByRole('link', { name: /dashboard/i })).not.toBeInTheDocument();
  });

  it('renders nothing without a provider', () => {
    render(<FocusExitControl />);

    expect(screen.queryByRole('button', { name: 'Go back' })).not.toBeInTheDocument();
  });
});
