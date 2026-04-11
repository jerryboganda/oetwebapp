import { render, screen } from '@testing-library/react';
const { mockFetchReadingHome, mockFetchMockReports, mockTrack, mockUseAuth } = vi.hoisted(() => ({
  mockFetchReadingHome: vi.fn(),
  mockFetchMockReports: vi.fn(),
  mockTrack: vi.fn(),
  mockUseAuth: vi.fn(),
}));

vi.mock('next/link', () => ({
  default: ({ children, href, ...props }: any) => <a href={href} {...props}>{children}</a>,
}));

vi.mock('motion/react', () => ({
  motion: {
    div: ({ children, initial: _initial, animate: _animate, transition: _transition, ...props }: any) => <div {...props}>{children}</div>,
  },
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

vi.mock('@/lib/api', () => ({
  fetchReadingHome: mockFetchReadingHome,
  fetchMockReports: mockFetchMockReports,
}));

import ReadingPage from './page';

describe('Reading page', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mockUseAuth.mockReturnValue({
      isAuthenticated: true,
      loading: false,
    });
    mockFetchReadingHome.mockResolvedValue({
      intro: 'Reading practice focuses on rapid detail extraction and inference control.',
      featuredTasks: [
        {
          contentId: 'rt-001',
          title: 'Health Policy - Hospital-Acquired Infections',
          difficulty: 'medium',
          estimatedDurationMinutes: 30,
          scenarioType: 'Part C',
        },
      ],
      mockSets: [{ route: '/mocks/setup' }],
    });
    mockFetchMockReports.mockResolvedValue([
      {
        id: 'mock-1',
        title: 'Reading Mock',
        summary: 'Reading gains are transferring.',
        date: '2026-03-29',
        overallScore: '68%',
      },
    ]);
  });

  it('renders through the shared learner dashboard shell without a second page-root width wrapper', async () => {
    const { container } = render(<ReadingPage />);

    expect(await screen.findByText('Build reading accuracy before you validate it in mocks')).toBeInTheDocument();
    expect(screen.getByTestId('learner-dashboard-shell')).toBeInTheDocument();
    expect(container.querySelector('[class*="max-w-5xl"][class*="mx-auto"][class*="px-4"]')).not.toBeInTheDocument();
  });
});
