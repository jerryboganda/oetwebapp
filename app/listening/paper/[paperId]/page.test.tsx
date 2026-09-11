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
        {
          id: 'ext-c-1',
          partCode: 'C1',
          title: 'Part C — Extract 1',
          audioUrl: 'https://cdn.example/c1.mp3',
          contextIntro: 'You hear a presentation about community health.',
        },
        {
          id: 'ext-c-2',
          partCode: 'C2',
          title: 'Part C — Extract 2',
          audioUrl: 'https://cdn.example/c2.mp3',
          contextIntro: 'You hear a presentation about workplace wellbeing.',
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
      ...Array.from({ length: 12 }, (_, index) => {
        const number = 31 + index;
        return {
          id: `q-${number}`,
          number,
          partCode: number <= 36 ? 'C1' : 'C2',
          type: 'multiple_choice_3',
          text: `What is the key point in question ${number}?`,
          options: [`Option A for ${number}`, `Option B for ${number}`, `Option C for ${number}`],
          optionKeys: ['A', 'B', 'C'],
          points: 1,
        };
      }),
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

  it('holds Q31–Q36 on C1 audio and moves the sub-section control to Q36', async () => {
    const session = makeMockSession({
      attempt: {
        attemptId: 'attempt-c-101',
        paperId: 'paper-1',
        mode: 'exam',
        // A1, B, C1, C2 are the authored sub-sections in this fixture.
        sectionCursor: 2,
        answers: {},
        serverNow: new Date().toISOString(),
      },
    });
    mockGetListeningSession.mockResolvedValue(session);
    mockSaveListeningAnswer.mockResolvedValue({ success: true });
    // The server owns the one-way cursor; C2 sits at index 3.
    mockAdvanceListeningSection.mockResolvedValue({ sectionCursor: 3 });

    const user = userEvent.setup();
    await act(async () => {
      render(<ListeningPaperPlayerPage params={Promise.resolve({ paperId: 'paper-1' })} />);
    });

    await waitFor(() => {
      expect(screen.getByText('What is the key point in question 31?')).toBeInTheDocument();
    });
    expect(screen.getAllByRole('tab')).toHaveLength(12);

    const audioSrcs = () => Array.from(document.querySelectorAll('audio'))
      .map((element) => element.getAttribute('src'));

    // Step Q31 → Q36. The whole extract stays on the single C1 source.
    for (let number = 32; number <= 36; number += 1) {
      await user.click(screen.getByRole('button', { name: /next question/i }));
      await waitFor(() => {
        expect(screen.getByText(`What is the key point in question ${number}?`)).toBeInTheDocument();
      });
      expect(audioSrcs()).toContain('https://cdn.example/c1.mp3');
      expect(audioSrcs()).not.toContain('https://cdn.example/c2.mp3');
    }

    // Q36 is the boundary: the transition replaces "Next Question"…
    expect(screen.queryByRole('button', { name: /next question/i })).not.toBeInTheDocument();
    expect(screen.getByRole('button', { name: /advance to next sub-section/i })).toBeInTheDocument();
    // …and the exam is not submittable from here.
    expect(screen.queryByRole('button', { name: /submit attempt/i })).not.toBeInTheDocument();

    // The regression from the 11 Sep report: Q36 → Q37 used to move the card
    // without starting C2, leaving the candidate in silence.
    await user.click(screen.getByRole('button', { name: /advance to next sub-section/i }));
    await user.click(await screen.findByRole('button', { name: /^continue$/i }));

    await waitFor(() => {
      expect(screen.getByText('What is the key point in question 37?')).toBeInTheDocument();
    });
    expect(audioSrcs()).toContain('https://cdn.example/c2.mp3');
    expect(audioSrcs()).not.toContain('https://cdn.example/c1.mp3');
    expect(mockAdvanceListeningSection).toHaveBeenCalledWith('attempt-c-101', 3);
  });

  it('offers Submit Exam at Q42 instead of another sub-section transition', async () => {
    const session = makeMockSession({
      attempt: {
        attemptId: 'attempt-c-202',
        paperId: 'paper-1',
        mode: 'exam',
        // C2 is the final sub-section at index 3.
        sectionCursor: 3,
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
      expect(screen.getByText('What is the key point in question 37?')).toBeInTheDocument();
    });
    // C2 audio is live from the moment the sub-section opens.
    expect(Array.from(document.querySelectorAll('audio')).map((element) => element.getAttribute('src')))
      .toContain('https://cdn.example/c2.mp3');

    for (let number = 38; number <= 42; number += 1) {
      await user.click(screen.getByRole('button', { name: /next question/i }));
      await waitFor(() => {
        expect(screen.getByText(`What is the key point in question ${number}?`)).toBeInTheDocument();
      });
    }

    expect(screen.queryByRole('button', { name: /next question/i })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /advance to next sub-section/i })).not.toBeInTheDocument();
    expect(screen.getByRole('button', { name: /submit attempt/i })).toHaveTextContent('Submit Exam');
  });

  it('routes a jump into the next extract through the transition and lands on the tapped card', async () => {
    const session = makeMockSession({
      attempt: {
        attemptId: 'attempt-c-303',
        paperId: 'paper-1',
        mode: 'exam',
        sectionCursor: 2,
        answers: {},
        serverNow: new Date().toISOString(),
      },
    });
    mockGetListeningSession.mockResolvedValue(session);
    mockSaveListeningAnswer.mockResolvedValue({ success: true });
    mockAdvanceListeningSection.mockResolvedValue({ sectionCursor: 3 });

    const user = userEvent.setup();
    await act(async () => {
      render(<ListeningPaperPlayerPage params={Promise.resolve({ paperId: 'paper-1' })} />);
    });

    await waitFor(() => {
      expect(screen.getByText('What is the key point in question 31?')).toBeInTheDocument();
    });

    // A Q31–Q42 workspace stays visible while C1 plays, but tapping a C2 card
    // must not open it without the audio — it takes the same transition.
    await user.click(screen.getByRole('tab', { name: /Question 40/ }));
    await user.click(await screen.findByRole('button', { name: /^continue$/i }));

    await waitFor(() => {
      expect(screen.getByText('What is the key point in question 40?')).toBeInTheDocument();
    });
    expect(Array.from(document.querySelectorAll('audio')).map((element) => element.getAttribute('src')))
      .toContain('https://cdn.example/c2.mp3');
    expect(mockAdvanceListeningSection).toHaveBeenCalledWith('attempt-c-303', 3);
  });
});
