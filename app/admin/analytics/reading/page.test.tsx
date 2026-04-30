import { screen } from '@testing-library/react';
import { renderWithRouter } from '@/tests/test-utils';

const { mockUseAdminAuth, mockGetReadingAdminAnalytics } = vi.hoisted(() => ({
  mockUseAdminAuth: vi.fn(),
  mockGetReadingAdminAnalytics: vi.fn(),
}));

vi.mock('@/lib/hooks/use-admin-auth', () => ({ useAdminAuth: () => mockUseAdminAuth() }));
vi.mock('@/lib/reading-authoring-api', () => ({
  getReadingAdminAnalytics: (days?: number) => mockGetReadingAdminAnalytics(days),
}));

import ReadingAnalyticsPage from './page';

const analyticsFixture = {
  generatedAt: '2026-04-29T12:00:00Z',
  windowDays: 30,
  summary: {
    totalPapers: 1,
    publishedPapers: 1,
    examReadyPapers: 1,
    authoredQuestions: 42,
    totalAttempts: 3,
    submittedAttempts: 2,
    activeAttempts: 1,
    averageRawScore: 25,
    averageScaledScore: 292,
    passRatePercent: 50,
    unansweredRatePercent: 12.5,
  },
  papers: [
    {
      paperId: 'p1',
      title: 'Reading Sample 1',
      status: 'Published',
      difficulty: 'standard',
      questionCount: 42,
      totalPoints: 42,
      partACount: 20,
      partBCount: 6,
      partCCount: 16,
      isCanonicalShapeComplete: true,
      isExamReady: true,
      attemptCount: 3,
      submittedCount: 2,
      averageRawScore: 25,
      averageScaledScore: 292,
      passRatePercent: 50,
      averageCompletionSeconds: 3300,
    },
  ],
  partBreakdown: [
    { partCode: 'A', questionCount: 20, opportunities: 40, correctCount: 20, unansweredCount: 4, accuracyPercent: 50 },
    { partCode: 'B', questionCount: 6, opportunities: 12, correctCount: 2, unansweredCount: 3, accuracyPercent: 16.7 },
    { partCode: 'C', questionCount: 16, opportunities: 32, correctCount: 10, unansweredCount: 5, accuracyPercent: 31.3 },
  ],
  skillBreakdown: [
    { label: 'Inference', questionCount: 8, opportunities: 16, correctCount: 2, unansweredCount: 4, accuracyPercent: 12.5 },
  ],
  hardestQuestions: [
    {
      paperId: 'p1',
      paperTitle: 'Reading Sample 1',
      questionId: 'q-b-1',
      partCode: 'B',
      label: 'Part B Q1',
      displayOrder: 1,
      questionType: 'MultipleChoice3',
      skillTag: 'Inference',
      stem: 'What should the nurse do first?',
      opportunities: 2,
      answeredCount: 2,
      correctCount: 0,
      unansweredCount: 0,
      accuracyPercent: 0,
    },
  ],
  modeBreakdown: [
    { mode: 'Exam', attemptCount: 3, submittedCount: 2, averageRawScore: 25, averageScaledScore: 292, passRatePercent: 50 },
  ],
  actionInsights: [
    { id: 'part_b', title: 'Review Part B performance', description: 'Part B is currently the lowest accuracy section.', tone: 'danger' },
  ],
};

describe('ReadingAnalyticsPage', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mockUseAdminAuth.mockReturnValue({ isAuthenticated: true, role: 'admin' });
    mockGetReadingAdminAnalytics.mockResolvedValue(analyticsFixture);
  });

  it('renders Reading analytics sections from the admin aggregate endpoint', async () => {
    renderWithRouter(<ReadingAnalyticsPage />);

    expect(await screen.findByRole('main', { name: /reading analytics/i })).toBeInTheDocument();
    expect(screen.getByRole('heading', { name: 'Reading Analytics' })).toBeInTheDocument();
    expect(await screen.findByText('Part B')).toBeInTheDocument();
    expect(screen.getAllByText('Inference').length).toBeGreaterThan(0);
    expect(screen.getByText('Review Part B performance')).toBeInTheDocument();
    expect(screen.getAllByText('Reading Sample 1').length).toBeGreaterThan(0);
  });
});