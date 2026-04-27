import { render, screen } from '@testing-library/react';
import { AdminPermission } from '@/lib/admin-permissions';

const { mockListLessons, mockListTopics, mockUseCurrentUser, api } = vi.hoisted(() => ({
  mockListLessons: vi.fn(),
  mockListTopics: vi.fn(),
  mockUseCurrentUser: vi.fn(),
  api: {
    adminArchiveGrammarLessonV2: vi.fn(),
    adminListGrammarLessonsV2: vi.fn(),
    adminListGrammarTopics: vi.fn(),
    adminPublishGrammarLessonV2: vi.fn(),
    adminUnpublishGrammarLessonV2: vi.fn(),
  },
}));

api.adminListGrammarLessonsV2 = mockListLessons;
api.adminListGrammarTopics = mockListTopics;

vi.mock('@/lib/hooks/use-current-user', () => ({ useCurrentUser: () => mockUseCurrentUser() }));
vi.mock('@/lib/api', () => api);

import AdminGrammarDashboard from './page';

function renderGrammarPage(adminPermissions: string[]) {
  mockUseCurrentUser.mockReturnValue({
    user: { adminPermissions },
    role: 'admin',
    isAuthenticated: true,
    isLoading: false,
    pendingMfaChallenge: null,
  });
  mockListTopics.mockResolvedValue([]);
  mockListLessons.mockResolvedValue({ items: [] });

  render(<AdminGrammarDashboard />);
}

describe('AdminGrammarDashboard permissions', () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it('hides the empty lesson authoring link without content write permission', async () => {
    renderGrammarPage([AdminPermission.ContentRead]);

    expect(await screen.findByText('No lessons yet.')).toBeInTheDocument();
    expect(screen.queryByRole('link', { name: /author the first one/i })).not.toBeInTheDocument();
  });

  it('shows the empty lesson authoring link for content writers', async () => {
    renderGrammarPage([AdminPermission.ContentWrite]);

    expect(await screen.findByRole('link', { name: /author the first one/i })).toHaveAttribute(
      'href',
      '/admin/content/grammar/lessons/new',
    );
  });
});