import { screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { renderWithRouter } from '@/tests/test-utils';
const { mockCreateMockSession, mockFetchMockOptions, mockTrack, mockPush } = vi.hoisted(() => ({
  mockCreateMockSession: vi.fn(),
  mockFetchMockOptions: vi.fn(),
  mockTrack: vi.fn(),
  mockPush: vi.fn(),
}));
vi.mock('@/components/layout/app-shell', () => ({
  LearnerDashboardShell: ({ children }: { children: React.ReactNode }) => (
    <div data-testid="learner-dashboard-shell">{children}</div>
  ),
}));

vi.mock('@/components/layout/admin-dashboard-shell', () => ({
  LearnerDashboardShell: ({ children }: { children: React.ReactNode }) => (
    <div data-testid="learner-dashboard-shell">{children}</div>
  ),
}));

vi.mock('@/components/layout/expert-dashboard-shell', () => ({
  LearnerDashboardShell: ({ children }: { children: React.ReactNode }) => (
    <div data-testid="learner-dashboard-shell">{children}</div>
  ),
}));

vi.mock('@/components/layout/learner-dashboard-shell', () => ({
  LearnerDashboardShell: ({ children }: { children: React.ReactNode }) => (
    <div data-testid="learner-dashboard-shell">{children}</div>
  ),
}));

vi.mock('@/components/layout/sponsor-dashboard-shell', () => ({
  LearnerDashboardShell: ({ children }: { children: React.ReactNode }) => (
    <div data-testid="learner-dashboard-shell">{children}</div>
  ),
}));

vi.mock('@/components/layout/learner-workspace-container', () => ({
  LearnerDashboardShell: ({ children }: { children: React.ReactNode }) => (
    <div data-testid="learner-dashboard-shell">{children}</div>
  ),
}));

vi.mock('@/components/layout/notification-center', () => ({
  LearnerDashboardShell: ({ children }: { children: React.ReactNode }) => (
    <div data-testid="learner-dashboard-shell">{children}</div>
  ),
}));

vi.mock('@/components/layout/notification-preferences-panel', () => ({
  LearnerDashboardShell: ({ children }: { children: React.ReactNode }) => (
    <div data-testid="learner-dashboard-shell">{children}</div>
  ),
}));

vi.mock('@/components/layout/top-nav', () => ({
  LearnerDashboardShell: ({ children }: { children: React.ReactNode }) => (
    <div data-testid="learner-dashboard-shell">{children}</div>
  ),
}));

vi.mock('@/components/layout/sidebar', () => ({
  LearnerDashboardShell: ({ children }: { children: React.ReactNode }) => (
    <div data-testid="learner-dashboard-shell">{children}</div>
  ),
}));

vi.mock('@/lib/analytics', () => ({ analytics: { track: mockTrack } }));
vi.mock('@/lib/api', () => ({
  createMockSession: mockCreateMockSession,
  fetchMockOptions: mockFetchMockOptions,
}));

import MockSetup from './page';

describe('Mock setup page', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mockFetchMockOptions.mockResolvedValue({
      mockTypes: [
        { id: 'full', label: 'Full Mock', description: 'All four OET sub-tests.' },
        { id: 'sub', label: 'Single Sub-test', description: 'Focused mock evidence.' },
      ],
      subTypes: [
        { id: 'listening', label: 'Listening' },
        { id: 'reading', label: 'Reading' },
        { id: 'writing', label: 'Writing' },
        { id: 'speaking', label: 'Speaking' },
      ],
      modes: [
        { id: 'exam', label: 'Exam Mode' },
        { id: 'practice', label: 'Practice Mode' },
      ],
      professions: [{ id: 'medicine', label: 'Medicine' }],
      reviewSelections: [],
      wallet: { availableCredits: 2 },
      availableBundles: [
        {
          id: 'bundle-full',
          bundleId: 'bundle-full',
          title: 'Full Bundle',
          mockType: 'full',
          subtest: null,
          professionId: null,
          appliesToAllProfessions: true,
          estimatedDurationMinutes: 167,
          sections: [
            { id: 'full-listening', subtest: 'listening', title: 'Listening', timeLimitMinutes: 42, reviewEligible: false, contentPaperId: 'lt-001' },
            { id: 'full-reading', subtest: 'reading', title: 'Reading', timeLimitMinutes: 60, reviewEligible: false, contentPaperId: 'rt-001' },
            { id: 'full-writing', subtest: 'writing', title: 'Writing', timeLimitMinutes: 45, reviewEligible: true, contentPaperId: 'wt-001' },
            { id: 'full-speaking', subtest: 'speaking', title: 'Speaking', timeLimitMinutes: 20, reviewEligible: true, contentPaperId: 'st-001' },
          ],
        },
        {
          id: 'bundle-reading',
          bundleId: 'bundle-reading',
          title: 'Reading Bundle',
          mockType: 'sub',
          subtest: 'reading',
          professionId: null,
          appliesToAllProfessions: true,
          estimatedDurationMinutes: 60,
          sections: [
            { id: 'reading-section', subtest: 'reading', title: 'Reading', timeLimitMinutes: 60, reviewEligible: false, contentPaperId: 'rt-002' },
          ],
        },
      ],
    });
    mockCreateMockSession.mockResolvedValue({ sessionId: 'mock-sess-1', redirectUrl: '/mocks/mock-sess-1' });
  });

  it('renders the mock setup form through the shared learner dashboard shell', async () => {
    renderWithRouter(<MockSetup />, { router: { push: mockPush } });
    expect(await screen.findByText('Full Mock')).toBeInTheDocument();
    expect(screen.getByTestId('learner-dashboard-shell')).toBeInTheDocument();
  });

  it('displays mock type options from backend setup options', async () => {
    renderWithRouter(<MockSetup />, { router: { push: mockPush } });
    expect(await screen.findByText('Full Mock')).toBeInTheDocument();
    expect(screen.getByText('Single Sub-test')).toBeInTheDocument();
  });

  it('preselects a sub-test from the query string and starts the matching bundle', async () => {
    const user = userEvent.setup();
    renderWithRouter(<MockSetup />, {
      router: { push: mockPush },
      searchParams: new URLSearchParams('subtest=reading'),
    });

    expect(await screen.findByText('Reading Bundle')).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: /reading bundle/i }));
    const startButton = screen.getByRole('button', { name: /start mock test/i });
    await waitFor(() => expect(startButton).toBeEnabled());
    await user.click(startButton);

    await waitFor(() => expect(mockCreateMockSession).toHaveBeenCalledWith(expect.objectContaining({
      type: 'sub',
      subType: 'reading',
      bundleId: 'bundle-reading',
      reviewSelection: 'none',
    })));
    expect(mockPush).toHaveBeenCalledWith('/mocks/player/mock-sess-1');
  });
});
