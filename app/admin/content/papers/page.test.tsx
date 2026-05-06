import { render, screen } from '@testing-library/react';
import { AdminPermission } from '@/lib/admin-permissions';

const {
  mockListPapers,
  mockArchive,
  mockCreate,
  mockUseAdminAuth,
  mockUseCurrentUser,
  mockReplace,
  mockSearchParams,
} = vi.hoisted(() => ({
  mockListPapers: vi.fn(),
  mockArchive: vi.fn(),
  mockCreate: vi.fn(),
  mockUseAdminAuth: vi.fn(),
  mockUseCurrentUser: vi.fn(),
  mockReplace: vi.fn(),
  mockSearchParams: new Map<string, string>(),
}));

vi.mock('next/navigation', () => ({
  useRouter: () => ({ replace: mockReplace, push: vi.fn(), prefetch: vi.fn() }),
  usePathname: () => '/admin/content/papers',
  useSearchParams: () => ({
    get: (key: string) => mockSearchParams.get(key) ?? null,
    toString: () => {
      const parts: string[] = [];
      mockSearchParams.forEach((v, k) => parts.push(`${encodeURIComponent(k)}=${encodeURIComponent(v)}`));
      return parts.join('&');
    },
  }),
}));

vi.mock('@/lib/hooks/use-admin-auth', () => ({ useAdminAuth: () => mockUseAdminAuth() }));
vi.mock('@/lib/hooks/use-current-user', () => ({ useCurrentUser: () => mockUseCurrentUser() }));
vi.mock('@/lib/content-upload-api', () => ({
  listContentPapers: (q: unknown) => mockListPapers(q),
  archiveContentPaper: (id: string) => mockArchive(id),
  createContentPaper: (b: unknown) => mockCreate(b),
}));
vi.mock('@/lib/content-upload-defaults', () => ({
  DEFAULT_CONTENT_SOURCE_PROVENANCE: 'official',
}));

import ContentPapersListPage from './page';

describe('ContentPapersListPage URL deep-link', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mockSearchParams.clear();
    mockUseAdminAuth.mockReturnValue({ isAuthenticated: true, role: 'admin' });
    mockUseCurrentUser.mockReturnValue({
      user: { adminPermissions: [AdminPermission.ContentWrite, AdminPermission.ContentRead] },
      role: 'admin',
      isAuthenticated: true,
      isLoading: false,
      pendingMfaChallenge: null,
    });
    mockListPapers.mockResolvedValue([]);
  });

  it('initialises filterSubtest from ?subtest=listening and queries the API with it', async () => {
    mockSearchParams.set('subtest', 'listening');

    render(<ContentPapersListPage />);

    // Wait for queueMicrotask-scheduled load() to fire.
    await screen.findByText('Content Papers');

    expect(mockListPapers).toHaveBeenCalledWith(
      expect.objectContaining({ subtest: 'listening' }),
    );

    const subtestSelect = screen.getByLabelText('Subtest') as HTMLSelectElement;
    expect(subtestSelect.value).toBe('listening');
  });

  it('falls back to no subtest filter when the URL lacks ?subtest', async () => {
    render(<ContentPapersListPage />);

    await screen.findByText('Content Papers');

    expect(mockListPapers).toHaveBeenCalledWith(
      expect.objectContaining({ subtest: undefined }),
    );

    const subtestSelect = screen.getByLabelText('Subtest') as HTMLSelectElement;
    expect(subtestSelect.value).toBe('');
  });
});
