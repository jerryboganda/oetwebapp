import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { AdminPermission } from '@/lib/admin-permissions';
import type { AdminPublishRequest } from '@/lib/types/admin';

const { mockGetPublishRequests, mockUseAdminAuth, mockUseCurrentUser, api } = vi.hoisted(() => ({
  mockGetPublishRequests: vi.fn(),
  mockUseAdminAuth: vi.fn(),
  mockUseCurrentUser: vi.fn(),
  api: {
    approvePublishRequest: vi.fn(),
    rejectPublishRequest: vi.fn(),
    editorApproveContent: vi.fn(),
    editorRejectContent: vi.fn(),
    publisherApproveContent: vi.fn(),
    publisherRejectContent: vi.fn(),
  },
}));

vi.mock('@/lib/hooks/use-admin-auth', () => ({ useAdminAuth: () => mockUseAdminAuth() }));
vi.mock('@/lib/hooks/use-current-user', () => ({ useCurrentUser: () => mockUseCurrentUser() }));
vi.mock('@/lib/admin', () => ({ getAdminPublishRequestsData: mockGetPublishRequests }));
vi.mock('@/lib/api', () => api);

import PublishRequestsPage from './page';

function makeRequest(overrides: Partial<AdminPublishRequest> = {}): AdminPublishRequest {
  return {
    id: 'request-1',
    contentItemId: 'content-1',
    requestedBy: 'author-1',
    requestedByName: 'Author One',
    reviewedBy: null,
    reviewedByName: null,
    status: 'pending',
    stage: 'editor_review',
    requestNote: null,
    reviewNote: null,
    requestedAt: '2026-04-28T09:00:00.000Z',
    reviewedAt: null,
    editorReviewedBy: null,
    editorReviewedByName: null,
    editorReviewedAt: null,
    editorNotes: null,
    publisherApprovedBy: null,
    publisherApprovedByName: null,
    publisherApprovedAt: null,
    publisherNotes: null,
    rejectedBy: null,
    rejectedByName: null,
    rejectedAt: null,
    rejectionReason: null,
    rejectionStage: null,
    ...overrides,
  };
}

function renderPage(adminPermissions: string[], items: AdminPublishRequest[]) {
  mockUseAdminAuth.mockReturnValue({ isAuthenticated: true, role: 'admin' });
  mockUseCurrentUser.mockReturnValue({
    user: { adminPermissions },
    role: 'admin',
    isAuthenticated: true,
    isLoading: false,
    pendingMfaChallenge: null,
  });
  mockGetPublishRequests.mockResolvedValue({ items, total: items.length, page: 1, pageSize: 20 });

  render(<PublishRequestsPage />);
}

describe('PublishRequestsPage permissions', () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it('blocks read-only content admins without loading the queue', () => {
    renderPage([AdminPermission.ContentRead], []);

    expect(screen.getByText('Publish workflow permission is required.')).toBeInTheDocument();
    expect(mockGetPublishRequests).not.toHaveBeenCalled();
  });

  it('hides legacy pending review controls for editor-review-only admins', async () => {
    renderPage(
      [AdminPermission.ContentEditorReview],
      [
        makeRequest({ id: 'editor-request', status: 'editor_review', stage: 'editor_review' }),
        makeRequest({ id: 'legacy-request', contentItemId: 'legacy-content', status: 'pending' }),
      ],
    );

    expect(await screen.findByRole('button', { name: 'Review (Editor)' })).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Review' })).not.toBeInTheDocument();
  });

  it('hides legacy pending review controls for publisher-approval-only admins', async () => {
    renderPage(
      [AdminPermission.ContentPublisherApproval],
      [
        makeRequest({ id: 'publisher-request', status: 'publisher_approval', stage: 'publisher_approval' }),
        makeRequest({ id: 'legacy-request', contentItemId: 'legacy-content', status: 'pending' }),
      ],
    );

    expect(await screen.findByRole('button', { name: 'Review (Publisher)' })).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Review' })).not.toBeInTheDocument();
  });

  it('keeps legacy pending approve and reject controls for content publishers', async () => {
    const user = userEvent.setup();
    renderPage([AdminPermission.ContentPublish], [makeRequest({ id: 'legacy-request', status: 'pending' })]);

    await user.click(await screen.findByRole('button', { name: 'Review' }));

    expect(screen.getByRole('button', { name: 'Approve' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Reject' })).toBeInTheDocument();
  });
});