import { createElement } from 'react';
import { act, fireEvent, render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { LISTENING_PREVIEW_SECONDS } from '@/lib/listening-sections';

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
  mockSubmitAudioCheck,
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
  mockSubmitAudioCheck: vi.fn(),
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

vi.mock('@/lib/listening-pathway-api', () => ({
  submitAudioCheck: mockSubmitAudioCheck,
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

// Canonical motion/react Proxy mock — strips motion-specific props and
// renders the underlying DOM element so RTL queries see real children.
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
 * "Start audio" button stays disabled.
 *
 * Re-querying and clicking inside `waitFor`, asserting the click actually
 * enabled the start button, makes the interaction robust to that transient
 * remount without weakening any assertion: it simply retries the click against
 * the live button until the readiness state commits.
 */
async function runMockedReadiness() {
  await waitFor(() => {
    const button = screen.getByRole('button', { name: /run mocked readiness/i });
    fireEvent.click(button);
    expect(screen.getByRole('button', { name: /start audio/i })).not.toBeDisabled();
  });
}

type Mode = 'practice' | 'exam' | 'home';

interface SessionOverrides {
  mode?: Mode;
  canScrub?: boolean;
  onePlayOnly?: boolean;
  expiresAt?: string | null;
}

function makeSession(overrides: SessionOverrides = {}) {
  const mode: Mode = overrides.mode ?? 'practice';
  return {
    paper: {
      id: 'lp-001',
      sourceKind: 'content_paper',
      title: 'OET Listening Practice Paper 1',
      slug: 'oet-listening-practice-paper-1',
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
    attempt: {
      attemptId: 'attempt-1', paperId: 'lp-001', state: 'in_progress', mode,
      startedAt: '2026-04-01T00:00:00Z', submittedAt: null, completedAt: null,
      elapsedSeconds: 0, lastClientSyncAt: null, answers: {},
      expiresAt: overrides.expiresAt ?? null,
    },
    questions: [
      { id: 'q-1', number: 1, partCode: 'A1', text: 'Q1?', type: 'short_answer', options: [], points: 1 },
    ],
    modePolicy: {
      mode,
      // Audio is non-pausable in every mode now (owner directive 2026-06-27):
      // the backend DTO hard-codes these regardless of mode. Overrides remain
      // honored so a test can still assert a hypothetical relaxed policy.
      canPause: false,
      canScrub: overrides.canScrub ?? false,
      onePlayOnly: overrides.onePlayOnly ?? true,
      autosave: true,
      transcriptPolicy: 'per_item_post_attempt',
    },
    scoring: { maxRawScore: 42, passRawScore: 30, passScaledScore: 350 },
    readiness: { objectiveReady: true, questionCount: 42, audioAvailable: true, missingReason: null },
  };
}

function makeV2State(state = 'a1_preview') {
  return {
    attemptId: 'attempt-1',
    mode: 'exam',
    state,
    locks: [],
    windowDurationMs: 30_000,
    windowRemainingMs: 30_000,
    confirmRequired: true,
    freeNavigation: false,
    oneWayLocks: true,
    unansweredWarningRequired: true,
  };
}

function makeTwoSectionSession(overrides: SessionOverrides = {}) {
  const session = makeSession(overrides);
  return {
    ...session,
    paper: {
      ...session.paper,
      extracts: [
        {
          partCode: 'A1', displayOrder: 1, kind: 'consultation', title: 'Extract 1',
          accentCode: 'en-GB', speakers: [],
          audioStartMs: 12_000, audioEndMs: 240_000,
        },
        {
          partCode: 'A2', displayOrder: 2, kind: 'consultation', title: 'Extract 2',
          accentCode: 'en-GB', speakers: [],
          audioStartMs: 250_000, audioEndMs: 480_000,
        },
      ],
    },
    questions: [
      { id: 'q-a1', number: 1, partCode: 'A1', text: 'A1 first blank?', type: 'short_answer', options: [], points: 1 },
      { id: 'q-a2', number: 13, partCode: 'A2', text: 'A2 resumed blank?', type: 'short_answer', options: [], points: 1 },
    ],
  };
}

function makeFinalReviewSession(overrides: SessionOverrides = {}) {
  const session = makeSession(overrides);
  return {
    ...session,
    paper: {
      ...session.paper,
      extracts: [
        {
          partCode: 'C2', displayOrder: 1, kind: 'presentation', title: 'Final extract',
          accentCode: 'en-GB', speakers: [],
          audioStartMs: 12_000, audioEndMs: 240_000,
        },
      ],
    },
    questions: [
      { id: 'q-c2-38', number: 38, partCode: 'C2', text: 'C2 first blank?', type: 'short_answer', options: [], points: 1 },
      { id: 'q-c2-39', number: 39, partCode: 'C2', text: 'C2 answered blank?', type: 'short_answer', options: [], points: 1 },
      { id: 'q-c2-40', number: 40, partCode: 'C2', text: 'C2 final blank?', type: 'short_answer', options: [], points: 1 },
    ],
  };
}

/**
 * Build the same normalized question shape used by the learner API for a
 * focused standalone part. The player tests intentionally keep the fixture
 * local: source fidelity is covered by the backend corpus/migration tests,
 * while these cases exercise the real route's autoplay and question-workspace
 * behavior for every standalone part.
 */
function makeStandalonePartSession(part: 'a' | 'b' | 'c') {
  const session = makeSession({ mode: 'practice' });
  if (part === 'a') {
    return {
      ...session,
      paper: {
        ...session.paper,
        extracts: [
          {
            partCode: 'A1', displayOrder: 1, kind: 'consultation', title: 'Part A extract',
            accentCode: 'en-GB', speakers: [], audioStartMs: 0, audioEndMs: 120_000,
          },
        ],
      },
      questions: [
        { id: 'q-a-1', number: 1, partCode: 'A1', text: 'Standalone Part A source question 1', type: 'short_answer', options: [], points: 1 },
      ],
    };
  }

  if (part === 'b') {
    return {
      ...session,
      paper: {
        ...session.paper,
        extracts: [
          {
            partCode: 'B', displayOrder: 1, kind: 'workplace', title: 'Part B extract',
            accentCode: 'en-GB', speakers: [], audioStartMs: 0, audioEndMs: 240_000,
          },
        ],
      },
      questions: Array.from({ length: 6 }, (_, index) => {
        const number = 25 + index;
        return {
          id: `q-b-${number}`,
          number,
          partCode: 'B',
          text: `Standalone Part B source question ${number}`,
          type: 'multiple_choice',
          options: [`Source option A for Q${number}`, `Source option B for Q${number}`, `Source option C for Q${number}`],
          points: 1,
        };
      }),
    };
  }

  return {
    ...session,
    paper: {
      ...session.paper,
      // A Part C replacement may be uploaded once at the parent-part level;
      // both C1 and C2 must resolve that fallback while still transitioning
      // forward exactly once.
      audioUrlByPart: { C: 'https://cdn.example/audio-part-c.mp3' },
      extracts: [
        {
          partCode: 'C1', displayOrder: 1, kind: 'presentation', title: 'Part C extract 1',
          accentCode: 'en-GB', speakers: [], audioStartMs: 0, audioEndMs: 240_000,
        },
        {
          partCode: 'C2', displayOrder: 2, kind: 'presentation', title: 'Part C extract 2',
          accentCode: 'en-GB', speakers: [], audioStartMs: 240_000, audioEndMs: 480_000,
        },
      ],
    },
    questions: Array.from({ length: 12 }, (_, index) => {
      const number = 31 + index;
      const partCode = number <= 36 ? 'C1' : 'C2';
      return {
        id: `q-c-${number}`,
        number,
        partCode,
        text: `Standalone Part C source question ${number}`,
        type: 'multiple_choice',
        options: [`Source option A for Q${number}`, `Source option B for Q${number}`, `Source option C for Q${number}`],
        points: 1,
      };
    }),
  };
}

/** A full strict-player Part B fixture with one navigable card per Q25–Q30. */
function makeFullPartBSession() {
  const session = makeStandalonePartSession('b');
  return {
    ...session,
    attempt: { ...session.attempt, mode: 'exam' as const },
    modePolicy: { ...session.modePolicy, mode: 'exam' as const, canPause: false, canScrub: false, onePlayOnly: true },
    paper: {
      ...session.paper,
      extracts: Array.from({ length: 6 }, (_, index) => ({
        partCode: 'B', displayOrder: index + 1, kind: 'workplace', title: `Part B extract ${index + 1}`,
        accentCode: 'en-GB', speakers: [], audioStartMs: index * 40_000, audioEndMs: (index + 1) * 40_000,
      })),
    },
  };
}

/** A full strict-player Part C fixture with both extracts and Q31–Q42. */
function makeFullPartCSession() {
  const session = makeStandalonePartSession('c');
  return {
    ...session,
    attempt: { ...session.attempt, mode: 'exam' as const },
    modePolicy: { ...session.modePolicy, mode: 'exam' as const, canPause: false, canScrub: false, onePlayOnly: true },
  };
}

function makeAdvanceResult(overrides: Record<string, unknown> = {}) {
  return {
    outcome: 'applied',
    state: makeV2State(),
    confirmToken: null,
    confirmTokenTtlMs: null,
    rejectionReason: null,
    rejectionDetail: null,
    ...overrides,
  };
}

describe('Listening player — CBLA fidelity (preview / attempt timer / one-play / extract progress)', () => {
  let seekCalls: number[];
  let playCalls: number;

  beforeEach(() => {
    // resetAllMocks (not clearAllMocks) so per-test `mockResolvedValueOnce`
    // queues are also flushed. clearAllMocks only wipes call history; it leaves
    // any *unconsumed* one-shot resolution from a prior test queued, which then
    // shifts this test's `mockV2Advance` sequence (e.g. the intended
    // confirm-required result gets served the leftover applied result instead,
    // so the confirm follow-up advance never fires). The defaults below are
    // re-established immediately after the reset.
    vi.resetAllMocks();
    // Default-resolve all backend mocks so background timers (heartbeat,
    // autosave) never blow up when fake timers tick past 15s.
    mockHeartbeat.mockResolvedValue({ attemptId: 'attempt-1', elapsedSeconds: 0, lastClientSyncAt: '' });
    mockSaveAnswer.mockResolvedValue(undefined);
    mockRecordIntegrity.mockResolvedValue(undefined);
    mockV2GetState.mockResolvedValue(makeV2State());
    mockV2Advance.mockResolvedValue(makeAdvanceResult());
    mockRecordTechReadiness.mockResolvedValue({
      audioOk: true,
      durationMs: 1500,
      checkedAt: '2026-04-01T00:00:00Z',
      ttlMs: 900_000,
    });
    mockSubmitAudioCheck.mockResolvedValue({
      success: true,
      currentStage: 'diagnostic',
      audioCheckPassedAt: '2026-04-01T00:00:00Z',
    });
    mockStartListeningAttempt.mockResolvedValue({
      attemptId: 'attempt-1', paperId: 'lp-001', state: 'in_progress', mode: 'exam',
      startedAt: '2026-04-01T00:00:00Z', submittedAt: null, completedAt: null,
      elapsedSeconds: 0, lastClientSyncAt: null, answers: {},
    });
    mockUseSearchParams.mockReturnValue({
      get: (key: string) => {
        if (key === 'attemptId') return 'attempt-1';
        if (key === 'mode') return 'practice';
        return null;
      },
    });

    seekCalls = [];
    playCalls = 0;
    Object.defineProperty(HTMLMediaElement.prototype, 'currentTime', {
      configurable: true,
      get() {
        return (this as unknown as { __ct?: number }).__ct ?? 0;
      },
      set(value: number) {
        (this as unknown as { __ct?: number }).__ct = value;
        seekCalls.push(value);
      },
    });
    const proto = HTMLMediaElement.prototype as unknown as { play: () => Promise<void>; pause: () => void };
    proto.play = () => {
      playCalls += 1;
      return Promise.resolve();
    };
    proto.pause = () => undefined;
    Object.defineProperty(HTMLElement.prototype, 'scrollIntoView', {
      configurable: true,
      value: vi.fn(),
    });
  });

  afterEach(() => {
    vi.useRealTimers();
  });

  it.each([
    { part: 'a' as const, focus: 'part-a', firstNumber: 1, lastNumber: 1 },
    { part: 'b' as const, focus: 'part-b', firstNumber: 25, lastNumber: 30 },
    { part: 'c' as const, focus: 'part-c', firstNumber: 31, lastNumber: 42 },
  ])(
    'autoplays standalone Part $part and keeps playback stable while navigating its questions',
    async ({ part, focus, firstNumber, lastNumber }) => {
      mockUseSearchParams.mockReturnValue({
        get: (key: string) => {
          if (key === 'attemptId') return 'attempt-1';
          if (key === 'mode') return 'practice';
          if (key === 'focus') return focus;
          return null;
        },
      });
      mockGetListeningSession.mockResolvedValue(makeStandalonePartSession(part));

      const { container } = render(<ListeningPlayer />);

      await waitFor(() => {
        expect(container.querySelector('audio')).not.toBeNull();
        expect(screen.getByText(`Standalone Part ${part.toUpperCase()} source question ${firstNumber}`)).toBeInTheDocument();
      });
      await waitFor(() => expect(playCalls).toBeGreaterThanOrEqual(1));

      if (lastNumber !== firstNumber) {
        expect(screen.getByRole('button', { name: `Go to question ${lastNumber}` })).toBeInTheDocument();
        const playbackCallsAfterInitialStart = playCalls;
        fireEvent.click(screen.getByRole('button', { name: `Go to question ${lastNumber}` }));

        await waitFor(() => {
          expect(screen.getByText(`Standalone Part ${part.toUpperCase()} source question ${lastNumber}`)).toBeInTheDocument();
        });
        expect(playCalls).toBe(playbackCallsAfterInitialStart);
      }
    },
  );

  it('moves standalone Part C from C1 to C2 when the first audio completes', async () => {
    mockUseSearchParams.mockReturnValue({
      get: (key: string) => {
        if (key === 'attemptId') return 'attempt-1';
        if (key === 'mode') return 'practice';
        if (key === 'focus') return 'part-c';
        return null;
      },
    });
    mockGetListeningSession.mockResolvedValue(makeStandalonePartSession('c'));

    const { container } = render(<ListeningPlayer />);
    await waitFor(() => {
      expect(screen.getByText('Standalone Part C source question 31')).toBeInTheDocument();
      expect(container.querySelector('audio')).not.toBeNull();
    });

    const audio = container.querySelector('audio') as HTMLAudioElement;
    await waitFor(() => {
      fireEvent.ended(audio);
      expect(screen.getByText('Standalone Part C source question 37')).toBeInTheDocument();
    });
    expect(screen.queryByText('Standalone Part C source question 31')).not.toBeInTheDocument();
  });

  it('loads all six Full Exam Part B questions in the legacy player and preserves answers across jumps', async () => {
    mockUseSearchParams.mockReturnValue({
      get: (key: string) => {
        if (key === 'attemptId') return 'attempt-1';
        if (key === 'mode') return 'exam';
        return null;
      },
    });
    mockGetListeningSession.mockResolvedValue(makeFullPartBSession());
    mockV2GetState.mockResolvedValue(makeV2State('b_audio'));

    const { container } = render(<ListeningPlayer />);

    await waitFor(() => {
      expect(screen.getByText('Standalone Part B source question 25')).toBeInTheDocument();
      expect(container.querySelector('audio')).not.toBeNull();
    });
    await waitFor(() => expect(playCalls).toBeGreaterThanOrEqual(1));
    expect(screen.getAllByRole('button', { name: /^Go to question (25|26|27|28|29|30)$/ })).toHaveLength(6);

    const q25Options = screen.getAllByRole('radio');
    expect(q25Options).toHaveLength(3);
    expect(q25Options[1]).not.toBeDisabled();
    await userEvent.setup().click(q25Options[1]);
    await waitFor(() => expect(screen.getAllByRole('radio')[1]).toHaveAttribute('aria-checked', 'true'));

    const playbackCallsAfterInitialStart = playCalls;
    fireEvent.click(screen.getByRole('button', { name: 'Go to question 30' }));
    await waitFor(() => expect(screen.getByText('Standalone Part B source question 30')).toBeInTheDocument());
    expect(screen.getAllByRole('radio')).toHaveLength(3);

    fireEvent.click(screen.getByRole('button', { name: 'Go to question 25' }));
    await waitFor(() => expect(screen.getByText('Standalone Part B source question 25')).toBeInTheDocument());
    expect(screen.getAllByRole('radio')[1]).toHaveAttribute('aria-checked', 'true');
    expect(playCalls).toBe(playbackCallsAfterInitialStart);
  });

  it('loads the complete Full Exam Part C workspace across C1/C2 with next, jump, and answer persistence', async () => {
    mockUseSearchParams.mockReturnValue({
      get: (key: string) => {
        if (key === 'attemptId') return 'attempt-1';
        if (key === 'mode') return 'exam';
        return null;
      },
    });
    mockGetListeningSession.mockResolvedValue(makeFullPartCSession());
    mockV2GetState.mockResolvedValue(makeV2State('c1_audio'));

    const { container } = render(<ListeningPlayer />);

    await waitFor(() => {
      expect(screen.getByText('Standalone Part C source question 31')).toBeInTheDocument();
      expect(container.querySelector('audio')).not.toBeNull();
    });
    await waitFor(() => expect(playCalls).toBeGreaterThanOrEqual(1));
    expect(screen.getAllByRole('button', { name: /^Go to question (3[1-9]|4[0-2])$/ })).toHaveLength(12);

    // Sequential Next must change the mounted card, not only its number.
    fireEvent.click(screen.getByRole('button', { name: 'Next question' }));
    await waitFor(() => expect(screen.getByText('Standalone Part C source question 32')).toBeInTheDocument());

    const playbackCallsAfterInitialStart = playCalls;
    fireEvent.click(screen.getByRole('button', { name: 'Go to question 40' }));
    await waitFor(() => {
      expect(screen.getByText('Standalone Part C source question 40')).toBeInTheDocument();
      expect(screen.getByText('Part C — Extract 2')).toBeInTheDocument();
    });
    const q40Options = screen.getAllByRole('radio');
    expect(q40Options).toHaveLength(3);
    expect(q40Options[1]).not.toBeDisabled();
    await userEvent.setup().click(q40Options[1]);
    await waitFor(() => expect(screen.getAllByRole('radio')[1]).toHaveAttribute('aria-checked', 'true'));

    fireEvent.click(screen.getByRole('button', { name: 'Go to question 34' }));
    await waitFor(() => expect(screen.getByText('Standalone Part C source question 34')).toBeInTheDocument());
    fireEvent.click(screen.getByRole('button', { name: 'Go to question 42' }));
    await waitFor(() => expect(screen.getByText('Standalone Part C source question 42')).toBeInTheDocument());
    fireEvent.click(screen.getByRole('button', { name: 'Go to question 40' }));
    await waitFor(() => expect(screen.getByText('Standalone Part C source question 40')).toBeInTheDocument());
    expect(screen.getAllByRole('radio')[1]).toHaveAttribute('aria-checked', 'true');
    expect(playCalls).toBe(playbackCallsAfterInitialStart);
  });

  it('drops straight into the audio phase with no pre-audio reading window', async () => {
    mockGetListeningSession.mockResolvedValue(makeSession());

    const { container } = render(<ListeningPlayer />);

    // Audio mounts immediately — the practice player has no reading-window gate.
    await waitFor(() => {
      expect(container.querySelector('audio')).not.toBeNull();
    });
    // There is no pre-audio reading-window countdown in the Listening module.
    expect(screen.queryByTestId('listening-preview-banner')).not.toBeInTheDocument();
    expect(mockRecordTechReadiness).not.toHaveBeenCalled();
    expect(mockV2Advance).not.toHaveBeenCalled();
    // Standalone practice starts the authorized section audio automatically.
    await waitFor(() => expect(playCalls).toBeGreaterThanOrEqual(1));
  });

  it('requires tech readiness and applies the first V2 transition before strict exam start', async () => {
    mockUseSearchParams.mockReturnValue({
      get: (key: string) => {
        if (key === 'mode') return 'exam';
        return null;
      },
    });
    mockGetListeningSession.mockResolvedValue(
      makeSession({ mode: 'exam', canScrub: false, onePlayOnly: true }),
    );
    mockV2Advance
      .mockResolvedValueOnce(makeAdvanceResult({
        outcome: 'confirm-required',
        state: null,
        confirmToken: 'confirm-1',
        confirmTokenTtlMs: 30_000,
      }))
      .mockResolvedValueOnce(makeAdvanceResult());

    const { container } = render(<ListeningPlayer />);

    const startButton = await screen.findByRole('button', { name: /start audio/i });
    expect(startButton).toBeDisabled();
    expect(container.querySelector('audio')).toBeNull();

    await runMockedReadiness();
    fireEvent.click(screen.getByRole('button', { name: /start audio/i }));

    await waitFor(() => {
      expect(mockRecordTechReadiness).toHaveBeenCalledWith('attempt-1', expect.objectContaining({ audioOk: true, durationMs: 1500 }));
    });
    expect(mockV2Advance).toHaveBeenNthCalledWith(1, 'attempt-1', 'a1_preview', null);
    expect(mockV2Advance).toHaveBeenNthCalledWith(2, 'attempt-1', 'a1_preview', 'confirm-1');
    expect(screen.queryByTestId('listening-preview-banner')).not.toBeInTheDocument();
    expect(container.querySelector('audio')).not.toBeNull();
  });

  it('derives strict exam behavior from mock launch params when mode is absent', async () => {
    mockUseSearchParams.mockReturnValue({
      get: (key: string) => {
        if (key === 'mockAttemptId') return 'mock-1';
        if (key === 'mockSectionId') return 'section-1';
        if (key === 'mockMode') return 'exam';
        if (key === 'strictness') return 'exam';
        if (key === 'deliveryMode') return 'computer';
        if (key === 'strictTimer') return 'true';
        return null;
      },
    });
    mockGetListeningSession.mockResolvedValue(
      makeSession({ mode: 'exam', canScrub: false, onePlayOnly: true }),
    );

    render(<ListeningPlayer />);

    expect(await screen.findByRole('button', { name: /start audio/i })).toBeDisabled();
    expect(screen.getByRole('button', { name: /run mocked readiness/i })).toBeInTheDocument();
    expect(mockGetListeningSession).toHaveBeenCalledWith('lp-001', { mode: 'exam', attemptId: null });
  });

  it('passes pathwayStage from launch URL to scoped session lookup', async () => {
    mockUseSearchParams.mockReturnValue({
      get: (key: string) => {
        if (key === 'mode') return 'practice';
        if (key === 'pathwayStage') return 'foundation_partA';
        return null;
      },
    });
    const session = makeSession();
    mockGetListeningSession.mockResolvedValue({ ...session, attempt: null });

    render(<ListeningPlayer />);

    await waitFor(() => {
      expect(mockGetListeningSession).toHaveBeenCalledWith('lp-001', {
        mode: 'practice',
        attemptId: null,
        pathwayStage: 'foundation_partA',
      });
    });
    // Standalone practice is authorized on load and should begin playback
    // without requiring a second Start Audio click.
    await waitFor(() => expect(playCalls).toBeGreaterThanOrEqual(1));
  });

  it('renders R08 tools for Part B/C questions', async () => {
    const baseSession = makeSession();
    mockGetListeningSession.mockResolvedValue({
      ...baseSession,
      paper: {
        ...baseSession.paper,
        extracts: [
          {
            partCode: 'B', displayOrder: 1, kind: 'workplace', title: 'Extract B',
            accentCode: 'en-GB', speakers: [],
            audioStartMs: 12_000, audioEndMs: 90_000,
          },
        ],
      },
      questions: [
        {
          id: 'q-b-1', number: 7, partCode: 'B', text: 'What should the nurse do next?', type: 'single_choice',
          options: ['Update the chart', 'Call the family', 'Delay the medicine'], points: 1,
        },
      ],
    });

    render(<ListeningPlayer />);

    await waitFor(() => expect(screen.getByText('What should the nurse do next?')).toBeInTheDocument());
    expect(screen.getByRole('button', { name: /flag question 7/i })).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /stem/i })).not.toBeInTheDocument();
    expect(screen.getByRole('button', { name: /strike out option a/i })).toBeInTheDocument();
    const questionSurface = screen.getByTestId('listening-question-surface');
    expect(questionSurface).toHaveStyle({ fontSize: '100%' });
    fireEvent.click(screen.getByRole('button', { name: /increase question zoom/i }));
    await waitFor(() => expect(screen.getByTestId('listening-question-surface')).toHaveStyle({ fontSize: '110%' }));

    mockGetListeningSession.mockResolvedValue(makeSession());
  });

  it('fails closed on strict resume when V2 state cannot be verified', async () => {
    mockUseSearchParams.mockReturnValue({
      get: (key: string) => {
        if (key === 'attemptId') return 'attempt-1';
        if (key === 'mode') return 'exam';
        return null;
      },
    });
    mockGetListeningSession.mockResolvedValue(
      makeSession({ mode: 'exam', canScrub: false, onePlayOnly: true }),
    );
    mockV2GetState.mockRejectedValueOnce(new Error('state unavailable'));

    const { container } = render(<ListeningPlayer />);

    await waitFor(() => {
      expect(screen.getByText(/could not verify this strict listening attempt/i)).toBeInTheDocument();
    });
    expect(screen.queryByTestId('listening-preview-banner')).not.toBeInTheDocument();
    expect(container.querySelector('audio')).toBeNull();
  });

  it('uses the server session policy to fail closed for strict resumes even when mode is absent', async () => {
    mockUseSearchParams.mockReturnValue({
      get: (key: string) => {
        if (key === 'attemptId') return 'attempt-1';
        return null;
      },
    });
    mockGetListeningSession.mockResolvedValue(
      makeSession({ mode: 'exam', canScrub: false, onePlayOnly: true }),
    );
    mockV2GetState.mockRejectedValueOnce(new Error('state unavailable'));

    const { container } = render(<ListeningPlayer />);

    await waitFor(() => {
      expect(screen.getByText(/could not verify this strict listening attempt/i)).toBeInTheDocument();
    });
    expect(mockGetListeningSession).toHaveBeenCalledWith('lp-001', { mode: 'practice', attemptId: 'attempt-1' });
    expect(screen.queryByTestId('listening-preview-banner')).not.toBeInTheDocument();
    expect(container.querySelector('audio')).toBeNull();
  });

  it('hydrates a strict resume from the V2 FSM section and phase', async () => {
    mockUseSearchParams.mockReturnValue({
      get: (key: string) => {
        if (key === 'attemptId') return 'attempt-1';
        if (key === 'mode') return 'exam';
        return null;
      },
    });
    mockGetListeningSession.mockResolvedValue(
      makeTwoSectionSession({ mode: 'exam', canScrub: false, onePlayOnly: true }),
    );
    mockV2GetState.mockResolvedValueOnce({
      ...makeV2State('a2_audio'),
      windowDurationMs: 240_000,
      windowRemainingMs: 180_000,
    });

    render(<ListeningPlayer />);

    await waitFor(() => {
      expect(screen.getByText('A2 resumed blank?')).toBeInTheDocument();
    });
    expect(screen.queryByText('A1 first blank?')).not.toBeInTheDocument();
    expect(screen.queryByTestId('listening-preview-banner')).not.toBeInTheDocument();
  });

  it('keeps strict exam on the start screen when the V2 readiness transition is rejected', async () => {
    mockUseSearchParams.mockReturnValue({
      get: (key: string) => {
        if (key === 'mode') return 'exam';
        return null;
      },
    });
    mockGetListeningSession.mockResolvedValue(
      makeSession({ mode: 'exam', canScrub: false, onePlayOnly: true }),
    );
    mockV2Advance.mockResolvedValueOnce(makeAdvanceResult({
      outcome: 'rejected',
      state: null,
      rejectionReason: 'tech-readiness-required',
      rejectionDetail: 'Audio readiness check is required before starting this Listening attempt.',
    }));

    render(<ListeningPlayer />);

    await runMockedReadiness();
    await waitFor(() => expect(screen.getByRole('button', { name: /start audio/i })).not.toBeDisabled());
    fireEvent.click(screen.getByRole('button', { name: /start audio/i }));

    await waitFor(() => {
      expect(screen.getByText(/audio readiness check is required/i)).toBeInTheDocument();
    });
    expect(screen.queryByTestId('listening-preview-banner')).not.toBeInTheDocument();
  });

  it('advances strict preview through the V2 FSM before playing audio', async () => {
    mockUseSearchParams.mockReturnValue({
      get: (key: string) => {
        if (key === 'attemptId') return 'attempt-1';
        if (key === 'mode') return 'exam';
        return null;
      },
    });
    mockGetListeningSession.mockResolvedValue(
      makeSession({ mode: 'exam', canScrub: false, onePlayOnly: true }),
    );
    mockV2GetState.mockResolvedValueOnce(makeV2State('a1_preview'));
    mockV2Advance
      .mockResolvedValueOnce(makeAdvanceResult({
        outcome: 'confirm-required',
        state: null,
        confirmToken: 'confirm-audio',
        confirmTokenTtlMs: 30_000,
      }))
      .mockResolvedValueOnce(makeAdvanceResult({ state: makeV2State('a1_audio') }));

    vi.useFakeTimers();
    try {
      render(<ListeningPlayer />);

      await act(async () => {
        await vi.advanceTimersByTimeAsync(0);
      });
      expect(screen.getByTestId('listening-preview-banner')).toBeInTheDocument();

      for (let i = 0; i <= LISTENING_PREVIEW_SECONDS.A1 + 1; i += 1) {
        await act(async () => {
          await vi.advanceTimersByTimeAsync(1_000);
        });
      }

      await act(async () => {
        await Promise.resolve();
        await Promise.resolve();
      });
      expect(mockV2Advance).toHaveBeenNthCalledWith(1, 'attempt-1', 'a1_audio', null);
      expect(mockV2Advance).toHaveBeenNthCalledWith(2, 'attempt-1', 'a1_audio', 'confirm-audio');
      expect(playCalls).toBeGreaterThanOrEqual(1);
    } finally {
      vi.useRealTimers();
    }
  });

  it('fails closed when strict preview advance is rejected', async () => {
    mockUseSearchParams.mockReturnValue({
      get: (key: string) => {
        if (key === 'attemptId') return 'attempt-1';
        if (key === 'mode') return 'exam';
        return null;
      },
    });
    mockGetListeningSession.mockResolvedValue(
      makeSession({ mode: 'exam', canScrub: false, onePlayOnly: true }),
    );
    mockV2GetState.mockResolvedValueOnce(makeV2State('a1_preview'));
    mockV2Advance.mockResolvedValueOnce(makeAdvanceResult({
      outcome: 'rejected',
      state: null,
      rejectionReason: 'invalid-transition',
      rejectionDetail: 'Server refused audio transition.',
    }));

    vi.useFakeTimers();
    try {
      render(<ListeningPlayer />);

      await act(async () => {
        await vi.advanceTimersByTimeAsync(0);
      });

      for (let i = 0; i <= LISTENING_PREVIEW_SECONDS.A1 + 1; i += 1) {
        await act(async () => {
          await vi.advanceTimersByTimeAsync(1_000);
        });
      }
      await act(async () => {
        await Promise.resolve();
        await Promise.resolve();
      });

      expect(mockV2Advance).toHaveBeenCalledTimes(1);
      expect(mockV2Advance).toHaveBeenCalledWith('attempt-1', 'a1_audio', null);
      expect(screen.getByText(/server refused audio transition/i)).toBeInTheDocument();
      expect(playCalls).toBe(0);

      await act(async () => {
        await vi.advanceTimersByTimeAsync(3_000);
        await Promise.resolve();
      });
      expect(mockV2Advance).toHaveBeenCalledTimes(1);
    } finally {
      vi.useRealTimers();
    }
  });

  it('auto-advances a strict exam through the V2 review hop to the next section when audio ends', async () => {
    // Audio is non-pausable: reaching the section's cue end auto-advances with
    // NO manual Next/review button. In a strict exam the player must still hop
    // through the server review state (confirm-token round trip) before the
    // next section's preview, so the linear FSM stays in sync.
    mockUseSearchParams.mockReturnValue({
      get: (key: string) => {
        if (key === 'attemptId') return 'attempt-1';
        if (key === 'mode') return 'exam';
        return null;
      },
    });
    mockGetListeningSession.mockResolvedValue(
      makeTwoSectionSession({ mode: 'exam', canScrub: false, onePlayOnly: true }),
    );
    mockV2GetState.mockResolvedValue(makeV2State('a1_audio'));
    mockV2Advance
      // Hop 1: a1_audio → a1_review (confirm-token round trip).
      .mockResolvedValueOnce(makeAdvanceResult({
        outcome: 'confirm-required',
        state: null,
        confirmToken: 'confirm-review',
        confirmTokenTtlMs: 30_000,
      }))
      .mockResolvedValueOnce(makeAdvanceResult({
        state: { ...makeV2State('a1_review'), windowDurationMs: 75_000, windowRemainingMs: 75_000 },
      }))
      // Hop 2: a1_review → a2_preview (confirm-token round trip).
      .mockResolvedValueOnce(makeAdvanceResult({
        outcome: 'confirm-required',
        state: null,
        confirmToken: 'confirm-next',
        confirmTokenTtlMs: 30_000,
      }))
      .mockResolvedValueOnce(makeAdvanceResult({
        state: { ...makeV2State('a2_preview'), windowDurationMs: 30_000, windowRemainingMs: 30_000 },
      }));

    const { container } = render(<ListeningPlayer />);

    const audio = await waitFor(() => {
      const element = container.querySelector('audio');
      if (!element) throw new Error('audio element not yet mounted');
      return element as HTMLAudioElement;
    });
    // Cross the A1 cue end — no buttons; the player auto-advances.
    (audio as unknown as { __ct?: number }).__ct = 240;
    fireEvent.timeUpdate(audio);

    await waitFor(() => {
      expect(mockV2Advance).toHaveBeenNthCalledWith(1, 'attempt-1', 'a1_review', null);
    });
    expect(mockV2Advance).toHaveBeenNthCalledWith(2, 'attempt-1', 'a1_review', 'confirm-review');
    expect(mockV2Advance).toHaveBeenNthCalledWith(3, 'attempt-1', 'a2_preview', null);
    expect(mockV2Advance).toHaveBeenNthCalledWith(4, 'attempt-1', 'a2_preview', 'confirm-next');

    await waitFor(() => {
      expect(screen.getByText('A2 resumed blank?')).toBeInTheDocument();
      expect(screen.getByTestId('listening-preview-banner')).toHaveTextContent('00:30');
    });
  });

  it('locks strict review through the V2 FSM before opening the next section', async () => {
    mockUseSearchParams.mockReturnValue({
      get: (key: string) => {
        if (key === 'attemptId') return 'attempt-1';
        if (key === 'mode') return 'exam';
        return null;
      },
    });
    mockGetListeningSession.mockResolvedValue(
      makeTwoSectionSession({ mode: 'exam', canScrub: false, onePlayOnly: true }),
    );
    mockV2GetState.mockResolvedValue({
      ...makeV2State('a1_review'),
      windowDurationMs: 75_000,
      windowRemainingMs: 75_000,
    });
    mockV2Advance
      .mockResolvedValueOnce(makeAdvanceResult({
        outcome: 'confirm-required',
        state: null,
        confirmToken: 'confirm-next',
        confirmTokenTtlMs: 30_000,
      }))
      .mockResolvedValueOnce(makeAdvanceResult({
        state: { ...makeV2State('a2_preview'), windowDurationMs: 30_000, windowRemainingMs: 30_000 },
      }));

    render(<ListeningPlayer />);

    expect(await screen.findByTestId('listening-review-banner')).toHaveTextContent('01:15');
    fireEvent.click(screen.getByRole('button', { name: /^next$/i }));
    fireEvent.click(await screen.findByRole('button', { name: /lock & continue/i }));

    await waitFor(() => {
      expect(mockV2Advance).toHaveBeenNthCalledWith(1, 'attempt-1', 'a2_preview', null);
    });
    expect(mockV2Advance).toHaveBeenNthCalledWith(2, 'attempt-1', 'a2_preview', 'confirm-next');
    await waitFor(() => {
      expect(screen.getByText('A2 resumed blank?')).toBeInTheDocument();
      expect(screen.getByTestId('listening-preview-banner')).toHaveTextContent('00:30');
    });
  });

  it('auto-submits the attempt when the whole-attempt timer expires in exam mode', async () => {
    vi.useFakeTimers();
    try {
      const expiresAt = new Date(Date.now() + 5_000).toISOString();
      mockGetListeningSession.mockResolvedValue(
        makeSession({ mode: 'exam', canScrub: false, onePlayOnly: true, expiresAt }),
      );
      mockSubmit.mockResolvedValue({ attemptId: 'attempt-1' });

      render(<ListeningPlayer />);

      await act(async () => {
        await vi.advanceTimersByTimeAsync(0);
      });

      expect(screen.getByTestId('listening-attempt-timer')).toBeInTheDocument();

      await act(async () => {
        vi.setSystemTime(Date.now() + 6_000);
        await vi.advanceTimersByTimeAsync(2_000);
      });

      // Allow the auto-submit promise chain to flush.
      await act(async () => {
        await vi.advanceTimersByTimeAsync(0);
      });

      expect(mockSubmit).toHaveBeenCalledWith('attempt-1', {});
    } finally {
      vi.useRealTimers();
    }
  });

  it('lists exact unanswered question numbers before final submit', async () => {
    mockUseSearchParams.mockReturnValue({
      get: (key: string) => {
        if (key === 'attemptId') return 'attempt-1';
        if (key === 'mode') return 'exam';
        return null;
      },
    });
    mockGetListeningSession.mockResolvedValue(
      makeFinalReviewSession({ mode: 'exam', canScrub: false, onePlayOnly: true }),
    );
    mockV2GetState.mockResolvedValue({
      ...makeV2State('c2_review'),
      windowDurationMs: 120_000,
      windowRemainingMs: 120_000,
    });

    render(<ListeningPlayer />);

    expect(await screen.findByTestId('listening-review-banner')).toHaveTextContent('02:00');
    fireEvent.change(screen.getByLabelText(/answer for question 39/i), {
      target: { value: 'answered item' },
    });
    fireEvent.click(screen.getByRole('button', { name: /finish & submit/i }));

    const warning = await screen.findByText(/2 unanswered questions will score zero if you submit now: Q38, Q40\./i);
    expect(warning).toBeInTheDocument();
    expect(warning).not.toHaveTextContent('Q39');
  });

  it('keeps Submit visible before the last section and never shows a learner question paper', async () => {
    mockGetListeningSession.mockResolvedValue(makeTwoSectionSession());

    render(<ListeningPlayer />);

    expect(await screen.findByTestId('listening-submit-exam')).toBeVisible();
    expect(screen.queryByTestId('listening-question-paper')).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /show question paper/i })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /hide question paper/i })).not.toBeInTheDocument();
    expect(screen.queryByText(/question paper – part/i)).not.toBeInTheDocument();
  });

  it('includes the final in-memory answer when the timer expires before debounce save completes', async () => {
    vi.useFakeTimers();
    try {
      const expiresAt = new Date(Date.now() + 5_000).toISOString();
      mockGetListeningSession.mockResolvedValue(
        makeSession({ mode: 'exam', canScrub: false, onePlayOnly: true, expiresAt }),
      );
      mockSubmit.mockResolvedValue({ attemptId: 'attempt-1' });

      render(<ListeningPlayer />);

      await act(async () => {
        await vi.advanceTimersByTimeAsync(0);
      });

      fireEvent.change(screen.getByLabelText(/answer for question 1/i), {
        target: { value: 'final word' },
      });

      await act(async () => {
        vi.setSystemTime(Date.now() + 6_000);
        await vi.advanceTimersByTimeAsync(2_000);
      });

      await act(async () => {
        await vi.advanceTimersByTimeAsync(0);
      });

      expect(mockSubmit).toHaveBeenCalledWith('attempt-1', { 'q-1': 'final word' });
    } finally {
      vi.useRealTimers();
    }
  });

  it('auto-advances to the next section when the active extract ends — no manual Next button', async () => {
    // Non-strict practice: audio is non-pausable and there is no manual Next
    // control. There is also no pre-audio reading window — the section drops
    // straight into audio. Crossing the section's cue end auto-advances to the
    // next section with zero clicks.
    mockGetListeningSession.mockResolvedValue(makeTwoSectionSession({ mode: 'practice' }));

    vi.useFakeTimers();
    try {
      const { container } = render(<ListeningPlayer />);

      await act(async () => {
        await vi.advanceTimersByTimeAsync(0);
      });

      // No manual advance control exists during section audio.
      expect(screen.queryByRole('button', { name: /^next$/i })).not.toBeInTheDocument();
      expect(screen.getByText('A1 first blank?')).toBeInTheDocument();

      const audio = container.querySelector('audio') as HTMLAudioElement;
      // Before the cue end, the player stays on A1.
      (audio as unknown as { __ct?: number }).__ct = 100;
      await act(async () => {
        fireEvent.timeUpdate(audio);
        await vi.advanceTimersByTimeAsync(0);
      });
      expect(screen.getByText('A1 first blank?')).toBeInTheDocument();

      // Cross the A1 cue end → auto-advance straight into the A2 audio phase.
      (audio as unknown as { __ct?: number }).__ct = 240;
      await act(async () => {
        fireEvent.timeUpdate(audio);
        await vi.advanceTimersByTimeAsync(0);
      });
      // Flush the section-change → audio re-entry effects.
      await act(async () => {
        await vi.advanceTimersByTimeAsync(0);
      });

      expect(screen.getByText('A2 resumed blank?')).toBeInTheDocument();
      expect(screen.queryByText('A1 first blank?')).not.toBeInTheDocument();
    } finally {
      vi.useRealTimers();
    }
  });

  it('emits §17.11 audio_started and audio_ended attempt events with cuePointMs', async () => {
    mockUseSearchParams.mockReturnValue({
      get: (key: string) => {
        if (key === 'attemptId') return 'attempt-1';
        if (key === 'mode') return 'exam';
        return null;
      },
    });
    mockGetListeningSession.mockResolvedValue(
      makeSession({ mode: 'exam', canScrub: false, onePlayOnly: true }),
    );
    // Hydrate directly into the audio phase so onPlay is not short-circuited
    // by the pre-audio reading window. Must be persistent (not -Once): the
    // player re-fetches FSM state on heartbeat, and a -Once mock would let
    // subsequent fetches fall back to the beforeEach default (an early
    // preview-phase state), reverting `phase` to 'preview' and blocking the
    // audio_started emission. Mirrors the sibling strict-review test.
    mockV2GetState.mockResolvedValue(makeV2State('a1_audio'));

    const { container } = render(<ListeningPlayer />);

    const audio = await waitFor(() => {
      const el = container.querySelector('audio');
      if (!el) throw new Error('audio not yet rendered');
      return el as HTMLAudioElement;
    });

    // Drive the playhead forward and fire a real play → audio_started fires once.
    (audio as unknown as { __ct?: number }).__ct = 12;
    fireEvent.play(audio);

    await waitFor(() => {
      expect(mockRecordIntegrity).toHaveBeenCalledWith('attempt-1', 'audio_started', expect.any(String));
    });
    const startedCall = mockRecordIntegrity.mock.calls.find((call) => call[1] === 'audio_started');
    expect(JSON.parse(startedCall![2] as string)).toMatchObject({ cuePointMs: 12000 });

    // A second play while still in the same run must NOT re-emit audio_started.
    fireEvent.play(audio);
    expect(mockRecordIntegrity.mock.calls.filter((call) => call[1] === 'audio_started')).toHaveLength(1);

    // Ending the audio emits audio_ended.
    (audio as unknown as { __ct?: number }).__ct = 240;
    fireEvent.ended(audio);

    await waitFor(() => {
      expect(mockRecordIntegrity).toHaveBeenCalledWith('attempt-1', 'audio_ended', expect.any(String));
    });
    const endedCall = mockRecordIntegrity.mock.calls.find((call) => call[1] === 'audio_ended');
    expect(JSON.parse(endedCall![2] as string)).toMatchObject({ cuePointMs: 240000 });
  });

  it('emits an auto_submit attempt event when the whole-attempt timer expires', async () => {
    vi.useFakeTimers();
    try {
      const expiresAt = new Date(Date.now() + 5_000).toISOString();
      mockGetListeningSession.mockResolvedValue(
        makeSession({ mode: 'exam', canScrub: false, onePlayOnly: true, expiresAt }),
      );
      mockSubmit.mockResolvedValue({ attemptId: 'attempt-1' });

      render(<ListeningPlayer />);

      await act(async () => {
        await vi.advanceTimersByTimeAsync(0);
      });

      await act(async () => {
        vi.setSystemTime(Date.now() + 6_000);
        await vi.advanceTimersByTimeAsync(2_000);
      });
      await act(async () => {
        await vi.advanceTimersByTimeAsync(0);
      });

      expect(mockRecordIntegrity).toHaveBeenCalledWith('attempt-1', 'auto_submit', expect.any(String));
      expect(mockSubmit).toHaveBeenCalledWith('attempt-1', {});
    } finally {
      vi.useRealTimers();
    }
  });

  it('snaps a backwards seek back to the last known forward time in exam mode', async () => {
    mockGetListeningSession.mockResolvedValue(
      makeSession({ mode: 'exam', canScrub: false, onePlayOnly: true }),
    );

    const { container } = render(<ListeningPlayer />);

    const audio = await waitFor(() => {
      const el = container.querySelector('audio');
      if (!el) throw new Error('audio not yet rendered');
      return el as HTMLAudioElement;
    });

    await waitFor(() => expect(seekCalls).toContain(12));

    // Establish a forward-only known time at t=50.
    (audio as unknown as { __ct?: number }).__ct = 50;
    fireEvent.timeUpdate(audio);

    // Clear the spy log so we can assert on the snap-back write only.
    seekCalls.length = 0;

    // Simulate a backwards seek by the user (currentTime drops to 10).
    (audio as unknown as { __ct?: number }).__ct = 10;
    fireEvent.seeking(audio);

    // The handler must have written 50 back to currentTime.
    expect(seekCalls).toContain(50);
  });
});
