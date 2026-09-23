import { render, screen, waitFor } from '@testing-library/react';

const { mockListScenarios, mockListFreeSamples } = vi.hoisted(() => ({
  mockListScenarios: vi.fn(),
  mockListFreeSamples: vi.fn(),
}));

vi.mock('next/link', () => ({
  default: ({ children, href, ...rest }: React.AnchorHTMLAttributes<HTMLAnchorElement> & { href?: string }) => (
    <a href={href} {...rest}>
      {children}
    </a>
  ),
}));
vi.mock('@/components/layout/learner-dashboard-shell', () => ({
  LearnerDashboardShell: ({ children }: { children: React.ReactNode }) => <div>{children}</div>,
}));
vi.mock('@/components/domain/learner-surface', () => ({
  LearnerPageHero: ({ title }: { title: string }) => <h1>{title}</h1>,
  LearnerSurfaceSectionHeader: ({ title }: { title: string }) => <h2>{title}</h2>,
}));
vi.mock('@/lib/analytics', () => ({ analytics: { track: vi.fn() } }));
vi.mock('@/lib/writing/api', () => ({ listWritingScenarios: mockListScenarios }));
vi.mock('@/lib/api/free-samples', () => ({ listFreeSamples: mockListFreeSamples }));

import WritingPracticeLibraryPage from './page';

const SCENARIO = {
  id: 'sc-1',
  title: 'Discharge letter for Mr Jones',
  profession: 'medicine',
  letterType: 'LT-DG',
  topics: ['diabetes'],
};

describe('Writing practice library', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mockListScenarios.mockResolvedValue({ items: [SCENARIO], total: 1 });
    mockListFreeSamples.mockResolvedValue([]);
  });

  it('has no learner profession filter and never sends a profession to the catalogue API', async () => {
    render(<WritingPracticeLibraryPage />);

    expect(await screen.findByText('Discharge letter for Mr Jones')).toBeInTheDocument();
    expect(screen.queryByText('writing.practice.library.filters.profession')).not.toBeInTheDocument();
    // Only the letter-type select remains.
    expect(screen.getAllByRole('combobox')).toHaveLength(1);
    expect(mockListScenarios).toHaveBeenCalled();
    for (const [params] of mockListScenarios.mock.calls) expect(params).not.toHaveProperty('profession');
  });

  it('shows the same free-sample state as the hub (launcher data), not a separate featured entry', async () => {
    mockListFreeSamples.mockResolvedValue([{
      professionId: 'medicine',
      contentId: 'sc-1',
      state: 'retry_available',
      route: '/writing/submissions/sub-1/revise',
      limit: 2,
      successfulCount: 1,
      remaining: 1,
      lastResultRoute: '/writing/submissions/sub-1/results',
      lastSubmissionId: 'sub-1',
    }]);
    render(<WritingPracticeLibraryPage />);

    const card = await screen.findByTestId('writing-library-free-sample-card');
    expect(card).toHaveAttribute('href', '/writing/submissions/sub-1/revise');
    expect(card).toHaveTextContent('freeSample.writing.retryCta');
    expect(mockListFreeSamples).toHaveBeenCalledWith('writing');
    expect(screen.queryByText('Free featured case note')).not.toBeInTheDocument();
  });

  it('hides the free-sample entry when none is on offer', async () => {
    render(<WritingPracticeLibraryPage />);

    await screen.findByText('Discharge letter for Mr Jones');
    await waitFor(() => expect(mockListFreeSamples).toHaveBeenCalled());
    expect(screen.queryByTestId('writing-library-free-sample-card')).not.toBeInTheDocument();
  });
});
