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
  getSpeakingSimulationV11TutorOverride: vi.fn().mockResolvedValue(null),
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

  it('free sample with one result: primary CTA "Try Again - 1 Free Retry Remaining" to the same card', async () => {
    mockGetSession.mockResolvedValue({ ...SESSION, isFreeSample: true });
    mockGetResults.mockResolvedValue({ assessmentState: 'completed', retryable: false, failureReason: null });
    mockListFreeSamples.mockResolvedValue([
      { professionId: 'medicine', contentId: 'rpc-1', state: 'retry_available', route: '/speaking/roleplay/rpc-1?free=1' },
    ]);
    render(<SpeakingSessionResultsPage />);

    const cta = await screen.findByRole('link', { name: 'Try Again - 1 Free Retry Remaining' });
    expect(cta).toHaveAttribute('href', '/speaking/roleplay/rpc-1?free=1');
    // No paid "reattempt" of the free card.
    expect(screen.queryByText('Reattempt this speaking card')).not.toBeInTheDocument();
  });

  it('free sample after the second result: "Free sample completed"', async () => {
    mockListFreeSamples.mockResolvedValue([
      { professionId: 'medicine', contentId: 'rpc-1', state: 'completed', route: null },
    ]);
    render(<SpeakingSessionResultsPage />);

    expect(await screen.findByText('Free sample completed')).toBeInTheDocument();
    expect(screen.queryByRole('link', { name: /free retry/i })).not.toBeInTheDocument();
  });

  describe('wording follows what the learner handed in', () => {
    const PROCESSING = { assessmentState: 'processing', retryable: false, failureReason: null };
    const LIVE_NOTE = 'No audio recording is stored for live conversations, so there is nothing to play back. This transcript is what was marked.';

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
