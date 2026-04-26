import { screen } from '@testing-library/react';
const { mockFetchReadingTask, mockSubmitReadingAnswers, mockTrack } = vi.hoisted(() => ({
  mockFetchReadingTask: vi.fn(),
  mockSubmitReadingAnswers: vi.fn(),
  mockTrack: vi.fn(),
  mockPush: vi.fn(),
}));
vi.mock('@/components/layout/app-shell', () => ({
  AppShell: ({ children }: { children: React.ReactNode }) => <div data-testid="app-shell">{children}</div>,
  LearnerDashboardShell: ({ children }: { children: React.ReactNode }) => <div data-testid="learner-dashboard-shell">{children}</div>,
}));

vi.mock('@/components/layout/admin-dashboard-shell', () => ({
  AppShell: ({ children }: { children: React.ReactNode }) => <div data-testid="app-shell">{children}</div>,
  LearnerDashboardShell: ({ children }: { children: React.ReactNode }) => <div data-testid="learner-dashboard-shell">{children}</div>,
}));

vi.mock('@/components/layout/expert-dashboard-shell', () => ({
  AppShell: ({ children }: { children: React.ReactNode }) => <div data-testid="app-shell">{children}</div>,
  LearnerDashboardShell: ({ children }: { children: React.ReactNode }) => <div data-testid="learner-dashboard-shell">{children}</div>,
}));

vi.mock('@/components/layout/learner-dashboard-shell', () => ({
  AppShell: ({ children }: { children: React.ReactNode }) => <div data-testid="app-shell">{children}</div>,
  LearnerDashboardShell: ({ children }: { children: React.ReactNode }) => <div data-testid="learner-dashboard-shell">{children}</div>,
}));

vi.mock('@/components/layout/sponsor-dashboard-shell', () => ({
  AppShell: ({ children }: { children: React.ReactNode }) => <div data-testid="app-shell">{children}</div>,
  LearnerDashboardShell: ({ children }: { children: React.ReactNode }) => <div data-testid="learner-dashboard-shell">{children}</div>,
}));

vi.mock('@/components/layout/learner-workspace-container', () => ({
  AppShell: ({ children }: { children: React.ReactNode }) => <div data-testid="app-shell">{children}</div>,
  LearnerDashboardShell: ({ children }: { children: React.ReactNode }) => <div data-testid="learner-dashboard-shell">{children}</div>,
}));

vi.mock('@/components/layout/notification-center', () => ({
  AppShell: ({ children }: { children: React.ReactNode }) => <div data-testid="app-shell">{children}</div>,
  LearnerDashboardShell: ({ children }: { children: React.ReactNode }) => <div data-testid="learner-dashboard-shell">{children}</div>,
}));

vi.mock('@/components/layout/notification-preferences-panel', () => ({
  AppShell: ({ children }: { children: React.ReactNode }) => <div data-testid="app-shell">{children}</div>,
  LearnerDashboardShell: ({ children }: { children: React.ReactNode }) => <div data-testid="learner-dashboard-shell">{children}</div>,
}));

vi.mock('@/components/layout/top-nav', () => ({
  AppShell: ({ children }: { children: React.ReactNode }) => <div data-testid="app-shell">{children}</div>,
  LearnerDashboardShell: ({ children }: { children: React.ReactNode }) => <div data-testid="learner-dashboard-shell">{children}</div>,
}));

vi.mock('@/components/layout/sidebar', () => ({
  AppShell: ({ children }: { children: React.ReactNode }) => <div data-testid="app-shell">{children}</div>,
  LearnerDashboardShell: ({ children }: { children: React.ReactNode }) => <div data-testid="learner-dashboard-shell">{children}</div>,
}));
vi.mock('@/components/state/async-state-wrapper', () => ({
  AsyncStateWrapper: ({ children }: { children: React.ReactNode }) => <>{children}</>,
}));

vi.mock('@/hooks/use-analytics', () => ({
  useAnalytics: () => ({
    track: mockTrack,
  }),
}));

vi.mock('@/lib/api', () => ({
  fetchReadingTask: mockFetchReadingTask,
  submitReadingAnswers: mockSubmitReadingAnswers,
}));

import DiagnosticReadingPage from './page';
import { renderWithRouter } from '@/tests/test-utils';

describe('Diagnostic reading page', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mockFetchReadingTask.mockResolvedValue({
      id: 'rt-001',
      title: 'Diagnostic Reading Task',
      part: 'B',
      timeLimit: 15,
      texts: [
        {
          id: 'text-1',
          title: 'Clinical Notice',
          content: 'Read the diagnostic passage.',
        },
      ],
      questions: [
        {
          id: 'question-1',
          number: 1,
          text: 'What is the main point?',
          type: 'mcq',
          options: ['Option A', 'Option B'],
        },
      ],
    });
    mockSubmitReadingAnswers.mockResolvedValue({});
  });

  it('stays on the immersive app shell instead of the learner dashboard shell', async () => {
    renderWithRouter(<DiagnosticReadingPage />);

    expect(await screen.findByText('Question 1 of 1')).toBeInTheDocument();
    expect(screen.getByTestId('app-shell')).toBeInTheDocument();
    expect(screen.queryByTestId('learner-dashboard-shell')).not.toBeInTheDocument();
  });
});
