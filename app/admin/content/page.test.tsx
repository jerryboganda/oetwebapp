import { screen } from '@testing-library/react';
import { renderWithRouter } from '@/tests/test-utils';
const { mockGetAdminContentLibraryData, mockUseAdminAuth, mockPush } = vi.hoisted(() => ({
  mockGetAdminContentLibraryData: vi.fn(),
  mockUseAdminAuth: vi.fn(),
  mockPush: vi.fn(),
}));


vi.mock('@/lib/hooks/use-admin-auth', () => ({ useAdminAuth: () => mockUseAdminAuth() }));
vi.mock('@/lib/admin', () => ({ getAdminContentLibraryData: mockGetAdminContentLibraryData }));

import AdminContentLibraryPage from './page';

describe('Admin content library page', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mockUseAdminAuth.mockReturnValue({ isAuthenticated: true, role: 'admin' });
    mockGetAdminContentLibraryData.mockResolvedValue({
      items: [{ id: 'content-1', title: 'Hospital Discharge Letter', type: 'writing_task', profession: 'medicine', status: 'published', updatedAt: '2026-04-01T08:00:00.000Z', version: 2 }],
      total: 1,
    });
  });

  it('renders admin content library with items from the API', async () => {
    renderWithRouter(<AdminContentLibraryPage />, { router: { push: mockPush } });
    const matches = await screen.findAllByText('Hospital Discharge Letter');
    expect(matches.length).toBeGreaterThan(0);
  });
});
