import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

const {
  mockAttachPaperAsset,
  mockGetContentPaper,
  mockRemovePaperAsset,
  mockUploadFileChunked,
  mockValidateReadingPaper,
} = vi.hoisted(() => ({
  mockAttachPaperAsset: vi.fn(),
  mockGetContentPaper: vi.fn(),
  mockRemovePaperAsset: vi.fn(),
  mockUploadFileChunked: vi.fn(),
  mockValidateReadingPaper: vi.fn(),
}));

vi.mock('next/navigation', () => ({
  useParams: () => ({ paperId: 'paper-1' }),
}));

vi.mock('@/components/admin/layout/admin-settings-layout', () => ({
  AdminSettingsLayout: ({ children }: { children: React.ReactNode }) => <main>{children}</main>,
  SettingsSection: ({ title, description, children }: { title: string; description?: string; children: React.ReactNode }) => (
    <section aria-label={title}>
      <h2>{title}</h2>
      {description ? <p>{description}</p> : null}
      {children}
    </section>
  ),
}));

vi.mock('@/components/domain/admin/reading/ReadingWizardSteps', () => ({
  ReadingWizardSteps: () => <nav aria-label="Reading authoring steps" />,
}));

vi.mock('@/components/ui/alert', () => ({
  InlineAlert: ({ children, variant }: { children: React.ReactNode; variant?: string }) => <div role="status" data-variant={variant}>{children}</div>,
  Toast: ({ message, variant }: { message: string; variant?: string }) => <div role="alert" data-variant={variant}>{message}</div>,
}));

vi.mock('@/lib/content-upload-api', () => ({
  attachPaperAsset: mockAttachPaperAsset,
  getContentPaper: mockGetContentPaper,
  removePaperAsset: mockRemovePaperAsset,
  uploadFileChunked: mockUploadFileChunked,
}));

vi.mock('@/lib/reading-authoring-api', () => ({
  validateReadingPaper: mockValidateReadingPaper,
}));

import ReadingPdfAssetsPage from './page';

const partAAsset = {
  id: 'asset-a',
  role: 'QuestionPaper',
  part: 'A',
  mediaAssetId: 'media-a',
  title: 'Part A PDF',
  displayOrder: 1,
  isPrimary: true,
  createdAt: '2026-06-01T00:00:00Z',
  media: {
    id: 'media-a',
    originalFilename: 'part-a.pdf',
    mimeType: 'application/pdf',
    format: 'pdf',
    sizeBytes: 1024,
    durationSeconds: null,
    sha256: 'abcdef123456',
    mediaKind: 'Document',
    uploadedAt: '2026-06-01T00:00:00Z',
  },
};

function mockPaper() {
  mockGetContentPaper.mockResolvedValue({
    id: 'paper-1',
    subtestCode: 'reading',
    title: 'Reading Sample',
    slug: 'reading-sample',
    professionId: null,
    appliesToAllProfessions: true,
    difficulty: 'standard',
    estimatedDurationMinutes: 60,
    status: 'Draft',
    publishedRevisionId: null,
    cardType: null,
    letterType: null,
    priority: 0,
    tagsCsv: '',
    sourceProvenance: 'Owned source',
    createdAt: '2026-06-01T00:00:00Z',
    updatedAt: '2026-06-01T00:00:00Z',
    publishedAt: null,
    archivedAt: null,
    integrityAcknowledgedByAdminId: null,
    integrityAcknowledgedAt: null,
    assets: [partAAsset],
  });
  mockValidateReadingPaper.mockResolvedValue({
    isPublishReady: false,
    issues: [
      { code: 'part_b_pdf_required', severity: 'error', message: 'Part B requires one primary QuestionPaper PDF.', targetId: null },
      { code: 'part_c_pdf_required', severity: 'error', message: 'Part C requires one primary QuestionPaper PDF.', targetId: null },
    ],
    counts: { partACount: 20, partBCount: 6, partCCount: 16, totalPoints: 42 },
  });
}

describe('Reading PDF assets page', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mockPaper();
    mockUploadFileChunked.mockResolvedValue({ mediaAssetId: 'media-b', sha256: 'hash-b', sizeBytes: 2048, deduplicated: false });
    mockAttachPaperAsset.mockResolvedValue({});
    mockRemovePaperAsset.mockResolvedValue(undefined);
  });

  it('renders three required PDF slots and publish readiness status', async () => {
    render(<ReadingPdfAssetsPage />);

    expect(await screen.findByText('part-a.pdf')).toBeInTheDocument();
    expect(screen.getByText('PDF slots ready')).toBeInTheDocument();
    expect(screen.getByText('1/3')).toBeInTheDocument();
    expect(screen.getByText('Part B requires one primary QuestionPaper PDF.')).toBeInTheDocument();
    expect(screen.getByText('Part C requires one primary QuestionPaper PDF.')).toBeInTheDocument();
    // Part A has an asset so it shows "Replace"; Parts B + C + 8 section slots have no asset
    // so they show "Upload PDF / image". 2 required (B, C) + 8 section slots = 10 upload buttons.
    expect(screen.getAllByText('Upload PDF / image').length).toBeGreaterThanOrEqual(2);
    // Part A shows the replace button
    expect(screen.getByText('Replace')).toBeInTheDocument();
  });

  it('uploads a selected PDF as the primary QuestionPaper asset for its part', async () => {
    const user = userEvent.setup();
    render(<ReadingPdfAssetsPage />);

    // The first upload input (label "Upload PDF / image") belongs to Part B (Part A already has an asset)
    const uploadInputs = await screen.findAllByLabelText('Upload PDF / image');
    const file = new File(['%PDF-1.7'], 'part-b.pdf', { type: 'application/pdf' });
    await user.upload(uploadInputs[0], file);

    await waitFor(() => {
      expect(mockUploadFileChunked).toHaveBeenCalledWith(file, 'QuestionPaper', expect.any(Function));
    });
    expect(mockAttachPaperAsset).toHaveBeenCalledWith('paper-1', expect.objectContaining({
      role: 'QuestionPaper',
      mediaAssetId: 'media-b',
      part: 'B',
      title: 'part-b.pdf',
      makePrimary: true,
    }));
  });

  it('rejects non-PDF/image uploads before the chunked upload starts', async () => {
    const user = userEvent.setup({ applyAccept: false });
    render(<ReadingPdfAssetsPage />);

    const uploadInputs = await screen.findAllByLabelText('Upload PDF / image');
    const file = new File(['not a pdf'], 'notes.txt', { type: 'text/plain' });
    await user.upload(uploadInputs[0], file);

    expect(await screen.findByRole('alert')).toHaveTextContent('Upload a PDF or image file for the question paper slot.');
    expect(mockUploadFileChunked).not.toHaveBeenCalled();
    expect(mockAttachPaperAsset).not.toHaveBeenCalled();
  });
});
