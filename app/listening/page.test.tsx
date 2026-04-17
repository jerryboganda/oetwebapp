import { render, screen } from '@testing-library/react';
const { mockFetchListeningHome, mockFetchMockReports, mockTrack, mockUseAuth } = vi.hoisted(() => ({
  mockFetchListeningHome: vi.fn(),
  mockFetchMockReports: vi.fn(),
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
  analytics: { track: mockTrack },
}));

vi.mock('@/lib/api', () => ({
  fetchListeningHome: mockFetchListeningHome,
  fetchMockReports: mockFetchMockReports,
}));

import ListeningHome from './page';

describe('Listening page', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mockUseAuth.mockReturnValue({
      isAuthenticated: true,
      loading: false,
    });
    mockFetchListeningHome.mockResolvedValue({
      intro: 'Use this workspace to tighten detail capture.',
      featuredTasks: [{ contentId: 'lt-001', title: 'Consultation: Asthma Management Review', estimatedDurationMinutes: 25, difficulty: 'medium', scenarioType: 'Consultation' }],
      mockSets: [{ route: '/mocks/setup' }],
      transcriptBackedReview: { title: 'Review transcript evidence', route: '/listening/transcript' },
      distractorDrills: [{ route: '/listening/distractor-1' }],
      accessPolicyHints: { rationale: 'Use transcript-backed review after an attempt.' },
    });
    mockFetchMockReports.mockResolvedValue([{ id: 'mock-1', title: 'Listening Mock', summary: 'Improving.', date: '2026-03-29', overallScore: '72%' }]);
  });

  it('renders through the shared learner dashboard shell', async () => {
    render(<ListeningHome />);
    expect(await screen.findByText('Train listening accuracy before you test it under pressure')).toBeInTheDocument();
    expect(screen.getByTestId('learner-dashboard-shell')).toBeInTheDocument();
  });

  it('tracks module_entry analytics on mount', async () => {
    render(<ListeningHome />);
    await screen.findByText('Train listening accuracy before you test it under pressure');
    expect(mockTrack).toHaveBeenCalledWith('module_entry', { module: 'listening' });
  });

  it('displays featured listening tasks from the API', async () => {
    render(<ListeningHome />);
    expect(await screen.findByText('Consultation: Asthma Management Review')).toBeInTheDocument();
  });
});
