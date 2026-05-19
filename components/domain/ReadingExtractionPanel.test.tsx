import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { ReadingExtractionPanel } from './ReadingExtractionPanel';
import type {
  ReadingExtractionDraftDto,
  ReadingPartCode,
  ReadingStructureAdminDto,
  ReadingStructureManifestDto,
} from '@/lib/reading-authoring-api';

const {
  mockApproveReadingExtractionDraft,
  mockGetReadingStructureAdmin,
  mockListReadingExtractionDrafts,
  mockProposeReadingStructure,
  mockRejectReadingExtractionDraft,
} = vi.hoisted(() => ({
  mockApproveReadingExtractionDraft: vi.fn(),
  mockGetReadingStructureAdmin: vi.fn(),
  mockListReadingExtractionDrafts: vi.fn(),
  mockProposeReadingStructure: vi.fn(),
  mockRejectReadingExtractionDraft: vi.fn(),
}));

vi.mock('@/lib/reading-authoring-api', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@/lib/reading-authoring-api')>();
  return {
    ...actual,
    approveReadingExtractionDraft: mockApproveReadingExtractionDraft,
    getReadingStructureAdmin: mockGetReadingStructureAdmin,
    listReadingExtractionDrafts: mockListReadingExtractionDrafts,
    proposeReadingStructure: mockProposeReadingStructure,
    rejectReadingExtractionDraft: mockRejectReadingExtractionDraft,
  };
});

function buildManifest(): ReadingStructureManifestDto {
  const counts: Record<ReadingPartCode, number> = { A: 20, B: 6, C: 16 };
  return {
    parts: (Object.keys(counts) as ReadingPartCode[]).map((partCode) => ({
      partCode,
      timeLimitMinutes: partCode === 'A' ? 15 : 45,
      instructions: `Part ${partCode} instructions`,
      texts: [
        {
          displayOrder: 1,
          title: `Part ${partCode} text`,
          source: null,
          bodyHtml: `<p>Part ${partCode} body</p>`,
          wordCount: 120,
          topicTag: null,
        },
      ],
      questions: Array.from({ length: counts[partCode] }, (_, index) => ({
        displayOrder: index + 1,
        points: 1,
        questionType: partCode === 'A' ? 'ShortAnswer' : 'MultipleChoice3',
        stem: `Part ${partCode} question ${index + 1}`,
        optionsJson: partCode === 'A' ? '[]' : '["A","B","C"]',
        correctAnswerJson: partCode === 'A' ? '"answer"' : '"A"',
        acceptedSynonymsJson: null,
        caseSensitive: false,
        explanationMarkdown: null,
        skillTag: null,
        readingTextDisplayOrder: 1,
        optionDistractorsJson: null,
        reviewState: 'Draft',
      })),
    })),
  };
}

function buildStructure(manifest = buildManifest()): ReadingStructureAdminDto {
  return {
    paperId: 'paper-1',
    parts: manifest.parts.map((part) => ({
      id: `part-${part.partCode}`,
      partCode: part.partCode,
      timeLimitMinutes: part.timeLimitMinutes ?? 0,
      maxRawScore: part.questions.length,
      instructions: part.instructions,
      texts: part.texts.map((text) => ({
        id: `text-${part.partCode}-${text.displayOrder}`,
        readingPartId: `part-${part.partCode}`,
        ...text,
      })),
      questions: part.questions.map((question) => ({
        id: `q-${part.partCode}-${question.displayOrder}`,
        readingPartId: `part-${part.partCode}`,
        readingTextId: `text-${part.partCode}-1`,
        ...question,
      })),
    })),
  };
}

function buildDraft(overrides: Partial<ReadingExtractionDraftDto> = {}): ReadingExtractionDraftDto {
  return {
    id: 'draft-1',
    paperId: 'paper-1',
    mediaAssetId: null,
    status: 'Pending',
    manifest: buildManifest(),
    rawAiResponseJson: null,
    isStub: false,
    notes: null,
    createdByAdminId: 'admin-1',
    resolvedByAdminId: null,
    createdAt: new Date('2026-05-07T12:00:00Z').toISOString(),
    resolvedAt: null,
    ...overrides,
  };
}

describe('ReadingExtractionPanel', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mockGetReadingStructureAdmin.mockResolvedValue(buildStructure());
    mockListReadingExtractionDrafts.mockResolvedValue([buildDraft()]);
  });

  it('renders Reading draft counts and status', async () => {
    render(<ReadingExtractionPanel paperId="paper-1" />);

    expect(await screen.findByText('Pending')).toBeInTheDocument();
    expect(screen.getByText(/Current: 42\/42 items/i)).toBeInTheDocument();
    expect(screen.getByText(/Draft: 42\/42 items/i)).toBeInTheDocument();
    expect(screen.getAllByText(/A 20 · B 6 · C 16/i)).toHaveLength(2);
  });

  it('approves a non-stub draft and invokes onApplied', async () => {
    const user = userEvent.setup();
    const onApplied = vi.fn();
    mockApproveReadingExtractionDraft.mockResolvedValue(buildDraft({ status: 'Approved' }));

    render(<ReadingExtractionPanel paperId="paper-1" onApplied={onApplied} />);
    await screen.findByText('Pending');
    await user.click(screen.getByRole('button', { name: /approve & apply/i }));

    expect(mockApproveReadingExtractionDraft).toHaveBeenCalledWith('paper-1', 'draft-1');
    expect(onApplied).toHaveBeenCalledTimes(1);
  });

  it('blocks approving stub drafts but allows rejection with a reason', async () => {
    const user = userEvent.setup();
    mockListReadingExtractionDrafts.mockResolvedValue([
      buildDraft({ isStub: true, notes: 'No extracted Reading text.' }),
    ]);
    mockRejectReadingExtractionDraft.mockResolvedValue(buildDraft({ status: 'Rejected' }));

    render(<ReadingExtractionPanel paperId="paper-1" />);
    expect(await screen.findByText('Stub - Pending')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /approve & apply/i })).toBeDisabled();
    expect(screen.getByRole('button', { name: /^reject$/i })).toBeDisabled();

    await user.type(screen.getByLabelText(/rejection reason/i), 'wrong extracted structure');
    await user.click(screen.getByRole('button', { name: /^reject$/i }));

    expect(mockRejectReadingExtractionDraft).toHaveBeenCalledWith('paper-1', 'draft-1', 'wrong extracted structure');
  });

  it('creates a new extraction draft from the propose action', async () => {
    const user = userEvent.setup();
    mockProposeReadingStructure.mockResolvedValue(buildDraft());

    render(<ReadingExtractionPanel paperId="paper-1" />);
    await screen.findByText('Pending');
    await user.click(await screen.findByRole('button', { name: /propose with ai/i }));

    await waitFor(() => expect(mockProposeReadingStructure).toHaveBeenCalledWith('paper-1'));
    expect(mockListReadingExtractionDrafts.mock.calls.length).toBeGreaterThanOrEqual(2);
  });
});
