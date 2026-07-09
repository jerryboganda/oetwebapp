import { render, screen } from '@testing-library/react';
import type { Submission } from '@/lib/mock-data';

const {
  mockFetchSpeakingHome,
  mockFetchSubmissions,
  mockFetchMockReports,
  mockLearnerListSpeakingSharedResources,
  mockTrack,
} = vi.hoisted(() => ({
  mockFetchSpeakingHome: vi.fn(),
  mockFetchSubmissions: vi.fn(),
  mockFetchMockReports: vi.fn(),
  mockLearnerListSpeakingSharedResources: vi.fn(),
  mockTrack: vi.fn(),
}));

vi.mock('next/link', () => ({
  default: ({ children, href }: { children: React.ReactNode; href?: string }) => <a href={href}>{children}</a>,
}));

vi.mock('next/navigation', () => ({
  usePathname: () => '/speaking',
  useRouter: () => ({
    push: vi.fn(),
    replace: vi.fn(),
    prefetch: vi.fn(),
    refresh: vi.fn(),
    back: vi.fn(),
    forward: vi.fn(),
  }),
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

vi.mock('@/contexts/auth-context', () => ({
  useAuth: () => ({
    loading: false,
    user: {
      role: 'learner',
      activeProfessionId: 'medicine',
    },
  }),
}));

vi.mock('@/lib/api', () => ({
  fetchSpeakingHome: mockFetchSpeakingHome,
  fetchSubmissions: mockFetchSubmissions,
  fetchMockReports: mockFetchMockReports,
  learnerListSpeakingSharedResources: mockLearnerListSpeakingSharedResources,
  downloadSpeakingSharedResourceMedia: vi.fn(),
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
    mockFetchMockReports.mockResolvedValue([]);
    mockLearnerListSpeakingSharedResources.mockResolvedValue([]);
  });

  it('shows only the AI exam, tutor booking, and practice cards (everything else removed)', async () => {
    render(<SpeakingPage />);

    // The three surfaces the owner wants kept.
    expect(await screen.findByText('Get assessed by AI or book a live tutor')).toBeInTheDocument();
    expect(screen.getByText('Start Speaking Exam')).toBeInTheDocument();
    expect(screen.getByText('Book a Tutor')).toBeInTheDocument();
    expect(screen.getByText('Practise any speaking card on the platform')).toBeInTheDocument();
    expect(screen.getByText('Patient Handover - Post-Op Recovery')).toBeInTheDocument();

    // Everything else must be gone.
    expect(screen.queryByText('Recent Speaking Evidence')).not.toBeInTheDocument();
    expect(screen.queryByText('Recent Mock Reports')).not.toBeInTheDocument();
    expect(screen.queryByText('Drill Groups')).not.toBeInTheDocument();
    expect(screen.queryByText('Open Speaking Rules')).not.toBeInTheDocument();
    expect(screen.queryByText('Breaking Bad News')).not.toBeInTheDocument();
  });

  it('links Book a Tutor to the private-speaking booking page', async () => {
    render(<SpeakingPage />);

    const tutorLink = (await screen.findByText('Book a Tutor')).closest('a');
    expect(tutorLink).toHaveAttribute('href', '/private-speaking');
  });
});
