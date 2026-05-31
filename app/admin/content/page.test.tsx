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
    expect(screen.getByRole('heading', { name: 'OET subtest workspaces' })).toBeInTheDocument();
    expect(screen.getByRole('link', { name: /Reading Open workspace/i })).toHaveAttribute('href', '/admin/content/reading');
    expect(screen.getByRole('link', { name: /Listening Open workspace/i })).toHaveAttribute('href', '/admin/content/listening');
    expect(screen.getByRole('link', { name: /Writing Open workspace/i })).toHaveAttribute('href', '/admin/writing');
    expect(screen.getByRole('link', { name: /Speaking Open workspace/i })).toHaveAttribute('href', '/admin/speaking');
    expect(screen.getByRole('link', { name: /Mocks Open workspace/i })).toHaveAttribute('href', '/admin/content/mocks');
    expect(screen.getByRole('heading', { name: 'Library & learning assets' })).toBeInTheDocument();
    expect(screen.getByRole('link', { name: /Content Library Open workspace/i })).toBeInTheDocument();
    expect(screen.getByRole('link', { name: /Grammar Lessons Open workspace/i })).toBeInTheDocument();
    expect(screen.getByRole('heading', { name: 'Quality & governance' })).toBeInTheDocument();
    expect(screen.getByRole('link', { name: /Scoring System Open workspace/i })).toBeInTheDocument();
    expect(screen.getByRole('link', { name: /Quality Review Open workspace/i })).toBeInTheDocument();

    expect(screen.queryByRole('button', { name: /new content/i })).not.toBeInTheDocument();
    expect(screen.queryByRole('link', { name: /Bulk Import Open workspace/i })).not.toBeInTheDocument();
    expect(screen.queryByRole('link', { name: /Paper ZIP Import Open workspace/i })).not.toBeInTheDocument();
    expect(screen.queryByRole('link', { name: /AI Generation Open workspace/i })).not.toBeInTheDocument();
    expect(screen.queryByRole('link', { name: /Deduplication Open workspace/i })).not.toBeInTheDocument();
    expect(screen.queryByRole('link', { name: /Publish Requests Open workspace/i })).not.toBeInTheDocument();
  });

  it('shows every consolidated workflow for system admins', () => {
    renderHub([AdminPermission.SystemAdmin]);

    expect(screen.getByRole('button', { name: /new content/i })).toBeInTheDocument();
    expect(screen.getByRole('heading', { name: 'Import & automation' })).toBeInTheDocument();
    expect(screen.getByRole('link', { name: /Bulk Import Open workspace/i })).toBeInTheDocument();
    expect(screen.getByRole('link', { name: /Paper ZIP Import Open workspace/i })).toBeInTheDocument();
    expect(screen.getByRole('link', { name: /AI Generation Open workspace/i })).toBeInTheDocument();
    expect(screen.getByRole('link', { name: /Scoring System Open workspace/i })).toBeInTheDocument();
    expect(screen.getByRole('link', { name: /Quality Review Open workspace/i })).toBeInTheDocument();
    expect(screen.getByRole('link', { name: /Deduplication Open workspace/i })).toBeInTheDocument();
    expect(screen.getByRole('link', { name: /Publish Requests Open workspace/i })).toBeInTheDocument();
  });
});