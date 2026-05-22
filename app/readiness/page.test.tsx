import { render, screen } from '@testing-library/react';

const { mockFetchReadiness, mockFetchHistory, mockFetchForecast, mockRefresh, mockTrack } = vi.hoisted(() => ({
  mockFetchReadiness: vi.fn(),
  mockFetchHistory: vi.fn(),
  mockFetchForecast: vi.fn(),
  mockRefresh: vi.fn(),
  mockTrack: vi.fn(),
}));

vi.mock('@/components/layout', () => ({
  LearnerDashboardShell: ({ children }: { children: React.ReactNode }) => (
    <div data-testid="learner-dashboard-shell">{children}</div>
  ),
}));

vi.mock('@/lib/analytics', () => ({ analytics: { track: mockTrack } }));
vi.mock('@/lib/api', () => ({
  fetchReadiness: mockFetchReadiness,
  fetchReadinessHistory: mockFetchHistory,
  fetchReadinessForecast: mockFetchForecast,
  refreshReadiness: mockRefresh,
}));

import ReadinessCenter from './page';

const SAMPLE_READINESS = {
  targetDate: '2026-06-15',
  overallRisk: 'Moderate' as const,
  overallReadiness: 65,
  weeksRemaining: 10,
  recommendedStudyHours: 12,
  recommendedStudyHoursRationale: 'Close the 12-point gap by focusing on Writing.',
  targetDateProbability: 62,
  confidenceLevel: 'Medium' as const,
  dataPointCount: 14,
  weakestLink: 'Listening',
  subTests: [
    { id: 'reading', name: 'Reading', readiness: 68, target: 80, isWeakest: false, bg: 'bg-blue-50', color: 'text-blue-600', barColor: 'bg-blue-500', status: 'On track' },
    { id: 'listening', name: 'Listening', readiness: 55, target: 75, isWeakest: true, bg: 'bg-emerald-50', color: 'text-emerald-600', barColor: 'bg-emerald-500', status: 'Needs focus' },
    { id: 'writing', name: 'Writing', readiness: 62, target: 70, isWeakest: false, bg: 'bg-rose-50', color: 'text-rose-600', barColor: 'bg-rose-500', status: 'Improving' },
    { id: 'speaking', name: 'Speaking', readiness: 70, target: 75, isWeakest: false, bg: 'bg-purple-50', color: 'text-purple-600', barColor: 'bg-purple-500', status: 'On track' },
  ],
  vocabulary: { readiness: 45, target: 100, mastered: 180, masteryTarget: 600, accuracy30d: 76, dataPoints: 220 },
  evidence: { mocksCompleted: 3, practiceQuestions: 42, expertReviews: 2, vocabReviewed30d: 60, recentTrend: 'Steady improvement', lastUpdated: '2026-05-15' },
  blockers: [
    { id: 'b1', title: 'Low listening accuracy', description: 'Distractor control needs improvement.', severity: 'medium', actionLabel: 'Practice listening', actionHref: '/listening', impactScore: 30 },
  ],
};

describe('Readiness center page', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mockFetchReadiness.mockResolvedValue(SAMPLE_READINESS);
    mockFetchHistory.mockResolvedValue([]);
    mockFetchForecast.mockResolvedValue({ probability: 62, weeksNeeded: 8, weeksAvailable: 10, requiredImprovement: 10, slopePerWeek: 1.2, scenarios: [] });
  });

  it('renders readiness hero through the learner dashboard shell', async () => {
    render(<ReadinessCenter />);
    expect(await screen.findByText('Close the gap to exam day with evidence')).toBeInTheDocument();
    expect(screen.getByTestId('learner-dashboard-shell')).toBeInTheDocument();
  });

  it('tracks readiness_viewed analytics on mount', async () => {
    render(<ReadinessCenter />);
    await screen.findByText('Close the gap to exam day with evidence');
    expect(mockTrack).toHaveBeenCalledWith('readiness_viewed');
  });

  it('renders overall readiness score and recommended hours', async () => {
    render(<ReadinessCenter />);
    expect(await screen.findByText('65')).toBeInTheDocument();
    expect(screen.getByText('Recommended: 12 hrs/week')).toBeInTheDocument();
  });

  it('renders blocker with action link', async () => {
    render(<ReadinessCenter />);
    expect(await screen.findByText('Low listening accuracy')).toBeInTheDocument();
    expect(screen.getByText('Practice listening')).toBeInTheDocument();
  });
});
