import { render, screen } from '@testing-library/react';
const { mockFetchListeningHome, mockFetchMockReports, mockTrack } = vi.hoisted(() => ({
  mockFetchListeningHome: vi.fn(),
  mockFetchMockReports: vi.fn(),
  mockTrack: vi.fn(),
}));

vi.mock('next/link', () => ({
  default: ({ children, href, ...props }: any) => <a href={href} {...props}>{children}</a>,
}));

vi.mock('motion/react', () => ({
  motion: {
    div: ({ children, ...props }: any) => <div {...props}>{children}</div>,
    button: ({ children, ...props }: any) => <button {...props}>{children}</button>,
    span: ({ children, ...props }: any) => <span {...props}>{children}</span>,
    section: ({ children, ...props }: any) => <section {...props}>{children}</section>,
    p: ({ children, ...props }: any) => <p {...props}>{children}</p>,
  },
  useReducedMotion: () => false,
  AnimatePresence: ({ children }: any) => <div>{children}</div>,
}));

vi.mock('@/components/layout', () => ({
  LearnerDashboardShell: ({ children, workspaceClassName }: { children: React.ReactNode; workspaceClassName?: string }) => (
    <div data-testid="learner-dashboard-shell" data-workspace-class={workspaceClassName}>{children}</div>
  ),
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
