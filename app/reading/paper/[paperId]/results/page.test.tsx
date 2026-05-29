import { render, screen } from '@testing-library/react';
import { act } from 'react';
import type { ReadingAttemptReviewDto } from '@/lib/reading-authoring-api';

const {
  mockGetReadingAttemptReview,
  mockSearchParams,
} = vi.hoisted(() => ({
  mockGetReadingAttemptReview: vi.fn(),
  mockSearchParams: { current: new URLSearchParams('attemptId=attempt-1') },
}));

vi.mock('next/navigation', () => ({
  useSearchParams: () => mockSearchParams.current,
}));

vi.mock('@/components/layout', () => ({
  LearnerDashboardShell: ({ children }: { children: React.ReactNode }) => <div data-testid="learner-dashboard-shell">{children}</div>,
}));

vi.mock('@/lib/reading-authoring-api', async () => {
  const actual = await vi.importActual<typeof import('@/lib/reading-authoring-api')>('@/lib/reading-authoring-api');
  return {
    ...actual,
    getReadingAttemptReview: mockGetReadingAttemptReview,
  };
});

import ReadingPaperResultsPage from './page';

describe('Reading paper results page', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mockSearchParams.current = new URLSearchParams('attemptId=attempt-1');
  });

  it('uses the canonical scaled pass helper for the 350 pass anchor', async () => {
    mockGetReadingAttemptReview.mockResolvedValueOnce(buildReview({ scaledScore: 350, rawScore: 30, gradeLetter: 'B' }));

    await renderResults();

    expect(await screen.findByText('Reading pass evidence')).toBeInTheDocument();
    expect(screen.getByText('30/42 raw | 350/500 scaled')).toBeInTheDocument();
  });

  it('uses scaled score rather than raw score for below-anchor evidence', async () => {
    mockGetReadingAttemptReview.mockResolvedValueOnce(buildReview({ scaledScore: 349, rawScore: 30, gradeLetter: 'C+' }));

    await renderResults();

    expect(await screen.findByText('Below Reading pass anchor')).toBeInTheDocument();
    expect(screen.getByText('30/42 raw | 349/500 scaled')).toBeInTheDocument();
  });

  it('keeps subset practice attempts out of OET pass evidence', async () => {
    mockGetReadingAttemptReview.mockResolvedValueOnce(buildReview({ scaledScore: null, rawScore: 6, maxRawScore: 10, gradeLetter: '—' }));

    await renderResults();

    expect(await screen.findByText('Practice-only review')).toBeInTheDocument();
    expect(screen.getByText('6/10 practice marks')).toBeInTheDocument();
  });

  it('renders the spelling miss diagnostic and per-part accuracy percentage from the payload', async () => {
    mockGetReadingAttemptReview.mockResolvedValueOnce(
      buildReview({
        scaledScore: 350,
        rawScore: 30,
        gradeLetter: 'B',
        partAccuracyPercent: 75,
        items: [
          {
            questionId: 'q-a-1',
            partCode: 'A',
            displayOrder: 1,
            questionType: 'ShortAnswer',
            stem: 'Name the medication.',
            skillTag: 'detail',
            userAnswer: 'asprin',
            isCorrect: false,
            pointsEarned: 0,
            maxPoints: 1,
            missReason: 'spelling',
            correctAnswer: 'aspirin',
            explanationMarkdown: 'The accepted spelling is "aspirin".',
            elapsedMs: 42000,
          },
        ],
      }),
    );

    await renderResults();

    expect(await screen.findByTestId('reading-miss-spelling')).toBeInTheDocument();
    expect(screen.getByText('Spelling caused this miss')).toBeInTheDocument();
    expect(screen.getByTestId('reading-part-accuracy-A')).toHaveTextContent('75% correct');
    expect(screen.getByText('aspirin')).toBeInTheDocument();
    expect(screen.getByTestId('reading-explanation')).toBeInTheDocument();
  });

  it('renders tutor feedback entries when the attempt returns them', async () => {
    mockGetReadingAttemptReview.mockResolvedValueOnce(
      buildReview({
        scaledScore: 350,
        rawScore: 30,
        gradeLetter: 'B',
        feedback: [
          {
            id: 'fb-1',
            scope: 'test',
            targetRef: null,
            feedbackText: 'Strong inference work — tighten your Part A scanning speed.',
            createdAt: '2026-05-12T11:05:00.000Z',
            updatedAt: '2026-05-12T11:05:00.000Z',
          },
        ],
      }),
    );

    await renderResults();

    expect(await screen.findByTestId('reading-tutor-feedback')).toBeInTheDocument();
    expect(screen.getByText('Strong inference work — tighten your Part A scanning speed.')).toBeInTheDocument();
    expect(screen.getByText('Whole test')).toBeInTheDocument();
  });
});

async function renderResults() {
  render(<ReadingPaperResultsPage params={resolvedParams({ paperId: 'paper-1' })} />);
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

function buildReview(opts: {
  scaledScore: number | null;
  rawScore: number;
  maxRawScore?: number;
  gradeLetter: string;
  partAccuracyPercent?: number;
  items?: Array<Record<string, unknown>>;
  feedback?: Array<Record<string, unknown>>;
}): ReadingAttemptReviewDto {
  const maxRawScore = opts.maxRawScore ?? 42;
  const items = opts.items ?? [
    {
      questionId: 'q-a-1',
      partCode: 'A',
      displayOrder: 1,
      questionType: 'ShortAnswer',
      stem: 'Name the medication.',
      skillTag: 'detail',
      userAnswer: 'aspirin',
      isCorrect: true,
      pointsEarned: 1,
      maxPoints: 1,
    },
  ];
  return {
    attempt: {
      id: 'attempt-1',
      paperId: 'paper-1',
      status: 'Submitted',
      mode: opts.scaledScore === null ? 'Drill' : 'Exam',
      scopeQuestionIds: opts.scaledScore === null ? ['q-a-1'] : null,
      startedAt: '2026-05-12T10:00:00.000Z',
      submittedAt: '2026-05-12T11:00:00.000Z',
      rawScore: opts.rawScore,
      maxRawScore,
      scaledScore: opts.scaledScore,
      gradeLetter: opts.gradeLetter,
      partADeadlineAt: '2026-05-12T10:15:00.000Z',
      partBCDeadlineAt: '2026-05-12T11:00:00.000Z',
    },
    paper: {
      id: 'paper-1',
      title: 'Reading Sample Paper 1',
      slug: 'reading-sample-paper-1',
      subtestCode: 'reading',
    },
    policy: {
      showCorrectAnswerOnReview: false,
      showExplanationsAfterSubmit: false,
      showExplanationsOnlyIfWrong: false,
    },
    items: items as ReadingAttemptReviewDto['items'],
    clusters: [],
    partBreakdown: [
      {
        partCode: 'A',
        rawScore: opts.rawScore,
        maxRawScore,
        correctCount: opts.rawScore,
        incorrectCount: 0,
        unansweredCount: 0,
        ...(typeof opts.partAccuracyPercent === 'number' ? { accuracyPercent: opts.partAccuracyPercent } : {}),
      } as ReadingAttemptReviewDto['partBreakdown'][number],
    ],
    skillBreakdown: [
      { label: 'detail', correctCount: opts.rawScore, incorrectCount: 0, unansweredCount: 0, totalCount: maxRawScore },
    ],
    ...(opts.feedback ? { feedback: opts.feedback } : {}),
  } as ReadingAttemptReviewDto;
}
