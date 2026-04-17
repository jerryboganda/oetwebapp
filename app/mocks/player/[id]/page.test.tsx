import { screen } from '@testing-library/react';
const { mockFetchMockSession, mockSubmitMockSession, mockTrack, mockPush } = vi.hoisted(() => ({
  mockFetchMockSession: vi.fn(),
  mockSubmitMockSession: vi.fn(),
  mockTrack: vi.fn(),
  mockPush: vi.fn(),
}));

vi.mock('next/link', () => ({
  default: ({ children, href }: { children: React.ReactNode; href?: string }) => <a href={href}>{children}</a>,
}));


vi.mock('@/components/layout', () => ({
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
  fetchMockSession: mockFetchMockSession,
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
          id: 'reading',
          title: 'Reading',
          state: 'ready',
          reviewSelected: false,
        },
      ],
      reportRoute: '/mocks/report/mock-1',
    });
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
});
