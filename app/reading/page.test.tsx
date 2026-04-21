import { render, screen } from '@testing-library/react';

const { mockGetReadingHome, mockTrack, mockUseAuth } = vi.hoisted(() => ({
  mockGetReadingHome: vi.fn(),
  mockTrack: vi.fn(),
  mockUseAuth: vi.fn(),
}));

vi.mock('next/link', () => ({
  default: ({ children, href }: { children: React.ReactNode; href?: string }) => <a href={href}>{children}</a>,
}));

vi.mock('@/components/layout', () => ({
  LearnerDashboardShell: ({ children, workspaceClassName }: { children: React.ReactNode; workspaceClassName?: string }) => (
    <div data-testid="learner-dashboard-shell" data-workspace-class={workspaceClassName}>{children}</div>
  ),
}));

vi.mock('@/contexts/auth-context', () => ({
  useAuth: () => mockUseAuth(),
}));

vi.mock('@/lib/analytics', () => ({
  analytics: {
    track: mockTrack,
  },
}));

vi.mock('@/lib/reading-authoring-api', () => ({
  getReadingHome: mockGetReadingHome,
}));

import ReadingPage from './page';

describe('Reading page', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mockUseAuth.mockReturnValue({
      isAuthenticated: true,
      loading: false,
    });
    mockGetReadingHome.mockResolvedValue({
      intro: 'Reading practice uses full structured papers.',
      papers: [
        {
          id: 'paper-1',
          title: 'Reading Sample Paper 1',
          slug: 'reading-sample-paper-1',
          difficulty: 'standard',
          estimatedDurationMinutes: 60,
          publishedAt: '2026-04-20T00:00:00Z',
          route: '/reading/paper/paper-1',
          partACount: 20,
          partBCount: 6,
          partCCount: 16,
          totalPoints: 42,
          partATimerMinutes: 15,
          partBCTimerMinutes: 45,
          lastAttempt: null,
        },
      ],
      activeAttempts: [],
      recentResults: [],
      policy: {
        partATimerMinutes: 15,
        partBCTimerMinutes: 45,
        allowPausingAttempt: false,
        allowResumeAfterExpiry: false,
        showCorrectAnswerOnReview: true,
        showExplanationsAfterSubmit: true,
      },
      safeDrills: [],
    });
  });

  it('renders structured Reading papers without linking to the legacy player', async () => {
    const { container } = render(<ReadingPage />);

    expect(await screen.findByText('Build reading accuracy before you validate it in mocks')).toBeInTheDocument();
    expect(screen.getByText('Reading Sample Paper 1')).toBeInTheDocument();
    expect(screen.getByRole('link', { name: /start paper/i })).toHaveAttribute('href', '/reading/paper/paper-1');
    expect(container.querySelector('a[href="/reading/player/rt-001"]')).not.toBeInTheDocument();
    expect(screen.getByTestId('learner-dashboard-shell')).toBeInTheDocument();
    expect(container.querySelector('[class*="max-w-5xl"][class*="mx-auto"][class*="px-4"]')).not.toBeInTheDocument();
  });
});
