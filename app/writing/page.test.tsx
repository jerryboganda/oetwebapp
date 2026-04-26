import { render, screen } from '@testing-library/react';
import type { WritingTask, WritingSubmission } from '@/lib/mock-data';

const { mockFetchWritingHome, mockFetchWritingTasks, mockFetchWritingSubmissions, mockFetchMockReports, mockTrack } = vi.hoisted(() => ({
  mockFetchWritingHome: vi.fn(),
  mockFetchWritingTasks: vi.fn(),
  mockFetchWritingSubmissions: vi.fn(),
  mockFetchMockReports: vi.fn(),
  mockTrack: vi.fn(),
}));

vi.mock('next/link', () => ({
  default: ({ children, href }: { children: React.ReactNode; href?: string }) => <a href={href}>{children}</a>,
}));

vi.mock('next/navigation', () => ({
  useRouter: () => ({ push: vi.fn() }),
}));
vi.mock('@/components/layout/app-shell', () => ({
  LearnerDashboardShell: ({ children }: { children: React.ReactNode }) => <div data-testid="learner-dashboard-shell">{children}</div>,
}));

vi.mock('@/components/layout/admin-dashboard-shell', () => ({
  LearnerDashboardShell: ({ children }: { children: React.ReactNode }) => <div data-testid="learner-dashboard-shell">{children}</div>,
}));

vi.mock('@/components/layout/expert-dashboard-shell', () => ({
  LearnerDashboardShell: ({ children }: { children: React.ReactNode }) => <div data-testid="learner-dashboard-shell">{children}</div>,
}));

vi.mock('@/components/layout/learner-dashboard-shell', () => ({
  LearnerDashboardShell: ({ children }: { children: React.ReactNode }) => <div data-testid="learner-dashboard-shell">{children}</div>,
}));

vi.mock('@/components/layout/sponsor-dashboard-shell', () => ({
  LearnerDashboardShell: ({ children }: { children: React.ReactNode }) => <div data-testid="learner-dashboard-shell">{children}</div>,
}));

vi.mock('@/components/layout/learner-workspace-container', () => ({
  LearnerDashboardShell: ({ children }: { children: React.ReactNode }) => <div data-testid="learner-dashboard-shell">{children}</div>,
}));

vi.mock('@/components/layout/notification-center', () => ({
  LearnerDashboardShell: ({ children }: { children: React.ReactNode }) => <div data-testid="learner-dashboard-shell">{children}</div>,
}));

vi.mock('@/components/layout/notification-preferences-panel', () => ({
  LearnerDashboardShell: ({ children }: { children: React.ReactNode }) => <div data-testid="learner-dashboard-shell">{children}</div>,
}));

vi.mock('@/components/layout/top-nav', () => ({
  LearnerDashboardShell: ({ children }: { children: React.ReactNode }) => <div data-testid="learner-dashboard-shell">{children}</div>,
}));

vi.mock('@/components/layout/sidebar', () => ({
  LearnerDashboardShell: ({ children }: { children: React.ReactNode }) => <div data-testid="learner-dashboard-shell">{children}</div>,
}));

vi.mock('@/lib/analytics', () => ({ analytics: { track: mockTrack } }));

vi.mock('@/lib/api', () => ({
  fetchWritingHome: mockFetchWritingHome,
  fetchWritingTasks: mockFetchWritingTasks,
  fetchWritingSubmissions: mockFetchWritingSubmissions,
  fetchMockReports: mockFetchMockReports,
}));

import WritingHome from './page';

describe('Writing home page', () => {
  beforeEach(() => {
    vi.clearAllMocks();

    mockFetchWritingHome.mockResolvedValue({
      recommendedTask: {
        id: 'wt-001',
        title: 'Routine referral — dermatology',
        profession: 'Medicine',
        time: '40 mins',
        criteriaFocus: ['content', 'purpose'],
        scenarioType: 'Routine referral',
      },
      reviewCredits: { available: 2 },
      fullMockEntry: { title: 'Full mock', route: '/mocks/setup' },
    });

    mockFetchWritingTasks.mockResolvedValue([
      {
        id: 'wt-001',
        title: 'Routine referral — dermatology',
        profession: 'Medicine',
        difficulty: 'Medium',
        scenarioType: 'Routine referral',
        time: '40 mins',
        criteriaFocus: 'content',
        caseNotes: 'Case notes here',
      },
    ] satisfies WritingTask[]);

    mockFetchWritingSubmissions.mockResolvedValue([] satisfies WritingSubmission[]);
    mockFetchMockReports.mockResolvedValue([]);
  });

  it('shows rulebook entry points on the writing home surface', async () => {
    render(<WritingHome />);

    expect(await screen.findByText(/Study the exact rules your writing is judged against/i)).toBeInTheDocument();
    expect(screen.getByRole('link', { name: /Open Rulebook/i })).toHaveAttribute('href', '/writing/rulebook');
    expect(screen.getByRole('link', { name: /Model Answers/i })).toHaveAttribute('href', '/writing/model');
  });
});
