import { render, screen } from '@testing-library/react';
const { mockFetchTrendData, mockFetchCompletionData, mockFetchSubmissionVolume, mockFetchProgressEvidenceSummary, mockTrack } = vi.hoisted(() => ({
  mockFetchTrendData: vi.fn(),
  mockFetchCompletionData: vi.fn(),
  mockFetchSubmissionVolume: vi.fn(),
  mockFetchProgressEvidenceSummary: vi.fn(),
  mockTrack: vi.fn(),
}));

vi.mock('recharts', () => ({
  LineChart: ({ children }: { children?: React.ReactNode }) => <div data-testid="line-chart">{children}</div>,
  Line: () => null, AreaChart: ({ children }: { children?: React.ReactNode }) => <div>{children}</div>,
  Area: () => null, BarChart: ({ children }: { children?: React.ReactNode }) => <div>{children}</div>,
  Bar: () => null, XAxis: () => null, YAxis: () => null,
  CartesianGrid: () => null, Tooltip: () => null,
  ResponsiveContainer: ({ children }: { children?: React.ReactNode }) => <div>{children}</div>,
  Legend: () => null,
}));
vi.mock('@/components/layout/app-shell', () => ({
  LearnerDashboardShell: ({ children, workspaceClassName }: { children: React.ReactNode; workspaceClassName?: string }) => (
    <div data-testid="learner-dashboard-shell" data-workspace-class={workspaceClassName}>{children}</div>
  ),
}));

vi.mock('@/components/layout/admin-dashboard-shell', () => ({
  LearnerDashboardShell: ({ children, workspaceClassName }: { children: React.ReactNode; workspaceClassName?: string }) => (
    <div data-testid="learner-dashboard-shell" data-workspace-class={workspaceClassName}>{children}</div>
  ),
}));

vi.mock('@/components/layout/expert-dashboard-shell', () => ({
  LearnerDashboardShell: ({ children, workspaceClassName }: { children: React.ReactNode; workspaceClassName?: string }) => (
    <div data-testid="learner-dashboard-shell" data-workspace-class={workspaceClassName}>{children}</div>
  ),
}));

vi.mock('@/components/layout/learner-dashboard-shell', () => ({
  LearnerDashboardShell: ({ children, workspaceClassName }: { children: React.ReactNode; workspaceClassName?: string }) => (
    <div data-testid="learner-dashboard-shell" data-workspace-class={workspaceClassName}>{children}</div>
  ),
}));

vi.mock('@/components/layout/sponsor-dashboard-shell', () => ({
  LearnerDashboardShell: ({ children, workspaceClassName }: { children: React.ReactNode; workspaceClassName?: string }) => (
    <div data-testid="learner-dashboard-shell" data-workspace-class={workspaceClassName}>{children}</div>
  ),
}));

vi.mock('@/components/layout/learner-workspace-container', () => ({
  LearnerDashboardShell: ({ children, workspaceClassName }: { children: React.ReactNode; workspaceClassName?: string }) => (
    <div data-testid="learner-dashboard-shell" data-workspace-class={workspaceClassName}>{children}</div>
  ),
}));

vi.mock('@/components/layout/notification-center', () => ({
  LearnerDashboardShell: ({ children, workspaceClassName }: { children: React.ReactNode; workspaceClassName?: string }) => (
    <div data-testid="learner-dashboard-shell" data-workspace-class={workspaceClassName}>{children}</div>
  ),
}));

vi.mock('@/components/layout/notification-preferences-panel', () => ({
  LearnerDashboardShell: ({ children, workspaceClassName }: { children: React.ReactNode; workspaceClassName?: string }) => (
    <div data-testid="learner-dashboard-shell" data-workspace-class={workspaceClassName}>{children}</div>
  ),
}));

vi.mock('@/components/layout/top-nav', () => ({
  LearnerDashboardShell: ({ children, workspaceClassName }: { children: React.ReactNode; workspaceClassName?: string }) => (
    <div data-testid="learner-dashboard-shell" data-workspace-class={workspaceClassName}>{children}</div>
  ),
}));

vi.mock('@/components/layout/sidebar', () => ({
  LearnerDashboardShell: ({ children, workspaceClassName }: { children: React.ReactNode; workspaceClassName?: string }) => (
    <div data-testid="learner-dashboard-shell" data-workspace-class={workspaceClassName}>{children}</div>
  ),
}));

vi.mock('@/lib/analytics', () => ({ analytics: { track: mockTrack } }));

vi.mock('@/lib/api', () => ({
  fetchTrendData: mockFetchTrendData,
  fetchCompletionData: mockFetchCompletionData,
  fetchSubmissionVolume: mockFetchSubmissionVolume,
  fetchProgressEvidenceSummary: mockFetchProgressEvidenceSummary,
}));

import ProgressDashboard from './page';

describe('Progress dashboard page', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mockFetchTrendData.mockResolvedValue([{ date: 'Week 1', reading: 60, listening: 55, writing: 50, speaking: 48 }]);
    mockFetchCompletionData.mockResolvedValue([{ day: 'Mon', completed: 3 }]);
    mockFetchSubmissionVolume.mockResolvedValue([{ week: 'W1', submissions: 12 }]);
    mockFetchProgressEvidenceSummary.mockResolvedValue({
      reviewUsage: { averageTurnaroundHours: 2.5 },
      freshness: { usesFallbackSeries: false, generatedAt: new Date().toISOString() },
    });
  });

  it('renders through the shared learner dashboard shell', async () => {
    render(<ProgressDashboard />);
    expect(await screen.findByText('See whether recent effort is turning into better evidence')).toBeInTheDocument();
    expect(screen.getByTestId('learner-dashboard-shell')).toBeInTheDocument();
  });

  it('tracks progress_viewed analytics on mount', async () => {
    render(<ProgressDashboard />);
    await screen.findByText('See whether recent effort is turning into better evidence');
    expect(mockTrack).toHaveBeenCalledWith('progress_viewed');
  });
});
