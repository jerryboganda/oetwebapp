import { createContext } from 'react';
import { fireEvent, render, screen } from '@testing-library/react';
import { NextRouterProvider, renderWithRouter } from '@/tests/test-utils';

// Root-cause regression guard for the Issue-01 header/safe-area fix: the
// header used to combine a FIXED height (h-14/h-24) with padding-top from
// env(safe-area-inset-top) on the SAME element. Any non-zero inset then ate
// into that fixed height, squeezing the icon row off its centerline —
// exactly the "icons pushed upward / crowd the status bar" symptom reported
// on large-screen Android devices. The fix splits the safe-area padding
// (outer element, no fixed height — grows to fit inset + content) from the
// fixed-height icon row (inner element). This test locks that structure in
// place rather than asserting pixel-perfect rendering, which needs a real
// device/WebView (see docs/artifacts/mobile-performance/06-device-test-matrix.md).

const { mockSignOut, mockUseAuth } = vi.hoisted(() => ({
  mockSignOut: vi.fn(),
  mockUseAuth: vi.fn(),
}));

vi.mock('@/contexts/auth-context', () => ({
  AuthContext: createContext({ signOut: mockSignOut, user: null }),
  useAuth: () => mockUseAuth(),
}));

vi.mock('@/lib/mobile/haptics', () => ({
  triggerImpactHaptic: vi.fn(),
}));

// TopNav's non-structural children (search, notifications, streak badges,
// theme toggle, tour launcher, help drawer) each carry their own data
// fetching / heavier subtrees that are irrelevant to the header box model
// under test — stub them so this test stays focused and fast.
vi.mock('@/components/layout/global-search', () => ({
  SearchTrigger: ({ onClick, className }: { onClick: () => void; className?: string }) => (
    <button type="button" data-testid="search-trigger" className={className} onClick={onClick}>
      Search anything
    </button>
  ),
}));
vi.mock('@/components/layout/notification-center', () => ({ NotificationCenter: () => <div data-testid="notification-center" /> }));
vi.mock('@/components/layout/learner-streak-badges', () => ({ LearnerStreakBadges: () => null }));
vi.mock('@/components/ui/theme-toggle', () => ({ ThemeToggle: () => <div data-testid="theme-toggle" /> }));
vi.mock('@/components/onboarding/tour-launcher', () => ({ TourLauncher: () => null }));
vi.mock('@/components/onboarding/help-center-drawer', () => ({
  HelpCenterDrawer: ({ open }: { open: boolean }) => (open ? <div data-testid="help-center-drawer" /> : null),
}));

import { TopNav } from '../top-nav';

describe('TopNav header safe-area box model', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mockUseAuth.mockReturnValue({ user: { avatarUrl: null }, signOut: mockSignOut });
  });

  it('keeps the safe-area inset padding off the fixed-height icon row (learner/showBrand header)', () => {
    renderWithRouter(<TopNav showBrand userSummary={{ displayName: 'Learner', email: 'l@example.com' }} />, {
      pathname: '/',
    });

    const header = screen.getByRole('banner');
    // The safe-area utility class lives on the header itself, which has no
    // fixed-height class — it must be free to grow by the live inset amount.
    expect(header.className).toMatch(/\bsafe-area-inset-top\b/);
    expect(header.className).not.toMatch(/\bh-14\b/);
    expect(header.className).not.toMatch(/\blg:h-24\b/);

    // The fixed-height content row is a distinct child element that actually
    // holds the hamburger/logo/actions, so it always gets its full declared
    // height for centering regardless of how tall the inset above it is.
    const menuButton = screen.getByRole('button', { name: /open menu/i });
    const contentRow = menuButton.closest('.h-14');
    expect(contentRow).not.toBeNull();
    expect(contentRow).not.toBe(header);
    expect(contentRow?.className).toMatch(/\bitems-center\b/);
  });

  it('keeps the safe-area inset padding off the fixed-height icon row (compact/admin header)', () => {
    renderWithRouter(<TopNav userSummary={{ displayName: 'Admin', email: 'a@example.com' }} />, { pathname: '/admin' });

    const header = screen.getByRole('banner');
    expect(header.className).toMatch(/\bsafe-area-inset-top\b/);
    expect(header.className).not.toMatch(/\bh-11\b/);

    const menuButton = screen.getByRole('button', { name: /open menu/i });
    const contentRow = menuButton.closest('.h-11');
    expect(contentRow).not.toBeNull();
    expect(contentRow).not.toBe(header);
  });
});

// The admin/expert shells (and soon the learner shell) keep TopNav mounted
// across navigations, so its overlays must close when the pathname changes.
describe('TopNav overlays close on route change', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mockUseAuth.mockReturnValue({ user: { avatarUrl: null }, signOut: mockSignOut });
  });

  it('closes the mobile menu when the pathname changes', () => {
    const at = (pathname: string) => (
      <NextRouterProvider pathname={pathname}>
        <TopNav userSummary={{ displayName: 'Admin', email: 'a@example.com' }} />
      </NextRouterProvider>
    );
    const { rerender } = render(at('/admin'));

    fireEvent.click(screen.getByRole('button', { name: /open menu/i }));
    expect(screen.getByRole('navigation', { name: /mobile menu/i })).toBeInTheDocument();

    rerender(at('/admin/users'));
    expect(screen.queryByRole('navigation', { name: /mobile menu/i })).not.toBeInTheDocument();
    expect(screen.getByRole('button', { name: /open menu/i })).toHaveAttribute('aria-expanded', 'false');
  });

  it('closes the profile menu and the help drawer when the pathname changes', () => {
    const at = (pathname: string) => (
      <NextRouterProvider pathname={pathname}>
        <TopNav showBrand userSummary={{ displayName: 'Learner', email: 'l@example.com' }} />
      </NextRouterProvider>
    );
    const { rerender } = render(at('/'));

    const trigger = screen.getByRole('button', { name: /learner/i });
    fireEvent.click(trigger);
    fireEvent.click(screen.getByRole('menuitem', { name: /help & guided tours/i }));
    expect(screen.getByTestId('help-center-drawer')).toBeInTheDocument();
    fireEvent.click(trigger);
    expect(screen.getByRole('menu')).toBeInTheDocument();

    rerender(at('/reading'));
    expect(screen.queryByRole('menu')).not.toBeInTheDocument();
    expect(screen.queryByTestId('help-center-drawer')).not.toBeInTheDocument();
    expect(trigger).toHaveAttribute('aria-expanded', 'false');
  });
});

// The palette itself is mounted once by AppShell; TopNav only renders triggers,
// and only when AppShell hands it an opener.
describe('TopNav search triggers', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mockUseAuth.mockReturnValue({ user: { avatarUrl: null }, signOut: mockSignOut });
  });

  it('renders no trigger without onOpenSearch', () => {
    renderWithRouter(<TopNav showBrand userSummary={{ displayName: 'Learner', email: 'l@example.com' }} />, { pathname: '/' });

    expect(screen.queryByTestId('search-trigger')).not.toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: /open menu/i }));
    expect(screen.queryByTestId('search-trigger')).not.toBeInTheDocument();
  });

  it('puts the bar in the learner header and a mobile entry in the menu that opens the palette', () => {
    const onOpenSearch = vi.fn();
    renderWithRouter(
      <TopNav showBrand onOpenSearch={onOpenSearch} userSummary={{ displayName: 'Learner', email: 'l@example.com' }} />,
      { pathname: '/' },
    );
    expect(screen.getAllByTestId('search-trigger')).toHaveLength(1);

    fireEvent.click(screen.getByRole('button', { name: /open menu/i }));
    const menuTrigger = screen.getAllByTestId('search-trigger')[1];
    expect(menuTrigger).toHaveClass('md:hidden');

    fireEvent.click(menuTrigger);
    expect(onOpenSearch).toHaveBeenCalledTimes(1);
    expect(screen.queryByRole('navigation', { name: /mobile menu/i })).not.toBeInTheDocument();
  });
});
