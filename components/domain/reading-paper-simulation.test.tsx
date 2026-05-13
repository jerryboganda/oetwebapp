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
