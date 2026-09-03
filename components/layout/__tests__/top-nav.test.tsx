import { createContext } from 'react';
import { screen } from '@testing-library/react';
import { renderWithRouter } from '@/tests/test-utils';

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
vi.mock('@/components/layout/global-search', () => ({ GlobalSearch: () => <div data-testid="global-search" /> }));
vi.mock('@/components/layout/notification-center', () => ({ NotificationCenter: () => <div data-testid="notification-center" /> }));
vi.mock('@/components/layout/learner-streak-badges', () => ({ LearnerStreakBadges: () => null }));
vi.mock('@/components/ui/theme-toggle', () => ({ ThemeToggle: () => <div data-testid="theme-toggle" /> }));
vi.mock('@/components/onboarding/tour-launcher', () => ({ TourLauncher: () => null }));
vi.mock('@/components/onboarding/help-center-drawer', () => ({ HelpCenterDrawer: () => null }));

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
