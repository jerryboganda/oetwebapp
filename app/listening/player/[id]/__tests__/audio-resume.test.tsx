import { createElement } from 'react';
import { act, fireEvent, render, screen, waitFor } from '@testing-library/react';

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
  mockAudioResume,
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
  mockAudioResume: vi.fn(),
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

vi.mock('@/lib/listening/v2-api', () => ({
  listeningV2Api: {
    getState: mockV2GetState,
    advance: mockV2Advance,
    recordTechReadiness: mockRecordTechReadiness,
    audioResume: mockAudioResume,
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

// Skip the pre-audio reading window so phase transitions directly into
// 'audio' and the onPlay handler stops early-returning. The real 30s
// preview is exercised by cbla-fidelity.test.tsx and is irrelevant to
// the audio-resume protocol under test here.
vi.mock('@/lib/listening-sections', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@/lib/listening-sections')>();
  return {
    ...actual,
    LISTENING_PREVIEW_SECONDS: { A1: 0, A2: 0, B: 0, C1: 0, C2: 0 },
  };
});

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

function makeSession(mode: 'exam' | 'practice' = 'exam') {
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
      expiresAt: null,
    },
    questions: [
      { id: 'q-1', number: 1, partCode: 'A1', text: 'Q1?', type: 'short_answer', options: [], points: 1 },
    ],
    modePolicy: {
      mode,
      canPause: mode === 'practice',
      canScrub: mode === 'practice',
      onePlayOnly: mode === 'exam',
      autosave: true,
      transcriptPolicy: 'per_item_post_attempt',
    },
    scoring: { maxRawScore: 42, passRawScore: 30, passScaledScore: 350 },
    readiness: { objectiveReady: true, questionCount: 42, audioAvailable: true, missingReason: null },
  };
}

function makePartBSession() {
  const session = makeSession('exam');
  return {
    ...session,
    paper: {
      ...session.paper,
      extracts: [
        {
          partCode: 'B', displayOrder: 1, kind: 'workplace', title: 'Extract 1',
          accentCode: 'en-GB', speakers: [],
          audioStartMs: 12_000, audioEndMs: 52_000,
        },
        {
          partCode: 'B', displayOrder: 2, kind: 'workplace', title: 'Extract 2',
          accentCode: 'en-GB', speakers: [],
          audioStartMs: 52_000, audioEndMs: 92_000,
        },
      ],
    },
    questions: [
      {
        id: 'q-b-1', number: 25, partCode: 'B', text: 'What is the speaker mainly concerned about?',
        type: 'multiple_choice', options: ['The time', 'The dose', 'The letter'], points: 1,
      },
    ],
  };
}

function makeV2State(state = 'a1_audio') {
  return {
    attemptId: 'attempt-1',
    mode: 'exam',
    state,
    locks: [],
    windowDurationMs: 240_000,
    windowRemainingMs: 200_000,
    confirmRequired: false,
    freeNavigation: false,
    oneWayLocks: true,
    unansweredWarningRequired: true,
  };
}

async function elapsePreviewWindow() {
  // With LISTENING_PREVIEW_SECONDS mocked to 0, the preview→audio
  // transition fires synchronously from the section-setup effect. We
  // still need one microtask flush so the resulting setState commits
  // and the audio element mounts.
  await act(async () => {
    await Promise.resolve();
    await Promise.resolve();
  });
}

describe('Listening player — strict-mode audio-resume server validation (C8g)', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mockHeartbeat.mockResolvedValue({ attemptId: 'attempt-1', elapsedSeconds: 0, lastClientSyncAt: '' });
    mockSaveAnswer.mockResolvedValue(undefined);
    mockRecordIntegrity.mockResolvedValue(undefined);
    mockV2GetState.mockResolvedValue(makeV2State());
    mockV2Advance.mockResolvedValue({
      outcome: 'applied',
      state: makeV2State(),
      confirmToken: null,
      confirmTokenTtlMs: null,
      rejectionReason: null,
      rejectionDetail: null,
    });
    mockRecordTechReadiness.mockResolvedValue({
      audioOk: true,
      durationMs: 1500,
      checkedAt: '2026-04-01T00:00:00Z',
      ttlMs: 900_000,
    });
    mockUseSearchParams.mockReturnValue({
      get: (key: string) => {
        if (key === 'attemptId') return 'attempt-1';
        if (key === 'mode') return 'exam';
        return null;
      },
    });

    Object.defineProperty(HTMLMediaElement.prototype, 'currentTime', {
      configurable: true,
      get() {
        return (this as unknown as { __ct?: number }).__ct ?? 0;
      },
      set(value: number) {
        (this as unknown as { __ct?: number }).__ct = value;
      },
    });
    const proto = HTMLMediaElement.prototype as unknown as { play: () => Promise<void>; pause: () => void };
    proto.play = () => Promise.resolve();
    proto.pause = () => undefined;
  });

  afterEach(() => {
    vi.useRealTimers();
  });

  it('calls listeningV2Api.audioResume when the audio element resumes after a pause in exam mode', async () => {
    mockGetListeningSession.mockResolvedValue(makeSession('exam'));
    mockAudioResume.mockResolvedValue({
      resume: true,
      serverState: 'a1_audio',
      resumeAtMs: 30_000,
      reason: 'in-window',
    });

    const { container } = render(<ListeningPlayer />);

    await act(async () => {
      await Promise.resolve();
    });
    await elapsePreviewWindow();

    const audio = await waitFor(() => {
      const el = container.querySelector('audio');
      if (!el) throw new Error('audio element not yet mounted');
      return el as HTMLAudioElement;
    });

    // Simulate the audio having advanced ~30s into the extract.
    (audio as unknown as { __ct?: number }).__ct = 30;

    act(() => {
      fireEvent.pause(audio);
    });
    act(() => {
      fireEvent.play(audio);
    });

    await waitFor(() => {
      expect(mockAudioResume).toHaveBeenCalledTimes(1);
    });
    expect(mockAudioResume).toHaveBeenCalledWith('attempt-1', 30_000);
  });

  it('uses the server mode policy for resume validation when the route has no mode query', async () => {
    mockUseSearchParams.mockReturnValue({
      get: (key: string) => {
        if (key === 'attemptId') return 'attempt-1';
        return null;
      },
    });
    mockGetListeningSession.mockResolvedValue(makeSession('exam'));
    mockAudioResume.mockResolvedValue({
      resume: true,
      serverState: 'a1_audio',
      resumeAtMs: 30_000,
      reason: 'in-window',
    });

    const { container } = render(<ListeningPlayer />);
    await act(async () => {
      await Promise.resolve();
    });

    const audio = await waitFor(() => {
      const el = container.querySelector('audio');
      if (!el) throw new Error('audio element not yet mounted');
      return el as HTMLAudioElement;
    });

    (audio as unknown as { __ct?: number }).__ct = 30;
    act(() => {
      fireEvent.pause(audio);
    });
    act(() => {
      fireEvent.play(audio);
    });

    await waitFor(() => {
      expect(mockAudioResume).toHaveBeenCalledTimes(1);
    });
    expect(mockAudioResume).toHaveBeenCalledWith('attempt-1', 30_000);
  });

  it('pauses strict playback while audio-resume validation is pending and resumes after approval', async () => {
    mockGetListeningSession.mockResolvedValue(makeSession('exam'));
    let approveResume!: (value: { resume: true; serverState: string; resumeAtMs: number; reason: string }) => void;
    mockAudioResume.mockReturnValue(new Promise((resolve) => {
      approveResume = resolve;
    }));
    const pauseSpy = vi.fn();
    const playSpy = vi.fn(() => Promise.resolve());
    const proto = HTMLMediaElement.prototype as unknown as { play: () => Promise<void>; pause: () => void };
    proto.play = playSpy;
    proto.pause = pauseSpy;

    const { container } = render(<ListeningPlayer />);
    await act(async () => {
      await Promise.resolve();
    });
    await elapsePreviewWindow();

    const audio = await waitFor(() => {
      const el = container.querySelector('audio');
      if (!el) throw new Error('audio element not yet mounted');
      return el as HTMLAudioElement;
    });

    (audio as unknown as { __ct?: number }).__ct = 30;
    act(() => {
      fireEvent.pause(audio);
    });
    act(() => {
      fireEvent.play(audio);
    });

    await waitFor(() => {
      expect(mockAudioResume).toHaveBeenCalledTimes(1);
    });
    expect(pauseSpy).toHaveBeenCalled();
    await act(async () => {
      await Promise.resolve();
    });

    const pauseCallsAfterInitialBlock = pauseSpy.mock.calls.length;
    const playCallsBeforeSecondAttempt = playSpy.mock.calls.length;
    await act(async () => {
      fireEvent.click(screen.getByRole('button', { name: /play audio/i }));
      await Promise.resolve();
    });
    expect(playSpy).toHaveBeenCalledTimes(playCallsBeforeSecondAttempt);
    expect(pauseSpy.mock.calls.length).toBeGreaterThan(pauseCallsAfterInitialBlock);
    expect(mockAudioResume).toHaveBeenCalledTimes(1);

    const pauseCallsAfterTransportBlock = pauseSpy.mock.calls.length;
    act(() => {
      fireEvent.play(audio);
    });
    expect(pauseSpy.mock.calls.length).toBeGreaterThan(pauseCallsAfterTransportBlock);
    expect(mockAudioResume).toHaveBeenCalledTimes(1);

    const playCallsWhileValidationPending = playSpy.mock.calls.length;

    await act(async () => {
      approveResume({
        resume: true,
        serverState: 'a1_audio',
        resumeAtMs: 30_000,
        reason: 'in-window',
      });
      await Promise.resolve();
    });

    await waitFor(() => {
      expect(playSpy).toHaveBeenCalledTimes(playCallsWhileValidationPending + 1);
    });
  });

  it('surfaces an inline alert and realigns the playhead when the server force-advances out of grace', async () => {
    mockGetListeningSession.mockResolvedValue(makeSession('exam'));
    mockV2GetState
      .mockResolvedValueOnce(makeV2State('a1_audio'))
      .mockResolvedValueOnce({
        ...makeV2State('a1_review'),
        windowRemainingMs: 60_000,
      });
    mockAudioResume
      .mockResolvedValueOnce({
        resume: false,
        serverState: 'a1_review',
        resumeAtMs: 90_000,
        reason: 'out-of-window',
      })
      .mockResolvedValueOnce({
        resume: true,
        serverState: 'a1_audio',
        resumeAtMs: 90_000,
        reason: 'in-window',
      });

    const { container } = render(<ListeningPlayer />);
    await act(async () => {
      await Promise.resolve();
    });
    await elapsePreviewWindow();

    const audio = await waitFor(() => {
      const el = container.querySelector('audio');
      if (!el) throw new Error('audio element not yet mounted');
      return el as HTMLAudioElement;
    });

    (audio as unknown as { __ct?: number }).__ct = 30;
    act(() => {
      fireEvent.pause(audio);
    });
    act(() => {
      fireEvent.play(audio);
    });

    await waitFor(() => {
      expect(mockAudioResume).toHaveBeenCalledTimes(1);
    });
    await act(async () => {
      await Promise.resolve();
      await Promise.resolve();
    });
    expect(screen.getByText(/Your section moved on while audio was paused\./i)).toBeInTheDocument();
    expect((audio as unknown as { __ct: number }).__ct).toBe(90);
    await waitFor(() => {
      expect(screen.getByTestId('listening-review-banner')).toBeInTheDocument();
    });
    expect(screen.getByText('01:00')).toBeInTheDocument();

    act(() => {
      fireEvent.play(audio);
    });
    await waitFor(() => {
      expect(mockAudioResume).toHaveBeenCalledTimes(2);
    });
  });

  it('pauses strict playback when audio-resume validation fails', async () => {
    mockGetListeningSession.mockResolvedValue(makeSession('exam'));
    mockAudioResume
      .mockRejectedValueOnce(new Error('network down'))
      .mockResolvedValueOnce({
        resume: true,
        serverState: 'a1_audio',
        resumeAtMs: 30_000,
        reason: 'in-window',
      });
    const pauseSpy = vi.fn();
    const proto = HTMLMediaElement.prototype as unknown as { play: () => Promise<void>; pause: () => void };
    proto.pause = pauseSpy;

    const { container } = render(<ListeningPlayer />);
    await act(async () => {
      await Promise.resolve();
    });
    await elapsePreviewWindow();

    const audio = await waitFor(() => {
      const el = container.querySelector('audio');
      if (!el) throw new Error('audio element not yet mounted');
      return el as HTMLAudioElement;
    });

    (audio as unknown as { __ct?: number }).__ct = 30;
    act(() => {
      fireEvent.pause(audio);
    });
    act(() => {
      fireEvent.play(audio);
    });

    await waitFor(() => {
      expect(mockAudioResume).toHaveBeenCalledTimes(1);
    });
    await waitFor(() => {
      expect(screen.getByText(/Audio resume could not be verified with the server/i)).toBeInTheDocument();
    });
    expect(pauseSpy).toHaveBeenCalled();

    act(() => {
      fireEvent.play(audio);
    });
    await waitFor(() => {
      expect(mockAudioResume).toHaveBeenCalledTimes(2);
    });
  });

  it('keeps strict Part B locked until every workplace extract reaches its end cue', async () => {
    mockGetListeningSession.mockResolvedValue(makePartBSession());
    mockV2GetState.mockResolvedValue(makeV2State('b_audio'));

    const { container } = render(<ListeningPlayer />);
    await act(async () => {
      await Promise.resolve();
      await Promise.resolve();
    });

    const audio = await waitFor(() => {
      const el = container.querySelector('audio');
      if (!el) throw new Error('audio element not yet mounted');
      return el as HTMLAudioElement;
    });

    (audio as unknown as { __ct?: number }).__ct = 53;
    act(() => {
      fireEvent.timeUpdate(audio);
    });

    const nextButton = await screen.findByRole('button', { name: /^next/i });
    expect(nextButton).toBeDisabled();

    (audio as unknown as { __ct?: number }).__ct = 93;
    act(() => {
      fireEvent.timeUpdate(audio);
    });

    await waitFor(() => {
      expect(screen.getByRole('button', { name: /^next/i })).not.toBeDisabled();
    });
  });

  it('does not call audioResume in practice mode', async () => {
    mockUseSearchParams.mockReturnValue({
      get: (key: string) => {
        if (key === 'attemptId') return 'attempt-1';
        if (key === 'mode') return 'practice';
        return null;
      },
    });
    mockGetListeningSession.mockResolvedValue(makeSession('practice'));

    const { container } = render(<ListeningPlayer />);
    await act(async () => {
      await Promise.resolve();
    });
    await elapsePreviewWindow();

    const audio = await waitFor(() => {
      const el = container.querySelector('audio');
      if (!el) throw new Error('audio element not yet mounted');
      return el as HTMLAudioElement;
    });

    (audio as unknown as { __ct?: number }).__ct = 30;
    act(() => {
      fireEvent.pause(audio);
    });
    act(() => {
      fireEvent.play(audio);
    });

    await act(async () => {
      await Promise.resolve();
    });
    expect(mockAudioResume).not.toHaveBeenCalled();
  });
});
