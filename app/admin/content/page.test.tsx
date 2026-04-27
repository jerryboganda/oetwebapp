import { screen } from '@testing-library/react';
import { renderWithRouter } from '@/tests/test-utils';
import { AdminPermission } from '@/lib/admin-permissions';

const { mockUseAdminAuth, mockUseCurrentUser, mockPush } = vi.hoisted(() => ({
  mockUseAdminAuth: vi.fn(),
  mockUseCurrentUser: vi.fn(),
  mockPush: vi.fn(),
}));

vi.mock('@/lib/hooks/use-admin-auth', () => ({ useAdminAuth: () => mockUseAdminAuth() }));
vi.mock('@/lib/hooks/use-current-user', () => ({ useCurrentUser: () => mockUseCurrentUser() }));

import AdminContentHubPage from './page';

function renderHub(adminPermissions: string[]) {
  mockUseAdminAuth.mockReturnValue({ isAuthenticated: true, role: 'admin' });
  mockUseCurrentUser.mockReturnValue({
    user: { adminPermissions },
    role: 'admin',
    isAuthenticated: true,
    isLoading: false,
    pendingMfaChallenge: null,
  });

  renderWithRouter(<AdminContentHubPage />, { router: { push: mockPush } });
}

describe('AdminContentHubPage', () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it('hides write and publish workflows for read-only content admins', () => {
    renderHub([AdminPermission.ContentRead]);

    expect(screen.getByRole('heading', { name: 'Content Hub' })).toBeInTheDocument();
    expect(screen.getByText('Content Library')).toBeInTheDocument();
    expect(screen.getByText('Content Papers')).toBeInTheDocument();
    expect(screen.getByText('Item Analytics')).toBeInTheDocument();
    expect(screen.getByText('Grammar Lessons')).toBeInTheDocument();
    expect(screen.getByText('Quality Review')).toBeInTheDocument();

    expect(screen.queryByRole('button', { name: /new content/i })).not.toBeInTheDocument();
    expect(screen.queryByText('Bulk Import')).not.toBeInTheDocument();
    expect(screen.queryByText('Paper ZIP Import')).not.toBeInTheDocument();
    expect(screen.queryByText('AI Generation')).not.toBeInTheDocument();
    expect(screen.queryByText('Deduplication')).not.toBeInTheDocument();
    expect(screen.queryByText('Publish Requests')).not.toBeInTheDocument();
  });

  it('shows every consolidated workflow for system admins', () => {
    renderHub([AdminPermission.SystemAdmin]);

    expect(screen.getByRole('button', { name: /new content/i })).toBeInTheDocument();
    expect(screen.getByText('Bulk Import')).toBeInTheDocument();
    expect(screen.getByText('Paper ZIP Import')).toBeInTheDocument();
    expect(screen.getByText('AI Generation')).toBeInTheDocument();
    expect(screen.getByText('Item Analytics')).toBeInTheDocument();
    expect(screen.getByText('Quality Review')).toBeInTheDocument();
    expect(screen.getByText('Deduplication')).toBeInTheDocument();
    expect(screen.getByText('Publish Requests')).toBeInTheDocument();
  });
});