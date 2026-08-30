import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { act } from 'react';
import type { ReadingLearnerStructureDto } from '@/lib/reading-authoring-api';

const {
  mockClearReadingPaperAnnotations,
  mockCompleteMockSection,
  mockFetchAuthorizedObjectUrl,
  mockGetReadingAttempt,
  mockGetReadingPaperAnnotations,
  mockGetReadingStructureLearner,
  mockLockReadingPartA,
  mockPush,
  mockResumeReadingBreak,
  mockSaveReadingAnswer,
  mockSaveReadingAnnotations,
  mockGetReadingAnnotations,
  mockSearchParams,
  mockStartReadingAttempt,
  mockSubmitReadingAttempt,
} = vi.hoisted(() => ({
  mockClearReadingPaperAnnotations: vi.fn(),
  mockCompleteMockSection: vi.fn(),
  mockFetchAuthorizedObjectUrl: vi.fn(),
  mockGetReadingAttempt: vi.fn(),
  mockGetReadingPaperAnnotations: vi.fn(),
  mockGetReadingStructureLearner: vi.fn(),
  mockLockReadingPartA: vi.fn(),
  mockPush: vi.fn(),
  mockResumeReadingBreak: vi.fn(),
  mockSaveReadingAnswer: vi.fn(),
  mockSaveReadingAnnotations: vi.fn(),
  mockGetReadingAnnotations: vi.fn(),
  mockSearchParams: { current: new URLSearchParams() },
  mockStartReadingAttempt: vi.fn(),
  mockSubmitReadingAttempt: vi.fn(),
}));

vi.mock('next/navigation', () => ({
  useRouter: () => ({ push: mockPush }),
  useSearchParams: () => mockSearchParams.current,
}));

vi.mock('@/components/layout', () => ({
  LearnerDashboardShell: ({ children }: { children: React.ReactNode }) => <div data-testid="learner-dashboard-shell">{children}</div>,
}));

vi.mock('@/lib/api', async () => {
  const actual = await vi.importActual<typeof import('@/lib/api')>('@/lib/api');
  return {
    // The player narrows save failures with `err instanceof ApiError`, so the
    // real class has to survive the mock or that check throws.
    ApiError: actual.ApiError,
    completeMockSection: mockCompleteMockSection,
    fetchAuthorizedObjectUrl: mockFetchAuthorizedObjectUrl,
  };
});

vi.mock('@/lib/reading-authoring-api', async () => {
  const actual = await vi.importActual<typeof import('@/lib/reading-authoring-api')>('@/lib/reading-authoring-api');
  return {
    ...actual,
    clearReadingPaperAnnotations: mockClearReadingPaperAnnotations,
    getReadingAttempt: mockGetReadingAttempt,
    getReadingPaperAnnotations: mockGetReadingPaperAnnotations,
    getReadingStructureLearner: mockGetReadingStructureLearner,
    lockReadingPartA: mockLockReadingPartA,
    resumeReadingBreak: mockResumeReadingBreak,
    saveReadingAnswer: mockSaveReadingAnswer,
    saveReadingAnnotations: mockSaveReadingAnnotations,
    getReadingAnnotations: mockGetReadingAnnotations,
    // useReadingAnnotations consumes this adapter; point it at the mocked fns
    // so rule-out toggles don't hit the real network layer.
    readingAnnotationsApi: {
      saveAnnotations: mockSaveReadingAnnotations,
      getAnnotations: mockGetReadingAnnotations,
    },
    startReadingAttempt: mockStartReadingAttempt,
    submitReadingAttempt: mockSubmitReadingAttempt,
  };
});

import ReadingPaperPlayerPage from './page';

const baseNow = Date.parse('2026-05-12T10:00:00.000Z');
const startedAt = new Date(baseNow).toISOString();
const partADeadlineAt = new Date(baseNow + 15 * 60_000).toISOString();
const partBCDeadlineAt = new Date(baseNow + 60 * 60_000).toISOString();

describe('Reading paper player page', () => {
  beforeEach(() => {
    vi.useFakeTimers({ shouldAdvanceTime: true });
    vi.setSystemTime(baseNow);
    vi.clearAllMocks();
    Object.defineProperty(window, 'open', {
      value: vi.fn(() => ({ closed: false, close: vi.fn(), location: { assign: vi.fn() }, opener: null })),
      configurable: true,
    });
    class ResizeObserverStub {
      observe() {}
      unobserve() {}
      disconnect() {}
    }
    Object.defineProperty(window, 'ResizeObserver', {
      writable: true,
      configurable: true,
      value: ResizeObserverStub,
    });
    mockSearchParams.current = new URLSearchParams();
    mockFetchAuthorizedObjectUrl.mockResolvedValue('blob:http://localhost/reading-paper');
    mockGetReadingStructureLearner.mockResolvedValue(buildStructure());
    mockGetReadingPaperAnnotations.mockResolvedValue([]);
    mockClearReadingPaperAnnotations.mockResolvedValue(undefined);
    mockStartReadingAttempt.mockResolvedValue({
      attemptId: 'attempt-1',
      startedAt,
      deadlineAt: partBCDeadlineAt,
      partADeadlineAt,
      partBCDeadlineAt,
      answeredCount: 0,
      canResume: true,
      paperTitle: 'Reading Sample Paper 1',
      partATimerMinutes: 15,
      partBCTimerMinutes: 45,
      partABreakAvailable: true,
      partABreakResumed: false,
      partBCTimerPausedAt: null,
      partBCPausedSeconds: 0,
      partABreakMaxSeconds: 300,
    });
    mockGetReadingAttempt.mockResolvedValue(buildAttempt());
    mockSaveReadingAnswer.mockResolvedValue(undefined);
    mockLockReadingPartA.mockResolvedValue(buildEarlyLockState());
    mockResumeReadingBreak.mockResolvedValue(buildResumedBreakState());
    mockSaveReadingAnnotations.mockResolvedValue(undefined);
    mockGetReadingAnnotations.mockResolvedValue({ annotationsJson: null });
    mockSubmitReadingAttempt.mockResolvedValue({
      rawScore: 1,
      maxRawScore: 3,
      scaledScore: 120,
      gradeLetter: 'E',
      correctCount: 1,
      incorrectCount: 0,
      unansweredCount: 2,
      answers: [],
    });
  });

  afterEach(() => {
    vi.useRealTimers();
  });

  it('starts the canonical computer attempt, flushes answers, and navigates to canonical results', async () => {
    const user = userEvent.setup({ advanceTimers: vi.advanceTimersByTime });

    await renderPlayer();

    await user.click(await screen.findByRole('button', { name: /start attempt/i }));
    expect(await screen.findByRole('timer', { name: /part a window/i })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /question 1, unanswered/i })).toBeInTheDocument();

    await user.type(screen.getByPlaceholderText(/type your answer/i), 'aspirin');
    await user.click(screen.getByRole('button', { name: /^flag$/i }));
    expect(screen.getByRole('button', { name: /question 1, answered, flagged/i })).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: /submit attempt for grading/i }));
    expect(await screen.findByRole('dialog', { name: /submit reading attempt/i })).toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: /submit now/i }));

    await waitFor(() => {
      expect(mockSaveReadingAnswer).toHaveBeenCalledWith('attempt-1', 'q-a-1', '"aspirin"', expect.any(Number));
      expect(mockSubmitReadingAttempt).toHaveBeenCalledWith('attempt-1');
      expect(mockPush).toHaveBeenCalledWith('/reading/paper/paper-1/results?attemptId=attempt-1');
    });
  });

  it('renders the Part A PDF viewer with annotation tooling for resumed paper attempts', async () => {
    mockSearchParams.current = new URLSearchParams('presentation=paper&attemptId=attempt-1');

    await renderPlayer();

    // PDF-only rebuild: the Part A passage is delivered as a PDF region with
    // an in-viewer annotation toolbar (no separate "answer sheet" / printed
    // paper-sim surface).
    expect(await screen.findByLabelText(/part a document/i)).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /text highlight/i })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /^marker$/i })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /clear pdf/i })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /clear paper/i })).toBeInTheDocument();
  });

  it('keeps computer delivery for legacy paper-presentation URL hints', async () => {
    mockSearchParams.current = new URLSearchParams('presentation=paper&attemptId=attempt-1');
    mockGetReadingStructureLearner.mockResolvedValueOnce(buildStructure({ allowPaperReadingMode: false }));

    await renderPlayer();

    expect(screen.queryByText(/paper simulation is disabled by the current reading policy/i)).not.toBeInTheDocument();
    expect(screen.queryByLabelText(/paper-based reading simulation/i)).not.toBeInTheDocument();
    expect(screen.getByRole('tabpanel', { name: /part a/i })).toBeInTheDocument();
  });

  it('does not auto-start a full attempt from legacy mode=practice URL hints', async () => {
    mockSearchParams.current = new URLSearchParams('mode=practice&part=A');

    await renderPlayer();

    expect(await screen.findByRole('button', { name: /start attempt/i })).toBeInTheDocument();
    expect(mockStartReadingAttempt).not.toHaveBeenCalled();
  });

  it('saves fallback matching text references as A-D values', async () => {
    const user = userEvent.setup({ advanceTimers: vi.advanceTimersByTime });
    mockGetReadingStructureLearner.mockResolvedValueOnce(buildStructure({ partAMatching: true }));

    await renderPlayer();
    await user.click(await screen.findByRole('button', { name: /start attempt/i }));
    await user.click(await screen.findByRole('button', { name: /text a.*triage extract/i }));

    await act(async () => {
      vi.advanceTimersByTime(450);
      await Promise.resolve();
    });

    await waitFor(() => {
      expect(mockSaveReadingAnswer).toHaveBeenCalledWith('attempt-1', 'q-a-1', '"A"', expect.any(Number));
    });
  });

  it('moves passage highlighting into the PDF viewer and clears paper annotations', async () => {
    const user = userEvent.setup({ advanceTimers: vi.advanceTimersByTime });
    vi.spyOn(window, 'confirm').mockReturnValue(true);

    await renderPlayer();
    await user.click(await screen.findByRole('button', { name: /start attempt/i }));

    // The PDF-only rebuild replaced the old HTML passage-text highlight scope
    // with the PDF viewer's annotation toolbar.
    expect(await screen.findByLabelText(/part a document/i)).toBeInTheDocument();
    expect(document.querySelector('[data-reading-highlight-scope="passage"]')).toBeNull();
    expect(screen.getByRole('button', { name: /text highlight/i })).toBeInTheDocument();

    // Clearing all highlights routes through the annotations API for this paper.
    await user.click(screen.getByRole('button', { name: /clear paper/i }));
    await waitFor(() => {
      expect(mockClearReadingPaperAnnotations).toHaveBeenCalledWith('paper-1', { scope: 'paper' });
    });
  });

  it('exposes screen-reader hints and high-contrast state through the reading a11y controls', async () => {
    const user = userEvent.setup({ advanceTimers: vi.advanceTimersByTime });

    await renderPlayer();
    await user.click(await screen.findByRole('button', { name: /start attempt/i }));

    await user.click(screen.getByRole('button', { name: /accessibility settings/i }));
    await user.click(screen.getByRole('checkbox', { name: /high-contrast palette/i }));
    await user.click(screen.getByRole('checkbox', { name: /extra screen-reader hints/i }));

    const main = screen.getByRole('main');
    expect(main).toHaveAttribute('data-reading-contrast', 'high');
    expect(main).toHaveAttribute('aria-describedby', expect.stringContaining('reading-a11y-hints'));
    expect(screen.getByText(/screen reader hints are enabled/i)).toBeInTheDocument();
  });

  it('persists an MCQ rule-out (strikethrough) to the attempt', async () => {
    const user = userEvent.setup({ advanceTimers: vi.advanceTimersByTime });
    mockGetReadingStructureLearner.mockResolvedValueOnce(buildStructure({ partAMcq: true }));

    await renderPlayer();
    await user.click(await screen.findByRole('button', { name: /start attempt/i }));

    const optionA = screen.getByRole('radio', { name: /aspirin/i });
    expect(optionA).not.toBeChecked();

    // Rule out option A via the visible strike button (parity with Listening).
    await user.click(await screen.findByRole('button', { name: /rule out option a/i }));

    // Derived hook state flows back to the option renderer: the toggle flips to
    // "restore" (pressed) and the row renders struck-through.
    const restore = await screen.findByRole('button', { name: /restore option a/i });
    expect(restore).toHaveAttribute('aria-pressed', 'true');
    expect(optionA).not.toBeChecked();
    expect(screen.getByText('Aspirin').closest('label')).toHaveClass('line-through');

    // The debounced autosave PUTs the rule-out payload to the attempt.
    await act(async () => {
      vi.advanceTimersByTime(450);
      await Promise.resolve();
    });
    await waitFor(() => expect(mockSaveReadingAnnotations).toHaveBeenCalled());
    const lastCall = mockSaveReadingAnnotations.mock.calls.at(-1)!;
    expect(lastCall[0]).toBe('attempt-1');
    expect(JSON.parse(lastCall[1] as string).byQuestion['q-a-1'].struckOptions).toEqual(['A']);
  });

  it('renders Part C question numbers using official OET public numbering (Q7+)', async () => {
    // Resume a practice-mode attempt so Parts B/C are immediately accessible
    // (no Part A timer lock / transition screen in practice mode).
    mockSearchParams.current = new URLSearchParams('attemptId=attempt-1');
    mockGetReadingAttempt.mockResolvedValueOnce(buildAttempt({ mode: 'Drill' }));
    mockGetReadingStructureLearner.mockResolvedValueOnce(buildStructure({ partCSectionLocal: true }));

    const user = userEvent.setup({ advanceTimers: vi.advanceTimersByTime });
    await renderPlayer();

    await user.click(await screen.findByRole('tab', { name: /^part c/i }));

    // Part C C1 internal display order 1 must render as public Q7 (B 1..6, C 7..22).
    expect(await screen.findByRole('button', { name: /question 7, unanswered/i })).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /question 1, unanswered/i })).not.toBeInTheDocument();

    // Generic MCQ option labels render verbatim ("A. Option A", "D. Option D").
    expect(screen.getByText('Option A')).toBeInTheDocument();
    expect(screen.getByText('Option D')).toBeInTheDocument();
  });

  it('shows Part B/C questions and the collective Part B booklet when section shells are empty', async () => {
    mockSearchParams.current = new URLSearchParams('attemptId=attempt-1');
    mockGetReadingAttempt.mockResolvedValueOnce(buildAttempt({ mode: 'Drill' }));
    mockGetReadingStructureLearner.mockResolvedValueOnce(buildStructure({ officialBcLayout: true }));

    const user = userEvent.setup({ advanceTimers: vi.advanceTimersByTime });
    await renderPlayer();

    await user.click(await screen.findByRole('tab', { name: /^part b/i }));

    expect(await screen.findByLabelText(/part b document/i)).toBeInTheDocument();
    expect(screen.queryByText(/no section b1 document/i)).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /^b1/i })).not.toBeInTheDocument();
    expect(screen.getByRole('button', { name: /question 1, unanswered/i })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /question 2, unanswered/i })).toBeInTheDocument();

    await user.click(screen.getByRole('tab', { name: /^part c/i }));
    expect(await screen.findByLabelText(/part c document/i)).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /question 7, unanswered/i })).toBeInTheDocument();
  });

  it('hydrates persisted rule-out marks when resuming an attempt', async () => {
    mockSearchParams.current = new URLSearchParams('attemptId=attempt-1');
    mockGetReadingStructureLearner.mockResolvedValueOnce(buildStructure({ partAMcq: true }));
    mockGetReadingAttempt.mockResolvedValueOnce(buildAttempt({
      annotationsJson: JSON.stringify({ byQuestion: { 'q-a-1': { struckOptions: ['B'] } } }),
    }));

    await renderPlayer();

    // Option B (Paracetamol) shows ruled-out straight from the persisted payload.
    expect(await screen.findByRole('button', { name: /restore option b/i })).toHaveAttribute('aria-pressed', 'true');
    expect(screen.getByText('Paracetamol').closest('label')).toHaveClass('line-through');
    // Hydration must not trigger a write-back.
    expect(mockSaveReadingAnnotations).not.toHaveBeenCalled();
  });
  // -- Early "Submit Part A" (owner request 2026-08-29) ------------------

  it('offers Submit Part A during Part A of an exam attempt', async () => {
    const user = userEvent.setup({ advanceTimers: vi.advanceTimersByTime });

    await renderPlayer();
    await user.click(await screen.findByRole('button', { name: /start attempt/i }));

    expect(await screen.findByTestId('reading-submit-part-a')).toBeInTheDocument();
  });

  it('hides Submit Part A in practice modes', async () => {
    mockSearchParams.current = new URLSearchParams('attemptId=attempt-1');
    mockGetReadingAttempt.mockResolvedValue(buildAttempt({ mode: 'Drill' }));

    await renderPlayer();

    await waitFor(() => expect(mockGetReadingAttempt).toHaveBeenCalled());
    expect(screen.queryByTestId('reading-submit-part-a')).not.toBeInTheDocument();
  });

  it('cancelling the Submit Part A dialog keeps the candidate in Part A', async () => {
    const user = userEvent.setup({ advanceTimers: vi.advanceTimersByTime });

    await renderPlayer();
    await user.click(await screen.findByRole('button', { name: /start attempt/i }));
    await user.click(await screen.findByTestId('reading-submit-part-a'));

    expect(await screen.findByRole('dialog', { name: /submit part a/i })).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: /^cancel$/i }));

    await waitFor(() => expect(screen.queryByRole('dialog', { name: /submit part a/i })).not.toBeInTheDocument());
    expect(mockLockReadingPartA).not.toHaveBeenCalled();
    expect(screen.getByPlaceholderText(/type your answer/i)).toBeEnabled();
    expect(screen.getByTestId('reading-submit-part-a')).toBeInTheDocument();
  });

  it('flushes dirty Part A answers before locking the section', async () => {
    const user = userEvent.setup({ advanceTimers: vi.advanceTimersByTime });

    await renderPlayer();
    await user.click(await screen.findByRole('button', { name: /start attempt/i }));
    await user.type(screen.getByPlaceholderText(/type your answer/i), 'aspirin');

    await user.click(await screen.findByTestId('reading-submit-part-a'));
    fireEvent.click(await screen.findByTestId('reading-confirm-submit-part-a'));

    await waitFor(() => expect(mockLockReadingPartA).toHaveBeenCalledWith('attempt-1'));
    expect(mockSaveReadingAnswer).toHaveBeenCalledWith('attempt-1', 'q-a-1', '"aspirin"', expect.any(Number));
    // The answer must land before the section closes, or the server rejects it.
    expect(mockSaveReadingAnswer.mock.invocationCallOrder[0])
      .toBeLessThan(mockLockReadingPartA.mock.invocationCallOrder[0]);
  });

  it('opens the break screen with Resume Test after an early Part A submit', async () => {
    const user = userEvent.setup({ advanceTimers: vi.advanceTimersByTime });

    await renderPlayer();
    await user.click(await screen.findByRole('button', { name: /start attempt/i }));
    await user.click(await screen.findByTestId('reading-submit-part-a'));
    fireEvent.click(await screen.findByTestId('reading-confirm-submit-part-a'));

    // Advance past the lock instant so the derived timers agree with the
    // server's new Part A deadline.
    await act(async () => {
      vi.setSystemTime(baseNow + 5 * 60_000 + 1_000);
      vi.advanceTimersByTime(1_000);
    });

    expect(await screen.findByRole('button', { name: /resume reading test/i })).toBeInTheDocument();
    expect(screen.getByText(/part a collected/i)).toBeInTheDocument();
    expect(screen.getByText(/part a locked/i)).toBeInTheDocument();
  });

  it('Resume Test after an early Part A submit starts Parts B and C', async () => {
    const user = userEvent.setup({ advanceTimers: vi.advanceTimersByTime });

    await renderPlayer();
    await user.click(await screen.findByRole('button', { name: /start attempt/i }));
    await user.click(await screen.findByTestId('reading-submit-part-a'));
    fireEvent.click(await screen.findByTestId('reading-confirm-submit-part-a'));

    await act(async () => {
      vi.setSystemTime(baseNow + 5 * 60_000 + 1_000);
      vi.advanceTimersByTime(1_000);
    });

    fireEvent.click(await screen.findByRole('button', { name: /resume reading test/i }));

    await waitFor(() => expect(mockResumeReadingBreak).toHaveBeenCalledWith('attempt-1'));
    expect(await screen.findByRole('timer', { name: /b\/c shared window/i })).toBeInTheDocument();
  });

  it('does not surface an autosave error when a Part A save loses the race to the lock', async () => {
    const user = userEvent.setup({ advanceTimers: vi.advanceTimersByTime });
    const { ApiError } = await import('@/lib/api');
    mockSaveReadingAnswer.mockRejectedValue(
      new ApiError(400, 'part_a_locked', 'Part A is locked because the 15-minute window has ended.', false),
    );

    await renderPlayer();
    await user.click(await screen.findByRole('button', { name: /start attempt/i }));
    await user.type(screen.getByPlaceholderText(/type your answer/i), 'aspirin');

    await act(async () => {
      vi.advanceTimersByTime(1_500);
    });

    await waitFor(() => expect(mockSaveReadingAnswer).toHaveBeenCalled());
    expect(screen.queryByText(/autosave failed/i)).not.toBeInTheDocument();
    expect(screen.queryByText(/save failed/i)).not.toBeInTheDocument();
  });

  it('aborts the early submit when a Part A answer genuinely fails to save', async () => {
    // Locking would discard the unsaved answer for good, so a real failure has
    // to keep the candidate in Part A with the error visible.
    const user = userEvent.setup({ advanceTimers: vi.advanceTimersByTime });
    const { ApiError } = await import('@/lib/api');
    mockSaveReadingAnswer.mockRejectedValue(
      new ApiError(503, 'server_error', 'Could not reach the server.', true),
    );

    await renderPlayer();
    await user.click(await screen.findByRole('button', { name: /start attempt/i }));
    await user.type(screen.getByPlaceholderText(/type your answer/i), 'aspirin');

    await user.click(await screen.findByTestId('reading-submit-part-a'));
    fireEvent.click(await screen.findByTestId('reading-confirm-submit-part-a'));

    await waitFor(() => expect(screen.getByText(/could not reach the server/i)).toBeInTheDocument());
    expect(mockLockReadingPartA).not.toHaveBeenCalled();
    expect(screen.getByTestId('reading-submit-part-a')).toBeInTheDocument();
  });

  it('locks Part A exactly once and blocks re-entry while the request is in flight', async () => {
    // Two things keep a single confirmation from producing two POSTs: the
    // dialog unmounts on confirm, and the toolbar action is disabled until the
    // request settles. (`lockPartAInFlight` guards the handler itself as
    // defence-in-depth for future refactors, and the server is idempotent.)
    const user = userEvent.setup({ advanceTimers: vi.advanceTimersByTime });
    let releaseLock: (() => void) | undefined;
    mockLockReadingPartA.mockImplementation(() => new Promise((resolve) => {
      releaseLock = () => resolve(buildEarlyLockState());
    }));

    await renderPlayer();
    await user.click(await screen.findByRole('button', { name: /start attempt/i }));

    await user.click(await screen.findByTestId('reading-submit-part-a'));
    fireEvent.click(await screen.findByTestId('reading-confirm-submit-part-a'));
    await waitFor(() => expect(mockLockReadingPartA).toHaveBeenCalledTimes(1));

    // Dialog gone, action disabled: the candidate cannot fire a second lock.
    expect(screen.queryByTestId('reading-confirm-submit-part-a')).not.toBeInTheDocument();
    await waitFor(() => expect(screen.getByTestId('reading-submit-part-a')).toBeDisabled());
    await user.click(screen.getByTestId('reading-submit-part-a'));
    expect(screen.queryByTestId('reading-confirm-submit-part-a')).not.toBeInTheDocument();

    await act(async () => {
      releaseLock?.();
      await Promise.resolve();
    });
    expect(mockLockReadingPartA).toHaveBeenCalledTimes(1);
  });
});

function buildAttempt(overrides?: { status?: string; annotationsJson?: string | null; mode?: 'Exam' | 'Learning' | 'Drill' | 'MiniTest' | 'ErrorBank' }) {
  return {
    id: 'attempt-1',
    paperId: 'paper-1',
    status: overrides?.status ?? 'InProgress',
    mode: overrides?.mode ?? 'Exam',
    scopeQuestionIds: null,
    startedAt,
    deadlineAt: partBCDeadlineAt,
    submittedAt: null,
    rawScore: null,
    scaledScore: null,
    maxRawScore: 3,
    partADeadlineAt,
    partBCDeadlineAt,
    partABreakAvailable: true,
    partABreakResumed: false,
    partBCTimerPausedAt: null,
    partBCPausedSeconds: 0,
    partABreakMaxSeconds: 300,
    answeredCount: 0,
    totalQuestions: 3,
    canResume: true,
    answers: [],
    showExplanations: false,
    annotationsJson: overrides?.annotationsJson ?? null,
  };
}

/**
 * What the server returns from POST /attempts/{id}/part-a/lock when the
 * candidate presses "Submit Part A" 5 minutes into the 15-minute window:
 * Part A closes now, the optional break opens, and Parts B/C are re-anchored
 * to 45 minutes from the lock instant (the unused Part A time is forfeited).
 */
function buildEarlyLockState() {
  const lockedAt = new Date(baseNow + 5 * 60_000).toISOString();
  return {
    attemptId: 'attempt-1',
    deadlineAt: new Date(baseNow + 5 * 60_000 + 45 * 60_000 + 300_000).toISOString(),
    partADeadlineAt: lockedAt,
    partBCDeadlineAt: new Date(baseNow + 5 * 60_000 + 45 * 60_000).toISOString(),
    partABreakAvailable: true,
    partABreakResumed: false,
    partBCTimerPausedAt: lockedAt,
    partBCPausedSeconds: 0,
    partABreakMaxSeconds: 300,
    serverNow: lockedAt,
  };
}

/** Pressing Resume Test straight after an early lock. */
function buildResumedBreakState() {
  const resumedAt = new Date(baseNow + 5 * 60_000).toISOString();
  return {
    ...buildEarlyLockState(),
    partABreakResumed: true,
    partBCTimerPausedAt: null,
    serverNow: resumedAt,
  };
}

async function renderPlayer() {
  render(<ReadingPaperPlayerPage params={resolvedParams({ paperId: 'paper-1' })} />);
  await act(async () => {
    await Promise.resolve();
  });
}

function resolvedParams<T>(value: T): Promise<T> {
  const promise = Promise.resolve(value) as Promise<T> & { status: 'fulfilled'; value: T };
  promise.status = 'fulfilled';
  promise.value = value;
  return promise;
}

function emptySection(code: 'B1' | 'B2' | 'B3' | 'B4' | 'B5' | 'B6' | 'C1' | 'C2', displayOrder: number) {
  return {
    id: `sec-${code}`,
    sectionCode: code,
    displayOrder,
    maxRawScore: code.startsWith('C') ? 8 : 1,
    contentPaperAssetId: null,
    questions: [],
  };
}

function buildStructure(opts?: { allowPaperReadingMode?: boolean; partAMatching?: boolean; partAMcq?: boolean; partCSectionLocal?: boolean; officialBcLayout?: boolean }): ReadingLearnerStructureDto {
  const partATexts = opts?.partAMatching
    ? [
      { id: 'text-a-2', displayOrder: 2, title: 'Medication extract', source: 'Clinic', bodyHtml: '<p>Use aspirin carefully.</p>', wordCount: 4, topicTag: null },
      { id: 'text-a-1', displayOrder: 1, title: 'Triage extract', source: 'Clinic', bodyHtml: '<p>Assess urgent referrals.</p>', wordCount: 3, topicTag: null },
    ]
    : [
      { id: 'text-a-1', displayOrder: 1, title: 'Text A', source: 'Clinic', bodyHtml: '<p>Use aspirin carefully.</p>', wordCount: 4, topicTag: null },
    ];
  const partAQuestions = opts?.partAMcq
    ? [
      { id: 'q-a-1', readingTextId: 'text-a-1', readingSectionId: null, displayOrder: 1, points: 1, questionType: 'MultipleChoice3' as const, stem: 'Which medication is described?', options: ['Aspirin', 'Paracetamol', 'Ibuprofen'] },
    ]
    : opts?.partAMatching
    ? [
      { id: 'q-a-1', readingTextId: 'text-a-1', readingSectionId: null, displayOrder: 1, points: 1, questionType: 'MatchingTextReference' as const, stem: 'Which text discusses triage?', options: [] },
    ]
    : [
      { id: 'q-a-1', readingTextId: 'text-a-1', readingSectionId: null, displayOrder: 1, points: 1, questionType: 'ShortAnswer' as const, stem: 'Name the medication.', options: [] },
    ];

  return {
    paper: {
      id: 'paper-1',
      title: 'Reading Sample Paper 1',
      slug: 'reading-sample-paper-1',
      subtestCode: 'reading',
      allowPaperReadingMode: opts?.allowPaperReadingMode ?? true,
      policy: {
        fontScaleUserControl: true,
        highContrastMode: true,
        screenReaderOptimised: true,
      },
      questionPaperAssets: [
        { id: 'asset-a', part: 'A', title: 'Part A PDF', downloadPath: '/v1/media/media-a/content' },
        ...(opts?.officialBcLayout
          ? [
            { id: 'asset-b', part: 'B', title: 'Part B booklet', downloadPath: '/v1/media/media-b/content' },
            { id: 'asset-b1', part: 'B1', title: 'B1 extract only', downloadPath: '/v1/media/media-b1/content' },
            { id: 'asset-c', part: 'C', title: 'Part C booklet', downloadPath: '/v1/media/media-c/content' },
            { id: 'asset-c1', part: 'C1', title: 'C1 extract only', downloadPath: '/v1/media/media-c1/content' },
          ]
          : []),
      ],
    },
    parts: [
      {
        id: 'part-a',
        partCode: 'A',
        timeLimitMinutes: 15,
        maxRawScore: 1,
        instructions: null,
        texts: partATexts,
        questions: partAQuestions,
      },
      {
        id: 'part-b',
        partCode: 'B',
        timeLimitMinutes: 45,
        maxRawScore: 1,
        instructions: null,
        texts: [
          { id: 'text-b-1', displayOrder: 1, title: 'Text B', source: 'Policy', bodyHtml: '<p>Policy extract.</p>', wordCount: 2, topicTag: null },
        ],
        sections: opts?.officialBcLayout
          ? (['B1', 'B2', 'B3', 'B4', 'B5', 'B6'] as const).map((code, index) => emptySection(code, index + 1))
          : undefined,
        questions: opts?.officialBcLayout
          ? [
            { id: 'q-b-1', readingTextId: 'text-b-1', readingSectionId: null, displayOrder: 1, points: 1, questionType: 'MultipleChoice3', stem: 'What is the policy purpose?', options: ['A', 'B', 'C'] },
            { id: 'q-b-2', readingTextId: 'text-b-1', readingSectionId: null, displayOrder: 2, points: 1, questionType: 'MultipleChoice3', stem: 'What should staff do next?', options: ['A', 'B', 'C'] },
          ]
          : [
            { id: 'q-b-1', readingTextId: 'text-b-1', readingSectionId: null, displayOrder: 21, points: 1, questionType: 'MultipleChoice3', stem: 'What is the policy purpose?', options: ['A', 'B', 'C'] },
          ],
      },
      {
        id: 'part-c',
        partCode: 'C',
        timeLimitMinutes: 45,
        maxRawScore: 1,
        instructions: null,
        texts: [
          { id: 'text-c-1', displayOrder: 1, title: 'Text C', source: 'Journal', bodyHtml: '<p>Journal extract.</p>', wordCount: 2, topicTag: null },
        ],
        sections: opts?.officialBcLayout
          ? [emptySection('C1', 1), emptySection('C2', 2)]
          : undefined,
        questions: [
          // Section-local internal display order (C1 = 1..8) when authored via the
          // answer-sheet builder; the legacy single-stream number (27) otherwise.
          // The builder stores generic option strings ("Option A" …) verbatim.
          { id: 'q-c-1', readingTextId: 'text-c-1', readingSectionId: null, displayOrder: opts?.partCSectionLocal || opts?.officialBcLayout ? 1 : 27, points: 1, questionType: 'MultipleChoice4', stem: 'What can be inferred?', options: opts?.partCSectionLocal || opts?.officialBcLayout ? ['Option A', 'Option B', 'Option C', 'Option D'] : ['A', 'B', 'C', 'D'] },
        ],
      },
    ],
  };
}
