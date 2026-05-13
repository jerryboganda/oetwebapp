import { render, screen } from '@testing-library/react';

const { mockMyPathway } = vi.hoisted(() => ({
  mockMyPathway: vi.fn(),
}));

vi.mock('@/components/layout', () => ({
  LearnerDashboardShell: ({ children }: { children: React.ReactNode }) => <div data-testid="learner-dashboard-shell">{children}</div>,
}));

vi.mock('@/lib/listening/v2-api', () => ({
  listeningV2Api: {
    myPathway: mockMyPathway,
  },
}));

import ListeningPathwayPage from './page';

describe('ListeningPathwayPage', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mockMyPathway.mockResolvedValue([
      { stage: 'diagnostic', status: 'Completed', scaledScore: 360, completedAt: '2026-05-11T00:00:00Z', actionHref: null },
      {
        stage: 'foundation_partA',
        status: 'InProgress',
        scaledScore: 320,
        completedAt: null,
        actionHref: '/listening/player/lp-001?mode=practice&pathwayStage=foundation_partA',
      },
      {
        stage: 'foundation_partB',
        status: 'Unlocked',
        scaledScore: null,
        completedAt: null,
        actionHref: '/listening/player/lp-001?mode=practice&pathwayStage=foundation_partB',
      },
      ...Array.from({ length: 9 }, (_, index) => ({
        stage: `locked_${index}`,
        status: 'Locked',
        scaledScore: null,
        completedAt: null,
        actionHref: null,
      })),
    ]);
  });

  it('renders the pathway in the learner shell with progress and actionable unlocked stages', async () => {
    render(<ListeningPathwayPage />);

    expect(await screen.findByRole('heading', { name: 'Your 12-stage Listening pathway' })).toBeInTheDocument();
    expect(screen.getByTestId('learner-dashboard-shell')).toBeInTheDocument();
    expect(screen.getByRole('heading', { name: '8% complete' })).toBeInTheDocument();
    expect(screen.getByRole('progressbar', { name: 'Listening pathway progress' })).toHaveAttribute('aria-valuenow', '8');
    expect(screen.getByRole('link', { name: /Continue Foundation - Part A/i })).toHaveAttribute(
      'href',
      '/listening/player/lp-001?mode=practice&pathwayStage=foundation_partA',
    );
    expect(screen.getAllByText(/^Stage \d+$/i)).toHaveLength(12);
  });
});