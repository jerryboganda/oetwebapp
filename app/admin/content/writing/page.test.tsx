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

import AdminWritingPapersPage from './page';

function makePaper(overrides: Partial<ContentPaperDto>): ContentPaperDto {
  return {
    id: 'p1',
    subtestCode: 'writing',
    title: 'Writing Sample',
    slug: 'writing-sample',
    professionId: 'medicine',
    appliesToAllProfessions: false,
    difficulty: 'standard',
    estimatedDurationMinutes: 45,
    status: 'Draft',
    publishedRevisionId: null,
    cardType: null,
    letterType: 'routine_referral',
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
    makePaper({ id: 'pub-1', title: 'Writing Routine Referral', slug: 'routine', status: 'Published' }),
    makePaper({ id: 'draft-1', title: 'Writing Urgent Referral', slug: 'urgent', status: 'Draft', letterType: 'urgent_referral' }),
    makePaper({ id: 'missing-1', title: 'Writing Needs Type', slug: 'missing-type', letterType: null }),
  ]);

  render(<AdminWritingPapersPage />);
}

describe('AdminWritingPapersPage', () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it('renders the section header, stats, and writing rows', async () => {
    setup([AdminPermission.ContentWrite, AdminPermission.ContentRead]);

    expect(await screen.findByText('Writing Papers')).toBeInTheDocument();
    expect(screen.getByText('Writing overview')).toBeInTheDocument();
    expect(screen.getByText('Missing assets')).toBeInTheDocument();
    expect(screen.getByText('Missing type')).toBeInTheDocument();

    expect(mockListPapers).toHaveBeenCalledWith(
      expect.objectContaining({ subtest: 'writing' }),
    );

    expect((await screen.findAllByText('Writing Routine Referral')).length).toBeGreaterThan(0);
    expect(screen.getAllByText('Writing Urgent Referral').length).toBeGreaterThan(0);
    expect(screen.getAllByText('Writing Needs Type').length).toBeGreaterThan(0);
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
    render(<AdminWritingPapersPage />);
    expect(screen.getByText('Admin access required.')).toBeInTheDocument();
    expect(mockListPapers).not.toHaveBeenCalled();
  });
});