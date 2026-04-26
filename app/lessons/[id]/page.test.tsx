import { fireEvent, screen } from '@testing-library/react';

const { mockFetchVideoLesson, mockUpdateVideoProgress, mockTrack } = vi.hoisted(() => ({
  mockFetchVideoLesson: vi.fn(),
  mockUpdateVideoProgress: vi.fn(),
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

vi.mock('@/lib/api', () => ({
  fetchVideoLesson: mockFetchVideoLesson,
  updateVideoProgress: mockUpdateVideoProgress,
}));

vi.mock('@/lib/analytics', () => ({
  analytics: { track: mockTrack },
}));

import VideoLessonPage from './page';
import { renderWithRouter } from '@/tests/test-utils';
import type { VideoLessonDetail } from '@/lib/types/video-lessons';

function detail(overrides: Partial<VideoLessonDetail> = {}): VideoLessonDetail {
  return {
    id: 'lesson-1',
    title: 'Referral letter walkthrough',
    description: 'A detailed OET writing lesson.',
    examTypeCode: 'oet',
    subtestCode: 'writing',
    category: 'strategy',
    difficultyLevel: 'intermediate',
    durationSeconds: 600,
    thumbnailUrl: null,
    instructorName: null,
    isAccessible: true,
    isPreviewEligible: false,
    requiresUpgrade: false,
    accessReason: 'subscription',
    source: 'content_hierarchy',
    progress: { watchedSeconds: 120, percentComplete: 20, completed: false, lastWatchedAt: '2026-04-01T00:00:00Z' },
    programId: 'program-1',
    programTitle: 'OET Core',
    trackId: 'track-1',
    trackTitle: 'Writing track',
    moduleId: 'module-1',
    moduleTitle: 'Writing',
    mediaAssetId: 'media-1',
    sortOrder: 1,
    videoUrl: '/media/video.mp4',
    captionUrl: '/media/captions.vtt',
    transcriptUrl: '/media/transcript.txt',
    chapters: [{ title: 'Planning', timeSeconds: 60 }],
    resources: [{ title: 'Letter checklist', type: 'pdf', url: '/media/checklist.pdf' }],
    previousLessonId: null,
    nextLessonId: 'lesson-2',
    ...overrides,
  };
}

describe('Video lesson detail page', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mockFetchVideoLesson.mockResolvedValue(detail());
    mockUpdateVideoProgress.mockResolvedValue({
      watchedSeconds: 135,
      percentComplete: 23,
      completed: false,
      completedAt: null,
      lastWatchedAt: '2026-04-01T00:05:00Z',
    });
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue({
      ok: true,
      text: () => Promise.resolve('Transcript line one.'),
    }));
  });

  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it('renders resume state, transcript, captions, chapters, and resources', async () => {
    const { container } = renderWithRouter(<VideoLessonPage />, {
      pathname: '/lessons/lesson-1',
      params: { id: 'lesson-1' },
    });

    expect(await screen.findByText('Referral letter walkthrough')).toBeInTheDocument();
    expect(await screen.findByText('Transcript line one.')).toBeInTheDocument();
    expect(screen.getByText('Resume at 2:00.')).toBeInTheDocument();
    expect(screen.getByText('Planning')).toBeInTheDocument();
    expect(screen.getByText('Letter checklist')).toBeInTheDocument();

    const video = container.querySelector('video');
    expect(video).not.toBeNull();
    Object.defineProperty(video, 'duration', { configurable: true, value: 600 });
    fireEvent.loadedMetadata(video!);
    expect(video!.currentTime).toBe(120);
    expect(container.querySelector('track')?.getAttribute('src')).toBe('/media/captions.vtt');
  });

  it('renders locked upgrade messaging without a video URL', async () => {
    mockFetchVideoLesson.mockResolvedValueOnce(detail({
      videoUrl: null,
      captionUrl: null,
      transcriptUrl: null,
      isAccessible: false,
      requiresUpgrade: true,
      accessReason: 'locked',
      progress: null,
      resources: [],
      chapters: [],
    }));

    renderWithRouter(<VideoLessonPage />, {
      pathname: '/lessons/lesson-locked',
      params: { id: 'lesson-locked' },
    });

    expect(await screen.findByText('Referral letter walkthrough')).toBeInTheDocument();
    expect(screen.getByText('Upgrade required')).toBeInTheDocument();
    expect(screen.getByText(/part of a paid package/i)).toBeInTheDocument();
  });
});
