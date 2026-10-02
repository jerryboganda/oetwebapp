import { act, fireEvent, render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { ApiError } from '@/lib/api/client';

/**
 * Practice session: load failures (Addendum Rev8 §16), the draft-first load
 * algorithm (resume without eligibility, consumed → grading, new attempt), the
 * sync engine wiring (offline submit block, conflict, pagehide flush) and the
 * time-up auto-submit backoff. The real sync hook runs; only the API is mocked.
 */

const {
  checkWritingScenarioEligibility,
  createWritingSubmission,
  getWritingDraftV2,
  getWritingHighlights,
  getWritingScenario,
  putWritingDraftV2,
  putWritingHighlights,
  mockPush,
  mockReplace,
} = vi.hoisted(() => ({
  checkWritingScenarioEligibility: vi.fn(),
  createWritingSubmission: vi.fn(),
  getWritingDraftV2: vi.fn(),
  getWritingHighlights: vi.fn(),
  getWritingScenario: vi.fn(),
  putWritingDraftV2: vi.fn(),
  putWritingHighlights: vi.fn(),
  mockPush: vi.fn(),
  mockReplace: vi.fn(),
}));

vi.mock('@/lib/writing/api', () => ({
  checkWritingScenarioEligibility,
  createWritingSubmission,
  getWritingDraftV2,
  getWritingHighlights,
  getWritingScenario,
  putWritingDraftV2,
  putWritingHighlights,
}));

vi.mock('next/navigation', () => ({
  useParams: () => ({ scenarioId: 'scenario-1' }),
  useRouter: () => ({ push: mockPush, replace: mockReplace }),
}));

vi.mock('@/components/layout/learner-dashboard-shell', () => ({
  LearnerDashboardShell: ({ children }: { children: React.ReactNode }) => <div>{children}</div>,
}));

vi.mock('@/lib/credit-feedback', () => ({ showCreditFeedback: vi.fn() }));

// The PDF viewer and reading overlay have their own suites; stub them so this
// one stays on the page's load / save / submit behaviour.
vi.mock('@/components/domain/writing/WritingStimulus', () => ({
  WritingStimulus: ({ scenario }: { scenario: { taskPromptMarkdown?: string | null } | null }) => (
    <div data-testid="stimulus">{scenario?.taskPromptMarkdown}</div>
  ),
}));
vi.mock('@/components/domain/writing/WritingReadingWindowOverlay', () => ({
  WritingReadingWindowOverlay: () => null,
}));
// Like the real editor (covered by WritingEditorV2.test.tsx with real Tiptap),
// the stub reads `initialContent` ONCE, at mount, and reports every edit.
vi.mock('@/components/domain/writing/WritingEditorV2', async () => {
  const { useState } = await import('react');
  return {
    WritingEditorV2: ({
      initialContent = '',
      onChange,
      disabled,
    }: {
      initialContent?: string;
      onChange?: (text: string, words: number) => void;
      disabled?: boolean;
    }) => {
      const [mounted] = useState(initialContent);
      return (
        <textarea
          data-testid="editor-stub"
          defaultValue={mounted}
          disabled={disabled}
          onChange={(event) => {
            const text = event.target.value;
            onChange?.(text, text.trim() ? text.trim().split(/\s+/).length : 0);
          }}
        />
      );
    },
  };
});

import WritingPracticeSessionPage from './page';

const SCENARIO = {
  id: 'scenario-1',
  title: 'Nursing discharge — Mrs Patel',
  letterType: 'LT-DG',
  profession: 'nursing',
  subDiscipline: null,
  topics: [],
  difficulty: 3,
  caseNotesStructured: [{ index: 1, text: 'Case note.', relevance: 'relevant' }],
  isDiagnostic: false,
  status: 'published',
  createdAt: '2026-09-01T00:00:00Z',
  updatedAt: '2026-09-01T00:00:00Z',
  taskPromptMarkdown: 'Write a discharge letter to the community nurse.',
  fixedInstructions: [],
  readingTimeSeconds: 300,
  writingTimeSeconds: 2400,
  wordGuideMin: 180,
  wordGuideMax: 200,
  stimulusPdfMediaAssetId: null,
  stimulusPdfDownloadPath: null,
};

function activeDraft(overrides: Record<string, unknown> = {}) {
  return {
    userId: 'u1',
    scenarioId: 'scenario-1',
    mode: 'practice',
    content: 'Dear Dr Green,',
    wordCount: 3,
    timeSpentSeconds: 60,
    lastSavedAt: '2026-10-02T10:00:00Z',
    version: 3,
    status: 'active',
    phase: 'writing',
    readingSecondsRemaining: 0,
    writingSecondsRemaining: 1234,
    ...overrides,
  };
}

function setOnline(online: boolean) {
  Object.defineProperty(window.navigator, 'onLine', { configurable: true, get: () => online });
  act(() => {
    window.dispatchEvent(new Event(online ? 'online' : 'offline'));
  });
}

beforeEach(() => {
  vi.clearAllMocks();
  localStorage.clear();
  Object.defineProperty(window.navigator, 'onLine', { configurable: true, get: () => true });
  getWritingScenario.mockResolvedValue(SCENARIO);
  getWritingDraftV2.mockResolvedValue(null);
  getWritingHighlights.mockResolvedValue(null);
  checkWritingScenarioEligibility.mockResolvedValue({ feedbackMessage: null });
  putWritingDraftV2.mockResolvedValue(activeDraft({ version: 4 }));
});

afterEach(() => {
  vi.useRealTimers();
});

describe('Writing practice session — load failures (Addendum Rev8 §16)', () => {
  it('shows a real error state with Retry when eligibility fails with a server error, then recovers', async () => {
    checkWritingScenarioEligibility
      .mockRejectedValueOnce(new ApiError(500, 'internal_server_error', 'An unexpected server error occurred.', true))
      .mockResolvedValueOnce({ feedbackMessage: null });
    const user = userEvent.setup();

    render(<WritingPracticeSessionPage />);

    expect(await screen.findByText('writing.practice.session.loadError.title')).toBeInTheDocument();
    // Candidate-safe copy — never the raw 5xx body, never a stuck loader.
    expect(screen.getByText('writing.practice.session.error.load')).toBeInTheDocument();
    expect(screen.queryByText('An unexpected server error occurred.')).not.toBeInTheDocument();
    expect(screen.queryByText('writing.practice.session.scenarioLoading')).not.toBeInTheDocument();
    expect(getWritingScenario).not.toHaveBeenCalled();
    expect(
      screen.getByRole('link', { name: 'writing.practice.session.loadError.backToLibrary' }),
    ).toHaveAttribute('href', '/writing/practice/library');

    await user.click(screen.getByRole('button', { name: 'writing.practice.session.loadError.retry' }));

    expect(await screen.findByText('Nursing discharge — Mrs Patel')).toBeInTheDocument();
    expect(checkWritingScenarioEligibility).toHaveBeenCalledTimes(2);
    expect(screen.getByTestId('stimulus')).toHaveTextContent('Write a discharge letter to the community nurse.');
    expect(screen.queryByText('writing.practice.session.loadError.title')).not.toBeInTheDocument();
  });

  it('opens the credits modal (not a dead-end banner) for premium_required', async () => {
    checkWritingScenarioEligibility.mockRejectedValue(
      new ApiError(402, 'premium_required', 'Writing practice requires an active subscription.', false),
    );

    render(<WritingPracticeSessionPage />);

    const dialog = await screen.findByRole('dialog', { name: /not enough credits/i });
    expect(dialog).toHaveTextContent('Writing practice requires an active subscription.');
    expect(screen.queryByText('writing.practice.session.loadError.title')).not.toBeInTheDocument();
    expect(getWritingScenario).not.toHaveBeenCalled();
  });

  it('explains an incomplete task in candidate-safe copy', async () => {
    checkWritingScenarioEligibility.mockRejectedValue(
      new ApiError(409, 'writing_task_incomplete', 'This writing task is being updated and cannot be opened yet.', false),
    );

    render(<WritingPracticeSessionPage />);

    expect(await screen.findByText('writing.practice.session.loadError.incomplete')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'writing.practice.session.loadError.retry' })).toBeInTheDocument();
  });

  it('never treats a failed draft load as an empty draft (no editor, no save, no charge)', async () => {
    getWritingDraftV2.mockRejectedValue(new ApiError(503, 'unavailable', 'Service unavailable', true));

    render(<WritingPracticeSessionPage />);

    expect(await screen.findByText('writing.practice.session.loadError.title')).toBeInTheDocument();
    expect(screen.queryByTestId('editor-stub')).not.toBeInTheDocument();
    expect(putWritingDraftV2).not.toHaveBeenCalled();
    expect(checkWritingScenarioEligibility).not.toHaveBeenCalled();
  });
});

describe('Writing practice session — draft-first load', () => {
  it('resumes a late-arriving draft with its text and server timer, without calling eligibility', async () => {
    getWritingDraftV2.mockImplementation(
      () => new Promise((resolve) => setTimeout(() => resolve(activeDraft({ content: 'Dear Dr Green,\n\nSaved text', wordCount: 5 })), 50)),
    );

    render(<WritingPracticeSessionPage />);

    const editor = (await screen.findByTestId('editor-stub')) as HTMLTextAreaElement;
    expect(editor.value).toBe('Dear Dr Green,\n\nSaved text');
    expect(checkWritingScenarioEligibility).not.toHaveBeenCalled();
    const timer = screen.getByTestId('writing-timer');
    expect(timer).toHaveAttribute('data-phase', 'writing');
    expect(Number(timer.getAttribute('data-seconds-remaining'))).toBeGreaterThanOrEqual(1230);
    expect(Number(timer.getAttribute('data-seconds-remaining'))).toBeLessThanOrEqual(1234);
    expect(screen.getByTestId('writing-resume-banner')).toBeInTheDocument();
    expect(screen.getByTestId('writing-draft-status')).toHaveAttribute('data-state', 'saved');
    // The restored text is already on the server: nothing to save.
    expect(putWritingDraftV2).not.toHaveBeenCalled();
  });

  it('starts a new attempt on a 404: eligibility, then a create-only save that anchors the reading clock', async () => {
    render(<WritingPracticeSessionPage />);

    await waitFor(() => expect(putWritingDraftV2).toHaveBeenCalled());
    expect(checkWritingScenarioEligibility).toHaveBeenCalledTimes(1);
    expect(putWritingDraftV2).toHaveBeenCalledWith(
      'scenario-1',
      'practice',
      expect.objectContaining({
        content: '',
        expectedVersion: 0,
        phase: 'reading',
        readingSecondsRemaining: 300,
        writingSecondsRemaining: 2400,
      }),
      undefined,
    );
    expect(screen.getByTestId('writing-timer')).toHaveAttribute('data-phase', 'reading');
    expect(screen.queryByTestId('writing-resume-banner')).not.toBeInTheDocument();
  });

  it('routes a submitted attempt that is still grading to the grading page', async () => {
    getWritingDraftV2.mockResolvedValue(
      activeDraft({ status: 'submitted', submissionId: 'sub-9', submissionStatus: 'grading' }),
    );

    render(<WritingPracticeSessionPage />);

    await waitFor(() => expect(mockReplace).toHaveBeenCalledWith('/writing/submissions/sub-9/grading'));
    expect(checkWritingScenarioEligibility).not.toHaveBeenCalled();
    expect(putWritingDraftV2).not.toHaveBeenCalled();
  });

  it('"Practice this again" after a graded attempt starts a new attempt on top of the submitted version', async () => {
    getWritingDraftV2.mockResolvedValue(
      activeDraft({ status: 'submitted', submissionId: 'sub-1', submissionStatus: 'graded', version: 7 }),
    );

    render(<WritingPracticeSessionPage />);

    await waitFor(() => expect(putWritingDraftV2).toHaveBeenCalled());
    expect(checkWritingScenarioEligibility).toHaveBeenCalledTimes(1);
    expect(putWritingDraftV2.mock.calls[0][2]).toMatchObject({ content: '', expectedVersion: 7, phase: 'reading' });
    expect(((await screen.findByTestId('editor-stub')) as HTMLTextAreaElement).value).toBe('');
  });

  it('restores unsynced text from this device over the older server copy', async () => {
    localStorage.setItem(
      'oet:writing-draft:v1:anonymous:scenario-1:practice',
      JSON.stringify({
        text: 'Dear Dr Green,\n\nTyped offline',
        wordCount: 5,
        baseVersion: 3,
        phase: 'writing',
        readingSecondsRemaining: 0,
        writingSecondsRemaining: 900,
        savedAt: Date.now(),
      }),
    );
    getWritingDraftV2.mockResolvedValue(activeDraft());

    render(<WritingPracticeSessionPage />);

    const editor = (await screen.findByTestId('editor-stub')) as HTMLTextAreaElement;
    expect(editor.value).toBe('Dear Dr Green,\n\nTyped offline');
    // Timers take the smaller remaining time.
    expect(Number(screen.getByTestId('writing-timer').getAttribute('data-seconds-remaining'))).toBeLessThanOrEqual(900);
    await waitFor(() =>
      expect(putWritingDraftV2).toHaveBeenCalledWith(
        'scenario-1',
        'practice',
        expect.objectContaining({ content: 'Dear Dr Green,\n\nTyped offline', expectedVersion: 3 }),
        undefined,
      ),
    );
  });
});

describe('Writing practice session — sync engine wiring', () => {
  it('blocks Submit while offline and shows the letter as saved on this device', async () => {
    getWritingDraftV2.mockResolvedValue(activeDraft());
    render(<WritingPracticeSessionPage />);
    await screen.findByTestId('editor-stub');
    expect(screen.getByTestId('writing-submit')).toBeEnabled();

    setOnline(false);
    fireEvent.change(screen.getByTestId('editor-stub'), { target: { value: 'Dear Dr Green, offline words' } });

    expect(screen.getByTestId('writing-submit')).toBeDisabled();
    expect(screen.getByText('writing.practice.session.draft.offlineSubmit')).toBeInTheDocument();
    expect(screen.getByTestId('writing-draft-status')).toHaveAttribute('data-state', 'pending-local');

    setOnline(true);
    await waitFor(() =>
      expect(putWritingDraftV2).toHaveBeenCalledWith(
        'scenario-1',
        'practice',
        expect.objectContaining({ content: 'Dear Dr Green, offline words' }),
        undefined,
      ),
    );
    expect(screen.getByTestId('writing-submit')).toBeEnabled();
  });

  it('shows the conflict banner when another device saved different text, and can switch and restore', async () => {
    getWritingDraftV2
      .mockResolvedValueOnce(activeDraft({ content: 'Hello' }))
      .mockResolvedValueOnce(activeDraft({ content: 'Text from the other device', version: 5 }));
    putWritingDraftV2.mockRejectedValueOnce(new ApiError(409, 'draft_version_conflict', 'Conflict', false));
    render(<WritingPracticeSessionPage />);
    fireEvent.change(await screen.findByTestId('editor-stub'), { target: { value: 'Hello world' } });

    expect(await screen.findByTestId('writing-draft-conflict', {}, { timeout: 5_000 })).toBeInTheDocument();
    expect(screen.getByTestId('writing-draft-status')).toHaveAttribute('data-state', 'error');

    fireEvent.click(screen.getByRole('button', { name: 'writing.practice.session.draft.useOther' }));
    expect(((await screen.findByTestId('editor-stub')) as HTMLTextAreaElement).value).toBe('Text from the other device');
    expect(screen.queryByTestId('writing-draft-conflict')).not.toBeInTheDocument();

    fireEvent.click(screen.getByRole('button', { name: 'writing.practice.session.draft.restorePrevious' }));
    expect((screen.getByTestId('editor-stub') as HTMLTextAreaElement).value).toBe('Hello world');
    await waitFor(
      () =>
        expect(putWritingDraftV2).toHaveBeenLastCalledWith(
          'scenario-1',
          'practice',
          expect.objectContaining({ content: 'Hello world', expectedVersion: 5 }),
          undefined,
        ),
      { timeout: 5_000 },
    );
  });

  it('flushes the draft with keepalive when the page is hidden away', async () => {
    getWritingDraftV2.mockResolvedValue(activeDraft());
    render(<WritingPracticeSessionPage />);
    await screen.findByTestId('editor-stub');
    expect(putWritingDraftV2).not.toHaveBeenCalled();

    act(() => {
      window.dispatchEvent(new Event('pagehide'));
    });

    expect(putWritingDraftV2).toHaveBeenCalledWith(
      'scenario-1',
      'practice',
      expect.objectContaining({ content: 'Dear Dr Green,', expectedVersion: 3, phase: 'writing' }),
      { keepalive: true },
    );
  });
});

describe('Writing practice session — time-up auto-submit', () => {
  function expiredWritingWindow() {
    vi.useFakeTimers({ shouldAdvanceTime: true });
    getWritingDraftV2.mockResolvedValue(activeDraft({ writingSecondsRemaining: 0 }));
  }

  it('retries a network failure with backoff (same key) and on "Submit now", never in a hot loop', async () => {
    expiredWritingWindow();
    createWritingSubmission.mockRejectedValue(new ApiError(0, 'network_error', 'Unable to connect.', true));

    render(<WritingPracticeSessionPage />);

    expect(await screen.findByTestId('writing-time-up')).toHaveTextContent('writing.practice.session.timeUp.pending');
    expect(createWritingSubmission).toHaveBeenCalledTimes(1);

    await act(async () => {
      await vi.advanceTimersByTimeAsync(5_000);
    });
    expect(createWritingSubmission).toHaveBeenCalledTimes(2);
    const keys = createWritingSubmission.mock.calls.map(([payload]) => payload.idempotencyKey);
    expect(new Set(keys).size).toBe(1);

    createWritingSubmission.mockResolvedValueOnce({ id: 'sub-1' });
    fireEvent.click(await screen.findByRole('button', { name: 'writing.practice.session.timeUp.submitNow' }));

    await waitFor(() => expect(mockPush).toHaveBeenCalledWith('/writing/submissions/sub-1/grading'));
    expect(createWritingSubmission).toHaveBeenCalledTimes(3);
    expect(createWritingSubmission.mock.calls[2][0]).toMatchObject({ letterContent: 'Dear Dr Green,' });
  });

  it('stops on a credits refusal instead of retrying', async () => {
    expiredWritingWindow();
    createWritingSubmission.mockRejectedValue(new ApiError(402, 'ai_credits_insufficient', 'No credits.', false));

    render(<WritingPracticeSessionPage />);

    expect(await screen.findByRole('dialog', { name: 'No AI credits remaining' })).toBeInTheDocument();
    await act(async () => {
      await vi.advanceTimersByTimeAsync(120_000);
    });
    expect(createWritingSubmission).toHaveBeenCalledTimes(1);
    expect(screen.queryByTestId('writing-time-up')).not.toBeInTheDocument();
  });
});
