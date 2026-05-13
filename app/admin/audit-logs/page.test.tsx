import { render, screen, waitFor } from '@testing-library/react';

const {
  mockUseAdminAuth,
  mockGetPageData,
  mockGetDetailData,
  mockExportLogs,
  mockSearchParams,
} = vi.hoisted(() => ({
  mockUseAdminAuth: vi.fn(),
  mockGetPageData: vi.fn(),
  mockGetDetailData: vi.fn(),
  mockExportLogs: vi.fn(),
  mockSearchParams: new Map<string, string>(),
}));

vi.mock('next/navigation', () => ({
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

vi.mock('@/lib/api', () => ({
  exportAdminAuditLogs: (...args: unknown[]) => mockExportLogs(...args),
}));

vi.mock('@/lib/admin', () => ({
  getAdminAuditLogPageData: (...args: unknown[]) => mockGetPageData(...args),
  getAdminAuditLogDetailData: (...args: unknown[]) => mockGetDetailData(...args),
}));

import AuditLogsPage from './page';

/**
 * Slice G shared diff (May 2026 billing hardening, 2026-05-12 closure):
 * the admin billing surfaces deep-link to `/admin/audit-logs?search=billing`
 * and `/admin/audit-logs?search=wallet_tier`, so the audit logs page must
 * read `?search=...` and pre-populate the search box + initial fetch.
 */
describe('AuditLogsPage — ?search= deep-link hydration', () => {
  const sampleRow = {
    id: 'evt-1',
    timestamp: '2026-05-12T00:00:00.000Z',
    actor: 'admin@example.com',
    action: 'billing.plan.update',
    resource: 'BillingPlan/PRO_MONTHLY',
    details: 'Updated plan price.',
  };

  beforeEach(() => {
    vi.clearAllMocks();
    mockSearchParams.clear();
    mockUseAdminAuth.mockReturnValue({ isAuthenticated: true, role: 'admin' });
    // Page logic hides the search Input behind the empty-state branch,
    // so we always seed at least one row to keep the search box mounted.
    mockGetPageData.mockResolvedValue({ items: [sampleRow], total: 1, page: 1, pageSize: 20 });
  });

  it('initialises searchQuery from ?search=billing and fires the API call with it', async () => {
    mockSearchParams.set('search', 'billing');

    render(<AuditLogsPage />);

    // Both the bootstrap call (no search) and the search-driven call run; we
    // only assert that at least one call carried the URL-derived search.
    await waitFor(() => {
      expect(
        mockGetPageData.mock.calls.some(([arg]) =>
          (arg as { search?: string })?.search === 'billing',
        ),
      ).toBe(true);
    });

    const searchInput = screen.getByPlaceholderText(/Search actions, actors, resources, or details/i) as HTMLInputElement;
    expect(searchInput.value).toBe('billing');
  });

  it('falls back to an empty search when the URL lacks ?search', async () => {
    render(<AuditLogsPage />);

    await waitFor(() => expect(mockGetPageData).toHaveBeenCalled());

    const searchInput = screen.getByPlaceholderText(/Search actions, actors, resources, or details/i) as HTMLInputElement;
    expect(searchInput.value).toBe('');
  });
});
