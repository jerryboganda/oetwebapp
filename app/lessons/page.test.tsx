import { screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

const { mockFetchVideoLessons, mockTrack } = vi.hoisted(() => ({
  mockFetchVideoLessons: vi.fn(),
  mockTrack: vi.fn(),
}));

vi.mock('next/image', () => ({
  default: ({ alt }: { alt: string }) => <span role="img" aria-label={alt} />,
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
  LearnerSurfaceSectionHeader: ({ title, description }: { title: string; description?: string }) => (
    <div><h2>{title}</h2>{description ? <p>{description}</p> : null}</div>
  ),
}));

vi.mock('@/components/domain/profession-selector', () => ({
  LearnerPageHero: ({ title, description }: { title: string; description: string }) => (
    <header><h1>{title}</h1><p>{description}</p></header>
  ),
  LearnerSurfaceSectionHeader: ({ title, description }: { title: string; description?: string }) => (
    <div><h2>{title}</h2>{description ? <p>{description}</p> : null}</div>
  ),
}));

vi.mock('@/components/domain/readiness-meter', () => ({
  LearnerPageHero: ({ title, description }: { title: string; description: string }) => (
    <header><h1>{title}</h1><p>{description}</p></header>
  ),
  LearnerSurfaceSectionHeader: ({ title, description }: { title: string; description?: string }) => (
    <div><h2>{title}</h2>{description ? <p>{description}</p> : null}</div>
  ),
}));

vi.mock('@/components/domain/weakest-link-card', () => ({
  LearnerPageHero: ({ title, description }: { title: string; description: string }) => (
    <header><h1>{title}</h1><p>{description}</p></header>
  ),
  LearnerSurfaceSectionHeader: ({ title, description }: { title: string; description?: string }) => (
    <div><h2>{title}</h2>{description ? <p>{description}</p> : null}</div>
  ),
}));

vi.mock('@/components/domain/criterion-breakdown-card', () => ({
  LearnerPageHero: ({ title, description }: { title: string; description: string }) => (
    <header><h1>{title}</h1><p>{description}</p></header>
  ),
  LearnerSurfaceSectionHeader: ({ title, description }: { title: string; description?: string }) => (
    <div><h2>{title}</h2>{description ? <p>{description}</p> : null}</div>
  ),
}));

vi.mock('@/components/domain/task-card', () => ({
  LearnerPageHero: ({ title, description }: { title: string; description: string }) => (
    <header><h1>{title}</h1><p>{description}</p></header>
  ),
  LearnerSurfaceSectionHeader: ({ title, description }: { title: string; description?: string }) => (
    <div><h2>{title}</h2>{description ? <p>{description}</p> : null}</div>
  ),
}));

vi.mock('@/components/domain/writing-case-notes-panel', () => ({
  LearnerPageHero: ({ title, description }: { title: string; description: string }) => (
    <header><h1>{title}</h1><p>{description}</p></header>
  ),
  LearnerSurfaceSectionHeader: ({ title, description }: { title: string; description?: string }) => (
    <div><h2>{title}</h2>{description ? <p>{description}</p> : null}</div>
  ),
}));

vi.mock('@/components/domain/writing-editor', () => ({
  LearnerPageHero: ({ title, description }: { title: string; description: string }) => (
    <header><h1>{title}</h1><p>{description}</p></header>
  ),
  LearnerSurfaceSectionHeader: ({ title, description }: { title: string; description?: string }) => (
    <div><h2>{title}</h2>{description ? <p>{description}</p> : null}</div>
  ),
}));

vi.mock('@/components/domain/writing-issue-list', () => ({
  LearnerPageHero: ({ title, description }: { title: string; description: string }) => (
    <header><h1>{title}</h1><p>{description}</p></header>
  ),
  LearnerSurfaceSectionHeader: ({ title, description }: { title: string; description?: string }) => (
    <div><h2>{title}</h2>{description ? <p>{description}</p> : null}</div>
  ),
}));

vi.mock('@/components/domain/revision-diff-viewer', () => ({
  LearnerPageHero: ({ title, description }: { title: string; description: string }) => (
    <header><h1>{title}</h1><p>{description}</p></header>
  ),
  LearnerSurfaceSectionHeader: ({ title, description }: { title: string; description?: string }) => (
    <div><h2>{title}</h2>{description ? <p>{description}</p> : null}</div>
  ),
}));

vi.mock('@/components/domain/speaking-role-card', () => ({
  LearnerPageHero: ({ title, description }: { title: string; description: string }) => (
    <header><h1>{title}</h1><p>{description}</p></header>
  ),
  LearnerSurfaceSectionHeader: ({ title, description }: { title: string; description?: string }) => (
    <div><h2>{title}</h2>{description ? <p>{description}</p> : null}</div>
  ),
}));

vi.mock('@/components/domain/mic-check-panel', () => ({
  LearnerPageHero: ({ title, description }: { title: string; description: string }) => (
    <header><h1>{title}</h1><p>{description}</p></header>
  ),
  LearnerSurfaceSectionHeader: ({ title, description }: { title: string; description?: string }) => (
    <div><h2>{title}</h2>{description ? <p>{description}</p> : null}</div>
  ),
}));

vi.mock('@/components/domain/audio-player-waveform', () => ({
  LearnerPageHero: ({ title, description }: { title: string; description: string }) => (
    <header><h1>{title}</h1><p>{description}</p></header>
  ),
  LearnerSurfaceSectionHeader: ({ title, description }: { title: string; description?: string }) => (
    <div><h2>{title}</h2>{description ? <p>{description}</p> : null}</div>
  ),
}));

vi.mock('@/components/domain/rulebook-findings-panel', () => ({
  LearnerPageHero: ({ title, description }: { title: string; description: string }) => (
    <header><h1>{title}</h1><p>{description}</p></header>
  ),
  LearnerSurfaceSectionHeader: ({ title, description }: { title: string; description?: string }) => (
    <div><h2>{title}</h2>{description ? <p>{description}</p> : null}</div>
  ),
}));

vi.mock('@/components/domain/exam-type-badge', () => ({
  LearnerPageHero: ({ title, description }: { title: string; description: string }) => (
    <header><h1>{title}</h1><p>{description}</p></header>
  ),
  LearnerSurfaceSectionHeader: ({ title, description }: { title: string; description?: string }) => (
    <div><h2>{title}</h2>{description ? <p>{description}</p> : null}</div>
  ),
}));

vi.mock('@/components/domain/OetStatementOfResultsCard', () => ({
  LearnerPageHero: ({ title, description }: { title: string; description: string }) => (
    <header><h1>{title}</h1><p>{description}</p></header>
  ),
  LearnerSurfaceSectionHeader: ({ title, description }: { title: string; description?: string }) => (
    <div><h2>{title}</h2>{description ? <p>{description}</p> : null}</div>
  ),
}));

vi.mock('@/lib/api', () => ({
  fetchVideoLessons: mockFetchVideoLessons,
}));

vi.mock('@/lib/analytics', () => ({
  analytics: { track: mockTrack },
}));

import LessonsPage from './page';
import { renderWithRouter } from '@/tests/test-utils';
import type { VideoLessonListItem } from '@/lib/types/video-lessons';

function lesson(overrides: Partial<VideoLessonListItem> = {}): VideoLessonListItem {
  return {
    id: 'lesson-1',
    title: 'Writing warm-up',
    description: 'Plan referral letters with better structure.',
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
    ...overrides,
  };
}

describe('Video lessons page', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mockFetchVideoLessons.mockResolvedValue([
      lesson(),
      lesson({
        id: 'lesson-2',
        title: 'Preview speaking clinic',
        subtestCode: 'speaking',
        isPreviewEligible: true,
        progress: { watchedSeconds: 180, percentComplete: 20, completed: false, lastWatchedAt: '2026-04-01T00:00:00Z' },
      }),
      lesson({
        id: 'lesson-3',
        title: 'Locked listening replay',
        subtestCode: 'listening',
        isAccessible: false,
        requiresUpgrade: true,
      }),
    ]);
  });

  it('renders result cards with preview and locked states', async () => {
    renderWithRouter(<LessonsPage />);

    expect(await screen.findByText('Writing warm-up')).toBeInTheDocument();
    expect(screen.getByText('Preview')).toBeInTheDocument();
    expect(screen.getByText('Locked')).toBeInTheDocument();
    expect(screen.queryByText(/undefined/i)).not.toBeInTheDocument();
    expect(screen.getByTestId('learner-dashboard-shell')).toBeInTheDocument();
  });

  it('filters by OET subtest through the lessons API', async () => {
    const user = userEvent.setup();
    renderWithRouter(<LessonsPage />);

    await screen.findByText('Writing warm-up');
    await user.selectOptions(screen.getByDisplayValue('All OET subtests'), 'speaking');

    await waitFor(() => {
      expect(mockFetchVideoLessons).toHaveBeenLastCalledWith({
        examTypeCode: 'oet',
        subtestCode: 'speaking',
        category: undefined,
      });
    });
  });

  it('shows an empty state when no lessons are returned', async () => {
    mockFetchVideoLessons.mockResolvedValueOnce([]);

    renderWithRouter(<LessonsPage />);

    expect(await screen.findByText('No video lessons match this view.')).toBeInTheDocument();
  });

  it('shows an error state when the lessons API fails', async () => {
    mockFetchVideoLessons.mockRejectedValueOnce(new Error('nope'));

    renderWithRouter(<LessonsPage />);

    expect(await screen.findByText('Could not load video lessons.')).toBeInTheDocument();
  });
});
