import { screen } from '@testing-library/react';

const { mockFetchVideoLessonProgram } = vi.hoisted(() => ({
  mockFetchVideoLessonProgram: vi.fn(),
}));

vi.mock('next/image', () => ({
  default: ({ alt }: { alt: string }) => <span role="img" aria-label={alt} />,
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
  fetchVideoLessonProgram: mockFetchVideoLessonProgram,
}));

import VideoLessonProgramPage from './page';
import { renderWithRouter } from '@/tests/test-utils';
import type { VideoLessonProgram } from '@/lib/types/video-lessons';

const program: VideoLessonProgram = {
  id: 'program-1',
  title: 'OET Writing Accelerator',
  description: 'A structured writing lesson path.',
  examTypeCode: 'oet',
  thumbnailUrl: null,
  isAccessible: true,
  tracks: [
    {
      id: 'track-1',
      title: 'Writing',
      description: 'Writing track',
      subtestCode: 'writing',
      modules: [
        {
          id: 'module-1',
          title: 'Referral letters',
          description: 'Core letter lessons',
          estimatedDurationMinutes: 10,
          lessons: [
            {
              id: 'lesson-1',
              title: 'Referral opening strategy',
              description: 'Start stronger.',
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
              source: 'content_hierarchy',
              progress: { watchedSeconds: 600, percentComplete: 100, completed: true, lastWatchedAt: '2026-04-01T00:00:00Z' },
              programId: 'program-1',
              moduleId: 'module-1',
              sortOrder: 1,
            },
          ],
        },
      ],
    },
  ],
};

describe('Video lesson program page', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mockFetchVideoLessonProgram.mockResolvedValue(program);
  });

  it('renders a real program outline with lesson links', async () => {
    renderWithRouter(<VideoLessonProgramPage />, {
      pathname: '/lessons/programs/program-1',
      params: { programId: 'program-1' },
    });

    expect(await screen.findByText('OET Writing Accelerator')).toBeInTheDocument();
    expect(screen.getByText('Referral letters')).toBeInTheDocument();
    const lessonLink = screen.getByRole('link', { name: /Referral opening strategy/i });
    expect(lessonLink).toHaveAttribute('href', '/lessons/lesson-1');
    expect(screen.getByText('Done')).toBeInTheDocument();
  });
});
