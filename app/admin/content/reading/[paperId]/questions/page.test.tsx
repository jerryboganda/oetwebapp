import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

const {
  mockGetReadingStructureAdmin,
  mockGetReadingStructureAdminPreview,
  mockUpsertReadingQuestion,
} = vi.hoisted(() => ({
  mockGetReadingStructureAdmin: vi.fn(),
  mockGetReadingStructureAdminPreview: vi.fn(),
  mockUpsertReadingQuestion: vi.fn(),
}));

vi.mock('next/navigation', () => ({
  useParams: () => ({ paperId: 'paper-1' }),
}));

vi.mock('next/link', () => ({
  default: ({ children, href, ...rest }: React.AnchorHTMLAttributes<HTMLAnchorElement> & { children: React.ReactNode; href?: string }) => (
    <a href={href} {...rest}>{children}</a>
  ),
}));

vi.mock('@/components/admin/layout/admin-table-layout', () => ({
  AdminTableLayout: ({ children, actions, title }: { children: React.ReactNode; actions?: React.ReactNode; title: string }) => (
    <main>
      <h1>{title}</h1>
      {actions}
      {children}
    </main>
  ),
}));

vi.mock('@/components/admin/ui/card', () => ({
  Card: ({ children }: { children: React.ReactNode }) => <section>{children}</section>,
  CardContent: ({ children }: { children: React.ReactNode }) => <div>{children}</div>,
  CardHeader: ({ children }: { children: React.ReactNode }) => <header>{children}</header>,
  CardTitle: ({ children }: { children: React.ReactNode }) => <h2>{children}</h2>,
  CardDescription: ({ children }: { children: React.ReactNode }) => <p>{children}</p>,
}));

vi.mock('@/components/admin/ui/button', () => ({
  Button: ({ children, startIcon, ...props }: React.ButtonHTMLAttributes<HTMLButtonElement> & { variant?: string; size?: string; loading?: boolean; startIcon?: React.ReactNode }) => (
    <button {...props}>{startIcon}{children}</button>
  ),
}));

vi.mock('@/components/admin/ui/badge', () => ({
  Badge: ({ children }: { children: React.ReactNode }) => <span>{children}</span>,
}));

vi.mock('@/components/admin/ui/skeleton', () => ({
  Skeleton: () => <div />,
}));

vi.mock('@/components/ui/alert', () => ({
  Toast: () => null,
}));

vi.mock('@/components/domain/admin/reading/ReadingWizardSteps', () => ({
  ReadingWizardSteps: () => <nav aria-label="reading-wizard" />,
}));

vi.mock('@/components/domain/admin/reading/ReadingPartTabs', () => ({
  ReadingPartTabs: ({ activeTab, onTabChange }: { activeTab: string; onTabChange: (tab: string) => void }) => (
    <div aria-label="reading-part-tabs">
      {(['A', 'B', 'C'] as const).map((tab) => (
        <button key={tab} type="button" aria-pressed={activeTab === tab} onClick={() => onTabChange(tab)}>
          Part {tab}
        </button>
      ))}
    </div>
  ),
}));

vi.mock('@/components/domain/reading-pdf-viewer', () => ({
  ReadingPdfViewer: ({ partCode }: { partCode: string }) => <div aria-label={`pdf-viewer-${partCode}`} />,
}));

vi.mock('./ReadingAnswerSheetBuilder', () => ({
  ReadingAnswerSheetBuilder: ({ partCode }: { partCode: string }) => <div aria-label={`answer-sheet-${partCode}`} />,
}));

vi.mock('./ReadingReviewPanel', () => ({
  ReadingReviewPanel: () => null,
}));

vi.mock('@/lib/reading-authoring-api', () => ({
  getReadingStructureAdmin: mockGetReadingStructureAdmin,
  getReadingStructureAdminPreview: mockGetReadingStructureAdminPreview,
  upsertReadingQuestion: mockUpsertReadingQuestion,
  removeReadingQuestion: vi.fn(),
  reorderReadingQuestions: vi.fn(),
}));

import ReadingQuestionsEditorPage from './page';

describe('Reading questions editor page', () => {
  beforeEach(() => {
    vi.clearAllMocks();
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
        },
      ],
    });
    mockGetReadingStructureAdminPreview.mockResolvedValue({
      paper: { questionPaperAssets: [] },
      parts: [],
    });
  });

  it('does not render the removed authoring metadata controls', async () => {
    const user = userEvent.setup();
    render(<ReadingQuestionsEditorPage />);

    await user.click(await screen.findByRole('button', { name: /add first question/i }));

    expect(screen.queryByLabelText(/distractor category/i)).not.toBeInTheDocument();
    expect(screen.queryByLabelText(/distractor rationale/i)).not.toBeInTheDocument();
    expect(screen.queryByLabelText(/difficulty \(1–5\)/i)).not.toBeInTheDocument();
    expect(screen.queryByLabelText(/paragraph index/i)).not.toBeInTheDocument();
    expect(screen.queryByLabelText(/skill tag/i)).not.toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: /json/i }));
    const jsonEditor = screen.getByLabelText(/question json/i) as HTMLTextAreaElement;
    expect(jsonEditor.value).not.toMatch(/skillTag|difficulty|paragraphIndex|distractorRationale|optionDistractors/i);
  });

  it('renders the PDF viewer keyed to the active part, then the active section', async () => {
    const user = userEvent.setup();
    mockGetReadingStructureAdmin.mockResolvedValue({
      paperId: 'paper-1',
      parts: [
        { id: 'part-a', partCode: 'A', timeLimitMinutes: 15, maxRawScore: 20, instructions: null, texts: [], questions: [] },
        {
          id: 'part-b',
          partCode: 'B',
          timeLimitMinutes: 45,
          maxRawScore: 6,
          instructions: null,
          texts: [],
          questions: [],
          sections: [
            { id: 'sec-b1', sectionCode: 'B1', displayOrder: 1, maxRawScore: 1, contentPaperAssetId: null, questions: [] },
            { id: 'sec-b2', sectionCode: 'B2', displayOrder: 2, maxRawScore: 1, contentPaperAssetId: null, questions: [] },
          ],
        },
      ],
    });

    render(<ReadingQuestionsEditorPage />);

    // Part A is active by default: the viewer is keyed to slot 'A'.
    expect(await screen.findByLabelText('pdf-viewer-A')).toBeInTheDocument();

    // Switch to Part B: the first section (B1) is active, so the viewer keys to 'B1'.
    await user.click(screen.getByRole('button', { name: /^part b$/i }));
    expect(await screen.findByLabelText('pdf-viewer-B1')).toBeInTheDocument();

    // Selecting section B2 re-keys the viewer to 'B2'.
    await user.click(screen.getByRole('button', { name: /^B2$/ }));
    expect(await screen.findByLabelText('pdf-viewer-B2')).toBeInTheDocument();
  });

  it('clears legacy metadata in the form save payload', async () => {
    const user = userEvent.setup();
    mockUpsertReadingQuestion.mockResolvedValue({ id: 'question-1' });
    render(<ReadingQuestionsEditorPage />);

    await user.click(await screen.findByRole('button', { name: /add first question/i }));
    await user.type(screen.getByLabelText(/stem \(question text\)/i), 'A question stem');
    await user.type(screen.getByLabelText(/correct answer/i), 'oxygen');
    await user.click(screen.getByRole('button', { name: /^save$/i }));

    expect(mockUpsertReadingQuestion).toHaveBeenCalledWith('paper-1', expect.objectContaining({
      skillTag: null,
      difficulty: null,
      paragraphIndex: null,
      distractorRationale: null,
    }));
  });
});
