import { createElement } from 'react';
import { act, fireEvent, render, screen, waitFor } from '@testing-library/react';
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
  submitListeningAttempt: mockSubmit,
  saveListeningAnswer: mockSaveAnswer,
  recordListeningIntegrityEvent: mockRecordIntegrity,
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

type Mode = 'practice' | 'exam' | 'home' | 'paper';

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
      canPause: mode === 'practice',
      canScrub: overrides.canScrub ?? (mode === 'practice'),
      onePlayOnly: overrides.onePlayOnly ?? (mode !== 'practice'),
      autosave: true,
      transcriptPolicy: 'per_item_post_attempt',
    },
    scoring: { maxRawScore: 42, passRawScore: 30, passScaledScore: 350 },
    readiness: { objectiveReady: true, questionCount: 42, audioAvailable: true, missingReason: null },
  };
}

describe('Listening player — CBLA fidelity (preview / attempt timer / one-play / extract progress)', () => {
  let seekCalls: number[];
  let playCalls: number;

  beforeEach(() => {
    vi.clearAllMocks();
    // Default-resolve all backend mocks so background timers (heartbeat,
    // autosave) never blow up when fake timers tick past 15s.
    mockHeartbeat.mockResolvedValue({ attemptId: 'attempt-1', elapsedSeconds: 0, lastClientSyncAt: '' });
    mockSaveAnswer.mockResolvedValue(undefined);
    mockRecordIntegrity.mockResolvedValue(undefined);
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
  });

  afterEach(() => {
    vi.useRealTimers();
  });

  it('renders the pre-audio reading window banner and does not auto-play audio', async () => {
    mockGetListeningSession.mockResolvedValue(makeSession());

    render(<ListeningPlayer />);

    await waitFor(() => {
      expect(screen.getByTestId('listening-preview-banner')).toBeInTheDocument();
    });
    expect(screen.getByTestId('listening-preview-banner')).toHaveTextContent(/Reading time/i);
    // Audio must NOT have been auto-played during the preview window.
    expect(playCalls).toBe(0);
  });

  it('auto-advances from preview to audio when the countdown reaches zero', async () => {
    mockGetListeningSession.mockResolvedValue(makeSession());

    vi.useFakeTimers();
    try {
      render(<ListeningPlayer />);

      // Flush the session.then() microtask chain and any zero-delay timers
      // queued by React so the preview banner mounts.
      await act(async () => {
        await vi.advanceTimersByTimeAsync(0);
      });

      expect(screen.getByTestId('listening-preview-banner')).toBeInTheDocument();

      // Drive the per-second countdown one tick at a time so React can
      // flush its render + new effect (which schedules the next 1000ms
      // setTimeout) between each fake clock advance.
      for (let i = 0; i <= LISTENING_PREVIEW_SECONDS.A1 + 1; i += 1) {
        await act(async () => {
          await vi.advanceTimersByTimeAsync(1_000);
        });
      }

      expect(screen.queryByTestId('listening-preview-banner')).not.toBeInTheDocument();
      expect(playCalls).toBeGreaterThanOrEqual(1);
    } finally {
      vi.useRealTimers();
    }
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

  it('keeps Next disabled until the active extract has completed', async () => {
    mockGetListeningSession.mockResolvedValue(
      makeSession({ mode: 'exam', canScrub: false, onePlayOnly: true }),
    );

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

      expect(screen.getByRole('button', { name: /next/i })).toBeDisabled();
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
