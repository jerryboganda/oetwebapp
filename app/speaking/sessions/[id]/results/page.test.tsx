import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

const {
  mockGetSession,
  mockGetResults,
  mockAiAssess,
  mockDual,
  mockListFreeSamples,
} = vi.hoisted(() => ({
  mockGetSession: vi.fn(),
  mockGetResults: vi.fn(),
  mockAiAssess: vi.fn(),
  mockDual: vi.fn(),
  mockListFreeSamples: vi.fn(),
}));

vi.mock('next/navigation', () => ({ useParams: () => ({ id: 'sess-1' }) }));
vi.mock('@/components/layout', () => ({
  LearnerDashboardShell: ({ children }: { children: React.ReactNode }) => <div>{children}</div>,
}));
vi.mock('@/components/domain/speaking/DualAssessmentLayout', () => ({
  DualAssessmentLayout: () => <div data-testid="dual-assessment" />,
}));
vi.mock('@/components/domain/speaking/SpeakingSimulationV11ReportView', () => ({
  SpeakingSimulationV11ReportView: () => <div data-testid="v11-report" />,
}));
vi.mock('@/components/domain/speaking/TranscriptPlayerWithComments', () => ({
  TranscriptPlayerWithComments: () => <div data-testid="transcript" />,
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
  getSpeakingSimulationV11Assessment: vi.fn().mockResolvedValue(null),
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
  });

  it('shows the processing state while grading runs', async () => {
    render(<SpeakingSessionResultsPage />);
    expect(await screen.findByText('Grading your role-play…')).toBeInTheDocument();
    expect(screen.getByTestId('dual-assessment')).toBeInTheDocument();
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
});
