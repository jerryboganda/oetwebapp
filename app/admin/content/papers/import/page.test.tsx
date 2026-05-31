import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { AdminPermission } from '@/lib/admin-permissions';

const {
  mockPostForm,
  mockPost,
  mockUseAdminAuth,
  mockUseCurrentUser,
} = vi.hoisted(() => ({
  mockPostForm: vi.fn(),
  mockPost: vi.fn(),
  mockUseAdminAuth: vi.fn(),
  mockUseCurrentUser: vi.fn(),
}));

vi.mock('@/lib/hooks/use-admin-auth', () => ({ useAdminAuth: () => mockUseAdminAuth() }));
vi.mock('@/lib/hooks/use-current-user', () => ({ useCurrentUser: () => mockUseCurrentUser() }));
vi.mock('@/lib/api', () => ({
  apiClient: {
    postForm: (...args: unknown[]) => mockPostForm(...args),
    post: (...args: unknown[]) => mockPost(...args),
  },
}));
vi.mock('@/lib/content-upload-defaults', () => ({
  DEFAULT_CONTENT_SOURCE_PROVENANCE: 'Source: approved test fixture',
}));

import BulkImportPage from './page';

describe('BulkImportPage', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mockUseAdminAuth.mockReturnValue({ isAuthenticated: true, isLoading: false, role: 'admin' });
    mockUseCurrentUser.mockReturnValue({
      user: { adminPermissions: [AdminPermission.ContentWrite, AdminPermission.ContentRead] },
    });
    mockPostForm.mockResolvedValue({
      sessionId: 'session-1',
      expiresAt: '2026-05-31T12:00:00Z',
      inventory: {
        totalFiles: 3,
        classifiedFileCount: 3,
        unclassifiedFileCount: 0,
        filesByExtension: { pdf: 2, txt: 1 },
        filesByTopLevel: { Speaking_: 2, 'Scoring System.txt': 1 },
      },
      readinessIssues: [],
      issues: [],
      papers: [
        {
          proposalId: 'paper-1',
          subtestCode: 'speaking',
          title: 'Card 4 ( Examination Card )_ MOST IMPORTANT TYPE',
          professionId: 'medicine',
          appliesToAllProfessions: false,
          cardType: 'examination',
          letterType: null,
          sourceProvenance: 'Source: approved test fixture',
          deliveryModes: ['paper', 'computer', 'oet_home'],
          officialShape: 'OET Speaking: approximately 20 minutes; full sub-test uses two 5-minute role plays.',
          readinessIssues: [{ code: 'structure_authoring_required', severity: 'warning', message: 'Structured card content required.' }],
          assets: [{ sourceRelativePath: 'Speaking_/Card 4/4.pdf', role: 'RoleCard', part: null, suggestedTitle: '4' }],
        },
      ],
      references: [
        {
          proposalId: 'ref-1',
          target: 'SpeakingSharedResource',
          title: 'Speaking Assessment Criteria',
          sourceRelativePath: 'Speaking_/Speaking Assessment Criteria.pdf',
          kind: null,
          professionId: null,
          sharedResourceKind: 'AssessmentCriteria',
          templateKey: null,
          sortOrder: null,
          sourceProvenance: 'Source: approved test fixture',
          readinessIssues: [],
        },
        {
          proposalId: 'ref-2',
          target: 'ScoringPolicyBody',
          title: 'Scoring System',
          sourceRelativePath: 'Scoring System.txt',
          kind: null,
          professionId: null,
          sharedResourceKind: null,
          templateKey: null,
          sortOrder: null,
          sourceProvenance: 'Source: approved test fixture',
          readinessIssues: [],
        },
      ],
    });
    mockPost.mockResolvedValue({
      createdPaperCount: 1,
      createdAssetCount: 3,
      createdReferenceCount: 2,
      deduplicatedAssetCount: 1,
      warnings: [],
    });
  });

  it('reviews paper and reference targets and commits all approved proposal ids', async () => {
    const user = userEvent.setup();
    const { container } = render(<BulkImportPage />);

    const input = container.querySelector('input[type="file"]') as HTMLInputElement;
    await user.upload(input, new File(['zip'], 'medicine.zip', { type: 'application/zip' }));

    expect(await screen.findByText('Reference targets (2)')).toBeInTheDocument();
    expect(screen.getByText('Speaking Assessment Criteria')).toBeInTheDocument();
    expect(screen.getByText('Scoring System')).toBeInTheDocument();
    expect(screen.getByText('.pdf: 2')).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: /commit approved proposals/i }));

    await waitFor(() => expect(mockPost).toHaveBeenCalled());
    expect(mockPost).toHaveBeenCalledWith('/v1/admin/imports/zip/session-1/commit', [
      expect.objectContaining({ proposalId: 'paper-1', approve: true }),
      expect.objectContaining({ proposalId: 'ref-1', approve: true }),
      expect.objectContaining({ proposalId: 'ref-2', approve: true }),
    ]);
  });
});