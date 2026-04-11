import { render, screen } from '@testing-library/react';
import type { Submission } from '@/lib/mock-data';

const { mockFetchSpeakingHome, mockFetchSubmissions, mockTrack } = vi.hoisted(() => ({
  mockFetchSpeakingHome: vi.fn(),
  mockFetchSubmissions: vi.fn(),
  mockTrack: vi.fn(),
}));

vi.mock('next/link', () => ({
  default: ({ children, href, ...props }: any) => <a href={href} {...props}>{children}</a>,
}));

vi.mock('motion/react', () => ({
  motion: {
    div: ({ children, initial: _initial, animate: _animate, transition: _transition, whileHover: _whileHover, whileTap: _whileTap, ...props }: any) => (
      <div {...props}>{children}</div>
    ),
  },
}));

vi.mock('@/components/layout', () => ({
  AppShell: ({ children }: { children: React.ReactNode }) => <div>{children}</div>,
  LearnerDashboardShell: ({ children, workspaceClassName }: { children: React.ReactNode; workspaceClassName?: string }) => (
    <div data-testid="learner-dashboard-shell" data-workspace-class={workspaceClassName}>{children}</div>
  ),
  LearnerWorkspaceContainer: ({ children, className }: { children: React.ReactNode; className?: string }) => (
    <div data-testid="learner-workspace-container" className={className}>{children}</div>
  ),
}));

vi.mock('@/lib/analytics', () => ({
  analytics: {
    track: mockTrack,
  },
}));

vi.mock('@/lib/api', () => ({
  fetchSpeakingHome: mockFetchSpeakingHome,
  fetchSubmissions: mockFetchSubmissions,
}));

import SpeakingPage from './page';

describe('Speaking page', () => {
  beforeEach(() => {
    vi.clearAllMocks();

    mockFetchSpeakingHome.mockResolvedValue({
      recommendedRolePlay: {
        id: 'sp-1',
        contentId: 'sp-1',
        title: 'Breaking Bad News - Cancer Diagnosis',
        criteriaFocus: 'appropriateness, grammar expression',
        profession: 'Clinical role play',
        duration: '20 mins',
        estimatedDurationMinutes: 20,
      },
      featuredTasks: [
        {
          id: 'sp-1',
          contentId: 'sp-1',
          title: 'Breaking Bad News - Cancer Diagnosis',
          profession: 'Clinical role play',
          duration: '20 mins',
          estimatedDurationMinutes: 20,
          criteriaFocus: 'appropriateness',
          difficulty: 'Medium',
          scenarioType: 'Role play',
        },
        {
          id: 'sp-2',
          contentId: 'sp-2',
          title: 'Patient Handover - Post-Op Recovery',
          profession: 'Nursing',
          duration: '20 mins',
          estimatedDurationMinutes: 20,
          criteriaFocus: 'fluency, appropriateness',
          difficulty: 'Medium',
          scenarioType: 'Clinical handover',
        },
        {
          id: 'sp-3',
          contentId: 'sp-3',
          title: 'Discharge Advice - Asthma',
          profession: 'Medicine',
          duration: '15 mins',
          estimatedDurationMinutes: 15,
          criteriaFocus: 'clarity',
          difficulty: 'Easy',
          scenarioType: 'Discharge advice',
        },
      ],
      drillGroups: [
        {
          id: 'pronunciation',
          title: 'Pronunciation drills',
          items: [{ id: 'dr-1', title: 'Stress important treatment words', route: '/speaking/phrasing/se-001' }],
        },
        {
          id: 'empathy_clarification',
          title: 'Empathy and clarification drills',
          items: [{ id: 'dr-2', title: 'Clarify concerns without losing structure', route: '/speaking/selection' }],
        },
      ],
      commonIssuesToImprove: [
        'Filler words interrupt flow',
        'One phrase became slightly informal',
      ],
      reviewCredits: {
        available: 3,
        route: '/reviews',
      },
    });

    mockFetchSubmissions.mockResolvedValue([
      {
        id: 'sub-1',
        contentId: 'sp-2',
        taskName: 'Patient Handover - Post-Op Recovery',
        subTest: 'Speaking',
        attemptDate: '2026-03-24T18:03:24.830217+00:00',
        scoreEstimate: '330-360',
        reviewStatus: 'reviewed',
        evaluationId: 'ev-1',
        canRequestReview: false,
        actions: {},
      },
    ] satisfies Submission[]);
  });

  it('shows dashboard-style speaking focus and readable speaking evidence dates', async () => {
    render(<SpeakingPage />);

    expect(await screen.findByText('Keep the next speaking move and recent evidence in view')).toBeInTheDocument();
    expect(screen.getByTestId('learner-dashboard-shell')).toBeInTheDocument();
    expect(screen.getByText('Recent Speaking Evidence')).toBeInTheDocument();
    expect(screen.getByText(/Mar\s+24,\s+2026/i)).toBeInTheDocument();
    expect(screen.queryByText('2026-03-24T18:03:24.830217+00:00')).not.toBeInTheDocument();
  });
});
