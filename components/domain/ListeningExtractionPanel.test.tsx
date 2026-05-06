import { render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { ListeningExtractionPanel } from './ListeningExtractionPanel';
import type {
  ListeningAuthoredQuestion,
  ListeningExtractionDraftDto,
} from '@/lib/listening-authoring-api';

const {
  mockListListeningExtractionDrafts,
  mockGetListeningStructure,
  mockProposeListeningStructure,
  mockApproveListeningExtractionDraft,
  mockRejectListeningExtractionDraft,
  mockGetListeningExtractionDraft,
} = vi.hoisted(() => ({
  mockListListeningExtractionDrafts: vi.fn(),
  mockGetListeningStructure: vi.fn(),
  mockProposeListeningStructure: vi.fn(),
  mockApproveListeningExtractionDraft: vi.fn(),
  mockRejectListeningExtractionDraft: vi.fn(),
  mockGetListeningExtractionDraft: vi.fn(),
}));

vi.mock('@/lib/listening-authoring-api', () => ({
  listListeningExtractionDrafts: mockListListeningExtractionDrafts,
  getListeningStructure: mockGetListeningStructure,
  proposeListeningStructure: mockProposeListeningStructure,
  approveListeningExtractionDraft: mockApproveListeningExtractionDraft,
  rejectListeningExtractionDraft: mockRejectListeningExtractionDraft,
  getListeningExtractionDraft: mockGetListeningExtractionDraft,
}));

function buildQuestion(n: number, overrides: Partial<ListeningAuthoredQuestion> = {}): ListeningAuthoredQuestion {
  const partCode: ListeningAuthoredQuestion['partCode'] =
    n <= 12 ? 'A1' : n <= 24 ? 'A2' : n <= 30 ? 'B' : n <= 36 ? 'C1' : 'C2';
  const type: ListeningAuthoredQuestion['type'] = n <= 24 ? 'short_answer' : 'multiple_choice_3';
  return {
    id: `q-${n}`,
    number: n,
    partCode,
    type,
    stem: `Authored stem ${n}`,
    options: type === 'multiple_choice_3' ? ['A', 'B', 'C'] : [],
    correctAnswer: `ans-${n}`,
    acceptedAnswers: [],
    explanation: null,
    skillTag: null,
    transcriptExcerpt: null,
    distractorExplanation: null,
    points: 1,
    ...overrides,
  };
}

function buildPendingDraft(overrides: Partial<ListeningExtractionDraftDto> = {}): ListeningExtractionDraftDto {
  // Diff fixture: question 1 is unchanged, question 2 has a different stem
  // (changed → green), question 43 doesn't exist on the right (removed not
  // applicable here, instead we simulate "added" by reusing q43 only on the
  // proposal side via the `extra` override).
  const proposed: ListeningAuthoredQuestion[] = [];
  for (let i = 1; i <= 42; i++) {
    if (i === 2) {
      proposed.push(buildQuestion(i, { stem: 'AI-rewritten stem 2', correctAnswer: 'new-ans-2' }));
    } else {
      proposed.push(buildQuestion(i));
    }
  }
  return {
    id: 'draft-1',
    paperId: 'paper-1',
    status: 'Pending',
    proposedAt: new Date('2026-05-06T12:00:00Z').toISOString(),
    proposedByUserId: 'admin-1',
    isStub: false,
    stubReason: null,
    summary: 'AI proposal across 42 items.',
    questions: proposed,
    rawAiResponseJson: null,
    decidedAt: null,
    decidedByUserId: null,
    decisionReason: null,
    ...overrides,
  };
}

describe('ListeningExtractionPanel', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mockGetListeningStructure.mockResolvedValue({
      questions: Array.from({ length: 42 }, (_, i) => buildQuestion(i + 1)),
      counts: { partACount: 24, partBCount: 6, partCCount: 12, totalItems: 42 },
    });
    mockListListeningExtractionDrafts.mockImplementation(async (_paperId: string, status?: string) => {
      if (status === 'Pending') return [buildPendingDraft()];
      return [];
    });
  });

  it('renders Pending draft with status badge and highlights changed rows', async () => {
    render(<ListeningExtractionPanel paperId="paper-1" />);

    expect(await screen.findByText('Pending')).toBeInTheDocument();
    // Changed row (q2) -> green tone, kind=changed
    const changedRow = await screen.findByTestId('diff-row-2');
    expect(changedRow).toHaveAttribute('data-diff-kind', 'changed');
    // Unchanged row (q1) -> kind=unchanged
    const unchanged = await screen.findByTestId('diff-row-1');
    expect(unchanged).toHaveAttribute('data-diff-kind', 'unchanged');
    // Right-side stem reflects the AI-rewritten value.
    expect(within(changedRow).getByText('AI-rewritten stem 2')).toBeInTheDocument();
  });

  it('approves a draft after confirmation and invokes onApplied', async () => {
    const user = userEvent.setup();
    const onApplied = vi.fn();
    mockApproveListeningExtractionDraft.mockResolvedValue(buildPendingDraft({ status: 'Approved' }));

    render(<ListeningExtractionPanel paperId="paper-1" onApplied={onApplied} />);
    await screen.findByText('Pending');

    await user.click(screen.getByRole('button', { name: /approve & apply/i }));
    // Confirm modal — second "Approve & apply" inside the dialog.
    const dialogs = await screen.findAllByRole('button', { name: /approve & apply/i });
    await user.click(dialogs[dialogs.length - 1]);

    expect(mockApproveListeningExtractionDraft).toHaveBeenCalledWith('paper-1', 'draft-1', '');
    expect(onApplied).toHaveBeenCalledTimes(1);
  });

  it('reject confirm is disabled until reason provided, then calls reject client', async () => {
    const user = userEvent.setup();
    mockRejectListeningExtractionDraft.mockResolvedValue(
      buildPendingDraft({ status: 'Rejected', decisionReason: 'wrong stems' }),
    );

    render(<ListeningExtractionPanel paperId="paper-1" />);
    await screen.findByText('Pending');

    await user.click(screen.getByRole('button', { name: /^reject$/i }));
    const confirmBtn = await screen.findByRole('button', { name: /confirm reject/i });
    expect(confirmBtn).toBeDisabled();

    const textarea = screen.getByLabelText(/rejection reason/i);
    await user.type(textarea, 'wrong stems');
    expect(screen.getByRole('button', { name: /confirm reject/i })).toBeEnabled();

    await user.click(screen.getByRole('button', { name: /confirm reject/i }));
    expect(mockRejectListeningExtractionDraft).toHaveBeenCalledWith('paper-1', 'draft-1', 'wrong stems');
  });

  it('disables approval for pending stub drafts while allowing rejection', async () => {
    mockListListeningExtractionDrafts.mockImplementation(async (_paperId: string, status?: string) => {
      if (status === 'Pending') return [buildPendingDraft({ isStub: true, stubReason: 'No extracted text.' })];
      return [];
    });

    render(<ListeningExtractionPanel paperId="paper-1" />);

    expect(await screen.findByText('Stub · Pending')).toBeInTheDocument();
    expect(screen.getByText(/Stub proposals cannot be approved/i)).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /approve & apply/i })).toBeDisabled();
    expect(screen.getByRole('button', { name: /^reject$/i })).toBeEnabled();
  });

  it('disables approval when the current authored structure cannot be loaded', async () => {
    mockGetListeningStructure.mockRejectedValueOnce(new Error('offline'));

    render(<ListeningExtractionPanel paperId="paper-1" />);

    expect(await screen.findByText(/Current structure could not be loaded/i)).toBeInTheDocument();
    expect(screen.getByText(/Approval is disabled/i)).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /approve & apply/i })).toBeDisabled();
  });

  it('surfaces a 409 conflict from approve as an error toast and refreshes drafts', async () => {
    const user = userEvent.setup();
    const conflict: Error & { status?: number; detail?: { error?: string } } = Object.assign(
      new Error('HTTP 409'),
      { status: 409, detail: { error: 'Draft is already Approved and cannot be approved.' } },
    );
    mockApproveListeningExtractionDraft.mockRejectedValueOnce(conflict);

    render(<ListeningExtractionPanel paperId="paper-1" />);
    await screen.findByText('Pending');

    await user.click(screen.getByRole('button', { name: /approve & apply/i }));
    const dialogs = await screen.findAllByRole('button', { name: /approve & apply/i });
    await user.click(dialogs[dialogs.length - 1]);

    expect(await screen.findByText(/already approved/i)).toBeInTheDocument();
    // A second list refresh runs after the 409 (one on mount + one after the conflict).
    expect(mockListListeningExtractionDrafts.mock.calls.length).toBeGreaterThanOrEqual(2);
  });
});
