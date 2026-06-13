import { screen } from '@testing-library/react';
import { renderWithRouter } from '@/tests/test-utils';
import { AdminPermission } from '@/lib/admin-permissions';

const { mockUseCurrentUser } = vi.hoisted(() => ({
  mockUseCurrentUser: vi.fn(),
}));

vi.mock('@/lib/hooks/use-current-user', () => ({ useCurrentUser: () => mockUseCurrentUser() }));

vi.mock('@/lib/api/speaking-role-play-cards', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@/lib/api/speaking-role-play-cards')>();
  return {
    ...actual,
    adminListRolePlayCards: vi.fn().mockResolvedValue([]),
    adminPublishRolePlayCard: vi.fn(),
    adminArchiveRolePlayCard: vi.fn(),
  };
});

vi.mock('@/lib/api', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@/lib/api')>();
  return {
    ...actual,
    fetchAdminSpeakingMockSets: vi.fn().mockResolvedValue([]),
    publishAdminSpeakingMockSet: vi.fn(),
    archiveAdminSpeakingMockSet: vi.fn(),
  };
});

import AdminSpeakingPage from './page';

function renderHub(adminPermissions: string[]) {
  mockUseCurrentUser.mockReturnValue({
    user: { adminPermissions },
    role: 'admin',
    isAuthenticated: true,
    isLoading: false,
    pendingMfaChallenge: null,
  });

  renderWithRouter(<AdminSpeakingPage />);
}

describe('AdminSpeakingPage', () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it('shows a unified authoring hub with New actions, tabs, and operations', async () => {
    renderHub([AdminPermission.SystemAdmin]);

    expect(await screen.findByRole('heading', { name: 'Speaking authoring' })).toBeInTheDocument();

    // New actions launch the wizards.
    expect(screen.getByRole('link', { name: /New role-play card/i })).toHaveAttribute('href', '/admin/speaking/cards/new');
    expect(screen.getByRole('link', { name: /New mock set/i })).toHaveAttribute('href', '/admin/speaking/mock-sets/new');

    // Tabs for the two content types.
    expect(screen.getByRole('button', { name: 'Role-play cards' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Mock sets' })).toBeInTheDocument();

    // Operations & quality section is preserved.
    expect(screen.getByRole('heading', { name: 'Operations & quality' })).toBeInTheDocument();
    expect(screen.getByRole('link', { name: /Result visibility Open workspace/i })).toHaveAttribute('href', '/admin/speaking/result-visibility');
    expect(screen.getByRole('link', { name: /Speaking analytics Open workspace/i })).toHaveAttribute('href', '/admin/analytics/speaking');
    expect(screen.getByRole('link', { name: /Recording audit Open workspace/i })).toHaveAttribute('href', '/admin/speaking/recordings/audit');
  });

  it('hides New actions and elevated operations from read-only content admins', async () => {
    renderHub([AdminPermission.ContentRead]);

    expect(await screen.findByRole('heading', { name: 'Speaking authoring' })).toBeInTheDocument();

    // Read-only: no write actions, no elevated operations.
    expect(screen.queryByRole('link', { name: /New role-play card/i })).not.toBeInTheDocument();
    expect(screen.queryByRole('link', { name: /New mock set/i })).not.toBeInTheDocument();
    expect(screen.queryByRole('heading', { name: 'Operations & quality' })).not.toBeInTheDocument();
    expect(screen.queryByRole('link', { name: /Result visibility Open workspace/i })).not.toBeInTheDocument();

    // But the content tabs are still visible for reading.
    expect(screen.getByRole('button', { name: 'Role-play cards' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Mock sets' })).toBeInTheDocument();
  });
});
