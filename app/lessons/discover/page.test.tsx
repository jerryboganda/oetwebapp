import { screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

const { mockFetchVideoLessons, mockTrack } = vi.hoisted(() => ({
  mockFetchVideoLessons: vi.fn(),
  mockTrack: vi.fn(),
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
vi.mock('@/components/domain/learner-surface', () => ({
  LearnerPageHero: ({ title, description }: { title: string; description: string }) => (
    <header><h1>{title}</h1><p>{description}</p></header>
  ),
}));

vi.mock('@/components/domain/profession-selector', () => ({
  LearnerPageHero: ({ title, description }: { title: string; description: string }) => (
    <header><h1>{title}</h1><p>{description}</p></header>
  ),
}));

vi.mock('@/components/domain/readiness-meter', () => ({
  LearnerPageHero: ({ title, description }: { title: string; description: string }) => (
    <header><h1>{title}</h1><p>{description}</p></header>
  ),
}));

vi.mock('@/components/domain/weakest-link-card', () => ({
  LearnerPageHero: ({ title, description }: { title: string; description: string }) => (
    <header><h1>{title}</h1><p>{description}</p></header>
  ),
}));

vi.mock('@/components/domain/criterion-breakdown-card', () => ({
  LearnerPageHero: ({ title, description }: { title: string; description: string }) => (
    <header><h1>{title}</h1><p>{description}</p></header>
  ),
}));

vi.mock('@/components/domain/task-card', () => ({
  LearnerPageHero: ({ title, description }: { title: string; description: string }) => (
    <header><h1>{title}</h1><p>{description}</p></header>
  ),
}));

vi.mock('@/components/domain/writing-case-notes-panel', () => ({
  LearnerPageHero: ({ title, description }: { title: string; description: string }) => (
    <header><h1>{title}</h1><p>{description}</p></header>
  ),
}));

vi.mock('@/components/domain/writing-editor', () => ({
  LearnerPageHero: ({ title, description }: { title: string; description: string }) => (
    <header><h1>{title}</h1><p>{description}</p></header>
  ),
}));

vi.mock('@/components/domain/writing-issue-list', () => ({
  LearnerPageHero: ({ title, description }: { title: string; description: string }) => (
    <header><h1>{title}</h1><p>{description}</p></header>
  ),
}));

vi.mock('@/components/domain/revision-diff-viewer', () => ({
  LearnerPageHero: ({ title, description }: { title: string; description: string }) => (
    <header><h1>{title}</h1><p>{description}</p></header>
  ),
}));

vi.mock('@/components/domain/speaking-role-card', () => ({
  LearnerPageHero: ({ title, description }: { title: string; description: string }) => (
    <header><h1>{title}</h1><p>{description}</p></header>
  ),
}));

vi.mock('@/components/domain/mic-check-panel', () => ({
  LearnerPageHero: ({ title, description }: { title: string; description: string }) => (
    <header><h1>{title}</h1><p>{description}</p></header>
  ),
}));

vi.mock('@/components/domain/audio-player-waveform', () => ({
  LearnerPageHero: ({ title, description }: { title: string; description: string }) => (
    <header><h1>{title}</h1><p>{description}</p></header>
  ),
}));

vi.mock('@/components/domain/rulebook-findings-panel', () => ({
  LearnerPageHero: ({ title, description }: { title: string; description: string }) => (
    <header><h1>{title}</h1><p>{description}</p></header>
  ),
}));

vi.mock('@/components/domain/exam-type-badge', () => ({
  LearnerPageHero: ({ title, description }: { title: string; description: string }) => (
    <header><h1>{title}</h1><p>{description}</p></header>
  ),
}));

vi.mock('@/components/domain/OetStatementOfResultsCard', () => ({
  LearnerPageHero: ({ title, description }: { title: string; description: string }) => (
    <header><h1>{title}</h1><p>{description}</p></header>
  ),
}));

vi.mock('@/lib/api', () => ({
  fetchVideoLessons: mockFetchVideoLessons,
}));

vi.mock('@/lib/analytics', () => ({
  analytics: { track: mockTrack },
}));

import DiscoverPage from './page';
import { renderWithRouter } from '@/tests/test-utils';
import type { VideoLessonListItem } from '@/lib/types/video-lessons';

const lessons: VideoLessonListItem[] = [
  {
    id: 'video-lesson-1',
    title: 'Writing task planning',
    description: 'Plan a referral letter.',
    examTypeCode: 'oet',
    subtestCode: 'writing',
    category: 'strategy',
    difficultyLevel: 'intermediate',
    durationSeconds: 900,
    thumbnailUrl: null,
    instructorName: null,
    isAccessible: true,
    isPreviewEligible: false,
    requiresUpgrade: false,
    source: 'content_hierarchy',
    progress: null,
    programId: 'program-1',
    moduleId: 'module-1',
    sortOrder: 1,
  },
];

describe('Video lesson discovery page', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mockFetchVideoLessons.mockResolvedValue(lessons);
  });

  it('routes discovered items to canonical video lesson detail pages', async () => {
    renderWithRouter(<DiscoverPage />);

    const link = await screen.findByRole('link', { name: /Writing task planning/i });
    expect(link).toHaveAttribute('href', '/lessons/video-lesson-1');
    expect(mockFetchVideoLessons).toHaveBeenCalledWith({ examTypeCode: 'oet', subtestCode: undefined });
  });

  it('filters results locally without creating invalid player links', async () => {
    const user = userEvent.setup();
    renderWithRouter(<DiscoverPage />);

    await screen.findByText('Writing task planning');
    await user.type(screen.getByPlaceholderText('Search video lessons, topics, or categories...'), 'speaking');

    expect(await screen.findByText('No video lessons found')).toBeInTheDocument();
    expect(screen.queryByRole('link', { name: /Writing task planning/i })).not.toBeInTheDocument();
  });
});
