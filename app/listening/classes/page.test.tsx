import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

const { mockList, mockAnalytics, mockCreate, mockAddMember, mockExportToCsv } = vi.hoisted(() => ({
  mockList: vi.fn(),
  mockAnalytics: vi.fn(),
  mockCreate: vi.fn(),
  mockAddMember: vi.fn(),
  mockExportToCsv: vi.fn(),
}));

vi.mock('@/components/layout/learner-dashboard-shell', () => ({
  LearnerDashboardShell: ({ children }: { children: React.ReactNode }) => <div data-testid="learner-dashboard-shell">{children}</div>,
}));

vi.mock('@/lib/listening/v2-api', () => ({
  teacherClassApi: {
    list: mockList,
    analytics: mockAnalytics,
    create: mockCreate,
    addMember: mockAddMember,
  },
}));

vi.mock('@/lib/csv-export', () => ({
  exportToCsv: mockExportToCsv,
}));

import ListeningTeacherClassesPage from './page';

describe('ListeningTeacherClassesPage', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mockList.mockResolvedValue([
      {
        id: 'class-1',
        name: 'Clinical Listening Group',
        description: 'April cohort',
        memberCount: 2,
        createdAt: '2026-05-01T00:00:00Z',
        updatedAt: '2026-05-01T00:00:00Z',
      },
    ]);
    mockAnalytics.mockResolvedValue({
      classId: 'class-1',
      className: 'Clinical Listening Group',
      description: 'April cohort',
      memberCount: 2,
      analytics: {
        days: 30,
        completedAttempts: 2,
        averageScaledScore: 325,
        percentLikelyPassing: 50,
        classPartAverages: [
          { partCode: 'A', earned: 18, max: 24, accuracyPercent: 75 },
          { partCode: 'B', earned: 3, max: 6, accuracyPercent: 50 },
        ],
        hardestQuestions: [
          { paperId: 'paper-1', paperTitle: 'Listening Practice Paper', questionNumber: 8, partCode: 'B', attemptCount: 4, accuracyPercent: 25 },
        ],
        distractorHeat: [
          { paperId: 'paper-1', questionNumber: 8, correctAnswer: 'B', wrongAnswerCount: 3 },
        ],
      },
    });
  });

  it('loads the first owned class and renders analytics', async () => {
    render(<ListeningTeacherClassesPage />);

    expect(await screen.findByRole('heading', { name: 'Class Listening analytics' })).toBeInTheDocument();
    expect(await screen.findByText('Clinical Listening Group')).toBeInTheDocument();
    expect(screen.getByText('325')).toBeInTheDocument();
    expect(screen.getByText('Part A')).toBeInTheDocument();
    expect(screen.getByText('Question 8 · Part B')).toBeInTheDocument();

    await waitFor(() => expect(mockAnalytics).toHaveBeenCalledWith('class-1', 30));
  });

  it('does not render raw wrong-answer text from the analytics payload', async () => {
    const user = userEvent.setup();
    mockAnalytics.mockResolvedValueOnce({
      classId: 'class-1',
      className: 'Clinical Listening Group',
      description: null,
      memberCount: 2,
      analytics: {
        days: 30,
        completedAttempts: 1,
        averageScaledScore: 310,
        percentLikelyPassing: 0,
        classPartAverages: [],
        hardestQuestions: [],
        distractorHeat: [
          {
            paperId: 'paper-1',
            questionNumber: 8,
            correctAnswer: 'B',
            wrongAnswerCount: 2,
            wrongAnswerHistogram: { 'patient.email@example.test': 2 },
          },
        ],
      },
    });

    render(<ListeningTeacherClassesPage />);

    expect(await screen.findByText('Correct answer B')).toBeInTheDocument();
    expect(screen.queryByText('patient.email@example.test')).not.toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: /export csv/i }));

    expect(mockExportToCsv).toHaveBeenCalledWith(
      expect.any(Array),
      'listening-class-clinical-listening-group-30d.csv',
    );
    const [rows] = mockExportToCsv.mock.calls[0];
    expect(JSON.stringify(rows)).not.toContain('patient.email@example.test');
  });

  it('does not export stale analytics after a failed refresh', async () => {
    const user = userEvent.setup();
    render(<ListeningTeacherClassesPage />);

    await screen.findByRole('button', { name: /export csv/i });
    await waitFor(() => expect(screen.getByRole('button', { name: /export csv/i })).toBeEnabled());

    mockAnalytics.mockRejectedValueOnce(new Error('Analytics request failed'));
    await user.click(screen.getByRole('button', { name: /refresh/i }));

    expect(await screen.findByText('Analytics request failed')).toBeInTheDocument();
    const exportButton = screen.getByRole('button', { name: /export csv/i });
    expect(exportButton).toBeDisabled();
    await user.click(exportButton);
    expect(mockExportToCsv).not.toHaveBeenCalled();
  });

  it('shows an empty state when there are no owned classes', async () => {
    mockList.mockResolvedValueOnce([]);

    render(<ListeningTeacherClassesPage />);

    expect(await screen.findByText('Create your first teacher class')).toBeInTheDocument();
    expect(mockAnalytics).not.toHaveBeenCalled();
  });
});