import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { act } from 'react';
import type { ReadingLearnerStructureDto } from '@/lib/reading-authoring-api';

const {
  mockClearReadingPaperAnnotations,
  mockCompleteMockSection,
  mockFetchAuthorizedObjectUrl,
  mockGetReadingAttempt,
  mockGetReadingPaperAnnotations,
  mockGetReadingStructureLearner,
  mockPush,
  mockResumeReadingBreak,
  mockSaveReadingAnswer,
  mockSearchParams,
  mockStartReadingAttempt,
  mockSubmitReadingAttempt,
} = vi.hoisted(() => ({
  mockClearReadingPaperAnnotations: vi.fn(),
  mockCompleteMockSection: vi.fn(),
  mockFetchAuthorizedObjectUrl: vi.fn(),
  mockGetReadingAttempt: vi.fn(),
  mockGetReadingPaperAnnotations: vi.fn(),
  mockGetReadingStructureLearner: vi.fn(),
  mockPush: vi.fn(),
  mockResumeReadingBreak: vi.fn(),
  mockSaveReadingAnswer: vi.fn(),
  mockSearchParams: { current: new URLSearchParams() },
  mockStartReadingAttempt: vi.fn(),
  mockSubmitReadingAttempt: vi.fn(),
}));

vi.mock('next/navigation', () => ({
  useRouter: () => ({ push: mockPush }),
  useSearchParams: () => mockSearchParams.current,
}));

vi.mock('@/components/layout', () => ({
  LearnerDashboardShell: ({ children }: { children: React.ReactNode }) => <div data-testid="learner-dashboard-shell">{children}</div>,
}));

vi.mock('@/lib/api', () => ({
  completeMockSection: mockCompleteMockSection,
  fetchAuthorizedObjectUrl: mockFetchAuthorizedObjectUrl,
}));

vi.mock('@/lib/reading-authoring-api', async () => {
  const actual = await vi.importActual<typeof import('@/lib/reading-authoring-api')>('@/lib/reading-authoring-api');
  return {
    ...actual,
    clearReadingPaperAnnotations: mockClearReadingPaperAnnotations,
    getReadingAttempt: mockGetReadingAttempt,
    getReadingPaperAnnotations: mockGetReadingPaperAnnotations,
    getReadingStructureLearner: mockGetReadingStructureLearner,
    resumeReadingBreak: mockResumeReadingBreak,
    saveReadingAnswer: mockSaveReadingAnswer,
    startReadingAttempt: mockStartReadingAttempt,
    submitReadingAttempt: mockSubmitReadingAttempt,
  };
});

import ReadingPaperPlayerPage from './page';

const baseNow = Date.parse('2026-05-12T10:00:00.000Z');
const startedAt = new Date(baseNow).toISOString();
const partADeadlineAt = new Date(baseNow + 15 * 60_000).toISOString();
const partBCDeadlineAt = new Date(baseNow + 60 * 60_000).toISOString();

describe('Reading paper player page', () => {
  beforeEach(() => {
    vi.useFakeTimers({ shouldAdvanceTime: true });
    vi.setSystemTime(baseNow);
    vi.clearAllMocks();
    Object.defineProperty(window, 'open', {
      value: vi.fn(() => ({ closed: false, close: vi.fn(), location: { assign: vi.fn() }, opener: null })),
      configurable: true,
    });
    mockSearchParams.current = new URLSearchParams();
    mockGetReadingStructureLearner.mockResolvedValue(buildStructure());
    mockGetReadingPaperAnnotations.mockResolvedValue([]);
    mockClearReadingPaperAnnotations.mockResolvedValue(undefined);
    mockStartReadingAttempt.mockResolvedValue({
      attemptId: 'attempt-1',
      startedAt,
      deadlineAt: partBCDeadlineAt,
      partADeadlineAt,
      partBCDeadlineAt,
      answeredCount: 0,
      canResume: true,
      paperTitle: 'Reading Sample Paper 1',
      partATimerMinutes: 15,
      partBCTimerMinutes: 45,
      partABreakAvailable: true,
      partABreakResumed: false,
      partBCTimerPausedAt: null,
      partBCPausedSeconds: 0,
      partABreakMaxSeconds: 300,
    });
    mockGetReadingAttempt.mockResolvedValue(buildAttempt());
    mockSaveReadingAnswer.mockResolvedValue(undefined);
    mockSubmitReadingAttempt.mockResolvedValue({
      rawScore: 1,
      maxRawScore: 3,
      scaledScore: 120,
      gradeLetter: 'E',
      correctCount: 1,
      incorrectCount: 0,
      unansweredCount: 2,
      answers: [],
    });
  });

  afterEach(() => {
    vi.useRealTimers();
  });

  it('starts the canonical computer attempt, flushes answers, and navigates to canonical results', async () => {
    const user = userEvent.setup({ advanceTimers: vi.advanceTimersByTime });

    await renderPlayer();

    await user.click(await screen.findByRole('button', { name: /start attempt/i }));
    expect(await screen.findByRole('timer', { name: /part a window/i })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /question 1, unanswered/i })).toBeInTheDocument();

    await user.type(screen.getByPlaceholderText(/type your answer/i), 'aspirin');
    await user.click(screen.getByRole('button', { name: /^flag$/i }));
    expect(screen.getByRole('button', { name: /question 1, answered, flagged/i })).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: /submit attempt for grading/i }));
    expect(await screen.findByRole('dialog', { name: /submit reading attempt/i })).toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: /submit now/i }));

    await waitFor(() => {
      expect(mockSaveReadingAnswer).toHaveBeenCalledWith('attempt-1', 'q-a-1', '"aspirin"', expect.any(Number));
      expect(mockSubmitReadingAttempt).toHaveBeenCalledWith('attempt-1');
      expect(mockPush).toHaveBeenCalledWith('/reading/paper/paper-1/results?attemptId=attempt-1');
    });
  });

  it('renders the Part A PDF viewer with annotation tooling for resumed paper attempts', async () => {
    mockSearchParams.current = new URLSearchParams('presentation=paper&attemptId=attempt-1');

    await renderPlayer();

    // PDF-only rebuild: the Part A passage is delivered as a PDF region with
    // an in-viewer annotation toolbar (no separate "answer sheet" / printed
    // paper-sim surface).
    expect(await screen.findByLabelText(/part a document/i)).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /text highlight/i })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /^marker$/i })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /clear pdf/i })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /clear paper/i })).toBeInTheDocument();
  });

  it('falls back to computer delivery when paper mode is disabled by policy', async () => {
    mockSearchParams.current = new URLSearchParams('presentation=paper&attemptId=attempt-1');
    mockGetReadingStructureLearner.mockResolvedValueOnce(buildStructure({ allowPaperReadingMode: false }));

    await renderPlayer();

    expect(await screen.findByText(/paper simulation is disabled by the current reading policy/i)).toBeInTheDocument();
    expect(screen.queryByLabelText(/paper-based reading simulation/i)).not.toBeInTheDocument();
    expect(screen.getByRole('tabpanel', { name: /part a/i })).toBeInTheDocument();
  });

  it('does not auto-start a full attempt from legacy mode=practice URL hints', async () => {
    mockSearchParams.current = new URLSearchParams('mode=practice&part=A');

    await renderPlayer();

    expect(await screen.findByRole('button', { name: /start attempt/i })).toBeInTheDocument();
    expect(mockStartReadingAttempt).not.toHaveBeenCalled();
  });

  it('saves fallback matching text references as A-D values', async () => {
    const user = userEvent.setup({ advanceTimers: vi.advanceTimersByTime });
    mockGetReadingStructureLearner.mockResolvedValueOnce(buildStructure({ partAMatching: true }));

    await renderPlayer();
    await user.click(await screen.findByRole('button', { name: /start attempt/i }));
    await user.click(await screen.findByRole('button', { name: /text a.*triage extract/i }));

    await act(async () => {
      vi.advanceTimersByTime(450);
      await Promise.resolve();
    });

    await waitFor(() => {
      expect(mockSaveReadingAnswer).toHaveBeenCalledWith('attempt-1', 'q-a-1', '"A"', expect.any(Number));
    });
  });

  it('moves passage highlighting into the PDF viewer and clears paper annotations', async () => {
    const user = userEvent.setup({ advanceTimers: vi.advanceTimersByTime });
    vi.spyOn(window, 'confirm').mockReturnValue(true);

    await renderPlayer();
    await user.click(await screen.findByRole('button', { name: /start attempt/i }));

    // The PDF-only rebuild replaced the old HTML passage-text highlight scope
    // with the PDF viewer's annotation toolbar.
    expect(await screen.findByLabelText(/part a document/i)).toBeInTheDocument();
    expect(document.querySelector('[data-reading-highlight-scope="passage"]')).toBeNull();
    expect(screen.getByRole('button', { name: /text highlight/i })).toBeInTheDocument();

    // Clearing all highlights routes through the annotations API for this paper.
    await user.click(screen.getByRole('button', { name: /clear paper/i }));
    await waitFor(() => {
      expect(mockClearReadingPaperAnnotations).toHaveBeenCalledWith('paper-1', { scope: 'paper' });
    });
  });

  it('exposes screen-reader hints and high-contrast state through the reading a11y controls', async () => {
    const user = userEvent.setup({ advanceTimers: vi.advanceTimersByTime });

    await renderPlayer();
    await user.click(await screen.findByRole('button', { name: /start attempt/i }));

    await user.click(screen.getByRole('button', { name: /accessibility settings/i }));
    await user.click(screen.getByRole('checkbox', { name: /high-contrast palette/i }));
    await user.click(screen.getByRole('checkbox', { name: /extra screen-reader hints/i }));

    const main = screen.getByRole('main');
    expect(main).toHaveAttribute('data-reading-contrast', 'high');
    expect(main).toHaveAttribute('aria-describedby', expect.stringContaining('reading-a11y-hints'));
    expect(screen.getByText(/screen reader hints are enabled/i)).toBeInTheDocument();
  });
});

function buildAttempt() {
  return {
    id: 'attempt-1',
    paperId: 'paper-1',
    status: 'InProgress',
    mode: 'Exam',
    scopeQuestionIds: null,
    startedAt,
    deadlineAt: partBCDeadlineAt,
    submittedAt: null,
    rawScore: null,
    scaledScore: null,
    maxRawScore: 3,
    partADeadlineAt,
    partBCDeadlineAt,
    partABreakAvailable: true,
    partABreakResumed: false,
    partBCTimerPausedAt: null,
    partBCPausedSeconds: 0,
    partABreakMaxSeconds: 300,
    answeredCount: 0,
    totalQuestions: 3,
    canResume: true,
    answers: [],
    showExplanations: false,
  };
}

async function renderPlayer() {
  render(<ReadingPaperPlayerPage params={resolvedParams({ paperId: 'paper-1' })} />);
  await act(async () => {
    await Promise.resolve();
  });
}

function resolvedParams<T>(value: T): Promise<T> {
  const promise = Promise.resolve(value) as Promise<T> & { status: 'fulfilled'; value: T };
  promise.status = 'fulfilled';
  promise.value = value;
  return promise;
}

function buildStructure(opts?: { allowPaperReadingMode?: boolean; partAMatching?: boolean }): ReadingLearnerStructureDto {
  const partATexts = opts?.partAMatching
    ? [
      { id: 'text-a-2', displayOrder: 2, title: 'Medication extract', source: 'Clinic', bodyHtml: '<p>Use aspirin carefully.</p>', wordCount: 4, topicTag: null },
      { id: 'text-a-1', displayOrder: 1, title: 'Triage extract', source: 'Clinic', bodyHtml: '<p>Assess urgent referrals.</p>', wordCount: 3, topicTag: null },
    ]
    : [
      { id: 'text-a-1', displayOrder: 1, title: 'Text A', source: 'Clinic', bodyHtml: '<p>Use aspirin carefully.</p>', wordCount: 4, topicTag: null },
    ];
  const partAQuestions = opts?.partAMatching
    ? [
      { id: 'q-a-1', readingTextId: 'text-a-1', readingSectionId: null, displayOrder: 1, points: 1, questionType: 'MatchingTextReference' as const, stem: 'Which text discusses triage?', options: [] },
    ]
    : [
      { id: 'q-a-1', readingTextId: 'text-a-1', readingSectionId: null, displayOrder: 1, points: 1, questionType: 'ShortAnswer' as const, stem: 'Name the medication.', options: [] },
    ];

  return {
    paper: {
      id: 'paper-1',
      title: 'Reading Sample Paper 1',
      slug: 'reading-sample-paper-1',
      subtestCode: 'reading',
      allowPaperReadingMode: opts?.allowPaperReadingMode ?? true,
      policy: {
        fontScaleUserControl: true,
        highContrastMode: true,
        screenReaderOptimised: true,
      },
      questionPaperAssets: [
        { id: 'asset-a', part: 'A', title: 'Part A PDF', downloadPath: '/v1/media/media-a/content' },
      ],
    },
    parts: [
      {
        id: 'part-a',
        partCode: 'A',
        timeLimitMinutes: 15,
        maxRawScore: 1,
        instructions: null,
        texts: partATexts,
        questions: partAQuestions,
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
          { id: 'q-b-1', readingTextId: 'text-b-1', readingSectionId: null, displayOrder: 21, points: 1, questionType: 'MultipleChoice3', stem: 'What is the policy purpose?', options: ['A', 'B', 'C'] },
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
          { id: 'q-c-1', readingTextId: 'text-c-1', readingSectionId: null, displayOrder: 27, points: 1, questionType: 'MultipleChoice4', stem: 'What can be inferred?', options: ['A', 'B', 'C', 'D'] },
        ],
      },
    ],
  };
}
