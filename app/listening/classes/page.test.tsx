import { render, screen } from '@testing-library/react';

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

// The classes page transitively imports `useCurrentUser → useAuth`, which
// throws if there is no AuthProvider in the tree. Mock both so the test can
// render the page standalone.
vi.mock('@/contexts/auth-context', () => ({
  useAuth: () => ({
    user: { userId: 'test-user', displayName: 'Test', email: 'test@example.com', role: 'expert', isEmailVerified: true },
    isAuthenticated: true,
    loading: false,
    signOut: vi.fn(),
  }),
}));

vi.mock('@/lib/hooks/use-current-user', () => ({
  useCurrentUser: () => ({
    user: { userId: 'test-user', displayName: 'Test', email: 'test@example.com', role: 'learner', isEmailVerified: true },
    role: 'learner',
    isAuthenticated: true,
    isLoading: false,
  }),
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

  it('renders the public launch hold instead of teacher analytics', () => {
    render(<ListeningTeacherClassesPage />);

    expect(screen.getByRole('alert')).toHaveTextContent(/only accessible to teaching staff/i);
    expect(mockList).not.toHaveBeenCalled();
    expect(mockAnalytics).not.toHaveBeenCalled();
    expect(mockExportToCsv).not.toHaveBeenCalled();
  });
});
