import { act, fireEvent, render, screen } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

const { getWritingSubmission, getWritingSubmissionGrade, retryWritingGrade, replace, close, translate, stream } = vi.hoisted(() => ({
  getWritingSubmission: vi.fn(),
  getWritingSubmissionGrade: vi.fn(),
  retryWritingGrade: vi.fn(),
  replace: vi.fn(),
  close: vi.fn(),
  translate: (key: string) => key,
  stream: { onGradeReady: null as null | (() => void) },
}));

const router = { replace };

vi.mock('next/navigation', () => ({
  useParams: () => ({ id: 'sub-1' }),
  useRouter: () => router,
}));

vi.mock('next-intl', () => ({ useTranslations: () => translate }));

vi.mock('@/lib/writing/api', () => ({
  getWritingSubmission,
  getWritingSubmissionGrade,
  retryWritingGrade,
}));

vi.mock('@/lib/writing/realtime', () => ({
  connectWritingSubmissionStream: (_id: string, handlers: { onGradeReady: () => void }) => {
    stream.onGradeReady = handlers.onGradeReady;
    return { close };
  },
}));

vi.mock('@/components/domain/learner-surface', () => ({
  LearnerPageHero: ({ title, description, highlights }: { title: string; description?: string; highlights?: { label: string; value: string }[] }) => (
    <header>
      <h1>{title}</h1>
      {description ? <p data-testid="hero-description">{description}</p> : null}
      {highlights?.map((h) => <p key={h.label} data-testid="hero-highlight">{h.value}</p>)}
    </header>
  ),
}));

import WritingSubmissionGradingPage from './page';

async function flush() {
  await act(async () => {
    for (let i = 0; i < 5; i += 1) await Promise.resolve();
  });
}

// A submission inside the 15-minute window, as the server reports it.
function windowed(releaseState: 'processing' | 'held', secondsLeft: number, status = 'grading') {
  const now = Date.now();
  return {
    id: 'sub-1',
    status,
    releaseState,
    releaseAt: new Date(now + secondsLeft * 1000).toISOString(),
    serverNow: new Date(now).toISOString(),
  };
}

describe('Writing grading progress and failure recovery', () => {
  beforeEach(() => {
    vi.useFakeTimers();
    vi.clearAllMocks();
    stream.onGradeReady = null;
    getWritingSubmission.mockResolvedValue({ id: 'sub-1', status: 'grading' });
    getWritingSubmissionGrade.mockRejectedValue(new Error('Grade not ready'));
  });

  afterEach(() => {
    vi.useRealTimers();
  });

  it('shows the 15:00 countdown and the exact release notice, never the internal pipeline steps', async () => {
    getWritingSubmission.mockResolvedValue(windowed('processing', 900, 'queued'));
    render(<WritingSubmissionGradingPage />);
    await flush();

    expect(screen.getByRole('timer')).toHaveTextContent('15:00');
    expect(screen.getByText('writing.release.notice')).toBeInTheDocument();
    expect(screen.queryByTestId('hero-description')).not.toBeInTheDocument();
    expect(screen.getByText('writing.release.savedNote')).toBeInTheDocument();
    expect(screen.queryByTestId('writing-grading-steps')).not.toBeInTheDocument();
    expect(screen.getByTestId('hero-highlight')).toHaveTextContent('writing.submissions.detail.status.queued');
  });

  it('does not promise 15 minutes to an account without a hold, and shows no countdown', async () => {
    getWritingSubmission.mockResolvedValue({ id: 'sub-1', status: 'grading', releaseState: 'processing', releaseAt: null });
    render(<WritingSubmissionGradingPage />);
    await flush();

    expect(screen.queryByRole('timer')).not.toBeInTheDocument();
    expect(screen.getByTestId('hero-description')).toHaveTextContent('writing.release.stillProcessing');
  });

  it('keeps a finished but held result hidden, then opens it once the server releases it', async () => {
    getWritingSubmission.mockResolvedValue(windowed('held', 600));
    render(<WritingSubmissionGradingPage />);
    await flush();

    // A grade-ready push while held is only a nudge: refetch, no redirect.
    await act(async () => { stream.onGradeReady?.(); });
    await flush();
    expect(replace).not.toHaveBeenCalled();
    expect(screen.getByRole('timer')).toBeInTheDocument();

    getWritingSubmission.mockResolvedValue({ id: 'sub-1', status: 'graded', releaseState: 'released' });
    await act(async () => { stream.onGradeReady?.(); });
    await flush();
    expect(replace).toHaveBeenCalledWith('/writing/submissions/sub-1/results');
  });

  it('refetches once when a held countdown reaches zero, and shows finalising rather than a result', async () => {
    getWritingSubmission.mockResolvedValue(windowed('held', 3));
    render(<WritingSubmissionGradingPage />);
    await flush();
    const callsBefore = getWritingSubmission.mock.calls.length;

    await act(async () => { await vi.advanceTimersByTimeAsync(3100); });

    expect(getWritingSubmission.mock.calls.length).toBeGreaterThan(callsBefore);
    expect(replace).not.toHaveBeenCalled();
    expect(screen.queryByRole('timer')).not.toBeInTheDocument();
    expect(screen.getByText('writing.release.finalising')).toBeInTheDocument();
  });

  it('a letter still processing past the window says it is still being assessed (never finalising) and leaves the refetching to the normal poll', async () => {
    getWritingSubmission.mockResolvedValue(windowed('processing', 1));
    render(<WritingSubmissionGradingPage />);
    await flush();
    const callsBefore = getWritingSubmission.mock.calls.length;

    await act(async () => { await vi.advanceTimersByTimeAsync(1500); });

    expect(getWritingSubmission.mock.calls.length).toBe(callsBefore);
    expect(screen.getByText('writing.release.stillProcessing')).toBeInTheDocument();
    expect(screen.queryByText('writing.release.finalising')).not.toBeInTheDocument();
    expect(replace).not.toHaveBeenCalled();
  });

  it('shows a failed background grade and retry without requiring a page refresh', async () => {
    getWritingSubmission.mockResolvedValueOnce({ id: 'sub-1', status: 'grading' })
      .mockResolvedValue({ id: 'sub-1', status: 'failed' });
    render(<WritingSubmissionGradingPage />);
    await flush();

    await act(async () => { await vi.advanceTimersByTimeAsync(5000); });

    expect(screen.getByTestId('writing-grading-failed')).toBeInTheDocument();
    expect(screen.getByTestId('writing-grading-retry')).toHaveTextContent('writing.submissions.grading.retry');
    expect(screen.getByText('writing.submissions.grading.failedTitle')).toBeInTheDocument();
    expect(replace).not.toHaveBeenCalled();
  });

  it('Retry re-grades the same submission and opens the result once it is graded', async () => {
    getWritingSubmission.mockResolvedValueOnce({ id: 'sub-1', status: 'failed', canRetry: true, failureCode: 'grading_delayed' })
      .mockResolvedValue({ id: 'sub-1', status: 'graded' });
    retryWritingGrade.mockResolvedValue({ id: 'sub-1', status: 'queued' });
    render(<WritingSubmissionGradingPage />);
    await flush();

    fireEvent.click(screen.getByTestId('writing-grading-retry'));
    await flush();

    expect(retryWritingGrade).toHaveBeenCalledTimes(1);
    expect(retryWritingGrade).toHaveBeenCalledWith('sub-1');
    expect(replace).toHaveBeenCalledWith('/writing/submissions/sub-1/results');
  });

  it('while the server retries by itself, says the letter is saved and offers no Retry', async () => {
    getWritingSubmission.mockResolvedValue({
      id: 'sub-1', status: 'queued', autoRetrying: true, canRetry: false, failureCode: 'grading_delayed',
    });
    render(<WritingSubmissionGradingPage />);
    await flush();

    expect(screen.getByText('writing.submissions.grading.delayedDescription')).toBeInTheDocument();
    expect(screen.queryByTestId('writing-grading-retry')).not.toBeInTheDocument();
    expect(screen.queryByTestId('writing-grading-failed')).not.toBeInTheDocument();
    expect(screen.getByText('writing.release.savedNote')).toBeInTheDocument();
  });

  it('a credits refusal links to AI credits and keeps Retry for after the top-up', async () => {
    getWritingSubmission.mockResolvedValue({
      id: 'sub-1', status: 'failed', canRetry: true, failureCode: 'credits_insufficient',
    });
    render(<WritingSubmissionGradingPage />);
    await flush();

    expect(screen.getByText('writing.submissions.grading.creditsTitle')).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'writing.submissions.grading.buyCredits' })).toHaveAttribute('href', '/ai-packages');
    expect(screen.getByTestId('writing-grading-retry')).toBeInTheDocument();
  });

  it.each([
    ['task_not_ready', 'writing.submissions.grading.notGradable.taskNotReady'],
    ['manual_review', 'writing.submissions.grading.notGradable.manualReview'],
    ['letter_invalid', 'writing.submissions.grading.notGradable.letterInvalid'],
  ])('a %s failure explains itself and offers no Retry', async (failureCode, message) => {
    getWritingSubmission.mockResolvedValue({ id: 'sub-1', status: 'failed', canRetry: false, failureCode });
    render(<WritingSubmissionGradingPage />);
    await flush();

    expect(screen.getByTestId('writing-grading-failed')).toHaveTextContent(message);
    expect(screen.queryByTestId('writing-grading-retry')).not.toBeInTheDocument();
  });
});
