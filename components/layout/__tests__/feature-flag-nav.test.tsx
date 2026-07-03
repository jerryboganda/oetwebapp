import { createContext } from 'react';
import { screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { Library, Sparkles, Video } from 'lucide-react';
import { renderWithRouter } from '@/tests/test-utils';

const { mockFetchLearnerFeatureFlag, mockFetchStreak, mockFetchXP, mockSignOut, mockUseAuth } = vi.hoisted(() => ({
  mockFetchLearnerFeatureFlag: vi.fn(),
  mockFetchStreak: vi.fn(),
  mockFetchXP: vi.fn(),
  mockSignOut: vi.fn(),
  mockUseAuth: vi.fn(),
}));

vi.mock('@/lib/api', () => ({
  fetchLearnerFeatureFlag: mockFetchLearnerFeatureFlag,
  fetchStreak: mockFetchStreak,
  fetchXP: mockFetchXP,
}));

vi.mock('@/contexts/auth-context', () => ({
  AuthContext: createContext({ signOut: mockSignOut }),
  useAuth: () => mockUseAuth(),
}));

vi.mock('@/components/layout/notification-center', () => ({
  NotificationCenter: () => <div data-testid="notification-center" />,
}));

import { Sidebar, type NavItem } from '../sidebar';
import { TopNav } from '../top-nav';

const learnerUser = {
  userId: 'learner-1',
  displayName: 'Amina Khan',
  email: 'amina@example.com',
  role: 'learner',
  isEmailVerified: true,
};

const adminUser = {
  userId: 'admin-1',
  displayName: 'Admin User',
  email: 'admin@example.com',
  role: 'admin',
  isEmailVerified: true,
};

function prepareFlag(enabled: boolean) {
  mockFetchLearnerFeatureFlag.mockResolvedValue({ key: 'video_lessons', enabled });
  mockFetchStreak.mockResolvedValue({ currentStreak: 3 });
  mockFetchXP.mockResolvedValue({ level: 4 });
  mockUseAuth.mockReturnValue({ user: learnerUser, signOut: mockSignOut });
}

describe('learner feature-gated navigation', () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  // Per the 2026-05-27 OET sample-test alignment, the Learn group (Grammar,
  // Live Classes, Lessons, Strategies, Recalls, AI Conversation) is intentionally
  // hidden from the candidate sidebar so the workspace mirrors the official
  // computer-based sample-test simplicity. Video Lessons therefore never appears
  // in the learner sidebar regardless of the `video_lessons` flag — feature-flag
  // gating remains exercised in admin/tutor workspaces and via TopNav sectioned
  // menus below.
  it('does not surface the Learn group in the learner sidebar when the flag is disabled', async () => {
    prepareFlag(false);

    renderWithRouter(<Sidebar workspaceRole="learner" />, { pathname: '/' });

    expect(screen.queryByRole('link', { name: /video lessons/i })).not.toBeInTheDocument();
  });

  it('still hides the Learn group from the learner sidebar even when the flag is enabled', async () => {
    prepareFlag(true);

    renderWithRouter(<Sidebar workspaceRole="learner" />, { pathname: '/' });

    expect(screen.queryByRole('link', { name: /video lessons/i })).not.toBeInTheDocument();
    expect(screen.queryByRole('link', { name: /grammar/i })).not.toBeInTheDocument();
  });

  it('keeps the Learn group visible to admin/tutor workspaces when the flag is enabled', async () => {
    mockFetchLearnerFeatureFlag.mockResolvedValue({ key: 'video_library', enabled: true });
    mockUseAuth.mockReturnValue({ user: adminUser, signOut: mockSignOut });

    renderWithRouter(<Sidebar workspaceRole="admin" />, { pathname: '/' });

    expect(await screen.findByRole('link', { name: /video library/i })).toHaveAttribute('href', '/videos');
  });

  it('hides feature-gated entries in the mobile menu while keeping other learn links available', async () => {
    prepareFlag(false);
    const learnSection: NavItem[] = [
      { href: '/lessons', label: 'Video Lessons', icon: <Video className="h-4 w-4" />, featureFlag: 'video_lessons' },
      { href: '/grammar', label: 'Grammar', icon: <span aria-hidden="true" /> },
    ];

    renderWithRouter(
      <TopNav
        workspaceRole="learner"
        userSummary={{ displayName: 'Amina Khan', email: 'amina@example.com' }}
        sectionedItems={[{ label: 'Learn', items: learnSection }]}
      />,
      { pathname: '/' },
    );

    await waitFor(() => {
      expect(mockFetchLearnerFeatureFlag).toHaveBeenCalledWith('video_lessons');
    });

    await userEvent.click(screen.getByRole('button', { name: /open menu/i }));

    expect(screen.getByRole('link', { name: /grammar/i })).toHaveAttribute('href', '/grammar');
    expect(screen.queryByRole('link', { name: /video lessons/i })).not.toBeInTheDocument();
  });

  it('prefers the canonical content tool route over the generic content library route in the mobile menu', async () => {
    mockUseAuth.mockReturnValue({ user: adminUser, signOut: mockSignOut });
    const adminSection: NavItem[] = [
      { href: '/admin/content', label: 'Content Library', icon: <Library className="h-4 w-4" />, matchPrefix: '/admin/content' },
      { href: '/admin/content/generation', label: 'Content Generation', icon: <Sparkles className="h-4 w-4" />, matchPrefix: '/admin/content/generation' },
    ];

    renderWithRouter(
      <TopNav
        workspaceRole="admin"
        userSummary={{ displayName: 'Admin User', email: 'admin@example.com' }}
        sectionedItems={[{ label: 'Content authoring', items: adminSection }]}
      />,
      { pathname: '/admin/content/generation' },
    );

    await userEvent.click(screen.getByRole('button', { name: /open menu/i }));

    expect(screen.getByRole('link', { name: /content generation/i })).toHaveAttribute('aria-current', 'page');
    expect(screen.getByRole('link', { name: /content library/i })).not.toHaveAttribute('aria-current');
  });
});
