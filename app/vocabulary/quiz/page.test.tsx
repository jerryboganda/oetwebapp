import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

const {
  mockFetchVocabQuiz,
  mockSubmitVocabQuiz,
  mockTrack,
  mockUseSearchParams,
} = vi.hoisted(() => ({
  mockFetchVocabQuiz: vi.fn(),
  mockSubmitVocabQuiz: vi.fn(),
  mockTrack: vi.fn(),
  mockUseSearchParams: vi.fn(() => ({ get: () => null })),
}));

vi.mock('next/link', () => ({
  default: ({ children, href }: { children: React.ReactNode; href?: string }) => <a href={href}>{children}</a>,
}));

vi.mock('next/navigation', () => ({
  useSearchParams: () => mockUseSearchParams(),
}));

vi.mock('@/components/layout', () => ({
  LearnerDashboardShell: ({ children }: { children: React.ReactNode }) => <div>{children}</div>,
}));

vi.mock('@/components/domain', () => ({
  LearnerPageHero: ({ title }: { title: string }) => <h1>{title}</h1>,
  LearnerSurfaceSectionHeader: ({ title }: { title: string }) => <h2>{title}</h2>,
}));

vi.mock('@/components/ui/motion-primitives', () => ({
  MotionSection: ({ children }: { children: React.ReactNode }) => <section>{children}</section>,
}));

vi.mock('@/components/ui/card', () => ({
  Card: ({ children }: { children: React.ReactNode }) => <div>{children}</div>,
}));

vi.mock('@/components/ui/skeleton', () => ({
  Skeleton: () => <div data-testid="skeleton" />,
  PageSkeleton: () => <div data-testid="page-skeleton" />,
}));

vi.mock('@/components/ui/alert', () => ({
  InlineAlert: ({ children }: { children: React.ReactNode }) => <div role="alert">{children}</div>,
}));

vi.mock('@/lib/analytics', () => ({
  analytics: { track: mockTrack },
}));

vi.mock('@/lib/api', () => ({
  fetchVocabQuiz: mockFetchVocabQuiz,
  submitVocabQuiz: mockSubmitVocabQuiz,
}));

import VocabQuizPage from './page';

describe('Vocabulary quiz page', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mockFetchVocabQuiz.mockResolvedValue({
      format: 'definition_match',
      questions: [
        {
          termId: 'vt-001',
          term: 'dyspnoea',
          format: 'definition_match',
          prompt: 'dyspnoea',
          options: ['Wrong A', 'Difficulty breathing.', 'Wrong B', 'Wrong C'],
          correctIndex: 1,
          correctAnswer: 'Difficulty breathing.',
          exampleSentence: null,
          audioUrl: null,
        },
      ],
    });
  });

  it('tracks vocab_quiz_viewed analytics on mount', async () => {
    render(<VocabQuizPage />);
    await screen.findByText('Vocabulary Quiz');
    expect(mockTrack).toHaveBeenCalledWith('vocab_quiz_viewed', { format: 'definition_match' });
  });

  it('renders quiz prompt and options', async () => {
    render(<VocabQuizPage />);
    expect(await screen.findByText('What does this word mean?')).toBeInTheDocument();
    expect(await screen.findByText('Difficulty breathing.')).toBeInTheDocument();
  });

  it('reveals correctness after user selects an answer', async () => {
    const user = userEvent.setup();
    render(<VocabQuizPage />);
    const option = await screen.findByRole('button', { name: /Difficulty breathing/i });
    await user.click(option);
    // "Finish Quiz" button appears after last question is answered
    expect(await screen.findByRole('button', { name: /Finish Quiz/i })).toBeInTheDocument();
  });

  it('shows premium gate when fetchVocabQuiz rejects with 402', async () => {
    const err = Object.assign(new Error('Premium required'), {
      code: 'VOCAB_PREMIUM_REQUIRED',
      status: 402,
    });
    // First call (mount, definition_match) resolves; second (fill_blank) rejects.
    mockFetchVocabQuiz.mockImplementation((_count: number, format: string) => {
      if (format === 'fill_blank') return Promise.reject(err);
      return Promise.resolve({ format: 'definition_match', questions: [] });
    });
    const user = userEvent.setup();
    render(<VocabQuizPage />);
    // Click the "Fill the Blank" premium format button
    const fillBlankBtn = await screen.findByRole('button', { name: /Fill the Blank/i });
    await user.click(fillBlankBtn);
    expect(await screen.findByText(/premium quiz format/i)).toBeInTheDocument();
  });
});
