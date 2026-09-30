import { act, render, screen, within } from '@testing-library/react';
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
  LearnerPageHero: ({ title }: { title: string }) => <h1>{title}</h1>,
}));

import WritingSubmissionGradingPage from './page';

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
    await act(async () => { await Promise.resolve(); });

    const scoring = screen.getByText('writing.submissions.grading.steps.scoring').closest('li');
    expect(scoring).not.toBeNull();
    expect(within(scoring!).getByText('writing.submissions.grading.status.inProgress')).toBeInTheDocument();
    expect(within(scoring!).queryByText('writing.submissions.grading.status.done')).not.toBeInTheDocument();
  });

  it('shows a failed background grade and retry without requiring a page refresh', async () => {
    getWritingSubmission.mockResolvedValueOnce({ id: 'sub-1', status: 'grading' })
      .mockResolvedValue({ id: 'sub-1', status: 'failed' });
    render(<WritingSubmissionGradingPage />);
    await act(async () => { await Promise.resolve(); });

    await act(async () => { await vi.advanceTimersByTimeAsync(5000); });

    expect(screen.getByRole('button', { name: 'writing.submissions.grading.retry' })).toBeInTheDocument();
    expect(screen.getByText('writing.submissions.grading.failedTitle')).toBeInTheDocument();
    expect(replace).not.toHaveBeenCalled();
  });
});