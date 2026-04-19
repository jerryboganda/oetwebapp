import { screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

const { mockFetchVideoLessons, mockTrack } = vi.hoisted(() => ({
  mockFetchVideoLessons: vi.fn(),
  mockTrack: vi.fn(),
}));

vi.mock('@/components/layout', () => ({
  LearnerDashboardShell: ({ children }: { children: React.ReactNode }) => (
    <div data-testid="learner-dashboard-shell">{children}</div>
  ),
}));

vi.mock('@/components/domain', () => ({
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
