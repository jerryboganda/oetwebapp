import { act, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { afterAll, afterEach, beforeAll, beforeEach, describe, expect, it, vi } from 'vitest';

/**
 * Paper session grading watch: the page keeps polling a queued/grading letter
 * for up to 60 minutes (the old 5-minute "did not complete" cut-off is gone),
 * opens the submission's real results page, and a failed grade offers Retry on
 * the SAME record (never a second submission) with translated copy.
 */

const api = vi.hoisted(() => ({
  beginWritingMockWriting: vi.fn(),
  checkWritingScenarioEligibility: vi.fn(),
  createWritingSubmission: vi.fn(),
  getWritingDraftV2: vi.fn(),
  getWritingHighlights: vi.fn(),
  getWritingMockSession: vi.fn(),
  getWritingScenario: vi.fn(),
  getWritingSubmission: vi.fn(),
  putWritingDraftV2: vi.fn(),
  putWritingHighlights: vi.fn(),
  retryWritingGrade: vi.fn(),
  submitWritingMock: vi.fn(),
}));

vi.mock('@/lib/writing/api', () => api);
vi.mock('@/lib/writing/exam-api', () => ({
  getWritingTask: vi.fn().mockResolvedValue(null),
  recordWritingAttemptEvent: vi.fn().mockResolvedValue(undefined),
}));
vi.mock('next/navigation', () => ({
  useParams: () => ({ id: 'scenario-1' }),
  useRouter: () => ({ push: vi.fn(), replace: vi.fn() }),
}));
vi.mock('@/lib/credit-feedback', () => ({ showCreditFeedback: vi.fn() }));
vi.mock('@/components/domain/writing/WritingReadingWindowOverlay', () => ({
  WritingReadingWindowOverlay: () => null,
}));
vi.mock('@/components/domain/writing/PaperBookletSimulation', () => ({
  PaperBookletSimulation: ({ onSubmit, loading }: { onSubmit: () => void; loading?: boolean }) => (
    <button type="button" disabled={loading} onClick={onSubmit}>
      stub-submit
    </button>
  ),
}));

import WritingPaperSessionPage from './page';

const SCENARIO = {
  id: 'scenario-1',
  title: 'Discharge letter',
  letterType: 'LT-DG',
  profession: 'nursing',
  subDiscipline: null,
  topics: [],
  difficulty: 3,
  caseNotesStructured: [],
  isDiagnostic: false,
  status: 'published',
  createdAt: '2026-09-01T00:00:00Z',
  updatedAt: '2026-09-01T00:00:00Z',
};

const assign = vi.fn();
let originalLocation: Location;

beforeAll(() => {
  originalLocation = window.location;
  Object.defineProperty(window, 'location', {
    configurable: true,
    writable: true,
    value: { assign, pathname: '/writing/paper/session/scenario-1', search: '' } as unknown as Location,
  });
});

afterAll(() => {
  Object.defineProperty(window, 'location', { configurable: true, writable: true, value: originalLocation });
});

beforeEach(() => {
  vi.useFakeTimers({ shouldAdvanceTime: true });
  vi.clearAllMocks();
  localStorage.clear();
  api.getWritingMockSession.mockRejectedValue(new Error('not a mock session'));
  api.checkWritingScenarioEligibility.mockResolvedValue({ feedbackMessage: null });
  api.getWritingScenario.mockResolvedValue(SCENARIO);
  api.getWritingDraftV2.mockResolvedValue(null);
  api.getWritingHighlights.mockResolvedValue(null);
  api.createWritingSubmission.mockResolvedValue({ id: 'sub-1' });
});

afterEach(() => {
  vi.useRealTimers();
});

async function submitLetter() {
  render(<WritingPaperSessionPage />);
  const submit = await screen.findByRole('button', { name: 'stub-submit' });
  await waitFor(() => expect(submit).toBeEnabled());
  fireEvent.click(submit);
  expect(await screen.findByTestId('writing-paper-grading')).toBeInTheDocument();
}

async function advance(ms: number) {
  await act(async () => {
    await vi.advanceTimersByTimeAsync(ms);
  });
}

describe('Paper session direct-launch clock', () => {
  it('saves the remaining reading/writing seconds with the draft heartbeat', async () => {
    api.putWritingDraftV2.mockResolvedValue({ content: '', wordCount: 0, version: 1 });
    render(<WritingPaperSessionPage />);
    await waitFor(() => expect(screen.getByRole('button', { name: 'stub-submit' })).toBeEnabled());

    await advance(10_000);

    expect(api.putWritingDraftV2).toHaveBeenCalledWith(
      'scenario-1',
      'mock',
      expect.objectContaining({ phase: 'reading', writingSecondsRemaining: 2400 }),
      undefined,
    );
    const reading = api.putWritingDraftV2.mock.calls[0][2].readingSecondsRemaining as number;
    expect(reading).toBeGreaterThan(280);
    expect(reading).toBeLessThanOrEqual(300);
  });
});

describe('Paper session grading watch', () => {
  it('keeps watching past the old 5-minute cut-off and opens the submission results when graded', async () => {
    api.getWritingSubmission.mockResolvedValue({ id: 'sub-1', status: 'grading' });
    await submitLetter();

    await advance(6 * 60_000);
    expect(screen.getByTestId('writing-paper-grading')).toHaveAttribute('data-state', 'grading');
    expect(screen.queryByTestId('writing-grading-failed')).not.toBeInTheDocument();
    expect(assign).not.toHaveBeenCalled();

    api.getWritingSubmission.mockResolvedValue({ id: 'sub-1', status: 'graded' });
    await advance(20_000);
    expect(assign).toHaveBeenCalledWith('/writing/submissions/sub-1/results');
  });

  it('shows the delayed state while the server retries by itself', async () => {
    api.getWritingSubmission.mockResolvedValue({ id: 'sub-1', status: 'queued', autoRetrying: true, failureCode: 'grading_delayed' });
    await submitLetter();
    await advance(5_000);

    const overlay = screen.getByTestId('writing-paper-grading');
    expect(overlay).toHaveAttribute('data-state', 'delayed');
    expect(overlay).toHaveTextContent('writing.paper.grading.delayedTitle');
  });

  it('a failed grade offers Retry on the same record and resumes watching — no second submission', async () => {
    api.getWritingSubmission.mockResolvedValue({ id: 'sub-1', status: 'failed', canRetry: true, failureCode: 'grading_delayed' });
    api.retryWritingGrade.mockResolvedValue({ id: 'sub-1', status: 'queued' });
    await submitLetter();
    await advance(5_000);

    const failed = screen.getByTestId('writing-grading-failed');
    expect(failed).toHaveTextContent('writing.paper.grading.failedTitle');
    expect(failed).not.toHaveTextContent('Grading did not complete');

    api.getWritingSubmission.mockResolvedValue({ id: 'sub-1', status: 'grading' });
    fireEvent.click(screen.getByTestId('writing-grading-retry'));
    await advance(0);

    expect(api.retryWritingGrade).toHaveBeenCalledWith('sub-1');
    expect(await screen.findByTestId('writing-paper-grading')).toBeInTheDocument();
    expect(api.createWritingSubmission).toHaveBeenCalledTimes(1);
  });

  it('does not offer Retry when the letter cannot be graded automatically', async () => {
    api.getWritingSubmission.mockResolvedValue({ id: 'sub-1', status: 'failed', canRetry: false, failureCode: 'manual_review' });
    await submitLetter();
    await advance(5_000);

    expect(screen.getByTestId('writing-grading-failed')).toHaveTextContent('writing.paper.grading.manualTitle');
    expect(screen.queryByTestId('writing-grading-retry')).not.toBeInTheDocument();
  });
});
