import { render, screen, waitFor } from '@testing-library/react';

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
}));

vi.mock('next/navigation', () => ({
  useParams: () => ({ id: 'lp-001' }),
  useRouter: () => ({ push: vi.fn(), replace: mockReplace }),
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

import ListeningPlayer from '../page';

function makeSession(overrides: Record<string, unknown> = {}) {
  const base = {
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
        {
          partCode: 'A2', displayOrder: 2, kind: 'consultation', title: 'Extract 2',
          accentCode: 'en-GB', speakers: [],
          audioStartMs: 250_000, audioEndMs: 500_000,
        },
      ],
    },
    attempt: {
      attemptId: 'attempt-1', paperId: 'lp-001', state: 'in_progress', mode: 'practice' as const,
      startedAt: '2026-04-01T00:00:00Z', submittedAt: null, completedAt: null,
      elapsedSeconds: 0, lastClientSyncAt: null, answers: {},
    },
    questions: [
      { id: 'q-1', number: 1, partCode: 'A1', text: 'Q1?', type: 'short_answer', options: [], points: 1 },
      { id: 'q-2', number: 2, partCode: 'A2', text: 'Q2?', type: 'short_answer', options: [], points: 1 },
    ],
    modePolicy: {
      mode: 'practice' as const,
      canPause: true,
      canScrub: true,
      onePlayOnly: false,
      autosave: true,
      transcriptPolicy: 'per_item_post_attempt',
    },
    scoring: { maxRawScore: 42, passRawScore: 30, passScaledScore: 350 },
    readiness: { objectiveReady: true, questionCount: 42, audioAvailable: true, missingReason: null },
  };
  return { ...base, ...overrides };
}

describe('Listening player — cue-point seek (C1) and content-locked (C2)', () => {
  let seekCalls: number[];

  beforeEach(() => {
    vi.clearAllMocks();
    mockUseSearchParams.mockReturnValue({
      get: (key: string) => (key === 'attemptId' ? 'attempt-1' : key === 'mode' ? 'practice' : null),
    });

    // Spy on HTMLMediaElement.currentTime setter so we can assert seeks.
    seekCalls = [];
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
    // jsdom's HTMLMediaElement.play / pause are no-ops by default; ensure pause exists.
    const proto = HTMLMediaElement.prototype as unknown as { play: () => Promise<void>; pause: () => void };
    proto.play = () => Promise.resolve();
    proto.pause = () => undefined;
  });

  it('seeks audio to extract.audioStartMs / 1000 when entering the audio phase', async () => {
    mockGetListeningSession.mockResolvedValue(makeSession());

    render(<ListeningPlayer />);

    // The player auto-enters the audio phase when an attemptId is present in
    // the route, so the cue-point seek effect should fire on mount once the
    // session resolves and the active extract (A1) is computed.
    await waitFor(() => {
      expect(seekCalls).toContain(12);
    });
  });

  it('renders ContentLockedNotice when getListeningSession rejects with 402 content_locked', async () => {
    const err = Object.assign(new Error('This listening paper requires an active subscription.'), {
      status: 402,
      detail: { code: 'content_locked', message: 'This listening paper requires an active subscription.' },
    });
    mockGetListeningSession.mockRejectedValue(err);

    render(<ListeningPlayer />);

    expect(await screen.findByText(/Subscription required/i)).toBeInTheDocument();
    expect(
      screen.getByText(/Tip: subscribers get the first extract of every paper to preview/i),
    ).toBeInTheDocument();
  });
});
