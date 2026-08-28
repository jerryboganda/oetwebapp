import { render, screen, waitFor, act, fireEvent } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi, beforeEach } from 'vitest';

const {
  mockGetListeningSession,
  mockStartListeningAttempt,
  mockSaveListeningAnswer,
  mockAdvanceListeningSection,
  mockSubmitListeningAttempt,
  mockRecordListeningIntegrityEvent,
  mockSubmitAudioCheck,
  mockFetchAuthorizedObjectUrl,
  mockRouterPush,
  mockRouterReplace,
} = vi.hoisted(() => ({
  mockGetListeningSession: vi.fn(),
  mockStartListeningAttempt: vi.fn(),
  mockSaveListeningAnswer: vi.fn().mockResolvedValue({ success: true }),
  mockAdvanceListeningSection: vi.fn().mockResolvedValue({}),
  mockSubmitListeningAttempt: vi.fn().mockResolvedValue({}),
  mockRecordListeningIntegrityEvent: vi.fn().mockResolvedValue({ success: true }),
  mockSubmitAudioCheck: vi.fn(),
  mockFetchAuthorizedObjectUrl: vi.fn(),
  mockRouterPush: vi.fn(),
  mockRouterReplace: vi.fn(),
}));

vi.mock('next/navigation', () => ({
  useRouter: () => ({
    push: mockRouterPush,
    replace: mockRouterReplace,
  }),
  useSearchParams: () => new URLSearchParams(''),
  usePathname: () => '/listening/paper/paper-1',
  useParams: () => ({ paperId: 'paper-1' }),
}));

vi.mock('@/components/layout', () => ({
  LearnerDashboardShell: ({ children }: { children: React.ReactNode }) => <div data-testid="learner-shell">{children}</div>,
}));

vi.mock('@/lib/api', async () => {
  const actual = await vi.importActual<typeof import('@/lib/api')>('@/lib/api');
  return {
    ...actual,
    fetchAuthorizedObjectUrl: mockFetchAuthorizedObjectUrl,
  };
});

vi.mock('@/lib/listening-pathway-api', () => ({
  submitAudioCheck: mockSubmitAudioCheck,
}));

vi.mock('@/lib/listening-api', async () => {
  const actual = await vi.importActual<typeof import('@/lib/listening-api')>('@/lib/listening-api');
  return {
    ...actual,
    getListeningSession: mockGetListeningSession,
    startListeningAttempt: mockStartListeningAttempt,
    saveListeningAnswer: mockSaveListeningAnswer,
    advanceListeningSection: mockAdvanceListeningSection,
    submitListeningAttempt: mockSubmitListeningAttempt,
    recordListeningIntegrityEvent: mockRecordListeningIntegrityEvent,
    getListeningPaperAnnotations: vi.fn().mockResolvedValue([]),
  };
});

vi.mock('@/lib/listening/v2-api', () => ({
  listeningV2Api: {
    recordTechReadiness: vi.fn().mockResolvedValue({}),
  },
}));

vi.mock('@/components/domain/listening/TechReadinessCheck', () => ({
  TechReadinessCheck: ({ onReady }: { onReady: (result: { audioOk: boolean; durationMs: number }) => void }) => (
    <button
      data-testid="mock-tech-readiness"
      onClick={() => onReady({ audioOk: true, durationMs: 1200 })}
    >
      Pass Sound Check
    </button>
  ),
}));

import ListeningPaperPlayerPage from './page';

function makeMockSession(overrides: Record<string, unknown> = {}) {
  return {
    paper: {
      id: 'paper-1',
      title: 'Listening Benchmark Test 1',
      audioAvailable: true,
      audioUnavailableReason: null,
      audioUrl: 'https://cdn.example/audio.mp3',
      audioUrlByPart: {
        A1: 'https://cdn.example/a1.mp3',
        A2: 'https://cdn.example/a2.mp3',
        B: 'https://cdn.example/b.mp3',
        C1: 'https://cdn.example/c1.mp3',
        C2: 'https://cdn.example/c2.mp3',
      },
      questionPaperUrlByPart: {},
      extracts: [
        {
          id: 'ext-a1',
          partCode: 'A1',
          title: 'Part A — Extract 1',
          audioUrl: 'https://cdn.example/a1.mp3',
          notesBody: '<p>Patient name: {{gap:1}}</p>',
        },
        {
          id: 'ext-b-1',
          partCode: 'B',
          title: 'Part B — Question 25',
          audioUrl: 'https://cdn.example/b.mp3',
          audioStartMs: 0,
          audioEndMs: 45000,
        },
        {
          id: 'ext-b-2',
          partCode: 'B',
          title: 'Part B — Question 26',
          audioUrl: 'https://cdn.example/b.mp3',
          audioStartMs: 46000,
          audioEndMs: 90000,
        },
      ],
    },
    questions: [
      {
        id: 'q-1',
        number: 1,
        partCode: 'A1',
        type: 'short_text',
        text: 'Patient name',
        options: [],
        points: 1,
      },
      {
        id: 'q-25',
        number: 25,
        partCode: 'B',
        type: 'multiple_choice_3',
        text: 'What is the nurse discussing?',
        options: ['Medication timing', 'Discharge plan', 'Dietary restrictions'],
        optionKeys: ['A', 'B', 'C'],
        points: 1,
      },
      {
        id: 'q-26',
        number: 26,
        partCode: 'B',
        type: 'multiple_choice_3',
        text: 'Why does the doctor recommend rest?',
        options: ['To reduce fever', 'To allow wound healing', 'To lower blood pressure'],
        optionKeys: ['A', 'B', 'C'],
        points: 1,
      },
    ],
    modePolicy: {
      mode: 'exam',
      canPause: false,
      canScrub: false,
      onePlayOnly: true,
      countdownWarningsSeconds: [120, 30],
    },
    preflight: {
      candidate: { displayName: 'Dr. Test Candidate', professionLabel: 'Medicine' },
      selectedTest: { title: 'Listening Benchmark Test 1' },
      eligibility: { eligible: true },
    },
    attempt: null,
    serverNow: new Date().toISOString(),
    ...overrides,
  };
}

describe('ListeningPaperPlayerPage', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mockFetchAuthorizedObjectUrl.mockImplementation(async (url: string) => url);
    window.HTMLMediaElement.prototype.play = vi.fn().mockImplementation(() => Promise.resolve());
    window.HTMLMediaElement.prototype.pause = vi.fn();
  });

  it('renders intro card and allows candidate to complete sound check and start exam', async () => {
    const session = makeMockSession();
    mockGetListeningSession.mockResolvedValue(session);
    mockSubmitAudioCheck.mockResolvedValue({ status: 'ok' });
    mockStartListeningAttempt.mockResolvedValue({
      attemptId: 'attempt-101',
      paperId: 'paper-1',
      mode: 'exam',
      sectionCursor: 0,
      answers: {},
      serverNow: new Date().toISOString(),
      feedbackMessage: null,
    });

    const user = userEvent.setup();
    await act(async () => {
      render(<ListeningPaperPlayerPage params={Promise.resolve({ paperId: 'paper-1' })} />);
    });

    await waitFor(() => expect(screen.getByRole('heading', { name: 'Listening Benchmark Test 1' })).toBeInTheDocument());
    expect(screen.getByText(/Confirm your test/i)).toBeInTheDocument();

    const startBtn = screen.getByRole('button', { name: /start exam/i });
    expect(startBtn).toBeDisabled();

    await user.click(screen.getByTestId('mock-tech-readiness'));
    expect(startBtn).toBeEnabled();

    await user.click(startBtn);

    await waitFor(() => {
      expect(screen.getByTestId('listening-audio-transport')).toBeInTheDocument();
    });
    await waitFor(() => {
      expect(window.HTMLMediaElement.prototype.play).toHaveBeenCalled();
    });
  });

  it('renders ListeningAudioTransport with exam mode props and attempt countdown', async () => {
    const session = makeMockSession({
      attempt: {
        attemptId: 'attempt-101',
        paperId: 'paper-1',
        mode: 'exam',
        sectionCursor: 0,
        answers: { 'q-1': 'Smith' },
        serverNow: new Date().toISOString(),
      },
    });
    mockGetListeningSession.mockResolvedValue(session);

    await act(async () => {
      render(<ListeningPaperPlayerPage params={Promise.resolve({ paperId: 'paper-1' })} />);
    });

    await waitFor(() => {
      expect(screen.getByTestId('listening-audio-transport')).toBeInTheDocument();
    });

    expect(screen.getByTestId('listening-attempt-timer')).toBeInTheDocument();
    expect(screen.queryByRole('slider')).not.toBeInTheDocument();
  });

  it('renders BCQuestionRenderer for Part B items and supports Next Question navigation', async () => {
    const session = makeMockSession({
      attempt: {
        attemptId: 'attempt-101',
        paperId: 'paper-1',
        mode: 'exam',
        sectionCursor: 1,
        answers: {},
        serverNow: new Date().toISOString(),
      },
    });
    mockGetListeningSession.mockResolvedValue(session);
    mockSaveListeningAnswer.mockResolvedValue({ success: true });

    const user = userEvent.setup();
    await act(async () => {
      render(<ListeningPaperPlayerPage params={Promise.resolve({ paperId: 'paper-1' })} />);
    });

    await waitFor(() => {
      expect(screen.getByText('What is the nurse discussing?')).toBeInTheDocument();
    });

    expect(screen.getByTestId('bc-question-renderer')).toBeInTheDocument();
    expect(screen.getByText('Medication timing')).toBeInTheDocument();

    const optionA = screen.getByRole('radio', { name: /medication timing/i });
    await user.click(optionA);

    expect(optionA).toHaveAttribute('aria-checked', 'true');

    // Simulate Part B extract audio playback completion
    const audioElement = document.querySelector('audio');
    if (audioElement) {
      fireEvent.ended(audioElement);
    }

    const nextBtn = screen.getByRole('button', { name: /next question/i });
    expect(nextBtn).toBeEnabled();
    await user.click(nextBtn);

    await waitFor(() => {
      expect(screen.getByText('Why does the doctor recommend rest?')).toBeInTheDocument();
    });
    expect(screen.getByText('To reduce fever')).toBeInTheDocument();
  });
});
