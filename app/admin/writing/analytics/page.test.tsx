import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import type { WritingAdminAnalyticsDto, WritingMarkingQualityDto } from '@/lib/writing/types';

const { mockGetOverview, mockGetQuality } = vi.hoisted(() => ({
  mockGetOverview: vi.fn(),
  mockGetQuality: vi.fn(),
}));

vi.mock('@/lib/writing/exam-api', () => ({
  getWritingAdminAnalytics: mockGetOverview,
  getWritingMarkingQuality: mockGetQuality,
}));

import AdminWritingAnalyticsOverviewPage from './page';

const baseOverview: WritingAdminAnalyticsDto = {
  totals: { tasks: 12, submissions: 140, reviewed: 100, learners: 65 },
  averageCriteria: {
    c1: 2.1,
    c2: 2.2,
    c3: 2.0,
    c4: 2.3,
    c5: 2.1,
    c6: 2.0,
  },
  averageBandByProfession: [
    { profession: 'medicine', averageBand: 355, attempts: 80 },
    { profession: 'nursing', averageBand: 346, attempts: 60 },
  ],
  averageBandByLetterType: [
    { letterType: 'LT-RR', averageBand: 352, attempts: 54 },
    { letterType: 'LT-UR', averageBand: 347, attempts: 46 },
  ],
  hardestTasks: [
    { taskId: 'task-1', title: 'Chest pain referral', averageBand: 338, attempts: 18 },
  ],
  commonMissingContent: [
    { itemText: 'Allergies not stated', count: 21 },
  ],
  commonIrrelevantContent: [
    { itemText: 'Excess social history', count: 15 },
  ],
  commonLanguageErrors: [
    {
      ruleId: 'W-GRAMMAR-1',
      ruleText: 'Subject/verb agreement',
      count: 26,
      criterion: 'c4',
    },
  ],
  wordCountDistribution: [
    { bucketLabel: '0-150', count: 13 },
    { bucketLabel: '151-200', count: 70 },
    { bucketLabel: '201-250', count: 57 },
  ],
  writingPhaseSeconds: { average: 1245, median: 1190 },
  abandonmentRatePercent: 9.2,
  resubmissionImprovementAverage: 14,
};

const baseQuality: WritingMarkingQualityDto = {
  tutorConsistency: [
    {
      tutorId: 'tutor-1',
      displayName: 'Dr Ahmed',
      reviews: 24,
      averageRawTotal: 13.1,
      leniencyDelta: 0.3,
      agreementCoefficient: 0.86,
    },
  ],
  aiVsTutorVariance: { meanAbsoluteDelta: 0.55, samples: 48 },
  averageReviewTurnaroundHours: 7.2,
  criteriaDisagreement: [
    { criterion: 'c1', meanAbsoluteDelta: 0.31 },
    { criterion: 'c4', meanAbsoluteDelta: 0.42 },
  ],
  moderationsTriggered: 5,
};

describe('AdminWritingAnalyticsOverviewPage', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mockGetOverview.mockResolvedValue(baseOverview);
    mockGetQuality.mockResolvedValue(baseQuality);
  });

  it('renders overview metrics and deep-link to rule violations', async () => {
    render(<AdminWritingAnalyticsOverviewPage />);

    expect(await screen.findByText('Writing analytics overview')).toBeInTheDocument();
    expect(screen.getByText('140')).toBeInTheDocument();

    const ruleLink = screen.getByRole('link', { name: /open rule violation dashboard/i });
    expect(ruleLink).toHaveAttribute('href', '/admin/writing/analytics/rule-violations');

    await waitFor(() => {
      expect(mockGetOverview).toHaveBeenCalledWith(
        expect.objectContaining({
          fromDate: expect.any(String),
          toDate: expect.any(String),
        }),
      );
    });
  });

  it('sends profession and letter-type filters to analytics endpoints', async () => {
    const user = userEvent.setup();
    render(<AdminWritingAnalyticsOverviewPage />);

    await screen.findByText('Writing analytics overview');
    await user.selectOptions(screen.getByLabelText('Profession filter'), 'medicine');
    await user.selectOptions(screen.getByLabelText('Letter type filter'), 'LT-UR');

    await waitFor(() => {
      expect(mockGetOverview).toHaveBeenLastCalledWith(
        expect.objectContaining({ profession: 'medicine', letterType: 'LT-UR' }),
      );
      expect(mockGetQuality).toHaveBeenLastCalledWith(
        expect.objectContaining({ profession: 'medicine', letterType: 'LT-UR' }),
      );
    });
  });

  it('shows an alert when loading analytics fails', async () => {
    mockGetOverview.mockRejectedValueOnce(new Error('analytics offline'));
    mockGetQuality.mockRejectedValueOnce(new Error('analytics offline'));

    render(<AdminWritingAnalyticsOverviewPage />);

    const alert = await screen.findByRole('alert');
    expect(alert).toHaveTextContent('analytics offline');

    const ruleLink = screen.getByRole('link', { name: /rule violations/i });
    expect(ruleLink).toHaveAttribute('href', '/admin/writing/analytics/rule-violations');
  });
});