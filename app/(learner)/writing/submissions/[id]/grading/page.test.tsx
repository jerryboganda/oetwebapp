import { act, fireEvent, render, screen, within } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

const { getWritingSubmission, getWritingSubmissionGrade, retryWritingGrade, replace, close, translate } = vi.hoisted(() => ({
  getWritingSubmission: vi.fn(),
  getWritingSubmissionGrade: vi.fn(),
  retryWritingGrade: vi.fn(),
  replace: vi.fn(),
  close: vi.fn(),
  translate: (key: string) => key,
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
  connectWritingSubmissionStream: () => ({ close }),
}));

vi.mock('@/components/domain/learner-surface', () => ({
  LearnerPageHero: ({ title, highlights }: { title: string; highlights?: { label: string; value: string }[] }) => (
    <header>
      <h1>{title}</h1>
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

describe('Writing grading progress and failure recovery', () => {
  beforeEach(() => {
    vi.useFakeTimers();
    vi.clearAllMocks();
    getWritingSubmission.mockResolvedValue({ id: 'sub-1', status: 'grading' });
    getWritingSubmissionGrade.mockRejectedValue(new Error('Grade not ready'));
  });

  afterEach(() => {
    vi.useRealTimers();
  });

  it('keeps scoring active while the server is grading instead of claiming the rubric is done', async () => {
    render(<WritingSubmissionGradingPage />);
    await flush();

    const scoring = screen.getByText('writing.submissions.grading.steps.scoring').closest('li');
    expect(scoring).not.toBeNull();
    expect(within(scoring!).getByText('writing.submissions.grading.status.inProgress')).toBeInTheDocument();
    expect(within(scoring!).queryByText('writing.submissions.grading.status.done')).not.toBeInTheDocument();
  });

  it('shows the "Preparing model answer" step and a translated status, never the raw token', async () => {
    render(<WritingSubmissionGradingPage />);
    await flush();

    const steps = screen.getByTestId('writing-grading-steps');
    expect(within(steps).getByText('writing.submissions.grading.steps.modelAnswer')).toBeInTheDocument();
    expect(within(steps).queryByText(/exemplar/)).not.toBeInTheDocument();
    expect(screen.getByTestId('hero-highlight')).toHaveTextContent('writing.submissions.detail.status.grading');
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
    expect(screen.getByTestId('writing-grading-steps')).toBeInTheDocument();
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
