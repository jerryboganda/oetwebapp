import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

const {
  mockMyPathway,
  mockRouterPush,
  mockNotFound,
  mockTrack,
} = vi.hoisted(() => ({
  mockMyPathway: vi.fn(),
  mockRouterPush: vi.fn(),
  mockNotFound: vi.fn(),
  mockTrack: vi.fn(),
}));

vi.mock('next/link', () => ({
  default: ({ children, href }: { children: React.ReactNode; href?: string }) => <a href={href}>{children}</a>,
}));

vi.mock('next/navigation', () => ({
  useParams: () => ({ part: 'b' }),
  useRouter: () => ({ push: mockRouterPush }),
  notFound: () => mockNotFound(),
}));

vi.mock('@/components/layout', () => ({
  LearnerDashboardShell: ({ children }: { children: React.ReactNode }) => <div data-testid="learner-shell">{children}</div>,
}));

vi.mock('@/components/domain', () => ({
  LearnerPageHero: ({ title, description }: { title: string; description: string }) => (
    <header><h1>{title}</h1><p>{description}</p></header>
  ),
}));

vi.mock('@/components/domain/learner-skeletons', () => ({
  LearnerSkeleton: () => <div data-testid="skeleton" />,
}));

vi.mock('@/components/ui/alert', () => ({
  InlineAlert: ({ children, variant }: { children: React.ReactNode; variant?: string }) => (
    <div role="alert" data-variant={variant}>{children}</div>
  ),
}));

vi.mock('@/lib/analytics', () => ({
  analytics: { track: mockTrack },
}));

vi.mock('@/lib/listening/v2-api', () => ({
  listeningV2Api: { myPathway: mockMyPathway },
}));

import ListeningPartPracticePage from './page';

interface StageOverrides {
  stage: string;
  status?: 'Locked' | 'Unlocked' | 'InProgress' | 'Completed';
  actionHref?: string | null;
}

function stage({ stage, status = 'Unlocked', actionHref = null }: StageOverrides) {
  return { stage, status, scaledScore: null, completedAt: null, actionHref };
}

// A pathway where the diagnostic has NOT been completed (it is merely Unlocked),
// yet the part foundations are unlocked entry points with runnable papers — the
// post-change behaviour where the diagnostic is optional, not a gate.
function pathwayWithoutDiagnostic() {
  return [
    stage({ stage: 'diagnostic', status: 'Unlocked', actionHref: '/listening/player/lp-1?mode=diagnostic&pathwayStage=diagnostic' }),
    stage({ stage: 'foundation_partA', actionHref: '/listening/player/lp-1?mode=practice&pathwayStage=foundation_partA' }),
    stage({ stage: 'foundation_partB', actionHref: '/listening/player/lp-1?mode=practice&pathwayStage=foundation_partB' }),
    stage({ stage: 'foundation_partC', actionHref: '/listening/player/lp-1?mode=practice&pathwayStage=foundation_partC' }),
    stage({ stage: 'drill_partA', status: 'Locked' }),
  ];
}

describe('Listening part practice dispatcher', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mockMyPathway.mockResolvedValue(pathwayWithoutDiagnostic());
  });

  it('lets a learner start Part B practice without completing the diagnostic', async () => {
    render(<ListeningPartPracticePage />);

    expect(await screen.findByRole('button', { name: /start part b practice/i })).toBeInTheDocument();
    // The old diagnostic-gate message must be gone.
    expect(screen.queryByText(/complete the listening diagnostic/i)).not.toBeInTheDocument();
    expect(mockNotFound).not.toHaveBeenCalled();
  });

  it('launches the part-scoped player with the Part B focus params', async () => {
    const user = userEvent.setup();
    render(<ListeningPartPracticePage />);

    await user.click(await screen.findByRole('button', { name: /start part b practice/i }));

    expect(mockRouterPush).toHaveBeenCalledTimes(1);
    const pushedUrl = mockRouterPush.mock.calls[0][0] as string;
    expect(pushedUrl).toContain('/listening/player/lp-1');
    expect(pushedUrl).toContain('mode=practice');
    expect(pushedUrl).toContain('pathwayStage=foundation_partB');
    expect(pushedUrl).toContain('focus=part-b');
    expect(pushedUrl).toContain('part=B');
  });

  it('shows a non-diagnostic fallback when no runnable paper is available', async () => {
    mockMyPathway.mockResolvedValue([
      stage({ stage: 'foundation_partB', status: 'Unlocked', actionHref: null }),
    ]);

    render(<ListeningPartPracticePage />);

    await waitFor(() => expect(mockMyPathway).toHaveBeenCalled());
    expect(await screen.findByText(/no part b listening paper is available yet/i)).toBeInTheDocument();
    expect(screen.queryByText(/complete the listening diagnostic/i)).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /start part b practice/i })).not.toBeInTheDocument();
  });
});
