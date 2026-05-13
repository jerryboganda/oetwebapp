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
}): ReadingAttemptReviewDto {
  const maxRawScore = opts.maxRawScore ?? 42;
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
      showCorrectAnswerOnReview: true,
      showExplanationsAfterSubmit: true,
      showExplanationsOnlyIfWrong: false,
    },
    items: [
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
        correctAnswer: 'aspirin',
        explanationMarkdown: 'Copied from Text A.',
      },
    ],
    clusters: [],
    partBreakdown: [
      { partCode: 'A', rawScore: opts.rawScore, maxRawScore, correctCount: opts.rawScore, incorrectCount: 0, unansweredCount: 0 },
    ],
    skillBreakdown: [
      { label: 'detail', correctCount: opts.rawScore, incorrectCount: 0, unansweredCount: 0, totalCount: maxRawScore },
    ],
  };
}
