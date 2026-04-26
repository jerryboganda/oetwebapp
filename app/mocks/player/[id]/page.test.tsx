import { screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
const { mockCompleteMockSection, mockFetchMockSession, mockStartMockSection, mockSubmitMockSession, mockTrack, mockPush } = vi.hoisted(() => ({
  mockCompleteMockSection: vi.fn(),
  mockFetchMockSession: vi.fn(),
  mockStartMockSection: vi.fn(),
  mockSubmitMockSession: vi.fn(),
  mockTrack: vi.fn(),
  mockPush: vi.fn(),
}));

vi.mock('next/link', () => ({
  default: ({ children, href }: { children: React.ReactNode; href?: string }) => <a href={href}>{children}</a>,
}));
vi.mock('@/components/layout/admin-dashboard-shell', () => ({
  LearnerDashboardShell: ({ children, workspaceClassName }: { children: React.ReactNode; workspaceClassName?: string }) => (
    <div data-testid="learner-dashboard-shell" data-workspace-class={workspaceClassName}>{children}</div>
  ),
}));

vi.mock('@/components/layout/expert-dashboard-shell', () => ({
  LearnerDashboardShell: ({ children, workspaceClassName }: { children: React.ReactNode; workspaceClassName?: string }) => (
    <div data-testid="learner-dashboard-shell" data-workspace-class={workspaceClassName}>{children}</div>
  ),
}));

vi.mock('@/components/layout/learner-dashboard-shell', () => ({
  LearnerDashboardShell: ({ children, workspaceClassName }: { children: React.ReactNode; workspaceClassName?: string }) => (
    <div data-testid="learner-dashboard-shell" data-workspace-class={workspaceClassName}>{children}</div>
  ),
}));

vi.mock('@/components/layout/sponsor-dashboard-shell', () => ({
  LearnerDashboardShell: ({ children, workspaceClassName }: { children: React.ReactNode; workspaceClassName?: string }) => (
    <div data-testid="learner-dashboard-shell" data-workspace-class={workspaceClassName}>{children}</div>
  ),
}));

vi.mock('@/components/layout/learner-workspace-container', () => ({
  LearnerDashboardShell: ({ children, workspaceClassName }: { children: React.ReactNode; workspaceClassName?: string }) => (
    <div data-testid="learner-dashboard-shell" data-workspace-class={workspaceClassName}>{children}</div>
  ),
}));

vi.mock('@/components/layout/notification-center', () => ({
  LearnerDashboardShell: ({ children, workspaceClassName }: { children: React.ReactNode; workspaceClassName?: string }) => (
    <div data-testid="learner-dashboard-shell" data-workspace-class={workspaceClassName}>{children}</div>
  ),
}));

vi.mock('@/components/layout/notification-preferences-panel', () => ({
  LearnerDashboardShell: ({ children, workspaceClassName }: { children: React.ReactNode; workspaceClassName?: string }) => (
    <div data-testid="learner-dashboard-shell" data-workspace-class={workspaceClassName}>{children}</div>
  ),
}));

vi.mock('@/components/layout/top-nav', () => ({
  LearnerDashboardShell: ({ children, workspaceClassName }: { children: React.ReactNode; workspaceClassName?: string }) => (
    <div data-testid="learner-dashboard-shell" data-workspace-class={workspaceClassName}>{children}</div>
  ),
}));

vi.mock('@/components/layout/sidebar', () => ({
  LearnerDashboardShell: ({ children, workspaceClassName }: { children: React.ReactNode; workspaceClassName?: string }) => (
    <div data-testid="learner-dashboard-shell" data-workspace-class={workspaceClassName}>{children}</div>
  ),
}));

vi.mock('@/components/layout/app-shell', () => ({
  AppShell: ({ children }: { children: React.ReactNode }) => <div data-testid="app-shell">{children}</div>,
}));

vi.mock('@/lib/analytics', () => ({
  analytics: {
    track: mockTrack,
  },
}));

vi.mock('@/lib/api', () => ({
  completeMockSection: mockCompleteMockSection,
  fetchMockSession: mockFetchMockSession,
  startMockSection: mockStartMockSection,
  submitMockSession: mockSubmitMockSession,
}));

import MockPlayerPage from './page';
import { renderWithRouter } from '@/tests/test-utils';

describe('Mock player page', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mockFetchMockSession.mockResolvedValue({
      sessionId: 'mock-1',
      config: {
        title: 'Full OET Mock Test',
        profession: 'Medicine',
        mode: 'exam',
        reviewSelection: 'writing',
        strictTimer: true,
      },
      sectionStates: [
        {
          id: 'section-reading',
          title: 'Reading section',
          subtest: 'reading',
          state: 'ready',
          launchRoute: '/reading/paper/paper-reading?mockAttemptId=mock-1&mockSectionId=section-reading',
          contentPaperId: 'paper-reading',
          contentPaperTitle: 'Published Reading Mock',
          timeLimitMinutes: 60,
          reviewSelected: false,
        },
      ],
      reportRoute: '/mocks/report/mock-1',
    });
    mockStartMockSection.mockResolvedValue({
      id: 'section-reading',
      title: 'Reading section',
      subtest: 'reading',
      state: 'in_progress',
      launchRoute: '/reading/paper/paper-reading?mockAttemptId=mock-1&mockSectionId=section-reading',
      timeLimitMinutes: 60,
      reviewSelected: false,
    });
    mockCompleteMockSection.mockResolvedValue({});
    mockSubmitMockSession.mockResolvedValue({});
  });

  it('renders the orchestrator route inside the shared learner dashboard shell', async () => {
    renderWithRouter(<MockPlayerPage />, {
      params: { id: 'mock-1' },
      router: { push: mockPush },
    });

    expect(await screen.findByText('Full OET Mock Test')).toBeInTheDocument();
    expect(screen.getByTestId('learner-dashboard-shell')).toBeInTheDocument();
  });

  it('launches sections through the backend-provided route', async () => {
    const user = userEvent.setup();
    renderWithRouter(<MockPlayerPage />, {
      params: { id: 'mock-1' },
      router: { push: mockPush },
    });

    const launchButton = await screen.findByRole('button', { name: /launch section workspace/i });
    await user.click(launchButton);

    expect(mockStartMockSection).toHaveBeenCalledWith('mock-1', 'section-reading');
    expect(mockPush).toHaveBeenCalledWith('/reading/paper/paper-reading?mockAttemptId=mock-1&mockSectionId=section-reading');
  });
});
