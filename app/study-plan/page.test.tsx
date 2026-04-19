import { screen } from '@testing-library/react';
import { renderWithRouter } from '@/tests/test-utils';
const { mockFetchStudyPlan, mockUpdateStudyPlanTask, mockPush, mockTrack, mockReschedule, mockSnooze, mockStart, mockRegenerate } = vi.hoisted(() => ({
  mockFetchStudyPlan: vi.fn(),
  mockUpdateStudyPlanTask: vi.fn(),
  mockPush: vi.fn(),
  mockTrack: vi.fn(),
  mockReschedule: vi.fn(),
  mockSnooze: vi.fn(),
  mockStart: vi.fn(),
  mockRegenerate: vi.fn(),
}));

vi.mock('@/components/layout', () => ({
  LearnerDashboardShell: ({ children, workspaceClassName }: { children: React.ReactNode; workspaceClassName?: string }) => (
    <div data-testid="learner-dashboard-shell" data-workspace-class={workspaceClassName}>{children}</div>
  ),
}));

vi.mock('@/hooks/use-analytics', () => ({ useAnalytics: () => ({ track: mockTrack }) }));

vi.mock('@/lib/api', () => ({
  fetchStudyPlan: mockFetchStudyPlan,
  updateStudyPlanTask: mockUpdateStudyPlanTask,
  rescheduleStudyPlanTask: mockReschedule,
  snoozeStudyPlanTask: mockSnooze,
  startStudyPlanTask: mockStart,
  regenerateStudyPlan: mockRegenerate,
  studyPlanIcsUrl: () => '/v1/study-plan/ics',
}));

import StudyPlanPage from './page';

describe('Study plan page', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mockFetchStudyPlan.mockResolvedValue([
      { id: 'task-1', title: 'Practice Reading Part C passages', subTest: 'Reading', duration: '30m', dueDate: '2026-04-20', status: 'not_started', section: 'today', contentId: 'rc-001', rationale: 'Recent reading scores show detail extraction gaps.' },
      { id: 'task-2', title: 'Complete listening distractor drill', subTest: 'Listening', duration: '20m', dueDate: '2026-04-21', status: 'not_started', section: 'thisWeek', contentId: 'ld-001', rationale: 'Listening accuracy needs improvement.' },
    ]);
    mockUpdateStudyPlanTask.mockResolvedValue({});
    mockReschedule.mockResolvedValue(undefined);
    mockSnooze.mockResolvedValue(undefined);
    mockStart.mockResolvedValue({ startUrl: '/reading' });
    mockRegenerate.mockResolvedValue({ planId: 'p1', version: 2, state: 'completed' });
  });

  it('renders study tasks through the shared learner dashboard shell', async () => {
    renderWithRouter(<StudyPlanPage />, { router: { push: mockPush } });
    expect(await screen.findByText('Practice Reading Part C passages')).toBeInTheDocument();
    expect(screen.getByTestId('learner-dashboard-shell')).toBeInTheDocument();
  });

  it('shows sub-test badges and task details', async () => {
    renderWithRouter(<StudyPlanPage />, { router: { push: mockPush } });
    expect(await screen.findByText('Reading')).toBeInTheDocument();
    expect(screen.getByText('Listening')).toBeInTheDocument();
  });

  it('exposes the regenerate plan button', async () => {
    renderWithRouter(<StudyPlanPage />, { router: { push: mockPush } });
    await screen.findByText('Practice Reading Part C passages');
    expect(screen.getByText('Regenerate plan')).toBeInTheDocument();
  });

  it('exposes the add to calendar button for ICS export', async () => {
    renderWithRouter(<StudyPlanPage />, { router: { push: mockPush } });
    await screen.findByText('Practice Reading Part C passages');
    expect(screen.getByText('Add to calendar')).toBeInTheDocument();
  });
});
