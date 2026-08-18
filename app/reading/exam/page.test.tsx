import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import type { ReadingHomeDto, ReadingHomePaperDto } from '@/lib/reading-authoring-api';

const { mockGetReadingHome, mockPush, mockStartReadingAttempt } = vi.hoisted(() => ({
  mockGetReadingHome: vi.fn(),
  mockPush: vi.fn(),
  mockStartReadingAttempt: vi.fn(),
}));

vi.mock('next/navigation', () => ({
  useRouter: () => ({ push: mockPush }),
}));

vi.mock('@/components/layout', () => ({
  LearnerDashboardShell: ({ children }: { children: React.ReactNode }) => (
    <div data-testid="learner-dashboard-shell">{children}</div>
  ),
}));

vi.mock('@/lib/reading-authoring-api', async () => {
  const actual = await vi.importActual<typeof import('@/lib/reading-authoring-api')>(
    '@/lib/reading-authoring-api',
  );
  return {
    ...actual,
    getReadingHome: mockGetReadingHome,
    startReadingAttempt: mockStartReadingAttempt,
  };
});

import ReadingFullExamPage from './page';

function paper(overrides: Partial<ReadingHomePaperDto>): ReadingHomePaperDto {
  return {
    id: 'paper-1',
    title: 'Jayden Book 01 — Bed Bugs',
    slug: 'jayden-book-01-bed-bugs',
    difficulty: 'standard',
    estimatedDurationMinutes: 60,
    publishedAt: '2026-08-18T00:00:00.000Z',
    route: '/reading/paper/paper-1',
    partACount: 20,
    partBCount: 6,
    partCCount: 16,
    totalPoints: 42,
    partATimerMinutes: 15,
    partBCTimerMinutes: 45,
    entitlement: { allowed: true, reason: 'ok', currentTier: 'pro', requiredScope: 'subtest:reading' },
    lastAttempt: null,
    ...overrides,
  };
}

function home(papers: ReadingHomePaperDto[]): ReadingHomeDto {
  return {
    intro: 'Build Reading accuracy',
    papers,
    activeAttempts: [],
    recentResults: [],
    policy: {
      partATimerMinutes: 15,
      partBCTimerMinutes: 45,
      allowPausingAttempt: false,
      allowResumeAfterExpiry: false,
      showCorrectAnswerOnReview: true,
      showExplanationsAfterSubmit: true,
      allowPaperReadingMode: true,
    },
    safeDrills: [],
  };
}

describe('Reading full exam page', () => {
  beforeEach(() => {
    mockGetReadingHome.mockReset();
    mockPush.mockReset();
    mockStartReadingAttempt.mockReset();
  });

  it('lists published papers under the official book folders instead of mocks', async () => {
    mockGetReadingHome.mockResolvedValue(
      home([
        paper({ id: 'jb1', title: 'Jayden Book 01 — Bed Bugs', slug: 'jayden-book-01-bed-bugs' }),
        paper({
          id: 'legacy',
          title: 'Older sample paper',
          slug: 'legacy-sample-reading',
        }),
      ]),
    );

    render(<ReadingFullExamPage />);

    expect(await screen.findByRole('heading', { name: 'Jayden Book' })).toBeInTheDocument();
    expect(screen.getByRole('heading', { name: 'Anna Hartford' })).toBeInTheDocument();
    expect(screen.getByRole('heading', { name: 'Atlas Practice Series' })).toBeInTheDocument();
    expect(screen.getByRole('heading', { name: 'Nova Practice Series' })).toBeInTheDocument();
    expect(screen.getByRole('heading', { name: 'VERY DIFFICULT READING EXAMS' })).toBeInTheDocument();
    expect(screen.getByText('Jayden Book 01 — Bed Bugs')).toBeInTheDocument();
    expect(screen.getByRole('heading', { name: 'Other papers' })).toBeInTheDocument();
    expect(screen.queryByText(/no mock bundles/i)).not.toBeInTheDocument();
  });

  it('starts a full exam attempt and opens the paper player', async () => {
    mockGetReadingHome.mockResolvedValue(
      home([paper({ id: 'jb1', title: 'Jayden Book 01 — Bed Bugs', slug: 'jayden-book-01-bed-bugs' })]),
    );
    mockStartReadingAttempt.mockResolvedValue({ attemptId: 'att-1' });

    render(<ReadingFullExamPage />);
    fireEvent.click(await screen.findByRole('button', { name: 'Start full exam' }));

    await waitFor(() => {
      expect(mockStartReadingAttempt).toHaveBeenCalledWith('jb1');
      expect(mockPush).toHaveBeenCalledWith('/reading/paper/jb1?attemptId=att-1');
    });
  });
});
