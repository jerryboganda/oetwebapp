import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

const {
  mockGetSession,
  mockGetResults,
  mockAiAssess,
  mockDual,
  mockListFreeSamples,
  mockV11Assessment,
} = vi.hoisted(() => ({
  mockGetSession: vi.fn(),
  mockGetResults: vi.fn(),
  mockAiAssess: vi.fn(),
  mockDual: vi.fn(),
  mockListFreeSamples: vi.fn(),
  mockV11Assessment: vi.fn(),
}));

vi.mock('next/navigation', () => ({ useParams: () => ({ id: 'sess-1' }) }));
vi.mock('@/components/layout', () => ({
  LearnerDashboardShell: ({ children }: { children: React.ReactNode }) => <div>{children}</div>,
}));
vi.mock('@/components/domain/speaking/DualAssessmentLayout', () => ({
  DualAssessmentLayout: () => <div data-testid="dual-assessment" />,
}));
vi.mock('@/components/domain/speaking/SpeakingSimulationV11ReportView', () => ({
  SpeakingSimulationV11ReportView: ({ inputKind }: { inputKind?: string | null }) => (
    <div data-testid="v11-report" data-input-kind={String(inputKind)} />
  ),
}));
vi.mock('@/components/domain/speaking/TranscriptPlayerWithComments', () => ({
  TranscriptPlayerWithComments: ({ hideAudioPlayer }: { hideAudioPlayer?: boolean }) => (
    <div data-testid="transcript" data-hide-audio-player={String(Boolean(hideAudioPlayer))} />
  ),
}));
vi.mock('@/lib/api', () => ({ ApiError: class ApiError extends Error {} }));
vi.mock('@/lib/api/speaking-assessments', () => ({ learnerGetDualAssessment: mockDual }));
vi.mock('@/lib/api/speaking-sessions', () => ({
  getSpeakingSession: mockGetSession,
  getSpeakingSessionResults: mockGetResults,
  getSpeakingSessionTranscript: vi.fn().mockResolvedValue(null),
  runAiAssessment: mockAiAssess,
}));
vi.mock('@/lib/api/free-samples', () => ({ listFreeSamples: mockListFreeSamples }));
vi.mock('@/lib/api/speaking-result-visibility', () => ({ getSpeakingResultVisibility: vi.fn().mockResolvedValue(null) }));
vi.mock('@/lib/api/speaking-simulation-v11', () => ({
  getSpeakingSimulationV11Assessment: mockV11Assessment,
}));
vi.mock('@/lib/analytics/speaking-events', () => ({ trackSpeaking: vi.fn() }));

import SpeakingSessionResultsPage from './page';

const SESSION = {
  sessionId: 'sess-1',
  mode: 'ai_self_practice',
  isFreeSample: false,
  submittedAt: '2026-09-23T10:06:00Z',
  card: { cardId: 'rpc-1', scenarioTitle: 'Chest pain review' },
};

describe('Speaking session results: processing → result, never a dead end', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mockGetSession.mockResolvedValue(SESSION);
    mockDual.mockResolvedValue({ sessionId: 'sess-1', ai: null, tutor: null, tutorHistory: [], divergence: null });
    mockGetResults.mockResolvedValue({ assessmentState: 'processing', retryable: false, failureReason: null });
    mockListFreeSamples.mockResolvedValue([]);
    mockAiAssess.mockResolvedValue({ state: 'processing' });
    mockV11Assessment.mockResolvedValue(null);
  });

  it('shows the processing state while grading runs', async () => {
    render(<SpeakingSessionResultsPage />);
    expect(await screen.findByText('Grading your role-play…')).toBeInTheDocument();
    expect(screen.getByTestId('dual-assessment')).toBeInTheDocument();
  });

  it('never calls the v1.1 report endpoints for a classic-graded session (they 404 on every poll)', async () => {
    render(<SpeakingSessionResultsPage />);
    expect(await screen.findByText('Grading your role-play…')).toBeInTheDocument();
    expect(mockV11Assessment).not.toHaveBeenCalled();
  });

  it('loads the v1.1 report when the session is v1.1-scored', async () => {
    mockGetResults.mockResolvedValue({ assessmentState: 'processing', retryable: false, failureReason: null, usesV11: true });
    render(<SpeakingSessionResultsPage />);
    await waitFor(() => expect(mockV11Assessment).toHaveBeenCalledWith('sess-1'));
  });

  it('offers "Try grading again" on a failed, retryable grade and re-requests /ai-assess', async () => {
    const user = userEvent.setup();
    mockGetResults.mockResolvedValue({ assessmentState: 'failed', retryable: true, failureReason: 'Transcription timed out.' });
    render(<SpeakingSessionResultsPage />);

    expect(await screen.findByText('Transcription timed out.')).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Try grading again' }));

    await waitFor(() => expect(mockAiAssess).toHaveBeenCalledWith('sess-1'));
    expect(await screen.findByText('Grading your role-play…')).toBeInTheDocument();
  });

  it('shows completion after the free Speaking attempt and directs new attempts to the paid route', async () => {
    mockGetSession.mockResolvedValue({ ...SESSION, isFreeSample: true });
    mockGetResults.mockResolvedValue({ assessmentState: 'completed', retryable: false, failureReason: null });
    mockListFreeSamples.mockResolvedValue([
      { professionId: 'medicine', contentId: 'rpc-1', state: 'completed', route: null, limit: 1, successfulCount: 1, remaining: 0 },
    ]);
    render(<SpeakingSessionResultsPage />);

    expect(await screen.findByText('Free sample completed')).toBeInTheDocument();
    const cta = await screen.findByRole('link', { name: 'Start new attempt' });
    expect(cta).toHaveAttribute('href', '/speaking/roleplay/rpc-1');
    expect(screen.getByText('This free sample is complete. Repeating the card starts a new attempt and uses Speaking credits.')).toBeInTheDocument();
  });

  describe('recommended drills: AI-generated, personalised, no tutor wording (owner spec 4 Oct 2026)', () => {
    const AI_WITH_DRILLS = {
      assessmentId: 'a-1',
      provider: 'provider',
      modelId: 'model',
      criterionScores: {},
      estimatedScaledScore: 340,
      readinessBand: 'borderline',
      overallSummary: 'Clear and kind.',
      confidenceBand: 'medium',
      generatedAt: '2026-10-04T10:00:00Z',
      isAdvisory: true,
      report: {
        strengths: [],
        priorityWeaknesses: [],
        drills: [{
          title: 'Explore concerns before advice',
          criterion: 'patientPerspective',
          weakPoint: 'You moved to advice before asking what worried the patient.',
          practise: 'Ask one open concern question before any advice.',
          example: 'What worries you most about this?',
        }],
      },
    };

    it('lists each drill with what happened, what to practise and an example to say', async () => {
      const user = userEvent.setup();
      mockDual.mockResolvedValue({ sessionId: 'sess-1', ai: AI_WITH_DRILLS, tutor: null, tutorHistory: [], divergence: null });
      mockGetResults.mockResolvedValue({ assessmentState: 'completed', retryable: false, failureReason: null });
      render(<SpeakingSessionResultsPage />);

      await user.click(await screen.findByRole('tab', { name: /Recommended drills/ }));

      const drills = await screen.findByTestId('speaking-drills');
      expect(drills).toHaveTextContent('Explore concerns before advice');
      expect(drills).toHaveTextContent('What happened');
      expect(drills).toHaveTextContent('You moved to advice before asking what worried the patient.');
      expect(drills).toHaveTextContent('Ask one open concern question before any advice.');
      expect(drills).toHaveTextContent('What worries you most about this?');
      expect(screen.queryByRole('link', { name: /Open drill/ })).not.toBeInTheDocument();
    });

    it('says drills arrive with the AI assessment while it is still being produced, never "tutor review"', async () => {
      const user = userEvent.setup();
      render(<SpeakingSessionResultsPage />);

      await user.click(await screen.findByRole('tab', { name: /Recommended drills/ }));

      expect(await screen.findByTestId('speaking-drills-empty')).toHaveTextContent('appear here as soon as your AI assessment is ready');
      expect(document.body.textContent).not.toMatch(/tutor review|request tutor|both estimates are advisory|no tutor review yet/i);
    });

    it('says plainly when an assessment had no drills to suggest', async () => {
      const user = userEvent.setup();
      mockDual.mockResolvedValue({
        sessionId: 'sess-1',
        ai: { ...AI_WITH_DRILLS, report: { strengths: [], priorityWeaknesses: [], drills: [] } },
        tutor: null,
        tutorHistory: [],
        divergence: null,
      });
      mockGetResults.mockResolvedValue({ assessmentState: 'completed', retryable: false, failureReason: null });
      render(<SpeakingSessionResultsPage />);

      await user.click(await screen.findByRole('tab', { name: /Recommended drills/ }));

      expect(await screen.findByTestId('speaking-drills-empty')).toHaveTextContent('No practice drills were suggested for this attempt.');
    });
  });

  describe('wording follows what the learner handed in', () => {
    const PROCESSING = { assessmentState: 'processing', retryable: false, failureReason: null };
    const LIVE_NOTE = 'The full conversation is not stored as one replayable recording. Short microphone clips are available with verified candidate-transcript evidence. Echo cancellation is enabled, but speaker audio may still be picked up by your microphone.';

    it.each<[string | undefined, RegExp]>([
      ['live_voice', /^We saved the transcript of your live conversation on .+ and queued it for marking\.$/],
      ['recording', /^We received your recording on .+ and queued it for marking\.$/],
      [undefined, /^We received your role-play on .+ and queued it for marking\.$/],
    ])('names it on the "Submission received" banner (inputKind %s)', async (inputKind, banner) => {
      mockGetResults.mockResolvedValue({ ...PROCESSING, inputKind });
      render(<SpeakingSessionResultsPage />);

      expect(await screen.findByText('Submission received')).toBeInTheDocument();
      expect(screen.getByText(banner)).toBeInTheDocument();
    });

    it.each<[string | undefined, string]>([
      ['live_voice', 'Your live conversation transcript is being marked. This page updates automatically.'],
      ['recording', 'Your recording is being transcribed and marked. This page updates automatically.'],
      [undefined, 'Your role-play is being marked. This page updates automatically.'],
    ])('says what is being marked while grading runs (inputKind %s)', async (inputKind, pending) => {
      mockGetResults.mockResolvedValue({ ...PROCESSING, inputKind });
      render(<SpeakingSessionResultsPage />);

      expect(await screen.findByText(pending)).toBeInTheDocument();
    });

    it('treats a human-tutor room as recorded even when the server says nothing', async () => {
      mockGetSession.mockResolvedValue({ ...SESSION, mode: 'live_tutor' });
      mockGetResults.mockResolvedValue({ ...PROCESSING, inputKind: null });
      render(<SpeakingSessionResultsPage />);

      expect(await screen.findByText(/^We received your recording on .+ and queued it for marking\.$/)).toBeInTheDocument();
      expect(screen.getByText('Your recording is being transcribed and marked. This page updates automatically.')).toBeInTheDocument();
    });

    it('keeps the live-conversation wording after "Try grading again"', async () => {
      const user = userEvent.setup();
      mockGetResults.mockResolvedValue({
        assessmentState: 'failed',
        retryable: true,
        failureReason: 'The grader was busy.',
        inputKind: 'live_voice',
      });
      render(<SpeakingSessionResultsPage />);

      await user.click(await screen.findByRole('button', { name: 'Try grading again' }));

      expect(await screen.findByText('Your live conversation transcript is being marked. This page updates automatically.')).toBeInTheDocument();
      expect(screen.queryByText(/role-play is being marked/)).not.toBeInTheDocument();
    });

    it('says the transcript is saved when a failed live-conversation grade comes with no reason', async () => {
      mockGetResults.mockResolvedValue({ assessmentState: 'failed', retryable: true, failureReason: null, inputKind: 'live_voice' });
      render(<SpeakingSessionResultsPage />);

      expect(await screen.findByText('Your transcript is saved. No credits were used for this failed grade.')).toBeInTheDocument();
    });

    it('never says "recording" outside the transcript tab for a live conversation', async () => {
      mockGetResults.mockResolvedValue({ ...PROCESSING, inputKind: 'live_voice' });
      render(<SpeakingSessionResultsPage />);
      await screen.findByText('Submission received');

      expect(document.body.textContent).not.toMatch(/recording/i);
    });

    it('uses the same banner in the v1.1 report view and tells it the kind', async () => {
      mockGetResults.mockResolvedValue({ ...PROCESSING, usesV11: true, inputKind: 'live_voice' });
      mockV11Assessment.mockResolvedValue({ assessmentId: 'a-1' });
      render(<SpeakingSessionResultsPage />);

      expect(await screen.findByTestId('v11-report')).toHaveAttribute('data-input-kind', 'live_voice');
      expect(screen.getByText(/^We saved the transcript of your live conversation on .+ and queued it for marking\.$/)).toBeInTheDocument();
      expect(document.body.textContent).not.toMatch(/v1\.1/);
    });

    it('hides the dead audio player and explains why on the transcript tab of a live conversation', async () => {
      const user = userEvent.setup();
      mockGetResults.mockResolvedValue({ ...PROCESSING, inputKind: 'live_voice' });
      render(<SpeakingSessionResultsPage />);

      await user.click(await screen.findByRole('tab', { name: 'Transcript' }));

      expect(screen.getByText(LIVE_NOTE)).toBeInTheDocument();
      expect(screen.getByTestId('transcript')).toHaveAttribute('data-hide-audio-player', 'true');
    });

    it('keeps the player strip, and adds no live-conversation note, for a recording', async () => {
      const user = userEvent.setup();
      mockGetResults.mockResolvedValue({ ...PROCESSING, inputKind: 'recording' });
      render(<SpeakingSessionResultsPage />);

      await user.click(await screen.findByRole('tab', { name: 'Transcript' }));

      expect(screen.queryByText(LIVE_NOTE)).not.toBeInTheDocument();
      expect(screen.getByTestId('transcript')).toHaveAttribute('data-hide-audio-player', 'false');
    });

    it('hides the player without a note when the kind is not known yet', async () => {
      const user = userEvent.setup();
      render(<SpeakingSessionResultsPage />);

      await user.click(await screen.findByRole('tab', { name: 'Transcript' }));

      expect(screen.queryByText(LIVE_NOTE)).not.toBeInTheDocument();
      expect(screen.getByTestId('transcript')).toHaveAttribute('data-hide-audio-player', 'true');
    });
  });
});
