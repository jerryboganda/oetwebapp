import { render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { AdminPermission } from '@/lib/admin-permissions';
import type { ContentPaperDto } from '@/lib/content-upload-api';

const {
  mockListPapers,
  mockArchive,
  mockBulk,
  mockUseAdminAuth,
  mockUseCurrentUser,
} = vi.hoisted(() => ({
  mockListPapers: vi.fn(),
  mockArchive: vi.fn(),
  mockBulk: vi.fn(),
  mockUseAdminAuth: vi.fn(),
  mockUseCurrentUser: vi.fn(),
}));

vi.mock('@/lib/hooks/use-admin-auth', () => ({ useAdminAuth: () => mockUseAdminAuth() }));
vi.mock('@/lib/hooks/use-current-user', () => ({ useCurrentUser: () => mockUseCurrentUser() }));
vi.mock('@/lib/content-upload-api', () => ({
  listContentPapers: (q: unknown) => mockListPapers(q),
  archiveContentPaper: (id: string) => mockArchive(id),
  approvePublishWritingPaper: vi.fn(),
  rejectWritingPaper: vi.fn(),
  submitWritingPaperForReview: vi.fn(),
  bulkContentPapers: (action: string, ids: string[], reason?: string) => mockBulk(action, ids, reason),
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
    integrityAcknowledgedByAdminId: null,
    integrityAcknowledgedAt: null,
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
    // The page eyebrow was renamed from "Writing overview" to "Catalog" when
    // the admin shell was unified — assert the new label.
    expect(screen.getByText('Catalog')).toBeInTheDocument();
    expect(screen.getByText('Missing assets')).toBeInTheDocument();
    expect(screen.getByText('Missing type')).toBeInTheDocument();

    expect(mockListPapers).toHaveBeenCalledWith(
      expect.objectContaining({ subtest: 'writing' }),
    );

    expect((await screen.findAllByText('Writing Routine Referral')).length).toBeGreaterThan(0);
    expect(screen.getAllByText('Writing Urgent Referral').length).toBeGreaterThan(0);
    expect(screen.getAllByText('Writing Needs Type').length).toBeGreaterThan(0);
  });

  async function selectRow(id: string) {
    const user = userEvent.setup();
    // DataTable renders both mobile + desktop views, so a row checkbox can
    // appear more than once. Toggling the first selects the row in state.
    const [checkbox] = screen.getAllByLabelText(`Select row ${id}`);
    await user.click(checkbox);
    return user;
  }

  it('gates Approve & Reject on every selected row being InReview', async () => {
    mockUseAdminAuth.mockReturnValue({ isAuthenticated: true, role: 'admin' });
    mockUseCurrentUser.mockReturnValue({
      user: { adminPermissions: [AdminPermission.ContentWrite, AdminPermission.ContentPublish] },
      role: 'admin',
      isAuthenticated: true,
      isLoading: false,
      pendingMfaChallenge: null,
    });
    mockListPapers.mockResolvedValue([
      makePaper({ id: 'review-1', title: 'In Review One', slug: 'r1', status: 'InReview' }),
      makePaper({ id: 'draft-1', title: 'Draft One', slug: 'd1', status: 'Draft' }),
    ]);
    render(<AdminWritingPapersPage />);

    await screen.findAllByText('In Review One');

    // Select only the InReview row -> approve & reject enabled.
    await selectRow('review-1');
    const bar = await screen.findByTestId('bulk-action-bar');
    expect(within(bar).getByRole('button', { name: /Approve & publish/i })).toBeEnabled();
    expect(within(bar).getByRole('button', { name: /^Reject$/i })).toBeEnabled();

    // Also select a Draft row -> not every selected row is InReview, so disabled.
    await selectRow('draft-1');
    expect(within(bar).getByRole('button', { name: /Approve & publish/i })).toBeDisabled();
    expect(within(bar).getByRole('button', { name: /^Reject$/i })).toBeDisabled();
    // Submit for review is also disabled because review-1 is not Draft.
    expect(within(bar).getByRole('button', { name: /Submit for review/i })).toBeDisabled();
  });

  it('opens a reason-required confirm for Reject and passes the reason to bulkContentPapers', async () => {
    mockUseAdminAuth.mockReturnValue({ isAuthenticated: true, role: 'admin' });
    mockUseCurrentUser.mockReturnValue({
      user: { adminPermissions: [AdminPermission.ContentWrite, AdminPermission.ContentPublish] },
      role: 'admin',
      isAuthenticated: true,
      isLoading: false,
      pendingMfaChallenge: null,
    });
    mockListPapers.mockResolvedValue([
      makePaper({ id: 'review-1', title: 'In Review One', slug: 'r1', status: 'InReview' }),
    ]);
    mockBulk.mockResolvedValue({ totalRequested: 1, succeeded: 1, skipped: 0, failed: 0, errors: [] });
    render(<AdminWritingPapersPage />);

    await screen.findAllByText('In Review One');
    const user = await selectRow('review-1');

    const bar = await screen.findByTestId('bulk-action-bar');
    await user.click(within(bar).getByRole('button', { name: /^Reject$/i }));

    // Confirm modal shows the reason textarea; the modal's confirm button is the
    // last "Reject" button (the first lives in the bulk action bar).
    const reasonField = await screen.findByLabelText('Rejection reason');
    const modalConfirm = () => {
      const buttons = screen.getAllByRole('button', { name: /^Reject$/i });
      return buttons[buttons.length - 1];
    };
    expect(modalConfirm()).toBeDisabled();

    await user.type(reasonField, 'Needs a clearer task prompt');
    expect(modalConfirm()).toBeEnabled();
    await user.click(modalConfirm());

    expect(mockBulk).toHaveBeenCalledWith('reject', ['review-1'], 'Needs a clearer task prompt');
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