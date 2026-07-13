import { render, screen } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';

const { mockFetchLeaderboard, mockFetchMyPosition, mockSetOptIn } = vi.hoisted(() => ({
  mockFetchLeaderboard: vi.fn(),
  mockFetchMyPosition: vi.fn(),
  mockSetOptIn: vi.fn(),
}));

vi.mock('@/components/layout', () => ({
  LearnerDashboardShell: ({ children }: { children: React.ReactNode }) => <div>{children}</div>,
}));

vi.mock('@/components/domain', () => ({
  LearnerPageHero: ({ title }: { title: string }) => <h1>{title}</h1>,
}));

vi.mock('@/components/ui/motion-primitives', () => ({
  MotionItem: ({ children, delayIndex, ...props }: React.HTMLAttributes<HTMLDivElement> & { delayIndex?: number }) => (
    <div data-animated="true" data-delay-index={delayIndex} {...props}>{children}</div>
  ),
}));

vi.mock('@/components/ui/skeleton', () => ({
  Skeleton: () => <div data-testid="skeleton" />,
}));

vi.mock('@/components/ui/alert', () => ({
  InlineAlert: ({ children }: { children: React.ReactNode }) => <div role="alert">{children}</div>,
}));

vi.mock('@/lib/analytics', () => ({
  analytics: { track: vi.fn() },
}));

vi.mock('@/lib/api', () => ({
  fetchLeaderboard: mockFetchLeaderboard,
  fetchMyLeaderboardPosition: mockFetchMyPosition,
  setLeaderboardOptIn: mockSetOptIn,
}));

import LeaderboardPage from './page';

describe('Leaderboard page', () => {
  it('bounds rendered and animated rows while retaining the current learner', async () => {
    mockFetchLeaderboard.mockResolvedValue({
      entries: Array.from({ length: 100 }, (_, index) => ({
        rank: index + 1,
        displayName: `Learner ${index + 1}`,
        totalXp: 10_000 - index,
        level: 5,
        isCurrentUser: index === 98,
      })),
    });
    mockFetchMyPosition.mockResolvedValue({
      rank: 99,
      totalXp: 9_902,
      level: 5,
      optedIn: true,
    });
    const client = new QueryClient({
      defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
    });

    render(
      <QueryClientProvider client={client}>
        <LeaderboardPage />
      </QueryClientProvider>,
    );

    expect(await screen.findByText('Learner 1')).toBeInTheDocument();
    expect(screen.getAllByTestId('leaderboard-entry')).toHaveLength(50);
    expect(screen.getByText('Learner 99')).toBeInTheDocument();
    expect(screen.getByText('(you)')).toBeInTheDocument();
    expect(screen.queryByText('Learner 50')).not.toBeInTheDocument();
    expect(document.querySelectorAll('[data-animated="true"]')).toHaveLength(20);
  });
});
