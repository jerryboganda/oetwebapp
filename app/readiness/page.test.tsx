import { render, screen } from '@testing-library/react';
const { mockFetchReadiness, mockTrack } = vi.hoisted(() => ({
  mockFetchReadiness: vi.fn(),
  mockTrack: vi.fn(),
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
  LearnerDashboardShell: ({ children }: { children: React.ReactNode }) => (
    <div data-testid="learner-dashboard-shell">{children}</div>
  ),
}));

vi.mock('@/lib/analytics', () => ({ analytics: { track: mockTrack } }));
vi.mock('@/lib/api', () => ({ fetchReadiness: mockFetchReadiness, fetchReadinessRisk: vi.fn().mockResolvedValue({ overallRisk: 'moderate', factors: [] }) }));

import ReadinessCenter from './page';

describe('Readiness center page', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mockFetchReadiness.mockResolvedValue({
      targetDate: '2026-06-15',
      overallRisk: 'Moderate',
      weeksRemaining: 10,
      recommendedStudyHours: 120,
      subTests: [
        { name: 'Reading', readiness: 68, target: 80, isWeakest: false },
        { name: 'Listening', readiness: 55, target: 75, isWeakest: true },
        { name: 'Writing', readiness: 62, target: 70, isWeakest: false },
        { name: 'Speaking', readiness: 70, target: 75, isWeakest: false },
      ],
      evidence: { mocksCompleted: 3, taskCount: 42 },
      blockers: [{ id: 'b1', title: 'Low listening accuracy', description: 'Distractor control needs improvement.' }],
    });
  });

  it('renders risk assessment through the shared learner dashboard shell', async () => {
    render(<ReadinessCenter />);
    expect(await screen.findByText('See what needs to close before your target date')).toBeInTheDocument();
    expect(screen.getByTestId('learner-dashboard-shell')).toBeInTheDocument();
  });

  it('tracks readiness_viewed analytics on mount', async () => {
    render(<ReadinessCenter />);
    await screen.findByText('See what needs to close before your target date');
    expect(mockTrack).toHaveBeenCalledWith('readiness_viewed');
  });
});
