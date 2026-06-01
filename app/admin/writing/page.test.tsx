import { screen } from '@testing-library/react';
import { renderWithRouter } from '@/tests/test-utils';
import { AdminPermission } from '@/lib/admin-permissions';

const { mockUseCurrentUser } = vi.hoisted(() => ({
  mockUseCurrentUser: vi.fn(),
}));

vi.mock('@/lib/hooks/use-current-user', () => ({ useCurrentUser: () => mockUseCurrentUser() }));

import AdminWritingHubPage from './page';

function renderHub(adminPermissions: string[]) {
  mockUseCurrentUser.mockReturnValue({
    user: { adminPermissions },
    role: 'admin',
    isAuthenticated: true,
    isLoading: false,
    pendingMfaChallenge: null,
  });

  renderWithRouter(<AdminWritingHubPage />);
}

describe('AdminWritingHubPage', () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it('groups writing work into authoring, quality, and AI sections', () => {
    renderHub([AdminPermission.SystemAdmin]);

    expect(screen.getByRole('heading', { name: 'Writing' })).toBeInTheDocument();
    expect(screen.getByRole('heading', { name: 'Authoring workspace' })).toBeInTheDocument();
    expect(screen.getByRole('link', { name: /Create task Open workspace/i })).toHaveAttribute('href', '/admin/writing/tasks/new');
    expect(screen.getByRole('link', { name: /Task library Open workspace/i })).toHaveAttribute('href', '/admin/writing/tasks');

    expect(screen.getByRole('heading', { name: 'Quality & release' })).toBeInTheDocument();
    expect(screen.getByRole('link', { name: /Analytics Open workspace/i })).toHaveAttribute('href', '/admin/writing/analytics');
    expect(screen.getByRole('link', { name: /Result visibility Open workspace/i })).toHaveAttribute('href', '/admin/writing/result-visibility');

    expect(screen.getByRole('heading', { name: 'AI assistance' })).toBeInTheDocument();
    expect(screen.getByRole('link', { name: /AI options Open workspace/i })).toHaveAttribute('href', '/admin/writing/options');
    expect(screen.getByRole('link', { name: /AI draft Open workspace/i })).toHaveAttribute('href', '/admin/writing/ai-draft');
  });

  it('hides elevated Writing workflows from read-only content admins', () => {
    renderHub([AdminPermission.ContentRead]);

    expect(screen.getByRole('heading', { name: 'Authoring workspace' })).toBeInTheDocument();
    expect(screen.getByRole('link', { name: /Task library Open workspace/i })).toBeInTheDocument();
    expect(screen.queryByRole('link', { name: /Create task Open workspace/i })).not.toBeInTheDocument();
    expect(screen.queryByRole('link', { name: /Result visibility Open workspace/i })).not.toBeInTheDocument();
    expect(screen.queryByRole('link', { name: /Analytics Open workspace/i })).not.toBeInTheDocument();
    expect(screen.queryByRole('link', { name: /AI options Open workspace/i })).not.toBeInTheDocument();
    expect(screen.queryByRole('link', { name: /AI draft Open workspace/i })).not.toBeInTheDocument();
  });
});