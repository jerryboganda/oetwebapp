/**
 * @vitest-environment jsdom
 */
import { render, screen, fireEvent } from '@testing-library/react';
import { describe, it, expect, vi } from 'vitest';

vi.mock('recharts', () => ({
  LineChart: ({ children }: { children?: React.ReactNode }) => <div data-testid="line-chart">{children}</div>,
  Line: () => null,
  XAxis: () => null,
  YAxis: () => null,
  CartesianGrid: () => null,
  Tooltip: () => null,
  ResponsiveContainer: ({ children }: { children?: React.ReactNode }) => <div>{children}</div>,
  Legend: () => null,
  ReferenceLine: ({ label }: { label?: { value?: string } }) => <span data-testid="ref-line">{label?.value ?? ''}</span>,
}));

import { ProgressTrendChart } from '../ProgressTrendChart';
import { ProgressSubtestMiniCards } from '../ProgressSubtestMiniCards';
import { ProgressComparativeTab } from '../ProgressComparativeTab';
import { ChartTabularFallback } from '../ChartTabularFallback';
import { ProgressRangePills } from '../ProgressRangePills';
import { ProgressReadinessStrip } from '../ProgressReadinessStrip';

const baseMeta = {
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
};

const baseSubtests = [
  { subtestCode: 'writing' as const, latestScaled: 340, latestGrade: 'C+', targetScaled: 350, gapToTarget: 10, deltaLast30Days: 12, attemptCount: 3, evaluationCount: 3, thresholdScaled: 350, thresholdReason: null },
  { subtestCode: 'speaking' as const, latestScaled: 360, latestGrade: 'B', targetScaled: 350, gapToTarget: -10, deltaLast30Days: -5, attemptCount: 2, evaluationCount: 2, thresholdScaled: 350, thresholdReason: null },
  { subtestCode: 'reading' as const, latestScaled: null, latestGrade: null, targetScaled: 350, gapToTarget: null, deltaLast30Days: null, attemptCount: 0, evaluationCount: 0, thresholdScaled: 350, thresholdReason: null },
  { subtestCode: 'listening' as const, latestScaled: null, latestGrade: null, targetScaled: 350, gapToTarget: null, deltaLast30Days: null, attemptCount: 0, evaluationCount: 0, thresholdScaled: 350, thresholdReason: null },
];

const samplePayload = {
  meta: baseMeta,
  subtests: baseSubtests,
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
  submissionVolume: [],
  reviewUsage: { totalRequests: 0, completedRequests: 0, averageTurnaroundHours: null, creditsConsumed: 0 },
  goals: { targetWritingScore: 350, targetSpeakingScore: 350, targetReadingScore: 350, targetListeningScore: 350, targetExamDate: '2026-07-01', daysToExam: 70, targetCountry: 'GB' },
  comparative: null,
  totals: { completedAttempts: 5, completedEvaluations: 5, mockAttempts: 1, writingSubmissions: 3, speakingSubmissions: 2 },
  freshness: { generatedAt: '2026-04-22T10:00:00Z', usesFallbackSeries: false, eTag: 'W/"abc"' },
};

describe('ProgressTrendChart', () => {
  it('shows the Grade B reference line', () => {
    render(<ProgressTrendChart payload={samplePayload} visibleSubtests={new Set(['writing', 'speaking', 'reading', 'listening'])} />);
    const refLine = screen.getByText(/Grade B \(350\)/);
    expect(refLine).toBeInTheDocument();
  });

  it('shows the empty state when fewer trend points than minEvaluationsForTrend', () => {
    const sparse = { ...samplePayload, trend: [] };
    render(<ProgressTrendChart payload={sparse} visibleSubtests={new Set(['writing'])} />);
    expect(screen.getByText(/Not enough data yet/i)).toBeInTheDocument();
  });

  it('renders the country-aware Writing threshold when it differs from Grade B', () => {
    const payload = {
      ...samplePayload,
      meta: { ...baseMeta, writingThreshold: 300, writingThresholdGrade: 'C+', targetCountry: 'US' },
    };
    render(<ProgressTrendChart payload={payload} visibleSubtests={new Set(['writing'])} />);
    expect(screen.getByText(/Writing 300 \(C\+\)/)).toBeInTheDocument();
  });
});

describe('ProgressSubtestMiniCards', () => {
  it('renders one toggle button per subtest', () => {
    render(<ProgressSubtestMiniCards subtests={baseSubtests} visible={new Set(['writing', 'speaking', 'reading', 'listening'])} onToggle={() => {}} />);
    expect(screen.getByLabelText(/Toggle Writing series/)).toBeInTheDocument();
    expect(screen.getByLabelText(/Toggle Speaking series/)).toBeInTheDocument();
    expect(screen.getByLabelText(/Toggle Reading series/)).toBeInTheDocument();
    expect(screen.getByLabelText(/Toggle Listening series/)).toBeInTheDocument();
  });

  it('reflects aria-pressed visibility state', () => {
    render(<ProgressSubtestMiniCards subtests={baseSubtests} visible={new Set(['writing'])} onToggle={() => {}} />);
    const writingBtn = screen.getByLabelText(/Toggle Writing series/);
    expect(writingBtn).toHaveAttribute('aria-pressed', 'true');
    const speakingBtn = screen.getByLabelText(/Toggle Speaking series/);
    expect(speakingBtn).toHaveAttribute('aria-pressed', 'false');
  });

  it('fires onToggle with the subtest code', () => {
    const onToggle = vi.fn();
    render(<ProgressSubtestMiniCards subtests={baseSubtests} visible={new Set(['writing'])} onToggle={onToggle} />);
    fireEvent.click(screen.getByLabelText(/Toggle Speaking series/));
    expect(onToggle).toHaveBeenCalledWith('speaking');
  });

  it('shows a hint to set country when Writing threshold reason is country_required', () => {
    const subtests = baseSubtests.map((s) =>
      s.subtestCode === 'writing'
        ? { ...s, thresholdReason: 'country_required' as string | null, thresholdScaled: null as number | null }
        : s,
    );
    render(<ProgressSubtestMiniCards subtests={subtests} visible={new Set(['writing'])} onToggle={() => {}} />);
    expect(screen.getByText(/Set your target country/i)).toBeInTheDocument();
  });
});

describe('ProgressComparativeTab', () => {
  it('shows the cohort-too-small message when below MinCohortSize', () => {
    render(<ProgressComparativeTab comparative={{ subtests: [], cohortSize: 5, minCohortSize: 30, hasSufficientCohort: false, cohortScopeDescription: 'OET medicine learners (last 90 days)' }} />);
    expect(screen.getByText(/Waiting for more peers/)).toBeInTheDocument();
  });

  it('renders comparative cards when cohort sufficient', () => {
    const comparative = {
      subtests: [
        { subtestCode: 'writing', yourScaled: 350, cohortAverage: 320, cohortMedian: 325, percentile: 75, tier: 'top25' as const },
      ],
      cohortSize: 35,
      minCohortSize: 30,
      hasSufficientCohort: true,
      cohortScopeDescription: 'OET medicine learners (last 90 days)',
    };
    render(<ProgressComparativeTab comparative={comparative} />);
    expect(screen.getByText('Top 25%')).toBeInTheDocument();
    expect(screen.getByText('writing')).toBeInTheDocument();
    expect(screen.getByText('75.0%')).toBeInTheDocument();
  });

  it('shows graceful empty state when comparative is null', () => {
    render(<ProgressComparativeTab comparative={null} />);
    expect(screen.getByText(/Comparative analytics are not available/i)).toBeInTheDocument();
  });
});

describe('ChartTabularFallback', () => {
  it('renders a screen-reader-only table with caption + headers + rows', () => {
    render(<ChartTabularFallback caption="Test chart" headers={['Week', 'Writing']} rows={[['W15', 320], ['W16', 340]]} />);
    expect(screen.getByText('Test chart')).toBeInTheDocument();
    expect(screen.getByText('320')).toBeInTheDocument();
    expect(screen.getByText('340')).toBeInTheDocument();
  });

  it('renders an em-dash for null cells', () => {
    render(<ChartTabularFallback caption="Test" headers={['Week', 'Reading']} rows={[['W15', null]]} />);
    expect(screen.getByText('—')).toBeInTheDocument();
  });
});

describe('ProgressRangePills', () => {
  it('marks the active range as aria-checked', () => {
    render(<ProgressRangePills value="30d" onChange={() => {}} />);
    expect(screen.getByRole('radio', { name: '30d' })).toHaveAttribute('aria-checked', 'true');
    expect(screen.getByRole('radio', { name: '14d' })).toHaveAttribute('aria-checked', 'false');
  });

  it('fires onChange with the new value', () => {
    const onChange = vi.fn();
    render(<ProgressRangePills value="90d" onChange={onChange} />);
    fireEvent.click(screen.getByRole('radio', { name: '14d' }));
    expect(onChange).toHaveBeenCalledWith('14d');
  });
});

describe('ProgressReadinessStrip', () => {
  it('hides itself when no targets and no exam date are set', () => {
    const payload = {
      ...samplePayload,
      goals: { targetWritingScore: null, targetSpeakingScore: null, targetReadingScore: null, targetListeningScore: null, targetExamDate: null, daysToExam: null, targetCountry: null },
    };
    const { container } = render(<ProgressReadinessStrip payload={payload} />);
    expect(container.firstChild).toBeNull();
  });

  it('shows the days-to-exam pill when present', () => {
    render(<ProgressReadinessStrip payload={samplePayload} />);
    expect(screen.getByText(/70 days to exam/)).toBeInTheDocument();
  });

  it('shows score-guarantee strip when policy enables it', () => {
    render(<ProgressReadinessStrip payload={samplePayload} />);
    expect(screen.getByText(/Score Guarantee eligible/)).toBeInTheDocument();
  });

  it('hides score-guarantee strip when policy disables it', () => {
    const payload = { ...samplePayload, meta: { ...baseMeta, showScoreGuaranteeStrip: false } };
    render(<ProgressReadinessStrip payload={payload} />);
    expect(screen.queryByText(/Score Guarantee eligible/)).not.toBeInTheDocument();
  });
});
