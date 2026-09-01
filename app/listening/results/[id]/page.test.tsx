import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';

const { mockGetListeningResult, mockListReports } = vi.hoisted(() => ({
  mockGetListeningResult: vi.fn(),
  mockListReports: vi.fn(),
}));

vi.mock('next/link', () => ({
  default: ({ children, href, ...rest }: { children: React.ReactNode; href?: string }) => (
    <a href={href} {...rest}>
      {children}
    </a>
  ),
}));

vi.mock('next/navigation', () => ({
  useParams: () => ({ id: 'attempt-listening-1' }),
  useRouter: () => ({ push: vi.fn(), back: vi.fn() }),
}));

vi.mock('@/components/layout', () => ({
  LearnerDashboardShell: ({ children, pageTitle }: { children: React.ReactNode; pageTitle?: string }) => (
    <div data-testid="learner-shell">
      <h1>{pageTitle}</h1>
      {children}
    </div>
  ),
}));

vi.mock('@/components/ui/button', () => ({
  Button: ({ children, asChild, ...props }: React.ButtonHTMLAttributes<HTMLButtonElement> & { asChild?: boolean }) => (
    asChild ? <>{children}</> : <button type="button" {...props}>{children}</button>
  ),
}));

vi.mock('@/components/ui/skeleton', () => ({
  Skeleton: () => <div data-testid="skeleton" />,
}));

vi.mock('@/components/ui/motion-primitives', () => ({
  MotionSection: ({ children }: { children: React.ReactNode }) => <section data-testid="motion-section">{children}</section>,
  MotionList: ({ children, className }: { children: React.ReactNode; className?: string }) => <div className={className}>{children}</div>,
  MotionItem: ({ children, className }: { children: React.ReactNode; className?: string }) => <div className={className}>{children}</div>,
  MotionCollapse: ({ children, open }: { children: React.ReactNode; open?: boolean }) => (open ? <div>{children}</div> : null),
}));

vi.mock('@/components/domain/results/results-score-panel', () => ({
  ResultsScorePanel: ({ title, eyebrow }: { title: string; eyebrow?: string }) => (
    <section data-testid="results-score-panel">
      <span>{eyebrow}</span>
      <h2>{title}</h2>
    </section>
  ),
}));

vi.mock('@/components/domain/results/score-band-graph', () => ({
  ScoreBandGraph: () => <div data-testid="score-band-graph" />,
}));

vi.mock('@/components/domain/results/score-conversion-evidence', () => ({
  ScoreConversionEvidence: () => <section data-testid="score-conversion-evidence">Score Conversion Evidence</section>,
}));

vi.mock('@/components/domain/results/listening-part-breakdown', () => ({
  ListeningPartBreakdown: () => <section data-testid="listening-part-breakdown">Listening Part Breakdown</section>,
}));

vi.mock('@/components/domain/results/time-used-summary', () => ({
  TimeUsedSummary: () => <section data-testid="time-used-summary">Time Used Summary</section>,
}));

vi.mock('@/components/domain/results/report-answer-control', () => ({
  ReportAnswerControl: () => <button type="button" data-testid="report-answer-control">Report Question</button>,
}));

vi.mock('@/lib/analytics', () => ({
  analytics: { track: vi.fn() },
}));

vi.mock('@/lib/listening-api', () => ({
  getListeningResult: mockGetListeningResult,
  listListeningAnswerKeyReports: mockListReports,
}));

vi.mock('@/lib/listening-result-display', () => ({
  hasApprovedListeningConversion: () => true,
}));

import ListeningResults from './page';
import type { ListeningReviewDto } from '@/lib/listening-api';

const mockResultData: ListeningReviewDto = {
  evaluationId: null,
  attemptId: 'attempt-listening-1',
  paper: {
    id: 'paper-101',
    sourceKind: 'content_paper',
    title: 'Official OET Listening Sample Test 1',
    slug: 'sample-test-1',
    difficulty: 'B2',
    estimatedDurationMinutes: 40,
    scenarioType: 'oet_listening',
    audioUrl: '/v1/media/audio.mp3',
    questionPaperUrl: null,
    audioUrlByPart: { A1: '/v1/media/a1.mp3', A2: '/v1/media/a2.mp3', B: '/v1/media/b.mp3', C1: '/v1/media/c1.mp3', C2: '/v1/media/c2.mp3' },
    audioScriptUrl: '/v1/media/script.pdf',
    audioAvailable: true,
    audioUnavailableReason: null,
    assetReadiness: { audio: true, questionPaper: true, answerKey: true, audioScript: true },
    transcriptPolicy: 'per_item_post_attempt',
    extracts: [],
  },
  rawScore: 32,
  maxRawScore: 42,
  scaledScore: 370,
  grade: 'B',
  passed: true,
  scoreDisplay: '32/42 (370/500 · Grade B)',
  scoreConversionTableVersionKey: 'listening-table-v1',
  scoreConversionErrorCode: null,
  correctCount: 32,
  incorrectCount: 10,
  unansweredCount: 0,
  invalidCount: 0,
  requiresAdminReview: false,
  adminReviewReason: null,
  timeUsed: {
    totalMilliseconds: 2400000,
    sections: [
      { sectionCode: 'Part A', elapsedMilliseconds: 900000 },
      { sectionCode: 'Part B', elapsedMilliseconds: 600000 },
      { sectionCode: 'Part C', elapsedMilliseconds: 900000 },
    ],
  },
  itemReview: [
    {
      questionId: 'q-1',
      number: 1,
      partCode: 'A',
      prompt: 'Patient reported onset of mild discomfort',
      type: 'short_answer',
      learnerAnswer: 'chest tightness',
      correctAnswer: 'chest tightness',
      isCorrect: true,
      isInvalid: false,
      pointsEarned: 1,
      maxPoints: 1,
      explanation: 'Speaker clearly mentions experiencing chest tightness.',
      missReason: null,
      errorType: null,
      options: [],
      transcript: {
        allowed: true,
        excerpt: 'I started feeling this chest tightness about two days ago.',
        distractorExplanation: null,
      },
      distractorExplanation: null,
    },
    {
      questionId: 'q-2',
      number: 2,
      partCode: 'A',
      prompt: 'Recommended dosage for maintenance',
      type: 'short_answer',
      learnerAnswer: '10mg',
      correctAnswer: '20mg',
      isCorrect: false,
      isInvalid: false,
      pointsEarned: 0,
      maxPoints: 1,
      explanation: 'The doctor specified twenty milligrams daily for maintenance.',
      missReason: 'WrongNumber',
      errorType: 'wrong_number',
      options: [],
      transcript: {
        allowed: true,
        excerpt: 'We will keep you on twenty milligrams as the maintenance dose.',
        distractorExplanation: '10mg was the starting initial dose, not the maintenance dose.',
      },
      distractorExplanation: '10mg was the starting initial dose, not the maintenance dose.',
    },
  ],
  transcriptSegments: [
    { startMs: 0, endMs: 5000, partCode: 'A', speakerId: 'doctor', text: 'Welcome. Please tell me what happened.' },
  ],
  errorClusters: [],
  recommendedNextDrill: null,
  transcriptAccess: { policy: 'post_submit', state: 'available', allowedQuestionIds: ['q-1', 'q-2'], reason: 'Submitted' },
  strengths: [],
  issues: [],
  generatedAt: null,
};

describe('Listening Results Page Layout and Drills Removal', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mockGetListeningResult.mockResolvedValue(mockResultData);
    mockListReports.mockResolvedValue({ items: [] });
  });

  it('renders strictly in the disciplined 4-stage vertical order: Score Summary -> Transcript Review -> Breakdown -> Detailed Review', async () => {
    render(<ListeningResults />);

    // Stage 1: Score Summary Panel
    const scorePanel = await screen.findByTestId('results-score-panel');
    const scoreEvidence = screen.getByTestId('score-conversion-evidence');

    // Stage 2: Full Transcript & Audio Review
    const transcriptCard = screen.getByText('Full transcript & audio — permanent access').closest('section')!;
    const showScriptCard = screen.getByRole('heading', { name: /show script/i }).closest('section')!;
    const openTranscriptLink = screen.getByRole('link', { name: /open transcript review/i });
    const showScriptLink = screen.getByRole('link', { name: /show script/i });

    // Stage 3: Performance Breakdown & Timing
    const partBreakdown = screen.getByTestId('listening-part-breakdown');
    const timeUsed = screen.getByTestId('time-used-summary');
    const aiPracticeNote = screen.getByText(/AI Practice Score — not an official OET result\./i);

    // Stage 4: Detailed Review
    const detailedReviewHeading = screen.getByRole('heading', { name: /detailed review/i });

    // Verify ordering between sections:
    // 1. Score Panel is at top, followed by Score Conversion Evidence
    expect(scorePanel.compareDocumentPosition(scoreEvidence) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();

    // 2. Score Conversion Evidence is followed by Full Transcript card, then Show Script card
    expect(scoreEvidence.compareDocumentPosition(transcriptCard) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
    expect(transcriptCard.compareDocumentPosition(showScriptCard) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();

    // 3. Transcript cards are followed by Part Breakdown, Time Used Summary, and AI Practice Score note
    expect(showScriptCard.compareDocumentPosition(partBreakdown) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
    expect(partBreakdown.compareDocumentPosition(timeUsed) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
    expect(timeUsed.compareDocumentPosition(aiPracticeNote) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();

    // 4. Breakdown & Timing is followed by Detailed Review accordion
    expect(aiPracticeNote.compareDocumentPosition(detailedReviewHeading) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();

    // Check link destinations
    expect(openTranscriptLink).toHaveAttribute('href', '/listening/review/attempt-listening-1');
    expect(showScriptLink).toHaveAttribute('href', '/listening/review/attempt-listening-1#show-script');
  });

  it('contains zero drill recommendation text, cards, or buttons anywhere on the page', async () => {
    render(<ListeningResults />);

    await screen.findByTestId('results-score-panel');

    // Completely verify absence of all drill recommendation phrases
    expect(screen.queryByText(/recommended next step/i)).toBeNull();
    expect(screen.queryByText(/don't leave gaps drill/i)).toBeNull();
    expect(screen.queryByText(/next drill/i)).toBeNull();
    expect(screen.queryByText(/start drill/i)).toBeNull();
    expect(screen.queryByText(/open recommended drill/i)).toBeNull();
    expect(screen.queryByText(/drill/i)).toBeNull();
  });

  it('renders detailed question analysis with candidate answer, correct answer, explanation, distractor trap, and report control', async () => {
    render(<ListeningResults />);

    await screen.findByTestId('results-score-panel');

    // Incorrect item (q-2) is automatically expanded
    expect(screen.getByText('Recommended dosage for maintenance')).toBeInTheDocument();
    expect(screen.getByText('10mg')).toBeInTheDocument();
    expect(screen.getByText('20mg')).toBeInTheDocument();
    expect(screen.getByText(/The doctor specified twenty milligrams daily for maintenance\./i)).toBeInTheDocument();
    expect(screen.getByText(/10mg was the starting initial dose, not the maintenance dose\./i)).toBeInTheDocument();
    expect(screen.getByText(/Missed because: Number \/ quantity/i)).toBeInTheDocument();
    expect(screen.getByTestId('report-answer-control')).toBeInTheDocument();

    // Clicking correct item (q-1) toggles its accordion open
    const correctQuestionButton = screen.getByRole('button', { name: /Patient reported onset of mild discomfort/i });
    fireEvent.click(correctQuestionButton);

    expect(screen.getByText(/Speaker clearly mentions experiencing chest tightness\./i)).toBeInTheDocument();
  });

  it('renders admin review warning if attempt requires admin review', async () => {
    mockGetListeningResult.mockResolvedValueOnce({
      ...mockResultData,
      requiresAdminReview: true,
      adminReviewReason: 'invalid_audio_tamper',
    });

    render(<ListeningResults />);

    const warning = await screen.findByTestId('listening-admin-review-warning');
    expect(warning).toBeInTheDocument();
    expect(warning).toHaveTextContent(/Listening attempt requires administrator review\./i);
    expect(warning).toHaveTextContent(/Reason: invalid audio tamper\./i);
  });
});
