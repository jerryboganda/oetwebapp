import { screen } from '@testing-library/react';
import { renderWithRouter } from '@/tests/test-utils';
const { mockFetchStudyPlan, mockUpdateStudyPlanTask, mockPush, mockTrack } = vi.hoisted(() => ({
  mockFetchStudyPlan: vi.fn(),
  mockUpdateStudyPlanTask: vi.fn(),
  mockPush: vi.fn(),
  mockTrack: vi.fn(),
}));

vi.mock('motion/react', () => ({
  motion: {
    div: ({ children, ...props }: any) => <div {...props}>{children}</div>,
    button: ({ children, ...props }: any) => <button {...props}>{children}</button>,
    span: ({ children, ...props }: any) => <span {...props}>{children}</span>,
    section: ({ children, ...props }: any) => <section {...props}>{children}</section>,
    p: ({ children, ...props }: any) => <p {...props}>{children}</p>,
  },
  useReducedMotion: () => false,
  AnimatePresence: ({ children }: any) => <div>{children}</div>,
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
}));

import StudyPlanPage from './page';

describe('Study plan page', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mockFetchStudyPlan.mockResolvedValue([
      { id: 'task-1', title: 'Practice Reading Part C passages', subTest: 'Reading', duration: '30m', dueDate: 'Today', status: 'not_started', section: 'today', contentId: 'rc-001', rationale: 'Recent reading scores show detail extraction gaps.' },
      { id: 'task-2', title: 'Complete listening distractor drill', subTest: 'Listening', duration: '20m', dueDate: 'Tomorrow', status: 'not_started', section: 'thisWeek', contentId: 'ld-001', rationale: 'Listening accuracy needs improvement.' },
    ]);
    mockUpdateStudyPlanTask.mockResolvedValue({});
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
});
