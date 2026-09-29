import { fireEvent, render, screen, waitFor } from '@testing-library/react';

const { mockFetchAuthorizedObjectUrl, mockGetReview } = vi.hoisted(() => ({
  mockFetchAuthorizedObjectUrl: vi.fn(),
  mockGetReview: vi.fn(),
}));

vi.mock('next/navigation', () => ({
  useParams: () => ({ id: 'attempt-1' }),
  useRouter: () => ({ push: vi.fn(), back: vi.fn() }),
}));

vi.mock('@/components/layout', () => ({
  LearnerDashboardShell: ({ children }: { children: React.ReactNode }) => <div>{children}</div>,
}));

vi.mock('@/components/ui/button', () => ({
  Button: ({ children, ...props }: React.ButtonHTMLAttributes<HTMLButtonElement>) => (
    <button type="button" {...props}>{children}</button>
  ),
}));

vi.mock('@/components/ui/alert', () => ({
  InlineAlert: ({ children, variant }: { children: React.ReactNode; variant?: string }) => (
    <div data-variant={variant}>{children}</div>
  ),
}));

vi.mock('@/components/ui/skeleton', () => ({
  Skeleton: () => <div data-testid="skeleton" />,
}));

vi.mock('@/components/domain', () => ({
  LearnerPageHero: () => <section data-testid="review-hero" />,
  LearnerSurfaceSectionHeader: ({ eyebrow, title, description }: {
    eyebrow: string;
    title: string;
    description?: string;
  }) => (
    <div>
      <p>{eyebrow}</p>
      <h2>{title}</h2>
      {description ? <p>{description}</p> : null}
    </div>
  ),
}));

vi.mock('@/components/domain/results/results-score-panel', () => ({
  ResultsScorePanel: ({ title }: { title: string }) => <section data-testid="score-panel">{title}</section>,
}));

vi.mock('@/components/domain/results/score-band-graph', () => ({
  ScoreBandGraph: () => <div data-testid="score-band" />,
}));

vi.mock('@/components/domain/results/score-conversion-evidence', () => ({
  ScoreConversionEvidence: () => <section data-testid="score-conversion">Score evidence</section>,
}));

vi.mock('@/components/domain/results/listening-part-breakdown', () => ({
  ListeningPartBreakdown: () => <section data-testid="part-breakdown" />,
}));

vi.mock('@/components/domain/results/time-used-summary', () => ({
  TimeUsedSummary: () => <section data-testid="time-used" />,
}));

vi.mock('@/components/domain/results/answer-comparison-card', () => ({
  AnswerComparisonCard: ({ children, label }: { children: React.ReactNode; label: string }) => (
    <section>{label}{children}</section>
  ),
}));

vi.mock('@/components/domain/results/report-answer-control', () => ({
  ReportAnswerControl: () => <button type="button">Report</button>,
}));

vi.mock('@/components/domain/listening/ListeningQuestionPaperViewer', () => ({
  ListeningQuestionPaperViewer: () => <div data-testid="script-pdf" />,
}));

vi.mock('@/components/domain/listening/ListeningFullTranscriptViewer', () => ({
  ListeningFullTranscriptViewer: ({ transcriptSegments }: { transcriptSegments: unknown[] }) => (
    <div data-testid="full-script">{transcriptSegments.length} segments</div>
  ),
}));

vi.mock('@/lib/analytics', () => ({
  analytics: { track: vi.fn() },
}));

vi.mock('@/lib/api', () => ({
  fetchAuthorizedObjectUrl: mockFetchAuthorizedObjectUrl,
}));

vi.mock('@/lib/listening-api', () => ({
  getListeningReview: mockGetReview,
  listListeningAnswerKeyReports: vi.fn().mockResolvedValue({ items: [] }),
}));

vi.mock('@/lib/listening-result-display', () => ({
  hasApprovedListeningConversion: () => false,
}));

vi.mock('@/lib/expert-listening-api', () => ({
  getListeningExpertFeedback: vi.fn().mockResolvedValue(null),
}));

import ListeningReviewPage from './page';
import type { ListeningReviewDto } from '@/lib/listening-api';

const review = {
  attemptId: 'attempt-1',
  paper: {
    id: 'paper-1',
    sourceKind: 'content_paper',
    title: 'Listening review paper',
    slug: 'listening-review-paper',
    difficulty: 'B2',
    estimatedDurationMinutes: 40,
    scenarioType: 'oet_listening',
    audioUrl: null,
    questionPaperUrl: null,
    audioUrlByPart: { A1: '/v1/media/audio-a1/content' },
    audioScriptUrl: '/v1/media/script/content',
    audioAvailable: true,
    audioUnavailableReason: null,
    assetReadiness: { audio: true, questionPaper: true, answerKey: false, audioScript: true },
    transcriptPolicy: 'per_item_post_attempt',
    extracts: [],
  },
  rawScore: 8,
  maxRawScore: 10,
  scaledScore: null,
  grade: 'B',
  passed: true,
  scoreDisplay: '8/10',
  correctCount: 8,
  incorrectCount: 2,
  unansweredCount: 0,
  itemReview: [{
    questionId: 'q-1',
    number: 1,
    partCode: 'A1',
    prompt: 'Complete the note',
    type: 'short_answer',
    learnerAnswer: 'clinic',
    correctAnswer: 'clinic',
    isCorrect: true,
    pointsEarned: 1,
    maxPoints: 1,
    explanation: null,
    errorType: null,
    options: [],
    transcript: null,
    distractorExplanation: null,
  }],
  errorClusters: [],
  recommendedNextDrill: null,
  transcriptAccess: { policy: 'post_submit', state: 'available', allowedQuestionIds: ['q-1'], reason: 'Submitted' },
  transcriptSegments: [{ startMs: 0, endMs: 1000, partCode: 'A1', speakerId: 's1', text: 'Welcome to the clinic.' }],
  strengths: [],
  issues: [],
  generatedAt: null,
} as unknown as ListeningReviewDto;

describe('Listening review audio replay', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mockGetReview.mockResolvedValue(review);
    mockFetchAuthorizedObjectUrl.mockResolvedValue('blob:review-audio');

    let currentTime = 37;
    Object.defineProperty(HTMLMediaElement.prototype, 'currentTime', {
      configurable: true,
      get: () => currentTime,
      set: (value: number) => { currentTime = value; },
    });
    Object.defineProperty(HTMLMediaElement.prototype, 'duration', {
      configurable: true,
      value: 183,
    });
    Object.defineProperty(HTMLMediaElement.prototype, 'play', {
      configurable: true,
      value: vi.fn().mockResolvedValue(undefined),
    });
    Object.defineProperty(HTMLMediaElement.prototype, 'pause', {
      configurable: true,
      value: vi.fn(),
    });
  });

  it('places Score Summary Panel before Show Script and Performance Breakdown in disciplined order', async () => {
    const { container } = render(<ListeningReviewPage />);

    const scorePanel = await screen.findByTestId('score-panel');
    const scoreEvidence = screen.getByTestId('score-conversion');
    const showScript = screen.getByRole('button', { name: /show script/i });
    const partBreakdown = screen.getByTestId('part-breakdown');
    const timeUsed = screen.getByTestId('time-used');

    // 1. Score Summary Panel: score panel -> score conversion
    expect(scorePanel.compareDocumentPosition(scoreEvidence) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
    // 2. Full Transcript & Audio Review: show script follows score conversion
    expect(scoreEvidence.compareDocumentPosition(showScript) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
    // 3. Performance Breakdown: part breakdown and time used follow transcript section
    expect(showScript.compareDocumentPosition(partBreakdown) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
    expect(partBreakdown.compareDocumentPosition(timeUsed) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();

    // Verify zero drill recommendation cards or texts
    expect(screen.queryByText(/recommended next step/i)).toBeNull();
    expect(screen.queryByText(/don't leave gaps drill/i)).toBeNull();
    expect(screen.queryByText(/next drill/i)).toBeNull();
    expect(screen.queryByText(/start drill/i)).toBeNull();
    expect(screen.queryByText(/open recommended drill/i)).toBeNull();

    await waitFor(() => {
      const audio = container.querySelector('audio');
      expect(audio).toHaveAttribute('src', 'blob:review-audio');
    });
    expect(mockFetchAuthorizedObjectUrl).toHaveBeenCalledWith('/v1/media/audio-a1/content');
  });

  it('loads real duration and replays the whole section from zero', async () => {
    const { container } = render(<ListeningReviewPage />);
    const replayButton = await screen.findByRole('button', { name: /replay full audio from start/i });

    await waitFor(() => expect(replayButton).toBeEnabled());
    const audio = container.querySelector('audio');
    expect(audio).not.toBeNull();

    fireEvent.loadedMetadata(audio as HTMLAudioElement);
    expect(await screen.findByText('Duration loaded: 3:03')).toBeInTheDocument();

    fireEvent.click(replayButton);
    expect(audio as HTMLAudioElement).toHaveProperty('currentTime', 0);
    expect(HTMLMediaElement.prototype.play).toHaveBeenCalledTimes(1);

    fireEvent.click(replayButton);
    expect(HTMLMediaElement.prototype.play).toHaveBeenCalledTimes(2);
  });
});
