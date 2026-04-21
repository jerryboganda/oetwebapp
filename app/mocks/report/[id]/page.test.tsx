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
      ],
      weakestCriterion: {
        subtest: 'Reading',
        criterion: 'Detail Extraction',
        description: 'More work is needed on exact detail extraction under time pressure.',
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
});
