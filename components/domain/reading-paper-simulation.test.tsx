import { fireEvent, render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { ReadingPaperSimulation } from './reading-paper-simulation';
import type { ReadingLearnerStructureDto } from '@/lib/reading-authoring-api';

const { mockFetchAuthorizedObjectUrl, mockOpen, mockPopupAssign, mockPopupClose } = vi.hoisted(() => ({
  mockFetchAuthorizedObjectUrl: vi.fn(),
  mockOpen: vi.fn(),
  mockPopupAssign: vi.fn(),
  mockPopupClose: vi.fn(),
}));

vi.mock('@/lib/api', () => ({
  fetchAuthorizedObjectUrl: mockFetchAuthorizedObjectUrl,
}));

const baseNow = Date.parse('2026-05-12T10:00:00.000Z');
const partADeadlineAt = new Date(baseNow + 15 * 60_000).toISOString();
const partBCDeadlineAt = new Date(baseNow + 60 * 60_000).toISOString();

describe('ReadingPaperSimulation', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    vi.stubGlobal('print', vi.fn());
    vi.stubGlobal('open', mockOpen);
    mockOpen.mockReturnValue({
      closed: false,
      close: mockPopupClose,
      location: { assign: mockPopupAssign },
      opener: null,
    });
    mockFetchAuthorizedObjectUrl.mockResolvedValue('blob:paper-a');
  });

  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it('renders Part A booklet, answer sheet, original PDF, print, and annotation controls', async () => {
    const user = userEvent.setup();
    const onAnswerChange = vi.fn();

    render(
      <ReadingPaperSimulation
        structure={buildStructure()}
        answers={{}}
        partADeadlineAt={partADeadlineAt}
        partBCDeadlineAt={partBCDeadlineAt}
        nowMs={baseNow + 5 * 60_000}
        locked={false}
        questionPaperAssets={[
          { id: 'asset-a', part: 'A', title: 'Part A PDF', downloadPath: '/v1/media/media-a/content' },
          { id: 'asset-bc', part: 'B+C', title: 'Part B+C PDF', downloadPath: '/v1/media/media-bc/content' },
          { id: 'asset-c', part: 'C', title: 'Part C PDF', downloadPath: '/v1/media/media-c/content' },
        ]}
        onAnswerChange={onAnswerChange}
      />,
    );

    expect(screen.getByRole('timer', { name: /part a wall timer, 10:00 remaining/i })).toBeInTheDocument();
    expect(screen.getByRole('heading', { name: /part a text booklet/i })).toBeInTheDocument();
    expect(screen.getByRole('heading', { name: /part a answer sheet/i })).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: /pdf a/i }));
    expect(mockFetchAuthorizedObjectUrl).toHaveBeenCalledWith('/v1/media/media-a/content');
    expect(mockOpen).toHaveBeenCalledWith('about:blank', '_blank');
    expect(mockPopupAssign).toHaveBeenCalledWith('blob:paper-a');
    expect(screen.getByRole('button', { name: /pdf b\+c/i })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /pdf c/i })).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: /highlighter/i }));
    expect(screen.getByRole('button', { name: /highlighter/i })).toHaveAttribute('aria-pressed', 'true');

    fireEvent.change(screen.getByRole('textbox'), { target: { value: 'aspirin' } });
    expect(onAnswerChange).toHaveBeenLastCalledWith(expect.objectContaining({ id: 'q-a-1' }), 'aspirin');

    await user.click(screen.getByRole('button', { name: /print paper view/i }));
    expect(window.print).toHaveBeenCalledTimes(1);
  });

  it('collects Part A and switches to the B/C booklet after the first deadline', () => {
    render(
      <ReadingPaperSimulation
        structure={buildStructure()}
        answers={{}}
        partADeadlineAt={partADeadlineAt}
        partBCDeadlineAt={partBCDeadlineAt}
        nowMs={baseNow + 20 * 60_000}
        locked={false}
        onAnswerChange={vi.fn()}
      />,
    );

    expect(screen.queryByRole('heading', { name: /part a answer sheet/i })).not.toBeInTheDocument();
    expect(screen.getByRole('heading', { name: /parts b and c combined booklet/i })).toBeInTheDocument();
    expect(screen.getByRole('timer', { name: /b\/c wall timer, 40:00 remaining/i })).toBeInTheDocument();
  });

  it('sanitizes Part B and C passage HTML in paper view', async () => {
    const user = userEvent.setup();
    const structure = buildStructure();
    structure.parts[1].texts[0].bodyHtml = '<p>Policy extract.</p><img src="x" onerror="alert(1)"><script>alert(2)</script>';
    structure.parts[2].texts[0].bodyHtml = '<p>Journal extract.</p><a href="javascript:alert(3)">unsafe</a>';

    const { container } = render(
      <ReadingPaperSimulation
        structure={structure}
        answers={{}}
        partADeadlineAt={partADeadlineAt}
        partBCDeadlineAt={partBCDeadlineAt}
        nowMs={baseNow + 20 * 60_000}
        locked={false}
        onAnswerChange={vi.fn()}
      />,
    );

    expect(screen.getByText('Policy extract.')).toBeInTheDocument();
    expect(container.innerHTML).not.toContain('<script');
    expect(container.innerHTML).not.toContain('onerror');
    expect(container.innerHTML).not.toContain('javascript:');

    await user.click(screen.getByRole('button', { name: /next/i }));

    expect(screen.getByText('Journal extract.')).toBeInTheDocument();
    expect(container.innerHTML).not.toContain('<script');
    expect(container.innerHTML).not.toContain('onerror');
    expect(container.innerHTML).not.toContain('javascript:');
  });

  it('pairs each Part B extract with its own question and stacks Part C questions beside the passage', async () => {
    const user = userEvent.setup();
    const structure = buildStructure();
    // Part B: two short extracts, each with its own single 3-option question.
    structure.parts[1].texts = [
      { id: 'text-b-1', displayOrder: 1, title: 'Extract B1', source: 'Policy', bodyHtml: '<p>Hand hygiene policy.</p>', wordCount: 3, topicTag: null },
      { id: 'text-b-2', displayOrder: 2, title: 'Extract B2', source: 'Notice', bodyHtml: '<p>Fire safety notice.</p>', wordCount: 3, topicTag: null },
    ];
    structure.parts[1].questions = [
      { id: 'q-b-1', readingTextId: 'text-b-1', displayOrder: 21, points: 1, questionType: 'MultipleChoice3', stem: 'B1 purpose?', options: ['A', 'B', 'C'] },
      { id: 'q-b-2', readingTextId: 'text-b-2', displayOrder: 22, points: 1, questionType: 'MultipleChoice3', stem: 'B2 purpose?', options: ['A', 'B', 'C'] },
    ];
    // Part C: one long passage with two four-option questions stacked beside it.
    structure.parts[2].questions = [
      { id: 'q-c-1', readingTextId: 'text-c-1', displayOrder: 27, points: 1, questionType: 'MultipleChoice4', stem: 'C inference one?', options: ['A', 'B', 'C', 'D'] },
      { id: 'q-c-2', readingTextId: 'text-c-1', displayOrder: 28, points: 1, questionType: 'MultipleChoice4', stem: 'C inference two?', options: ['A', 'B', 'C', 'D'] },
    ];

    render(
      <ReadingPaperSimulation
        structure={structure}
        answers={{}}
        partADeadlineAt={partADeadlineAt}
        partBCDeadlineAt={partBCDeadlineAt}
        nowMs={baseNow + 20 * 60_000}
        locked={false}
        onAnswerChange={vi.fn()}
      />,
    );

    // Part B page shows both extracts and both of their questions on one scroll.
    expect(screen.getByText('Hand hygiene policy.')).toBeInTheDocument();
    expect(screen.getByText('Fire safety notice.')).toBeInTheDocument();
    expect(screen.getByText('B1 purpose?')).toBeInTheDocument();
    expect(screen.getByText('B2 purpose?')).toBeInTheDocument();
    // Page indicator: Part B is a single page, Part C one page per text → 1/2.
    expect(screen.getByText('1/2')).toBeInTheDocument();

    // Advance to the Part C page: passage plus its two stacked questions.
    await user.click(screen.getByRole('button', { name: /next/i }));
    expect(screen.getByText('Journal extract.')).toBeInTheDocument();
    expect(screen.getByText('C inference one?')).toBeInTheDocument();
    expect(screen.getByText('C inference two?')).toBeInTheDocument();
  });
});

function buildStructure(): ReadingLearnerStructureDto {
  return {
    paper: {
      id: 'paper-1',
      title: 'Reading Sample Paper 1',
      slug: 'reading-sample-paper-1',
      subtestCode: 'reading',
      allowPaperReadingMode: true,
    },
    parts: [
      {
        id: 'part-a',
        partCode: 'A',
        timeLimitMinutes: 15,
        maxRawScore: 1,
        instructions: null,
        texts: [
          { id: 'text-a-1', displayOrder: 1, title: 'Text A', source: 'Clinic', bodyHtml: '<p>Use aspirin carefully.</p>', wordCount: 4, topicTag: null },
        ],
        questions: [
          { id: 'q-a-1', readingTextId: 'text-a-1', displayOrder: 1, points: 1, questionType: 'ShortAnswer', stem: 'Name the medication.', options: [] },
        ],
      },
      {
        id: 'part-b',
        partCode: 'B',
        timeLimitMinutes: 45,
        maxRawScore: 1,
        instructions: null,
        texts: [
          { id: 'text-b-1', displayOrder: 1, title: 'Text B', source: 'Policy', bodyHtml: '<p>Policy extract.</p>', wordCount: 2, topicTag: null },
        ],
        questions: [
          { id: 'q-b-1', readingTextId: 'text-b-1', displayOrder: 21, points: 1, questionType: 'MultipleChoice3', stem: 'What is the policy purpose?', options: ['A', 'B', 'C'] },
        ],
      },
      {
        id: 'part-c',
        partCode: 'C',
        timeLimitMinutes: 45,
        maxRawScore: 1,
        instructions: null,
        texts: [
          { id: 'text-c-1', displayOrder: 1, title: 'Text C', source: 'Journal', bodyHtml: '<p>Journal extract.</p>', wordCount: 2, topicTag: null },
        ],
        questions: [
          { id: 'q-c-1', readingTextId: 'text-c-1', displayOrder: 27, points: 1, questionType: 'MultipleChoice4', stem: 'What can be inferred?', options: ['A', 'B', 'C', 'D'] },
        ],
      },
    ],
  };
}
