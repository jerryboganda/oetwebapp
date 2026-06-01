import { screen } from '@testing-library/react';
import { renderWithRouter } from '@/tests/test-utils';
import { AdminPermission } from '@/lib/admin-permissions';

const { mockUseCurrentUser } = vi.hoisted(() => ({
  mockUseCurrentUser: vi.fn(),
}));

vi.mock('@/lib/hooks/use-current-user', () => ({ useCurrentUser: () => mockUseCurrentUser() }));

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

  it('groups speaking work into operations, authoring, and assets sections', () => {
    renderHub([AdminPermission.SystemAdmin]);

    expect(screen.getByRole('heading', { name: 'Speaking' })).toBeInTheDocument();
    expect(screen.getByRole('heading', { name: 'Operations & quality' })).toBeInTheDocument();
    expect(screen.getByRole('link', { name: /Result visibility Open workspace/i })).toHaveAttribute('href', '/admin/speaking/result-visibility');
    expect(screen.getByRole('link', { name: /Speaking analytics Open workspace/i })).toHaveAttribute('href', '/admin/analytics/speaking');
    expect(screen.getByRole('link', { name: /Recording audit Open workspace/i })).toHaveAttribute('href', '/admin/speaking/recordings/audit');

    expect(screen.getByRole('heading', { name: 'Content authoring' })).toBeInTheDocument();
    expect(screen.getByRole('link', { name: /Speaking authoring Open workspace/i })).toHaveAttribute('href', '/admin/content/papers?subtest=speaking');
    expect(screen.getByRole('link', { name: /Mock sets Open workspace/i })).toHaveAttribute('href', '/admin/content/speaking/mock-sets');

    expect(screen.getByRole('heading', { name: 'Shared assets' })).toBeInTheDocument();
    expect(screen.getByRole('link', { name: /Shared resources Open workspace/i })).toHaveAttribute('href', '/admin/content/speaking/shared-resources');
  });

  it('hides elevated Speaking operations from read-only content admins', () => {
    renderHub([AdminPermission.ContentRead]);

    expect(screen.queryByRole('heading', { name: 'Operations & quality' })).not.toBeInTheDocument();
    expect(screen.queryByRole('link', { name: /Result visibility Open workspace/i })).not.toBeInTheDocument();
    expect(screen.queryByRole('link', { name: /Speaking analytics Open workspace/i })).not.toBeInTheDocument();
    expect(screen.queryByRole('link', { name: /Recording audit Open workspace/i })).not.toBeInTheDocument();
    expect(screen.getByRole('heading', { name: 'Content authoring' })).toBeInTheDocument();
    expect(screen.getByRole('link', { name: /Speaking authoring Open workspace/i })).toBeInTheDocument();
    expect(screen.getByRole('heading', { name: 'Shared assets' })).toBeInTheDocument();
    expect(screen.getByRole('link', { name: /Shared resources Open workspace/i })).toBeInTheDocument();
  });
});