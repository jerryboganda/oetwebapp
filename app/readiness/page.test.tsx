import { render, screen } from '@testing-library/react';
const { mockFetchReadiness, mockTrack } = vi.hoisted(() => ({
  mockFetchReadiness: vi.fn(),
  mockTrack: vi.fn(),
}));

vi.mock('@/components/layout', () => ({
  LearnerDashboardShell: ({ children }: { children: React.ReactNode }) => (
    <div data-testid="learner-dashboard-shell">{children}</div>
  ),
}));

vi.mock('@/lib/analytics', () => ({ analytics: { track: mockTrack } }));
vi.mock('@/lib/api', () => ({ fetchReadiness: mockFetchReadiness, fetchReadinessRisk: vi.fn().mockResolvedValue({ overallRisk: 'moderate', factors: [] }) }));

import ReadinessCenter from './page';

describe('Readiness center page', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mockFetchReadiness.mockResolvedValue({
      targetDate: '2026-06-15',
      overallRisk: 'Moderate',
      weeksRemaining: 10,
      recommendedStudyHours: 120,
      subTests: [
        { id: 'reading', name: 'Reading', readiness: 68, target: 80, isWeakest: false, bg: 'bg-blue-50', color: 'text-blue-600', barColor: 'bg-blue-500', status: 'On track' },
        { id: 'listening', name: 'Listening', readiness: 55, target: 75, isWeakest: true, bg: 'bg-emerald-50', color: 'text-emerald-600', barColor: 'bg-emerald-500', status: 'Needs focus' },
        { id: 'writing', name: 'Writing', readiness: 62, target: 70, isWeakest: false, bg: 'bg-rose-50', color: 'text-rose-600', barColor: 'bg-rose-500', status: 'Improving' },
        { id: 'speaking', name: 'Speaking', readiness: 70, target: 75, isWeakest: false, bg: 'bg-purple-50', color: 'text-purple-600', barColor: 'bg-purple-500', status: 'On track' },
      ],
      evidence: { mocksCompleted: 3, practiceQuestions: 42, expertReviews: 2, recentTrend: 'Steady improvement', lastUpdated: '2025-04-15' },
      blockers: [{ id: 'b1', title: 'Low listening accuracy', description: 'Distractor control needs improvement.' }],
    });
  });

  it('renders risk assessment through the shared learner dashboard shell', async () => {
    render(<ReadinessCenter />);
    expect(await screen.findByText('See what needs to close before your target date')).toBeInTheDocument();
    expect(screen.getByTestId('learner-dashboard-shell')).toBeInTheDocument();
  });

  it('tracks readiness_viewed analytics on mount', async () => {
    render(<ReadinessCenter />);
    await screen.findByText('See what needs to close before your target date');
    expect(mockTrack).toHaveBeenCalledWith('readiness_viewed');
  });
});
