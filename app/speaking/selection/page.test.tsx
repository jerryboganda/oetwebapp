import { render, screen } from '@testing-library/react';

const { mockFetchSpeakingTasks, mockTrack } = vi.hoisted(() => ({
  mockFetchSpeakingTasks: vi.fn(),
  mockTrack: vi.fn(),
}));

vi.mock('next/link', () => ({
  default: ({ children, href }: { children: React.ReactNode; href?: string }) => <a href={href}>{children}</a>,
}));
vi.mock('@/components/layout/app-shell', () => ({
  LearnerDashboardShell: ({ children }: { children: React.ReactNode }) => <div data-testid="learner-dashboard-shell">{children}</div>,
}));

vi.mock('@/components/layout/admin-dashboard-shell', () => ({
  LearnerDashboardShell: ({ children }: { children: React.ReactNode }) => <div data-testid="learner-dashboard-shell">{children}</div>,
}));

vi.mock('@/components/layout/expert-dashboard-shell', () => ({
  LearnerDashboardShell: ({ children }: { children: React.ReactNode }) => <div data-testid="learner-dashboard-shell">{children}</div>,
}));

vi.mock('@/components/layout/learner-dashboard-shell', () => ({
  LearnerDashboardShell: ({ children }: { children: React.ReactNode }) => <div data-testid="learner-dashboard-shell">{children}</div>,
}));

vi.mock('@/components/layout/sponsor-dashboard-shell', () => ({
  LearnerDashboardShell: ({ children }: { children: React.ReactNode }) => <div data-testid="learner-dashboard-shell">{children}</div>,
}));

vi.mock('@/components/layout/learner-workspace-container', () => ({
  LearnerDashboardShell: ({ children }: { children: React.ReactNode }) => <div data-testid="learner-dashboard-shell">{children}</div>,
}));

vi.mock('@/components/layout/notification-center', () => ({
  LearnerDashboardShell: ({ children }: { children: React.ReactNode }) => <div data-testid="learner-dashboard-shell">{children}</div>,
}));

vi.mock('@/components/layout/notification-preferences-panel', () => ({
  LearnerDashboardShell: ({ children }: { children: React.ReactNode }) => <div data-testid="learner-dashboard-shell">{children}</div>,
}));

vi.mock('@/components/layout/top-nav', () => ({
  LearnerDashboardShell: ({ children }: { children: React.ReactNode }) => <div data-testid="learner-dashboard-shell">{children}</div>,
}));

vi.mock('@/components/layout/sidebar', () => ({
  LearnerDashboardShell: ({ children }: { children: React.ReactNode }) => <div data-testid="learner-dashboard-shell">{children}</div>,
}));

vi.mock('@/components/domain/task-card', () => ({
  TaskCard: ({ title }: { title: string }) => <div>{title}</div>,
}));

vi.mock('@/lib/api', () => ({
  fetchSpeakingTasks: mockFetchSpeakingTasks,
}));

vi.mock('@/lib/analytics', () => ({ analytics: { track: mockTrack } }));

import SpeakingTaskSelection from './page';

describe('Speaking selection page', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mockFetchSpeakingTasks.mockResolvedValue([
      {
        id: 'sp-1',
        title: 'Breaking Bad News - Cancer Diagnosis',
        profession: 'Medicine',
        duration: '20 mins',
        difficulty: 'Medium',
        criteriaFocus: 'appropriateness',
        scenarioType: 'Role play',
      },
    ]);
  });

  it('shows speaking rulebook entry points on the selection surface', async () => {
    render(<SpeakingTaskSelection />);

    expect(await screen.findByText(/See the exact speaking rules behind the transcript audit/i)).toBeInTheDocument();
    expect(screen.getByRole('link', { name: /Speaking rules/i })).toHaveAttribute('href', '/speaking/rulebook/RULE_22');
    expect(screen.getByRole('link', { name: /Breaking bad news/i })).toHaveAttribute('href', '/speaking/rulebook/RULE_44');
  });
});
