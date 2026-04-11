const fs = require('fs');
const path = require('path');

const MOTION_MOCK = `vi.mock('motion/react', () => ({
  motion: {
    div: ({ children, ...props }: any) => <div {...props}>{children}</div>,
    button: ({ children, ...props }: any) => <button {...props}>{children}</button>,
    span: ({ children, ...props }: any) => <span {...props}>{children}</span>,
    section: ({ children, ...props }: any) => <section {...props}>{children}</section>,
    p: ({ children, ...props }: any) => <p {...props}>{children}</p>,
  },
  useReducedMotion: () => false,
  AnimatePresence: ({ children }: any) => <div>{children}</div>,
}));`;

const tests = {
  'app/listening/page.test.tsx': `import { render, screen } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';

const { mockFetchListeningHome, mockFetchMockReports, mockTrack } = vi.hoisted(() => ({
  mockFetchListeningHome: vi.fn(),
  mockFetchMockReports: vi.fn(),
  mockTrack: vi.fn(),
}));

vi.mock('next/link', () => ({
  default: ({ children, href, ...props }: any) => <a href={href} {...props}>{children}</a>,
}));

${MOTION_MOCK}

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
`,

  'app/progress/page.test.tsx': `import { render, screen } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';

const { mockFetchTrendData, mockFetchCompletionData, mockFetchSubmissionVolume, mockFetchProgressEvidenceSummary, mockTrack } = vi.hoisted(() => ({
  mockFetchTrendData: vi.fn(),
  mockFetchCompletionData: vi.fn(),
  mockFetchSubmissionVolume: vi.fn(),
  mockFetchProgressEvidenceSummary: vi.fn(),
  mockTrack: vi.fn(),
}));

${MOTION_MOCK}

vi.mock('recharts', () => ({
  LineChart: ({ children }: any) => <div data-testid="line-chart">{children}</div>,
  Line: () => null, AreaChart: ({ children }: any) => <div>{children}</div>,
  Area: () => null, BarChart: ({ children }: any) => <div>{children}</div>,
  Bar: () => null, XAxis: () => null, YAxis: () => null,
  CartesianGrid: () => null, Tooltip: () => null,
  ResponsiveContainer: ({ children }: any) => <div>{children}</div>,
  Legend: () => null,
}));

vi.mock('@/components/layout', () => ({
  LearnerDashboardShell: ({ children, workspaceClassName }: { children: React.ReactNode; workspaceClassName?: string }) => (
    <div data-testid="learner-dashboard-shell" data-workspace-class={workspaceClassName}>{children}</div>
  ),
}));

vi.mock('@/lib/analytics', () => ({ analytics: { track: mockTrack } }));

vi.mock('@/lib/api', () => ({
  fetchTrendData: mockFetchTrendData,
  fetchCompletionData: mockFetchCompletionData,
  fetchSubmissionVolume: mockFetchSubmissionVolume,
  fetchProgressEvidenceSummary: mockFetchProgressEvidenceSummary,
}));

import ProgressDashboard from './page';

describe('Progress dashboard page', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mockFetchTrendData.mockResolvedValue([{ date: 'Week 1', reading: 60, listening: 55, writing: 50, speaking: 48 }]);
    mockFetchCompletionData.mockResolvedValue([{ day: 'Mon', completed: 3 }]);
    mockFetchSubmissionVolume.mockResolvedValue([{ week: 'W1', submissions: 12 }]);
    mockFetchProgressEvidenceSummary.mockResolvedValue({ reviewUsage: { averageTurnaroundHours: 2.5 } });
  });

  it('renders through the shared learner dashboard shell', async () => {
    render(<ProgressDashboard />);
    expect(await screen.findByText('See whether recent effort is turning into better evidence')).toBeInTheDocument();
    expect(screen.getByTestId('learner-dashboard-shell')).toBeInTheDocument();
  });

  it('tracks progress_viewed analytics on mount', async () => {
    render(<ProgressDashboard />);
    await screen.findByText('See whether recent effort is turning into better evidence');
    expect(mockTrack).toHaveBeenCalledWith('progress_viewed');
  });
});
`,

  'app/readiness/page.test.tsx': `import { render, screen } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';

const { mockFetchReadiness, mockTrack } = vi.hoisted(() => ({
  mockFetchReadiness: vi.fn(),
  mockTrack: vi.fn(),
}));

${MOTION_MOCK}

vi.mock('@/components/layout', () => ({
  LearnerDashboardShell: ({ children }: { children: React.ReactNode }) => (
    <div data-testid="learner-dashboard-shell">{children}</div>
  ),
}));

vi.mock('@/lib/analytics', () => ({ analytics: { track: mockTrack } }));
vi.mock('@/lib/api', () => ({ fetchReadiness: mockFetchReadiness }));

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
`,

  'app/study-plan/page.test.tsx': `import { render, screen } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';

const { mockFetchStudyPlan, mockUpdateStudyPlanTask, mockPush, mockTrack } = vi.hoisted(() => ({
  mockFetchStudyPlan: vi.fn(),
  mockUpdateStudyPlanTask: vi.fn(),
  mockPush: vi.fn(),
  mockTrack: vi.fn(),
}));

vi.mock('next/navigation', () => ({ useRouter: () => ({ push: mockPush }) }));

${MOTION_MOCK}

vi.mock('@/components/layout', () => ({
  LearnerDashboardShell: ({ children, workspaceClassName }: { children: React.ReactNode; workspaceClassName?: string }) => (
    <div data-testid="learner-dashboard-shell" data-workspace-class={workspaceClassName}>{children}</div>
  ),
}));

vi.mock('@/hooks/use-analytics', () => ({ useAnalytics: () => ({ track: mockTrack }) }));

vi.mock('@/lib/api', () => ({
  fetchStudyPlan: mockFetchStudyPlan,
  updateStudyPlanTask: mockUpdateStudyPlanTask,
}));

import StudyPlanPage from './page';

describe('Study plan page', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mockFetchStudyPlan.mockResolvedValue([
      { id: 'task-1', title: 'Practice Reading Part C passages', subTest: 'Reading', duration: '30m', dueDate: 'Today', status: 'not_started', section: 'today', contentId: 'rc-001', rationale: 'Recent reading scores show detail extraction gaps.' },
      { id: 'task-2', title: 'Complete listening distractor drill', subTest: 'Listening', duration: '20m', dueDate: 'Tomorrow', status: 'not_started', section: 'thisWeek', contentId: 'ld-001', rationale: 'Listening accuracy needs improvement.' },
    ]);
    mockUpdateStudyPlanTask.mockResolvedValue({});
  });

  it('renders study tasks through the shared learner dashboard shell', async () => {
    render(<StudyPlanPage />);
    expect(await screen.findByText('Practice Reading Part C passages')).toBeInTheDocument();
    expect(screen.getByTestId('learner-dashboard-shell')).toBeInTheDocument();
  });

  it('shows sub-test badges and task details', async () => {
    render(<StudyPlanPage />);
    expect(await screen.findByText('Reading')).toBeInTheDocument();
    expect(screen.getByText('Listening')).toBeInTheDocument();
  });
});
`,

  'app/achievements/page.test.tsx': `import { render, screen } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';

const { mockFetchXP, mockFetchStreak, mockFetchAchievements, mockTrack } = vi.hoisted(() => ({
  mockFetchXP: vi.fn(),
  mockFetchStreak: vi.fn(),
  mockFetchAchievements: vi.fn(),
  mockTrack: vi.fn(),
}));

${MOTION_MOCK}

vi.mock('@/components/layout', () => ({
  LearnerDashboardShell: ({ children }: { children: React.ReactNode }) => (
    <div data-testid="learner-dashboard-shell">{children}</div>
  ),
}));

vi.mock('@/lib/analytics', () => ({ analytics: { track: mockTrack } }));
vi.mock('@/lib/api', () => ({ fetchXP: mockFetchXP, fetchStreak: mockFetchStreak, fetchAchievements: mockFetchAchievements }));

import AchievementsPage from './page';

describe('Achievements page', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mockFetchXP.mockResolvedValue({ totalXp: 2450, level: 5, xpToNextLevel: 500, xpInCurrentLevel: 350 });
    mockFetchStreak.mockResolvedValue({ currentStreak: 7, longestStreak: 14, lastActivityDate: '2026-04-01' });
    mockFetchAchievements.mockResolvedValue([
      { achievementId: 'ach-1', title: 'First Practice', description: 'Complete your first task', category: 'practice', xpReward: 50, unlockedAt: '2026-03-20', earnedAt: '2026-03-20' },
      { achievementId: 'ach-2', title: 'Week Warrior', description: 'Maintain a 7-day streak', category: 'streak', xpReward: 100, unlockedAt: null, earnedAt: null },
    ]);
  });

  it('renders through the shared learner dashboard shell', async () => {
    render(<AchievementsPage />);
    expect(await screen.findByText('Achievements')).toBeInTheDocument();
    expect(screen.getByTestId('learner-dashboard-shell')).toBeInTheDocument();
  });

  it('displays XP level from the API', async () => {
    render(<AchievementsPage />);
    expect(await screen.findByText('5')).toBeInTheDocument();
  });

  it('shows unlocked and locked achievement sections', async () => {
    render(<AchievementsPage />);
    expect(await screen.findByText('First Practice')).toBeInTheDocument();
    expect(screen.getByText('Week Warrior')).toBeInTheDocument();
  });
});
`,

  'app/conversation/page.test.tsx': `import { render, screen } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';

const { mockGetConversationHistory, mockCreateConversation, mockTrack, mockPush } = vi.hoisted(() => ({
  mockGetConversationHistory: vi.fn(),
  mockCreateConversation: vi.fn(),
  mockTrack: vi.fn(),
  mockPush: vi.fn(),
}));

vi.mock('next/navigation', () => ({ useRouter: () => ({ push: mockPush }) }));
vi.mock('next/link', () => ({ default: ({ children, href, ...props }: any) => <a href={href} {...props}>{children}</a> }));

${MOTION_MOCK}

vi.mock('@/components/layout', () => ({
  LearnerDashboardShell: ({ children }: { children: React.ReactNode }) => (
    <div data-testid="learner-dashboard-shell">{children}</div>
  ),
}));

vi.mock('@/lib/analytics', () => ({ analytics: { track: mockTrack } }));
vi.mock('@/lib/api', () => ({ getConversationHistory: mockGetConversationHistory, createConversation: mockCreateConversation }));

import ConversationPage from './page';

describe('Conversation page', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mockGetConversationHistory.mockResolvedValue({
      items: [{ id: 'sess-1', taskTypeCode: 'oet-roleplay', examTypeCode: 'oet', state: 'evaluated', turnCount: 12, durationSeconds: 320, createdAt: '2026-04-01T10:00:00.000Z', completedAt: '2026-04-01T10:05:20.000Z' }],
    });
    mockCreateConversation.mockResolvedValue({ id: 'sess-new' });
  });

  it('renders through the shared learner dashboard shell', async () => {
    render(<ConversationPage />);
    expect(await screen.findByText('AI Conversation Practice')).toBeInTheDocument();
    expect(screen.getByTestId('learner-dashboard-shell')).toBeInTheDocument();
  });

  it('tracks conversation_page_viewed analytics on mount', async () => {
    render(<ConversationPage />);
    await screen.findByText('AI Conversation Practice');
    expect(mockTrack).toHaveBeenCalledWith('conversation_page_viewed');
  });

  it('displays task type options for starting new conversations', async () => {
    render(<ConversationPage />);
    expect(await screen.findByText('OET Clinical Role Play')).toBeInTheDocument();
    expect(screen.getByText('IELTS Part 2 Long Turn')).toBeInTheDocument();
  });
});
`,

  'app/community/page.test.tsx': `import { render, screen } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';

const { mockFetchForumCategories, mockFetchForumThreads, mockTrack } = vi.hoisted(() => ({
  mockFetchForumCategories: vi.fn(),
  mockFetchForumThreads: vi.fn(),
  mockTrack: vi.fn(),
}));

vi.mock('next/link', () => ({ default: ({ children, href, ...props }: any) => <a href={href} {...props}>{children}</a> }));

${MOTION_MOCK}

vi.mock('@/components/layout', () => ({
  LearnerDashboardShell: ({ children }: { children: React.ReactNode }) => (
    <div data-testid="learner-dashboard-shell">{children}</div>
  ),
}));

vi.mock('@/lib/analytics', () => ({ analytics: { track: mockTrack } }));
vi.mock('@/lib/api', () => ({ fetchForumCategories: mockFetchForumCategories, fetchForumThreads: mockFetchForumThreads }));

import CommunityPage from './page';

describe('Community page', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mockFetchForumCategories.mockResolvedValue([
      { id: 'cat-1', name: 'General Discussion', description: null, sortOrder: 1 },
      { id: 'cat-2', name: 'Study Tips', description: null, sortOrder: 2 },
    ]);
    mockFetchForumThreads.mockResolvedValue({
      threads: [{ id: 'thread-1', categoryId: 'cat-1', title: 'How to prepare for OET Reading Part C?', authorDisplayName: 'DrSarah', authorRole: 'learner', isPinned: true, isLocked: false, replyCount: 12, viewCount: 340, likeCount: 8, lastActivityAt: '2026-04-01T09:00:00.000Z' }],
    });
  });

  it('renders through the shared learner dashboard shell', async () => {
    render(<CommunityPage />);
    expect(await screen.findByText('Community')).toBeInTheDocument();
    expect(screen.getByTestId('learner-dashboard-shell')).toBeInTheDocument();
  });

  it('displays forum categories and threads from the API', async () => {
    render(<CommunityPage />);
    expect(await screen.findByText('General Discussion')).toBeInTheDocument();
    expect(screen.getByText('How to prepare for OET Reading Part C?')).toBeInTheDocument();
  });
});
`,

  'app/onboarding/page.test.tsx': `import { render, screen } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';

const { mockFetchOnboardingState, mockStartOnboarding, mockCompleteOnboarding, mockPush, mockTrack } = vi.hoisted(() => ({
  mockFetchOnboardingState: vi.fn(),
  mockStartOnboarding: vi.fn(),
  mockCompleteOnboarding: vi.fn(),
  mockPush: vi.fn(),
  mockTrack: vi.fn(),
}));

vi.mock('next/navigation', () => ({ useRouter: () => ({ push: mockPush }) }));

${MOTION_MOCK}

vi.mock('@/components/layout', () => ({
  LearnerDashboardShell: ({ children }: any) => (
    <div data-testid="learner-dashboard-shell">{children}</div>
  ),
}));

vi.mock('@/hooks/use-analytics', () => ({ useAnalytics: () => ({ track: mockTrack }) }));
vi.mock('@/lib/api', () => ({ fetchOnboardingState: mockFetchOnboardingState, startOnboarding: mockStartOnboarding, completeOnboarding: mockCompleteOnboarding }));

import OnboardingPage from './page';

describe('Onboarding page', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mockFetchOnboardingState.mockResolvedValue({ completed: false, currentStep: 1 });
    mockStartOnboarding.mockResolvedValue({});
    mockCompleteOnboarding.mockResolvedValue({});
  });

  it('renders the first onboarding step through the shared learner dashboard shell', async () => {
    render(<OnboardingPage />);
    expect(await screen.findByText('What is the OET?')).toBeInTheDocument();
    expect(screen.getByTestId('learner-dashboard-shell')).toBeInTheDocument();
  });

  it('tracks onboarding_started analytics when starting fresh', async () => {
    render(<OnboardingPage />);
    await screen.findByText('What is the OET?');
    expect(mockTrack).toHaveBeenCalledWith('onboarding_started');
  });

  it('displays stepper progress and navigation buttons', async () => {
    render(<OnboardingPage />);
    expect(await screen.findByText('1 of 3')).toBeInTheDocument();
  });
});
`,

  'app/expert/queue/page.test.tsx': `import { render, screen } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';

const { mockFetchReviewQueue, mockFetchExpertQueueFilterMetadata, mockTrack } = vi.hoisted(() => ({
  mockFetchReviewQueue: vi.fn(),
  mockFetchExpertQueueFilterMetadata: vi.fn(),
  mockTrack: vi.fn(),
}));

vi.mock('next/navigation', () => ({
  useRouter: () => ({ push: vi.fn(), replace: vi.fn() }),
  usePathname: () => '/expert/queue',
  useSearchParams: () => new URLSearchParams(''),
}));

${MOTION_MOCK}

vi.mock('@/lib/analytics', () => ({ analytics: { track: mockTrack } }));
vi.mock('@/lib/api', () => ({
  fetchReviewQueue: mockFetchReviewQueue,
  fetchExpertQueueFilterMetadata: mockFetchExpertQueueFilterMetadata,
  claimReview: vi.fn(), releaseReview: vi.fn(), isApiError: () => false,
}));

import ReviewQueuePage from './page';

describe('Expert queue page', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mockFetchExpertQueueFilterMetadata.mockResolvedValue({
      types: ['writing', 'speaking'], professions: ['medicine', 'nursing'],
      priorities: ['high', 'normal'], statuses: ['queued', 'assigned', 'in_progress'],
      confidenceBands: ['high', 'medium', 'low'], assignmentStates: ['assigned', 'unassigned'],
    });
    mockFetchReviewQueue.mockResolvedValue({
      items: [{ id: 'rev-1', learnerId: 'learner-1', learnerName: 'Dr Amina Khan', profession: 'medicine', subTest: 'writing', type: 'writing', aiConfidence: 'high', priority: 'high', slaDue: '2026-04-01T10:00:00.000Z', status: 'queued', createdAt: '2026-04-01T06:00:00.000Z', isOverdue: false, assignedTo: null }],
      total: 1, lastUpdatedAt: '2026-04-01T08:00:00.000Z',
    });
  });

  it('renders the expert review queue with items from the API', async () => {
    render(<ReviewQueuePage />);
    expect(await screen.findByText('Dr Amina Khan')).toBeInTheDocument();
  });
});
`,

  'app/admin/content/page.test.tsx': `import { render, screen } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';

const { mockGetAdminContentLibraryData, mockUseAdminAuth, mockPush } = vi.hoisted(() => ({
  mockGetAdminContentLibraryData: vi.fn(),
  mockUseAdminAuth: vi.fn(),
  mockPush: vi.fn(),
}));

vi.mock('next/navigation', () => ({ useRouter: () => ({ push: mockPush }) }));

${MOTION_MOCK}

vi.mock('@/lib/hooks/use-admin-auth', () => ({ useAdminAuth: () => mockUseAdminAuth() }));
vi.mock('@/lib/admin', () => ({ getAdminContentLibraryData: mockGetAdminContentLibraryData }));

import AdminContentLibraryPage from './page';

describe('Admin content library page', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mockUseAdminAuth.mockReturnValue({ isAuthenticated: true, role: 'admin' });
    mockGetAdminContentLibraryData.mockResolvedValue({
      items: [{ id: 'content-1', title: 'Hospital Discharge Letter', type: 'writing_task', profession: 'medicine', status: 'published', updatedAt: '2026-04-01T08:00:00.000Z', version: 2 }],
      total: 1,
    });
  });

  it('renders admin content library with items from the API', async () => {
    render(<AdminContentLibraryPage />);
    expect(await screen.findByText('Hospital Discharge Letter')).toBeInTheDocument();
  });
});
`,

  'app/mocks/setup/page.test.tsx': `import { render, screen } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';

const { mockCreateMockSession, mockTrack, mockPush } = vi.hoisted(() => ({
  mockCreateMockSession: vi.fn(),
  mockTrack: vi.fn(),
  mockPush: vi.fn(),
}));

vi.mock('next/navigation', () => ({ useRouter: () => ({ push: mockPush }) }));

${MOTION_MOCK}

vi.mock('@/components/layout', () => ({
  LearnerDashboardShell: ({ children }: { children: React.ReactNode }) => (
    <div data-testid="learner-dashboard-shell">{children}</div>
  ),
}));

vi.mock('@/lib/analytics', () => ({ analytics: { track: mockTrack } }));
vi.mock('@/lib/api', () => ({ createMockSession: mockCreateMockSession }));

import MockSetup from './page';

describe('Mock setup page', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mockCreateMockSession.mockResolvedValue({ sessionId: 'mock-sess-1', redirectUrl: '/mocks/mock-sess-1' });
  });

  it('renders the mock setup form through the shared learner dashboard shell', () => {
    render(<MockSetup />);
    expect(screen.getByTestId('learner-dashboard-shell')).toBeInTheDocument();
  });

  it('displays mock type options', () => {
    render(<MockSetup />);
    expect(screen.getByText('Full Mock')).toBeInTheDocument();
  });
});
`,

  'app/goals/page.test.tsx': `import { render, screen } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';

const { mockFetchExamFamilies, mockFetchUserProfile, mockUpdateUserProfile, mockPush, mockTrack } = vi.hoisted(() => ({
  mockFetchExamFamilies: vi.fn(),
  mockFetchUserProfile: vi.fn(),
  mockUpdateUserProfile: vi.fn(),
  mockPush: vi.fn(),
  mockTrack: vi.fn(),
}));

vi.mock('next/navigation', () => ({ useRouter: () => ({ push: mockPush }) }));

${MOTION_MOCK}

vi.mock('@/components/layout', () => ({
  LearnerDashboardShell: ({ children }: { children: React.ReactNode }) => (
    <div data-testid="learner-dashboard-shell">{children}</div>
  ),
}));

vi.mock('@/hooks/use-analytics', () => ({ useAnalytics: () => ({ track: mockTrack }) }));
vi.mock('@/lib/api', () => ({ fetchExamFamilies: mockFetchExamFamilies, fetchUserProfile: mockFetchUserProfile, updateUserProfile: mockUpdateUserProfile }));

import GoalsPage from './page';

describe('Goals setup page', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mockFetchExamFamilies.mockResolvedValue([{ code: 'oet', name: 'OET', isActive: true }]);
    mockFetchUserProfile.mockResolvedValue({ profession: 'medicine', examFamilyCode: 'oet' });
    mockUpdateUserProfile.mockResolvedValue({});
  });

  it('renders the goals form through the shared learner dashboard shell', () => {
    render(<GoalsPage />);
    expect(screen.getByTestId('learner-dashboard-shell')).toBeInTheDocument();
  });

  it('displays exam family selector', () => {
    render(<GoalsPage />);
    expect(screen.getByText('OET')).toBeInTheDocument();
  });
});
`,

  'app/practice/quick-session/page.test.tsx': `import { render, screen } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';

${MOTION_MOCK}

vi.mock('@/components/layout', () => ({
  LearnerDashboardShell: ({ children }: { children: React.ReactNode }) => (
    <div data-testid="learner-dashboard-shell">{children}</div>
  ),
}));

vi.mock('@/lib/analytics', () => ({ analytics: { track: vi.fn() } }));
vi.mock('@/lib/auth-client', () => ({ ensureFreshAccessToken: vi.fn().mockResolvedValue('test-token') }));
vi.mock('@/lib/env', () => ({ env: { apiBaseUrl: 'http://localhost:5000' } }));

import MobileQuickSessionPage from './page';

describe('Quick session page', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    global.fetch = vi.fn().mockRejectedValue(new Error('API unavailable'));
  });

  it('renders through the shared learner dashboard shell', () => {
    render(<MobileQuickSessionPage />);
    expect(screen.getByTestId('learner-dashboard-shell')).toBeInTheDocument();
  });
});
`,
};

// Write all files
for (const [filePath, content] of Object.entries(tests)) {
  const fullPath = path.resolve(__dirname, '..', filePath);
  fs.writeFileSync(fullPath, content, 'utf8');
  console.log('Written:', filePath);
}
console.log('Done: all', Object.keys(tests).length, 'test files generated');
