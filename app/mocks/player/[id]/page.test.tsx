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
  completeMockSection: mockCompleteMockSection,
  fetchMockSession: mockFetchMockSession,
  startMockSection: mockStartMockSection,
  submitMockSession: mockSubmitMockSession,
  recordMockProctoringEvents: vi.fn().mockResolvedValue(undefined),
  MOCK_PROCTORING_KINDS: [
    'fullscreen_exit',
    'visibility_hidden',
    'tab_switch',
    'paste_blocked',
    'copy_blocked',
    'mic_check_passed',
    'mic_check_failed',
    'cam_check_passed',
    'cam_check_failed',
    'audio_issue_reported',
    'network_drop',
    'multiple_displays_detected',
  ] as const,
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

  it('surfaces strict workflow policy and blocks report submission until all sections complete', async () => {
    const user = userEvent.setup();
    renderWithRouter(<MockPlayerPage />, {
      params: { id: 'mock-1' },
      router: { push: mockPush },
    });

    expect(await screen.findByText('Attempt → Review → Remediation')).toBeInTheDocument();
    expect(screen.getByText('Part A locks after its window; review is held until full submission.')).toBeInTheDocument();
    expect(screen.getByText(/0\/1 sections recorded/i)).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: /submit mock for report generation/i }));

    expect(mockSubmitMockSession).not.toHaveBeenCalled();
    expect(screen.getAllByText(/complete 1 remaining section before final report generation/i).length).toBeGreaterThan(0);
  });
});
