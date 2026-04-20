import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import type { WritingTask, WritingSubmission } from '@/lib/mock-data';

const { mockFetchWritingHome, mockFetchWritingTasks, mockFetchWritingSubmissions, mockTrack, mockRouterPush } = vi.hoisted(() => ({
  mockFetchWritingHome: vi.fn(),
  mockFetchWritingTasks: vi.fn(),
  mockFetchWritingSubmissions: vi.fn(),
  mockTrack: vi.fn(),
  mockRouterPush: vi.fn(),
}));

vi.mock('next/link', () => ({
  default: ({ children, href }: { children: React.ReactNode; href?: string }) => <a href={href}>{children}</a>,
}));

vi.mock('next/navigation', () => ({
  useRouter: () => ({ push: mockRouterPush }),
}));

vi.mock('@/components/layout', () => ({
  LearnerDashboardShell: ({ children }: { children: React.ReactNode }) => <div data-testid="learner-dashboard-shell">{children}</div>,
}));

vi.mock('@/lib/analytics', () => ({ analytics: { track: mockTrack } }));

vi.mock('@/lib/api', () => ({
  fetchWritingHome: mockFetchWritingHome,
  fetchWritingTasks: mockFetchWritingTasks,
  fetchWritingSubmissions: mockFetchWritingSubmissions,
}));

import WritingHome from './page';

describe('Writing home page', () => {
  beforeEach(() => {
    vi.clearAllMocks();

    const task: WritingTask = {
      id: 'wt-001',
      route: '/writing/player?taskId=wt-001',
      title: 'Routine referral - dermatology',
      profession: 'Medicine',
      difficulty: 'Medium',
      scenarioType: 'Routine referral',
      time: '40 mins',
      criteriaFocus: 'content',
      caseNotes: 'Case notes here',
    };

    mockFetchWritingHome.mockResolvedValue({
      recommendedTask: task,
      reviewCredits: { available: 2 },
      fullMockEntry: { title: 'Full mock', route: '/mocks/setup' },
      practiceLibrary: [task],
      criterionDrillLibrary: [
        {
          criterionCode: 'purpose',
          criterionLabel: 'Purpose',
          title: 'Purpose repair drill',
          rationale: 'Sharpen the opening purpose.',
          route: '/writing/player?taskId=wt-001&criterion=purpose',
        },
      ],
      pastSubmissions: [
        {
          id: 'we-001',
          taskId: 'wt-001',
          taskTitle: 'Completed referral',
          content: '',
          wordCount: 0,
          submittedAt: '2026-04-20T10:00:00Z',
          evalStatus: 'completed',
          reviewStatus: 'not_requested',
          route: '/writing/result?id=we-001',
        },
      ],
    });

    mockFetchWritingTasks.mockResolvedValue([task] satisfies WritingTask[]);
    mockFetchWritingSubmissions.mockResolvedValue([] satisfies WritingSubmission[]);
  });

  it('shows rulebook entry points on the writing home surface', async () => {
    render(<WritingHome />);

    expect(await screen.findByText(/Study the exact rules your writing is judged against/i)).toBeInTheDocument();
    expect(screen.getByRole('link', { name: /Writing rules/i })).toHaveAttribute('href', '/writing/rulebook/R03.4');
    expect(screen.getByRole('link', { name: /Discharge template rule/i })).toHaveAttribute('href', '/writing/rulebook/R14.2');
  });

  it('uses backend-driven drills and canonical past-submission routes', async () => {
    const user = userEvent.setup();
    render(<WritingHome />);

    await screen.findByText(/Study the exact rules your writing is judged against/i);

    await user.click(screen.getByRole('button', { name: /criterion drills/i }));
    await user.click(screen.getByText(/Purpose repair drill/i));
    expect(mockRouterPush).toHaveBeenCalledWith('/writing/player?taskId=wt-001&criterion=purpose');

    await user.click(screen.getByRole('button', { name: /past submissions/i }));
    await user.click(await screen.findByText(/Completed referral/i));
    expect(mockRouterPush).toHaveBeenCalledWith('/writing/result?id=we-001');
  });
});
