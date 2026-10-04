import { act, render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

const {
  ApiErrorMock,
  mockGetExamResults,
  mockGetCardStatus,
  mockRunAssessment,
  mockV11Assessment,
  mockV11Combined,
} = vi.hoisted(() => {
  class ApiErrorMock extends Error {
    status: number;
    code: string;
    userMessage: string;

    constructor(status: number, code: string, userMessage: string) {
      super(userMessage);
      this.status = status;
      this.code = code;
      this.userMessage = userMessage;
    }
  }

  return {
    ApiErrorMock,
    mockGetExamResults: vi.fn(),
    mockGetCardStatus: vi.fn(),
    mockRunAssessment: vi.fn(),
    mockV11Assessment: vi.fn(),
    mockV11Combined: vi.fn(),
  };
});

vi.mock('next/navigation', () => ({ useParams: () => ({ id: 'exam-1' }) }));
vi.mock('@/lib/api', () => ({ ApiError: ApiErrorMock }));
vi.mock('@/lib/api/speaking-exams', () => ({ getSpeakingExamResults: mockGetExamResults }));
vi.mock('@/lib/api/speaking-sessions', () => ({
  getSpeakingSessionResults: mockGetCardStatus,
  getSpeakingSessionTranscript: vi.fn().mockResolvedValue(null),
  runAiAssessment: mockRunAssessment,
}));
vi.mock('@/lib/api/speaking-simulation-v11', () => ({
  getSpeakingSimulationV11Assessment: mockV11Assessment,
  getSpeakingSimulationV11CombinedAssessment: mockV11Combined,
  getSpeakingSimulationV11TutorOverride: vi.fn().mockResolvedValue(null),
  runSpeakingSimulationV11CombinedAssessment: vi.fn().mockResolvedValue(null),
}));
// Same labels as the real map.
vi.mock('@/lib/api/speaking-assessments', () => ({
  readinessBandLabel: (band: string) => {
    const labels: Record<string, string> = {
      not_ready: 'Not yet ready',
      developing: 'Developing',
      borderline: 'Borderline',
      on_track: 'On track',
      exam_ready: 'Exam ready',
      strong: 'Strong',
      exceeds: 'Exceeds expectations',
    };
    return labels[band] ?? (band ? band.replace(/_/g, ' ') : 'Unknown');
  },
}));
vi.mock('@/components/domain/speaking/SpeakingSimulationV11ReportView', () => ({
  SpeakingSimulationV11ReportView: ({ inputKind }: { inputKind?: string | null }) => (
    <div data-testid="v11-report" data-input-kind={String(inputKind)} />
  ),
}));

import SpeakingExamResultsPage from './page';

const assessment = (score: number, band: string) => ({
  assessmentId: `assessment-${score}`,
  provider: 'provider',
  modelId: 'model',
  criterionScores: { intelligibility: { score: 5, maxScore: 6, rationale: 'Clear.', evidenceQuotes: [] as string[] } },
  estimatedScaledScore: score,
  readinessBand: band,
  overallSummary: 'Clear and kind.',
  confidenceBand: 'medium',
  generatedAt: '2026-10-01T10:00:00Z',
  isAdvisory: true,
});

const scoredCard = (cardNumber: number, sessionId: string, score: number, band: string) => ({
  cardNumber,
  sessionId,
  status: 'scored',
  assessment: assessment(score, band),
});

const scoredResults = (overrides: Record<string, unknown> = {}) => ({
  examId: 'exam-1',
  mode: 'ai',
  state: 'completed',
  overallStatus: 'scored',
  combinedScaledScore: 310,
  readinessBand: 'borderline',
  grade: 'C+',
  scoreLabel: 'provisional',
  cards: [scoredCard(1, 'sess-a', 300, 'borderline'), scoredCard(2, 'sess-b', 320, 'borderline')],
  ...overrides,
});

const pendingResults = (overrides: Record<string, unknown> = {}) => ({
  examId: 'exam-1',
  mode: 'ai',
  state: 'completed',
  overallStatus: 'pending',
  combinedScaledScore: null,
  readinessBand: null,
  cards: [
    { cardNumber: 1, sessionId: 'sess-a', status: 'pending', assessment: null },
    { cardNumber: 2, sessionId: 'sess-b', status: 'pending', assessment: null },
  ],
  ...overrides,
});

const notCompletedResults = (state: string) => pendingResults({
  state,
  cards: [
    { cardNumber: 1, sessionId: '', status: 'pending', assessment: null },
    { cardNumber: 2, sessionId: '', status: 'pending', assessment: null },
  ],
});

const cardStatus = (overrides: Record<string, unknown> = {}) => ({
  assessmentState: 'completed',
  retryable: false,
  failureReason: null,
  inputKind: 'live_voice',
  ...overrides,
});

// Card A failed (and can be retried unless overridden); card B is fine.
const cardAFailed = (overrides: Record<string, unknown> = {}) => async (sessionId: string) =>
  sessionId === 'sess-a'
    ? cardStatus({ assessmentState: 'failed', retryable: true, failureReason: 'The grader was busy.', ...overrides })
    : cardStatus();

async function flush(ms = 0) {
  await act(async () => {
    await vi.advanceTimersByTimeAsync(ms);
  });
}

describe('Speaking exam results page', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mockGetExamResults.mockResolvedValue(scoredResults());
    mockGetCardStatus.mockResolvedValue(cardStatus());
    mockRunAssessment.mockResolvedValue({ state: 'processing' });
    mockV11Assessment.mockResolvedValue(null);
    mockV11Combined.mockResolvedValue(null);
  });

  describe('a scored exam', () => {
    it.each<[string, string]>([
      ['exam_ready', 'Exam ready'],
      ['not_ready', 'Not yet ready'],
      ['developing', 'Developing'],
      ['borderline', 'Borderline'],
      ['strong', 'Strong'],
    ])('shows band %s as "%s", never the raw code', async (code, label) => {
      mockGetExamResults.mockResolvedValue(scoredResults({
        readinessBand: code,
        cards: [scoredCard(1, 'sess-a', 300, code), scoredCard(2, 'sess-b', 320, code)],
      }));
      render(<SpeakingExamResultsPage />);

      expect(await screen.findByText(`Readiness band: ${label}`)).toBeInTheDocument();
      expect(screen.getAllByText(`Band: ${label}`)).toHaveLength(2);
      expect(document.body.textContent).not.toContain(code);
    });

    it('shows ONE final score out of 500 with the OET letter as the grade, never the band as the grade', async () => {
      render(<SpeakingExamResultsPage />);

      expect(await screen.findByText('Grade C+')).toBeInTheDocument();
      expect(screen.getByTestId('grade-value')).toHaveTextContent('310');
      // The two cards keep their own breakdown beside the one combined result.
      expect(screen.getByText('300/500')).toBeInTheDocument();
      expect(screen.getByText('320/500')).toBeInTheDocument();
    });

    it.each<[number, string]>([
      [450, 'A'], [430, 'B'], [350, 'B'], [340, 'C+'], [290, 'C'], [190, 'D'], [90, 'E'],
    ])('derives the letter for a reported %i as Grade %s when the server sends none, and never B+', async (score, letter) => {
      mockGetExamResults.mockResolvedValue(scoredResults({ combinedScaledScore: score, grade: undefined }));
      render(<SpeakingExamResultsPage />);

      expect(await screen.findByText(`Grade ${letter}`)).toBeInTheDocument();
      expect(document.body.textContent).not.toContain('B+');
    });

    it('labels the score provisional until the grader has been calibrated', async () => {
      render(<SpeakingExamResultsPage />);

      expect(await screen.findByTestId('speaking-score-provisional')).toHaveTextContent('Provisional score — calibration in progress');
    });

    it('drops the provisional label once the server says the score is a calibrated practice estimate', async () => {
      mockGetExamResults.mockResolvedValue(scoredResults({ scoreLabel: 'ai_practice_estimate' }));
      render(<SpeakingExamResultsPage />);

      expect(await screen.findByText('AI practice estimate, not an official OET result.')).toBeInTheDocument();
      expect(screen.queryByTestId('speaking-score-provisional')).not.toBeInTheDocument();
    });

    it('treats a payload with no label as provisional, never as a settled score', async () => {
      mockGetExamResults.mockResolvedValue(scoredResults({ scoreLabel: undefined }));
      render(<SpeakingExamResultsPage />);

      expect(await screen.findByTestId('speaking-score-provisional')).toBeInTheDocument();
    });

    it('says the result is an AI practice estimate and not an official OET result', async () => {
      render(<SpeakingExamResultsPage />);

      expect(await screen.findByText('AI practice estimate, not an official OET result.')).toBeInTheDocument();
      expect(screen.getByText(/Official OET results can only be obtained from an OET test session/)).toBeInTheDocument();
    });

    it('links each card to its own details and transcript, and offers the history page', async () => {
      render(<SpeakingExamResultsPage />);

      const links = await screen.findAllByRole('link', { name: 'View details and transcript' });
      expect(links.map((link) => link.getAttribute('href'))).toEqual([
        '/speaking/sessions/sess-a/results',
        '/speaking/sessions/sess-b/results',
      ]);
      expect(screen.getByRole('link', { name: 'Back to Speaking' })).toHaveAttribute('href', '/speaking');
      expect(screen.getByRole('link', { name: 'View history' })).toHaveAttribute('href', '/submissions');
    });

    it('never says "recording" about a live conversation', async () => {
      render(<SpeakingExamResultsPage />);
      await screen.findByText('Combined result');

      expect(document.body.textContent).not.toMatch(/recording/i);
    });
  });

  describe('while the cards are being marked', () => {
    it('says it is scoring, and has no estimate note yet', async () => {
      mockGetExamResults.mockResolvedValue(pendingResults());
      mockGetCardStatus.mockResolvedValue(cardStatus({ assessmentState: 'processing' }));
      render(<SpeakingExamResultsPage />);

      expect(
        await screen.findByText('Scoring your exam… this can take a few minutes. This page refreshes automatically.'),
      ).toBeInTheDocument();
      expect(screen.queryByText('AI practice estimate, not an official OET result.')).not.toBeInTheDocument();
      expect(document.body.textContent).not.toMatch(/recording/i);
    });
  });

  describe('a card whose grading failed', () => {
    it('shows the reason from the server and asks it to grade the card again', async () => {
      const user = userEvent.setup();
      mockGetExamResults.mockResolvedValue(pendingResults());
      mockGetCardStatus.mockImplementation(cardAFailed());
      render(<SpeakingExamResultsPage />);

      expect(await screen.findByText('Card A: grading could not be completed')).toBeInTheDocument();
      expect(screen.getByText('The grader was busy.')).toBeInTheDocument();

      await user.click(screen.getByRole('button', { name: 'Try grading again' }));
      expect(mockRunAssessment).toHaveBeenCalledWith('sess-a');
      await waitFor(() => expect(screen.queryByText('Card A: grading could not be completed')).not.toBeInTheDocument());
    });

    it.each<[string, string | null, string]>([
      ['ai', 'live_voice', 'Your transcript is saved. No credits were used for this failed grade.'],
      ['ai', 'recording', 'Your recording is saved. No credits were used for this failed grade.'],
      ['ai', null, 'Your role-play is saved. No credits were used for this failed grade.'],
      // A tutor room is always recorded, whatever the server says.
      ['live_tutor', 'live_voice', 'Your recording is saved. No credits were used for this failed grade.'],
    ])('names what was saved when the server gives no reason (exam %s, card %s)', async (mode, inputKind, expected) => {
      mockGetExamResults.mockResolvedValue(pendingResults({ mode }));
      mockGetCardStatus.mockImplementation(cardAFailed({ failureReason: null, retryable: false, inputKind }));
      render(<SpeakingExamResultsPage />);

      expect(await screen.findByText(expected)).toBeInTheDocument();
    });

    it('shows why grading could not be restarted instead of hiding it', async () => {
      const user = userEvent.setup();
      mockGetExamResults.mockResolvedValue(pendingResults());
      mockGetCardStatus.mockImplementation(cardAFailed());
      mockRunAssessment.mockRejectedValue(new ApiErrorMock(429, 'rate_limited', 'Too many requests. Please wait a moment and try again.'));
      render(<SpeakingExamResultsPage />);

      await user.click(await screen.findByRole('button', { name: 'Try grading again' }));

      expect(await screen.findByText('Too many requests. Please wait a moment and try again.')).toBeInTheDocument();
      expect(screen.getByText('Card A: grading could not be completed')).toBeInTheDocument();
    });

    it('does not report the browser giving up as a failure: grading keeps running on the server', async () => {
      const user = userEvent.setup();
      mockGetExamResults.mockResolvedValue(pendingResults());
      mockGetCardStatus.mockImplementation(cardAFailed());
      mockRunAssessment.mockRejectedValue(new ApiErrorMock(408, 'request_timeout', 'The request timed out. Please try again.'));
      render(<SpeakingExamResultsPage />);

      await user.click(await screen.findByRole('button', { name: 'Try grading again' }));

      await waitFor(() => expect(screen.queryByText('Card A: grading could not be completed')).not.toBeInTheDocument());
      expect(screen.queryByText(/timed out/)).not.toBeInTheDocument();
    });
  });

  describe('an exam that was not completed', () => {
    beforeEach(() => {
      vi.useFakeTimers();
    });

    afterEach(() => {
      vi.useRealTimers();
    });

    it.each(['expired', 'cancelled'])('says so, offers a new exam and stops polling (%s)', async (state) => {
      mockGetExamResults.mockResolvedValue(notCompletedResults(state));
      render(<SpeakingExamResultsPage />);
      await flush();
      await flush();

      expect(screen.getByText('This exam was not completed.')).toBeInTheDocument();
      expect(screen.getByRole('link', { name: 'Start a new exam' })).toHaveAttribute('href', '/speaking/exam');
      expect(screen.getByRole('link', { name: 'Back to Speaking' })).toHaveAttribute('href', '/speaking');
      expect(screen.queryByText(/Scoring your exam/)).not.toBeInTheDocument();
      expect(document.querySelector('.animate-spin')).toBeNull();

      await flush(60_000);
      expect(mockGetExamResults).toHaveBeenCalledTimes(1);
    });

    it('still polls an exam that is simply waiting for its marks (control for the test above)', async () => {
      mockGetExamResults.mockResolvedValue(pendingResults());
      mockGetCardStatus.mockResolvedValue(cardStatus({ assessmentState: 'processing' }));
      render(<SpeakingExamResultsPage />);
      await flush();
      await flush();
      expect(mockGetExamResults).toHaveBeenCalledTimes(1);

      await flush(4_000);
      expect(mockGetExamResults).toHaveBeenCalledTimes(2);
    });
  });

  describe('the v1.1 report', () => {
    it('is told which kind the card handed in', async () => {
      mockGetCardStatus.mockImplementation(async (sessionId: string) =>
        sessionId === 'sess-a' ? cardStatus({ usesV11: true }) : cardStatus({ inputKind: 'recording' }));
      mockV11Assessment.mockResolvedValue({ status: 'Complete', assessmentId: 'v11-a' });
      render(<SpeakingExamResultsPage />);

      expect(await screen.findByTestId('v11-report')).toHaveAttribute('data-input-kind', 'live_voice');
    });

    it.each<[string, string, string]>([
      ['live_voice', 'live_voice', 'live_voice'],
      ['live_voice', 'recording', 'null'],
    ])('combined report: cards %s and %s give %s', async (kindA, kindB, expected) => {
      mockGetCardStatus.mockImplementation(async (sessionId: string) =>
        cardStatus({ usesV11: true, inputKind: sessionId === 'sess-a' ? kindA : kindB }));
      mockV11Assessment.mockResolvedValue({ status: 'Complete', assessmentId: 'v11' });
      mockV11Combined.mockResolvedValue({ status: 'Complete', assessmentId: 'v11-combined' });
      render(<SpeakingExamResultsPage />);

      expect(await screen.findByTestId('v11-report')).toHaveAttribute('data-input-kind', expected);
    });
  });
});
