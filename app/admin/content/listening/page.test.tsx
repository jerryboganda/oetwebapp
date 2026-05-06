import { render, screen } from '@testing-library/react';
import { AdminPermission } from '@/lib/admin-permissions';
import type { ContentPaperDto } from '@/lib/content-upload-api';

const { mockListPapers, mockArchive, mockUseAdminAuth, mockUseCurrentUser } = vi.hoisted(() => ({
  mockListPapers: vi.fn(),
  mockArchive: vi.fn(),
  mockUseAdminAuth: vi.fn(),
  mockUseCurrentUser: vi.fn(),
}));

vi.mock('@/lib/hooks/use-admin-auth', () => ({ useAdminAuth: () => mockUseAdminAuth() }));
vi.mock('@/lib/hooks/use-current-user', () => ({ useCurrentUser: () => mockUseCurrentUser() }));
vi.mock('@/lib/content-upload-api', () => ({
  listContentPapers: (q: unknown) => mockListPapers(q),
  archiveContentPaper: (id: string) => mockArchive(id),
}));

import AdminListeningPapersPage from './page';

function makePaper(overrides: Partial<ContentPaperDto>): ContentPaperDto {
  return {
    id: 'p1',
    subtestCode: 'listening',
    title: 'Listening Sample',
    slug: 'listening-sample',
    professionId: null,
    appliesToAllProfessions: true,
    difficulty: 'standard',
    estimatedDurationMinutes: 40,
    status: 'Draft',
    publishedRevisionId: null,
    cardType: null,
    letterType: null,
    priority: 0,
    tagsCsv: '',
    sourceProvenance: 'official',
    createdAt: '2026-04-01T00:00:00Z',
    updatedAt: '2026-04-02T00:00:00Z',
    publishedAt: null,
    archivedAt: null,
    assets: [],
    ...overrides,
  };
}

function setup(adminPermissions: string[]) {
  mockUseAdminAuth.mockReturnValue({ isAuthenticated: true, role: 'admin' });
  mockUseCurrentUser.mockReturnValue({
    user: { adminPermissions },
    role: 'admin',
    isAuthenticated: true,
    isLoading: false,
    pendingMfaChallenge: null,
  });
  mockListPapers.mockResolvedValue([
    makePaper({ id: 'pub-1', title: 'Listening Paper Alpha', slug: 'alpha', status: 'Published' }),
    makePaper({ id: 'draft-1', title: 'Listening Paper Beta', slug: 'beta', status: 'Draft' }),
    makePaper({ id: 'review-1', title: 'Listening Paper Gamma', slug: 'gamma', status: 'InReview' }),
  ]);

  render(<AdminListeningPapersPage />);
}

describe('AdminListeningPapersPage', () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it('renders the section header, stats, and all listening rows', async () => {
    setup([AdminPermission.ContentWrite, AdminPermission.ContentRead]);

    expect(await screen.findByText('Listening Papers')).toBeInTheDocument();

    // Stats panel labels (rendered immediately, no async dependency)
    expect(screen.getByText('Listening overview')).toBeInTheDocument();
    expect(screen.getByText('Total')).toBeInTheDocument();
    expect(screen.getByText('Drafts')).toBeInTheDocument();
    expect(screen.getByText('Missing assets')).toBeInTheDocument();

    // Always queries with subtest=listening
    expect(mockListPapers).toHaveBeenCalledWith(
      expect.objectContaining({ subtest: 'listening' }),
    );

    // Wait for the async load to flush rows into the table.
    const alphaMatches = await screen.findAllByText('Listening Paper Alpha', undefined, { timeout: 3000 });
    expect(alphaMatches.length).toBeGreaterThan(0);
    expect(screen.getAllByText('Listening Paper Beta').length).toBeGreaterThan(0);
    expect(screen.getAllByText('Listening Paper Gamma').length).toBeGreaterThan(0);
  });

  it('locks the page for non-admin sessions', () => {
    mockUseAdminAuth.mockReturnValue({ isAuthenticated: false, role: null });
    mockUseCurrentUser.mockReturnValue({
      user: null,
      role: null,
      isAuthenticated: false,
      isLoading: false,
      pendingMfaChallenge: null,
    });
    render(<AdminListeningPapersPage />);
    expect(screen.getByText('Admin access required.')).toBeInTheDocument();
    expect(mockListPapers).not.toHaveBeenCalled();
  });
});
