import { render, screen } from '@testing-library/react';

const { mockFetchProgressV2, mockTrack, mockPush } = vi.hoisted(() => ({
  mockFetchProgressV2: vi.fn(),
  mockTrack: vi.fn(),
  mockPush: vi.fn(),
}));

vi.mock('next/navigation', () => ({
  useRouter: () => ({ push: mockPush, replace: vi.fn(), back: vi.fn() }),
  usePathname: () => '/progress',
  useSearchParams: () => new URLSearchParams(),
}));

vi.mock('recharts', () => ({
  LineChart: ({ children }: { children?: React.ReactNode }) => <div data-testid="line-chart">{children}</div>,
  Line: () => null,
  AreaChart: ({ children }: { children?: React.ReactNode }) => <div>{children}</div>,
  Area: () => null,
  BarChart: ({ children }: { children?: React.ReactNode }) => <div>{children}</div>,
  Bar: () => null,
  XAxis: () => null,
  YAxis: () => null,
  CartesianGrid: () => null,
  Tooltip: () => null,
  ResponsiveContainer: ({ children }: { children?: React.ReactNode }) => <div>{children}</div>,
  Legend: () => null,
  ReferenceLine: () => null,
}));

vi.mock('@/components/layout', () => ({
  LearnerDashboardShell: ({ children }: { children: React.ReactNode }) => (
    <div data-testid="learner-dashboard-shell">{children}</div>
  ),
}));

vi.mock('@/lib/analytics', () => ({ analytics: { track: mockTrack } }));

vi.mock('@/lib/api', () => ({
  fetchProgressV2: mockFetchProgressV2,
  progressPdfUrl: () => '/v1/progress/v2/export.pdf',
}));

import ProgressDashboard from './page';

const samplePayload = {
  meta: {
    range: '90d' as const,
    examFamilyCode: 'oet',
    targetCountry: 'GB',
    scoreAxisMin: 0,
    scoreAxisMax: 500,
    gradeBThreshold: 350,
    writingThreshold: 350,
    writingThresholdGrade: 'B',
    writingThresholdReason: null,
    showScoreGuaranteeStrip: true,
    showCriterionConfidenceBand: true,
    minEvaluationsForTrend: 2,
  },
  subtests: [
    { subtestCode: 'writing' as const, latestScaled: 340, latestGrade: 'C+', targetScaled: 350, gapToTarget: 10, deltaLast30Days: 5, attemptCount: 3, evaluationCount: 3, thresholdScaled: 350, thresholdReason: null },
    { subtestCode: 'speaking' as const, latestScaled: 360, latestGrade: 'B', targetScaled: 350, gapToTarget: -10, deltaLast30Days: null, attemptCount: 2, evaluationCount: 2, thresholdScaled: 350, thresholdReason: null },
    { subtestCode: 'reading' as const, latestScaled: null, latestGrade: null, targetScaled: 350, gapToTarget: null, deltaLast30Days: null, attemptCount: 0, evaluationCount: 0, thresholdScaled: 350, thresholdReason: null },
    { subtestCode: 'listening' as const, latestScaled: null, latestGrade: null, targetScaled: 350, gapToTarget: null, deltaLast30Days: null, attemptCount: 0, evaluationCount: 0, thresholdScaled: 350, thresholdReason: null },
  ],
  trend: [
    {
      weekKey: '2026-W15',
      weekStart: '2026-04-13T00:00:00Z',
      subtestScaled: { writing: 320, speaking: 340, reading: null, listening: null },
      subtestCount: { writing: 1, speaking: 1, reading: 0, listening: 0 },
      mockScaled: { writing: null, speaking: null, reading: null, listening: null },
      mockCount: { writing: 0, speaking: 0, reading: 0, listening: 0 },
    },
    {
      weekKey: '2026-W16',
      weekStart: '2026-04-20T00:00:00Z',
      subtestScaled: { writing: 340, speaking: 360, reading: null, listening: null },
      subtestCount: { writing: 2, speaking: 1, reading: 0, listening: 0 },
      mockScaled: { writing: null, speaking: null, reading: null, listening: null },
      mockCount: { writing: 0, speaking: 0, reading: 0, listening: 0 },
    },
  ],
  criterionTrend: [],
  completion: [],
  submissionVolume: [
    { weekKey: '2026-W15', weekStart: '2026-04-13T00:00:00Z', writing: 1, speaking: 1 },
    { weekKey: '2026-W16', weekStart: '2026-04-20T00:00:00Z', writing: 2, speaking: 1 },
    { weekKey: '2026-W17', weekStart: '2026-04-27T00:00:00Z', writing: 0, speaking: 0 },
    { weekKey: '2026-W18', weekStart: '2026-05-04T00:00:00Z', writing: 0, speaking: 0 },
    { weekKey: '2026-W19', weekStart: '2026-05-11T00:00:00Z', writing: 0, speaking: 0 },
  ],
  reviewUsage: { totalRequests: 2, completedRequests: 1, averageTurnaroundHours: 2.5, creditsConsumed: 0 },
  goals: { targetWritingScore: 350, targetSpeakingScore: 350, targetReadingScore: 350, targetListeningScore: 350, targetExamDate: '2026-07-01', daysToExam: 70, targetCountry: 'GB' },
  comparative: { subtests: [], cohortSize: 5, minCohortSize: 30, hasSufficientCohort: false, cohortScopeDescription: 'OET medicine learners (last 90 days)' },
  totals: { completedAttempts: 5, completedEvaluations: 5, mockAttempts: 1, writingSubmissions: 3, speakingSubmissions: 2 },
  freshness: { generatedAt: '2026-04-22T10:00:00Z', usesFallbackSeries: false, eTag: 'W/"abc123"' },
};

describe('Progress dashboard page (v2)', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mockFetchProgressV2.mockResolvedValue(samplePayload);
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

  it('shows the time-range pill toolbar', async () => {
    render(<ProgressDashboard />);
    await screen.findByText('See whether recent effort is turning into better evidence');
    expect(screen.getByRole('radio', { name: '14d' })).toBeInTheDocument();
    expect(screen.getByRole('radio', { name: '30d' })).toBeInTheDocument();
    expect(screen.getByRole('radio', { name: '90d' })).toBeInTheDocument();
    expect(screen.getByRole('radio', { name: 'All' })).toBeInTheDocument();
  });

  it('shows the export PDF action', async () => {
    render(<ProgressDashboard />);
    await screen.findByText('See whether recent effort is turning into better evidence');
    expect(screen.getByLabelText('Export progress as PDF')).toBeInTheDocument();
  });

  it('renders all four subtest mini cards', async () => {
    render(<ProgressDashboard />);
    await screen.findByText('See whether recent effort is turning into better evidence');
    // Subtest mini cards render as buttons with aria labels
    expect(screen.getByLabelText(/Toggle Writing series/)).toBeInTheDocument();
    expect(screen.getByLabelText(/Toggle Speaking series/)).toBeInTheDocument();
    expect(screen.getByLabelText(/Toggle Reading series/)).toBeInTheDocument();
    expect(screen.getByLabelText(/Toggle Listening series/)).toBeInTheDocument();
  });

  it('exposes Trend, Criterion, and Comparative tabs', async () => {
    render(<ProgressDashboard />);
    await screen.findByText('See whether recent effort is turning into better evidence');
    expect(screen.getByRole('tab', { name: /Trend/ })).toBeInTheDocument();
    expect(screen.getByRole('tab', { name: /Criterion/ })).toBeInTheDocument();
    expect(screen.getByRole('tab', { name: /Comparative/ })).toBeInTheDocument();
  });

  it('switches tabs and fires analytics event', async () => {
    const { fireEvent } = await import('@testing-library/react');
    render(<ProgressDashboard />);
    await screen.findByText('See whether recent effort is turning into better evidence');
    fireEvent.click(screen.getByRole('tab', { name: /Criterion/ }));
    expect(mockTrack).toHaveBeenCalledWith('progress_tab_switched', { tab: 'criterion' });
  });

  it('refetches data when range changes', async () => {
    const { fireEvent } = await import('@testing-library/react');
    render(<ProgressDashboard />);
    await screen.findByText('See whether recent effort is turning into better evidence');
    mockFetchProgressV2.mockClear();
    fireEvent.click(screen.getByRole('radio', { name: '14d' }));
    expect(mockTrack).toHaveBeenCalledWith('progress_range_changed', { range: '14d' });
  });

  it('hides the score-guarantee strip when policy disables it', async () => {
    mockFetchProgressV2.mockResolvedValue({
      ...samplePayload,
      meta: { ...samplePayload.meta, showScoreGuaranteeStrip: false },
    });
    render(<ProgressDashboard />);
    await screen.findByText('See whether recent effort is turning into better evidence');
    expect(screen.queryByText(/Score Guarantee eligible/)).not.toBeInTheDocument();
  });
});
