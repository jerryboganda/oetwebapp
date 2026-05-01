import { screen } from '@testing-library/react';
const { mockAddToMyVocabulary, mockFetchMockReport, mockFetchVocabularyTerms, mockTrack } = vi.hoisted(() => ({
  mockAddToMyVocabulary: vi.fn(),
  mockFetchMockReport: vi.fn(),
  mockFetchVocabularyTerms: vi.fn(),
  mockTrack: vi.fn(),
}));

vi.mock('next/link', () => ({
  default: ({ children, href }: { children: React.ReactNode; href?: string }) => <a href={href}>{children}</a>,
}));


vi.mock('@/components/layout', () => ({
  LearnerDashboardShell: ({ children, workspaceClassName }: { children: React.ReactNode; workspaceClassName?: string }) => (
    <div data-testid="learner-dashboard-shell" data-workspace-class={workspaceClassName}>{children}</div>
  ),
}));

vi.mock('@/lib/analytics', () => ({
  analytics: {
    track: mockTrack,
  },
}));

vi.mock('@/lib/api', () => ({
  addToMyVocabulary: mockAddToMyVocabulary,
  fetchMockReport: mockFetchMockReport,
  fetchVocabularyTerms: mockFetchVocabularyTerms,
}));

import MockReportPage from './page';
import { renderWithRouter } from '@/tests/test-utils';

describe('Mock report page', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mockFetchMockReport.mockResolvedValue({
      id: 'mock-1',
      title: 'Full OET Mock Test',
      date: '2026-03-29',
      overallScore: '68%',
      summary: 'Mock evidence shows reading gains are transferring.',
      priorComparison: {
        exists: true,
        priorMockName: 'Mock A',
        overallTrend: 'up',
        details: 'Overall score improved from the prior attempt.',
      },
      subTests: [
        { id: 'reading', name: 'Reading', rawScore: '28/42', score: '68%' },
        { id: 'writing', name: 'Writing', rawScore: 'Pending', score: 'Pending', reviewState: 'in_review' },
      ],
      weakestCriterion: {
        subtest: 'Reading',
        criterion: 'Detail Extraction',
        description: 'More work is needed on exact detail extraction under time pressure.',
      },
      reviewSummary: {
        queued: 0,
        inReview: 1,
        completed: 1,
        pending: 0,
      },
    });
    mockFetchVocabularyTerms.mockResolvedValue([]);
    mockAddToMyVocabulary.mockResolvedValue({});
  });

  it('renders through the shared learner dashboard shell without a second page-root width wrapper', async () => {
    const { container } = renderWithRouter(<MockReportPage />);

    expect(await screen.findByText('Overall Performance')).toBeInTheDocument();
    expect(screen.getByTestId('learner-dashboard-shell')).toBeInTheDocument();
    expect(container.querySelector('[class*="max-w-3xl"][class*="mx-auto"][class*="px-4"]')).not.toBeInTheDocument();
  });

  it('surfaces ethical readiness guidance and teacher-review gating', async () => {
    renderWithRouter(<MockReportPage />);

    expect(await screen.findByText('Borderline readiness')).toBeInTheDocument();
    expect(screen.getByText('Estimated academy report')).toBeInTheDocument();
    expect(screen.getByText(/do not treat mock results as a guaranteed pass/i)).toBeInTheDocument();
    expect(screen.getByText('Teacher-marked sections still affect the final readiness report')).toBeInTheDocument();
    expect(screen.getByText(/Writing\/Speaking readiness should remain provisional until tutor feedback is returned/i)).toBeInTheDocument();
    expect(screen.getByText('Review in review')).toBeInTheDocument();
  });

  it('ends the report with a concrete 7-day remediation workflow', async () => {
    renderWithRouter(<MockReportPage />);

    expect(await screen.findByText('Your next 7-day plan')).toBeInTheDocument();
    expect(screen.getByText('Review every lost mark')).toBeInTheDocument();
    expect(screen.getByText('Repair Detail Extraction')).toBeInTheDocument();
    expect(screen.getByText('Complete a targeted micro-drill')).toBeInTheDocument();
    expect(screen.getByText('Attempt a sectional mock')).toBeInTheDocument();
    expect(screen.getByText('Book tutor review or retake')).toBeInTheDocument();
  });
});
