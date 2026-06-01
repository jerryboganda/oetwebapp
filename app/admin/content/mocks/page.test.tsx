import { screen } from '@testing-library/react';
import { renderWithRouter } from '@/tests/test-utils';
import { AdminPermission } from '@/lib/admin-permissions';

const { mockUseCurrentUser, mockFetchAdminMockBundles, mockListAdminMockLeakReports } = vi.hoisted(() => ({
  mockUseCurrentUser: vi.fn(),
  mockFetchAdminMockBundles: vi.fn(),
  mockListAdminMockLeakReports: vi.fn(),
}));

vi.mock('@/lib/hooks/use-current-user', () => ({ useCurrentUser: () => mockUseCurrentUser() }));
vi.mock('@/lib/content-upload-api', () => ({ listContentPapers: vi.fn() }));
vi.mock('@/lib/api', () => ({
  addAdminMockBundleSection: vi.fn(),
  archiveAdminMockBundle: vi.fn(),
  createAdminMockBundle: vi.fn(),
  fetchAdminMockBundles: (...args: unknown[]) => mockFetchAdminMockBundles(...args),
  listAdminMockLeakReports: (...args: unknown[]) => mockListAdminMockLeakReports(...args),
  publishAdminMockBundle: vi.fn(),
  reorderAdminMockBundleSections: vi.fn(),
  updateAdminMockBundle: vi.fn(),
}));

import AdminMockBundlesPage from './page';

describe('AdminMockBundlesPage', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mockUseCurrentUser.mockReturnValue({
      user: { adminPermissions: [AdminPermission.ContentRead] },
      role: 'admin',
      isAuthenticated: true,
      isLoading: false,
      pendingMfaChallenge: null,
    });
    mockFetchAdminMockBundles.mockResolvedValue({
      items: [
        {
          id: 'mock-route-1',
          title: 'Route 1',
          mockType: 'full',
          subtestCode: null,
          professionId: null,
          appliesToAllProfessions: true,
          status: 'draft',
          estimatedDurationMinutes: 180,
          sourceProvenance: 'Source: test',
          sections: [],
        },
      ],
    });
    mockListAdminMockLeakReports.mockResolvedValue({ items: [] });
  });

  it('shows read-only bundles without write or publish controls', async () => {
    renderWithRouter(<AdminMockBundlesPage />);

    expect(await screen.findByText('Route 1')).toBeInTheDocument();
    expect(screen.getByRole('link', { name: /item analysis/i })).toBeInTheDocument();
    expect(screen.queryByText('Create bundle')).not.toBeInTheDocument();
    expect(screen.queryByText('Build a complete mock end-to-end')).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /create/i })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /publish/i })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /edit/i })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /add section/i })).not.toBeInTheDocument();
  });
});