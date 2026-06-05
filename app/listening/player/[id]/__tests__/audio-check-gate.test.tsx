import { createElement } from 'react';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';

// WORK-STREAM 2 — when a strict Listening exam is blocked by the sound-check
// gate, the player must surface a "Run the sound check" CTA that links to
// /listening/audio-check?returnTo=<this player URL> rather than a dead-end
// error. The gate arrives two ways:
//   • the server `advance` rejects intro→a1_preview with `audio-check-required`
//   • `startListeningAttempt` throws a 400 carrying `listening_audio_check_required`
// Both paths are exercised below.

const {
  mockGetListeningSession,
  mockStartListeningAttempt,
  mockHeartbeat,
  mockSubmit,
  mockSaveAnswer,
  mockRecordIntegrity,
  mockTrack,
  mockUseSearchParams,
  mockReplace,
  mockPush,
  mockV2GetState,
  mockV2Advance,
  mockRecordTechReadiness,
} = vi.hoisted(() => ({
  mockGetListeningSession: vi.fn(),
  mockStartListeningAttempt: vi.fn(),
  mockHeartbeat: vi.fn(),
  mockSubmit: vi.fn(),
  mockSaveAnswer: vi.fn(),
  mockRecordIntegrity: vi.fn(),
  mockTrack: vi.fn(),
  mockUseSearchParams: vi.fn(),
  mockReplace: vi.fn(),
  mockPush: vi.fn(),
  mockV2GetState: vi.fn(),
  mockV2Advance: vi.fn(),
  mockRecordTechReadiness: vi.fn(),
}));

vi.mock('next/navigation', () => ({
  useParams: () => ({ id: 'lp-001' }),
  useRouter: () => ({ push: mockPush, replace: mockReplace }),
  useSearchParams: () => mockUseSearchParams(),
}));

vi.mock('@/lib/listening-api', () => ({
  getListeningSession: mockGetListeningSession,
  startListeningAttempt: mockStartListeningAttempt,
  heartbeatListeningAttempt: mockHeartbeat,
  recordListeningIntegrityEvent: mockRecordIntegrity,
}));

vi.mock('@/lib/listening/v2-api', () => ({
  listeningV2Api: {
    getState: mockV2GetState,
    advance: mockV2Advance,
    recordTechReadiness: mockRecordTechReadiness,
    saveAnswer: mockSaveAnswer,
    submit: mockSubmit,
  },
}));

vi.mock('@/components/domain/listening/TechReadinessCheck', () => ({
  TechReadinessCheck: ({ onReady }: { onReady: (result: { audioOk: boolean; durationMs: number }) => void }) => (
    <button type="button" onClick={() => onReady({ audioOk: true, durationMs: 1500 })}>
      Run mocked readiness
    </button>
  ),
}));

vi.mock('@/lib/api', () => ({
  completeMockSection: vi.fn(),
}));

vi.mock('@/lib/analytics', () => ({
  analytics: { track: mockTrack },
}));

vi.mock('@/components/layout/app-shell', () => ({
  AppShell: ({ children }: { children: React.ReactNode }) => <div data-testid="app-shell">{children}</div>,
}));

// Canonical motion/react Proxy mock — strips motion-specific props and renders
// the underlying DOM element so RTL queries see real children.
vi.mock('motion/react', () => {
  const stripMotion = (props: Record<string, unknown>) => {
    const out: Record<string, unknown> = {};
    for (const [key, value] of Object.entries(props)) {
      if (
        key === 'initial' || key === 'animate' || key === 'exit' || key === 'transition'
        || key === 'variants' || key === 'whileHover' || key === 'whileTap'
        || key === 'whileInView' || key === 'viewport' || key === 'layout'
        || key === 'layoutId' || key === 'drag' || key === 'dragConstraints'
      ) continue;
      out[key] = value;
    }
    return out;
  };
  const motion: any = new Proxy({}, {
    get: (_target, tag: string) => (props: any) => {
      const { children, ...rest } = props ?? {};
      return createElement(tag, stripMotion(rest), children);
    },
  });
  return {
    motion,
    useReducedMotion: () => false,
    AnimatePresence: ({ children }: { children: React.ReactNode }) => <>{children}</>,
  };
});

import ListeningPlayer from '../page';

/**
 * Click the mocked TechReadinessCheck "Run mocked readiness" button reliably.
 *
 * The strict intro card can re-mount once more right after the async session
 * load commits (the strict-resume effect resets `hasStarted`/`strictServerState`
 * on its post-session run, and the motion AnimatePresence mock defers an
 * exit-complete `setTimeout(0)`), which detaches the button instance a bare
 * `fireEvent.click(await findByRole(...))` captured a tick earlier. Clicking a
 * detached node is a silent no-op, so `techReadiness` is never set and the
 * "Start audio" button stays disabled — the source of the intermittent
 * sound-check-gate failures.
 *
 * Re-querying and clicking inside `waitFor`, asserting the click actually
 * enabled the start button, makes the interaction robust to that transient
 * remount without weakening any assertion.
 */
async function runMockedReadiness() {
  await waitFor(() => {
    const button = screen.getByRole('button', { name: /run mocked readiness/i });
    fireEvent.click(button);
    expect(screen.getByRole('button', { name: /start audio/i })).not.toBeDisabled();
  });
}

function makeExamSession() {
  return {
    paper: {
      id: 'lp-001',
      sourceKind: 'content_paper',
      title: 'OET Listening Exam Paper 1',
      slug: 'oet-listening-exam-paper-1',
      difficulty: 'medium',
      estimatedDurationMinutes: 42,
      scenarioType: 'standard',
      audioUrl: 'https://cdn.example/audio.mp3',
      questionPaperUrl: null,
      audioAvailable: true,
      audioUnavailableReason: null,
      assetReadiness: { audio: true, questionPaper: true, answerKey: true, audioScript: true },
      transcriptPolicy: 'per_item_post_attempt',
      extracts: [
        {
          partCode: 'A1', displayOrder: 1, kind: 'consultation', title: 'Extract 1',
          accentCode: 'en-GB', speakers: [],
          audioStartMs: 12_000, audioEndMs: 240_000,
        },
      ],
    },
    // No in-progress attempt — this is a fresh strict start.
    attempt: null,
    questions: [
      { id: 'q-1', number: 1, partCode: 'A1', text: 'Q1?', type: 'short_answer', options: [], points: 1 },
    ],
    modePolicy: {
      mode: 'exam',
      canPause: false,
      canScrub: false,
      onePlayOnly: true,
      autosave: true,
      transcriptPolicy: 'per_item_post_attempt',
    },
    scoring: { maxRawScore: 42, passRawScore: 30, passScaledScore: 350 },
    readiness: { objectiveReady: true, questionCount: 42, audioAvailable: true, missingReason: null },
  };
}

function makeAdvanceResult(overrides: Record<string, unknown> = {}) {
  return {
    outcome: 'applied',
    state: null,
    confirmToken: null,
    confirmTokenTtlMs: null,
    rejectionReason: null,
    rejectionDetail: null,
    ...overrides,
  };
}

// The CTA links back to this exact player URL so a passed check resumes the exam.
const EXPECTED_RETURN_TO = encodeURIComponent('/listening/player/lp-001?mode=exam');
const EXPECTED_HREF = `/listening/audio-check?returnTo=${EXPECTED_RETURN_TO}`;

describe('Listening player — WS2 sound-check gate CTA', () => {
  beforeEach(() => {
    // resetAllMocks (not clearAllMocks) so any per-test one-shot resolutions are
    // also flushed between tests; the defaults below are re-established right
    // after. clearAllMocks only clears call history, which can leave a prior
    // test's queued resolution in place and shift this test's mock sequence.
    vi.resetAllMocks();
    mockHeartbeat.mockResolvedValue({ attemptId: 'attempt-1', elapsedSeconds: 0, lastClientSyncAt: '' });
    mockSaveAnswer.mockResolvedValue(undefined);
    mockRecordIntegrity.mockResolvedValue(undefined);
    mockRecordTechReadiness.mockResolvedValue({
      audioOk: true,
      durationMs: 1500,
      checkedAt: '2026-05-29T00:00:00Z',
      ttlMs: 900_000,
    });
    // Strict exam, fresh start (no attemptId on the URL).
    mockUseSearchParams.mockReturnValue({
      get: (key: string) => (key === 'mode' ? 'exam' : null),
    });
    mockGetListeningSession.mockResolvedValue(makeExamSession());

    const proto = HTMLMediaElement.prototype as unknown as { play: () => Promise<void>; pause: () => void };
    proto.play = () => Promise.resolve();
    proto.pause = () => undefined;
  });

  it('renders the "Run the sound check" CTA when the first advance is rejected with audio-check-required', async () => {
    // Attempt is created, then the first strict transition is gated server-side.
    mockStartListeningAttempt.mockResolvedValue({ attemptId: 'attempt-1', mode: 'exam' });
    mockV2Advance.mockResolvedValue(makeAdvanceResult({
      outcome: 'rejected',
      state: null,
      rejectionReason: 'audio-check-required',
      rejectionDetail: 'Pass the Listening sound check before starting this exam.',
    }));

    render(<ListeningPlayer />);

    await runMockedReadiness();
    fireEvent.click(screen.getByRole('button', { name: /start audio/i }));

    // The dedicated sound-check panel renders with the recovery CTA. Re-query
    // inside waitFor (rather than holding the first matched node) because the
    // intro card re-renders once as `audioCheckRequired`/`startError` settle,
    // which can replace the panel's DOM node — a captured reference would point
    // at the detached predecessor and fail `toBeInTheDocument`.
    await waitFor(() => {
      expect(screen.getByTestId('listening-audio-check-required')).toBeInTheDocument();
    });

    // The recovery CTA links back to this exact player URL with the correct
    // returnTo. Re-query for the same reason as above.
    await waitFor(() => {
      expect(screen.getByRole('link', { name: /run the sound check/i })).toHaveAttribute('href', EXPECTED_HREF);
    });

    // It is a recovery affordance, not the preview banner (we never started).
    expect(screen.queryByTestId('listening-preview-banner')).not.toBeInTheDocument();
  });

  it('renders the CTA when startListeningAttempt throws listening_audio_check_required', async () => {
    // The start itself is gated (StartRelationalAttemptAsync validation 400).
    const err = Object.assign(new Error('Pass the Listening sound check before starting this exam.'), {
      status: 400,
      detail: { code: 'listening_audio_check_required', message: 'Pass the Listening sound check before starting this exam.' },
    });
    mockStartListeningAttempt.mockRejectedValue(err);

    render(<ListeningPlayer />);

    await runMockedReadiness();
    fireEvent.click(screen.getByRole('button', { name: /start audio/i }));

    // Re-query inside waitFor (see the sibling test) so a re-render that
    // replaces the panel's DOM node cannot leave us asserting on a detached CTA.
    await waitFor(() => {
      expect(screen.getByRole('link', { name: /run the sound check/i })).toHaveAttribute('href', EXPECTED_HREF);
    });
    // The first strict advance is never attempted when the start itself is gated.
    expect(mockV2Advance).not.toHaveBeenCalled();
  });
});
