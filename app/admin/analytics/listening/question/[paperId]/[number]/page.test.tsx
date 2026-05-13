/**
 * Vitest spec for the Listening per-question deep-dive admin page.
 * Mocks `useAdminAuth`, `useParams`, and `getListeningAdminAnalytics`.
 * Asserts the page filters the class-wide DTO to the routed
 * (paperId, questionNumber) tuple and surfaces the wrong-answer
 * histogram + misspellings panel.
 */
import { render, screen, waitFor } from '@testing-library/react';
import type { ListeningAdminAnalytics } from '@/lib/listening-authoring-api';

const { mockGetAnalytics, authState, paramsState } = vi.hoisted(() => ({
  mockGetAnalytics: vi.fn(),
  authState: {
    isAuthenticated: true as boolean,
    role: 'admin' as 'admin' | 'learner' | null,
  },
  paramsState: {
    paperId: 'lt-001',
    number: '12',
  },
}));

vi.mock('@/lib/listening-authoring-api', async () => {
  const actual = await vi.importActual<typeof import('@/lib/listening-authoring-api')>(
    '@/lib/listening-authoring-api',
  );
  return {
    ...actual,
    getListeningAdminAnalytics: mockGetAnalytics,
  };
});

vi.mock('@/lib/hooks/use-admin-auth', () => ({
  useAdminAuth: () => ({
    isAuthenticated: authState.isAuthenticated,
    role: authState.role,
  }),
}));

vi.mock('next/navigation', async () => {
  const actual = await vi.importActual<typeof import('next/navigation')>('next/navigation');
  return {
    ...actual,
    useParams: () => ({ paperId: paramsState.paperId, number: paramsState.number }),
  };
});

import ListeningQuestionDeepDivePage from './page';

const fixtureAnalytics: ListeningAdminAnalytics = {
  days: 30,
  completedAttempts: 42,
  averageScaledScore: 360,
  percentLikelyPassing: 61,
  classPartAverages: [
    { partCode: 'A', earned: 0, max: 0, accuracyPercent: 78 },
    { partCode: 'B', earned: 0, max: 0, accuracyPercent: 64 },
    { partCode: 'C', earned: 0, max: 0, accuracyPercent: 58 },
  ],
  hardestQuestions: [
    {
      paperId: 'lt-001',
      paperTitle: 'Asthma review',
      questionNumber: 12,
      partCode: 'B',
      attemptCount: 30,
      accuracyPercent: 28,
    },
    {
      paperId: 'lt-002',
      paperTitle: 'Other',
      questionNumber: 5,
      partCode: 'A',
      attemptCount: 10,
      accuracyPercent: 50,
    },
  ],
  distractorHeat: [
    {
      paperId: 'lt-001',
      questionNumber: 12,
      correctAnswer: 'C',
      wrongAnswerHistogram: { A: 7, B: 12, D: 3 },
    },
  ],
  commonMisspellings: [
    { correctAnswer: 'C', wrongSpelling: 'see', count: 4 },
  ],
};

describe('ListeningQuestionDeepDivePage', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    authState.isAuthenticated = true;
    authState.role = 'admin';
    paramsState.paperId = 'lt-001';
    paramsState.number = '12';
    mockGetAnalytics.mockResolvedValue(fixtureAnalytics);
  });

  it('renders the deep-dive panels filtered to the routed question', async () => {
    render(<ListeningQuestionDeepDivePage />);
    await waitFor(() => {
      expect(screen.getByTestId('listening-question-deep-dive')).toBeInTheDocument();
    });
    // Accuracy summary for Q12 of lt-001.
    expect(screen.getByText('28%')).toBeInTheDocument();
    // Correct answer chip.
    const correctAnswerCells = screen.getAllByText('C');
    expect(correctAnswerCells.length).toBeGreaterThan(0);
    // Distractor histogram lists the wrong buckets sorted by count desc.
    const distractorList = screen.getByTestId('listening-question-distractor-list');
    const rowText = distractorList.textContent ?? '';
    expect(rowText.indexOf('B')).toBeLessThan(rowText.indexOf('A'));
    expect(rowText.indexOf('A')).toBeLessThan(rowText.indexOf('D'));
    // Misspellings panel filtered to correctAnswer='C'.
    expect(screen.getByTestId('listening-question-misspellings')).toBeInTheDocument();
  });

  it('renders an info alert when the question has no class data', async () => {
    paramsState.number = '99';
    render(<ListeningQuestionDeepDivePage />);
    await waitFor(() => {
      expect(screen.getByText(/no class data for this question/i)).toBeInTheDocument();
    });
  });

  it('returns null when the viewer is not an admin', () => {
    authState.role = 'learner';
    const { container } = render(<ListeningQuestionDeepDivePage />);
    expect(container.firstChild).toBeNull();
  });

  it('renders an invalid-route warning when the question number is non-numeric', () => {
    paramsState.number = 'abc';
    render(<ListeningQuestionDeepDivePage />);
    expect(screen.getByText(/invalid question route/i)).toBeInTheDocument();
  });

  it('rejects partially numeric question route params without fetching analytics', () => {
    paramsState.number = '12abc';
    render(<ListeningQuestionDeepDivePage />);
    expect(screen.getByText(/invalid question route/i)).toBeInTheDocument();
    expect(mockGetAnalytics).not.toHaveBeenCalled();
  });
});
