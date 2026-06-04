import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

const {
  mockEnsureCanonicalParts,
  mockGetReadingStructureAdmin,
  mockUpsertReadingQuestion,
  mockValidateReadingPaper,
} = vi.hoisted(() => ({
  mockEnsureCanonicalParts: vi.fn(),
  mockGetReadingStructureAdmin: vi.fn(),
  mockUpsertReadingQuestion: vi.fn(),
  mockValidateReadingPaper: vi.fn(),
}));

vi.mock('@/components/domain/admin-route-surface', () => ({
  AdminRoutePanel: ({ children }: { children: React.ReactNode }) => <div>{children}</div>,
}));

vi.mock('@/components/ui/modal', () => ({
  Modal: ({ open, title, children }: { open: boolean; title: string; children: React.ReactNode }) => (
    open ? <section role="dialog" aria-label={title}>{children}</section> : null
  ),
}));

vi.mock('@/components/ui/alert', () => ({
  InlineAlert: ({ children }: { children: React.ReactNode }) => <div>{children}</div>,
}));

vi.mock('@/components/ui/skeleton', () => ({
  Skeleton: () => <div />,
}));

vi.mock('@/lib/reading-authoring-api', () => ({
  ensureCanonicalParts: mockEnsureCanonicalParts,
  exportReadingStructureManifest: vi.fn(),
  getReadingStructureAdmin: mockGetReadingStructureAdmin,
  importReadingStructureManifest: vi.fn(),
  removeReadingQuestion: vi.fn(),
  removeReadingText: vi.fn(),
  reorderReadingQuestions: vi.fn(),
  reorderReadingTexts: vi.fn(),
  getReadingQuestionReviewHistory: vi.fn(),
  transitionReadingQuestionReviewState: vi.fn(),
  upsertReadingQuestion: mockUpsertReadingQuestion,
  upsertReadingText: vi.fn(),
  validateReadingPaper: mockValidateReadingPaper,
}));

import { ReadingStructureEditor } from './ReadingStructureEditor';

describe('ReadingStructureEditor', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mockEnsureCanonicalParts.mockResolvedValue(undefined);
    mockValidateReadingPaper.mockResolvedValue({
      isPublishReady: true,
      issues: [],
      counts: { partACount: 20, partBCount: 6, partCCount: 16, totalPoints: 42 },
    });
    mockGetReadingStructureAdmin.mockResolvedValue({
      paperId: 'paper-1',
      parts: [
        {
          id: 'part-a',
          partCode: 'A',
          timeLimitMinutes: 15,
          maxRawScore: 20,
          instructions: null,
          texts: [],
          questions: [],
          sections: [],
        },
      ],
    });
  });

  it('removes the authoring-only metadata controls and clears legacy fields on save', async () => {
    const user = userEvent.setup();
    render(<ReadingStructureEditor paperId="paper-1" />);

    await user.click(await screen.findByRole('button', { name: /add question/i }));

    expect(screen.queryByLabelText(/skill tag/i)).not.toBeInTheDocument();
    expect(screen.queryByText(/distractor metadata/i)).not.toBeInTheDocument();

    await user.type(screen.getByLabelText(/stem/i), 'A new reading question');
    await user.click(screen.getByRole('button', { name: /^save$/i }));

    expect(mockUpsertReadingQuestion).toHaveBeenCalledWith('paper-1', expect.objectContaining({
      skillTag: null,
    }));
  });
});
