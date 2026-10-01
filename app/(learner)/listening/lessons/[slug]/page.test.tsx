import { render, screen } from '@testing-library/react';

const { get } = vi.hoisted(() => ({ get: vi.fn() }));

vi.mock('@/lib/api', () => ({ apiClient: { get } }));
vi.mock('next/navigation', () => ({
  useParams: () => ({ slug: 'gist-listening' }),
  usePathname: () => '/listening/lessons/gist-listening',
  useRouter: () => ({ push: vi.fn(), replace: vi.fn(), back: vi.fn(), prefetch: vi.fn() }),
}));

import ListeningLessonPage from './page';

const lesson = {
  id: 'l-1',
  slug: 'gist-listening',
  title: 'Listening for gist',
  skillCode: 'gist',
  estimatedMinutes: 25,
  videoUrl: null,
  bodyMarkdownEn: 'Focus on the overall purpose.',
  drillQuestionIds: [],
  quizQuestionIds: [],
  progress: {
    videoWatched: true,
    bodyRead: true,
    drill1Completed: true,
    drill2Completed: false,
    drill3Completed: false,
    quizScore: 80,
  },
};

describe('Listening lesson page', () => {
  it('ticks each step from its own progress field, not only the drills', async () => {
    get.mockResolvedValue(lesson);

    render(<ListeningLessonPage />);

    expect(await screen.findByRole('heading', { name: 'Listening for gist', level: 1 })).toBeInTheDocument();
    // Watch, Read, Drill 1 and Mini-quiz are done; Drills 2 and 3 are not.
    expect(screen.getAllByRole('img', { name: 'Completed' })).toHaveLength(4);
  });

  it('shows the quiz as not done until it has a score', async () => {
    get.mockResolvedValue({ ...lesson, progress: { ...lesson.progress, quizScore: null, videoWatched: false } });

    render(<ListeningLessonPage />);

    await screen.findByRole('heading', { name: 'Listening for gist', level: 1 });
    expect(screen.getAllByRole('img', { name: 'Completed' })).toHaveLength(2);
  });
});
