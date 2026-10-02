import { act, fireEvent, render, screen } from '@testing-library/react';
import type { WritingSubmissionDto } from '@/lib/writing/types';

const { getWritingSubmission, retryWritingGrade, push } = vi.hoisted(() => ({
  getWritingSubmission: vi.fn(),
  retryWritingGrade: vi.fn(),
  push: vi.fn(),
}));

vi.mock('next/navigation', () => ({
  useParams: () => ({ id: 'sub-1' }),
  useRouter: () => ({ push }),
}));

vi.mock('@/lib/writing/api', () => ({ getWritingSubmission, retryWritingGrade }));

vi.mock('@/components/domain/learner-surface', () => ({
  LearnerPageHero: ({ title }: { title: string }) => <h1>{title}</h1>,
}));

import WritingSubmissionDetailPage from './page';

const SUBMISSION: WritingSubmissionDto = {
  id: 'sub-1',
  userId: 'learner-1',
  scenarioId: 'scn-1',
  mode: 'practice',
  letterContent: 'Dear Dr Brown,',
  contentHash: 'hash',
  wordCount: 182,
  timeSpentSeconds: 2400,
  startedAt: '2026-10-02T09:00:00Z',
  submittedAt: '2026-10-02T09:40:00Z',
  isRevision: false,
  originalSubmissionId: null,
  status: 'failed',
  gradingTier: 'express',
  inputSource: 'editor',
  failureCode: 'grading_delayed',
  canRetry: true,
  autoRetrying: false,
  attemptCount: 1,
};

const RETRY = { name: 'writing.myWork.actions.retry' };

describe('Writing submission detail: Retry grading', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    getWritingSubmission.mockResolvedValue(SUBMISSION);
    retryWritingGrade.mockResolvedValue({ ...SUBMISSION, status: 'queued' });
  });

  it('re-grades the SAME submission exactly once when the server says canRetry, then opens its grading page', async () => {
    let finishRetry!: () => void;
    retryWritingGrade.mockReturnValue(new Promise((resolve) => { finishRetry = () => resolve({}); }));
    render(<WritingSubmissionDetailPage />);

    const retry = await screen.findByRole('button', RETRY);
    fireEvent.click(retry);
    fireEvent.click(retry);

    expect(retryWritingGrade).toHaveBeenCalledTimes(1);
    expect(retryWritingGrade).toHaveBeenCalledWith('sub-1');
    expect(screen.getByRole('button', { name: 'writing.myWork.actions.retrying' })).toBeDisabled();

    await act(async () => { finishRetry(); });

    expect(push).toHaveBeenCalledWith('/writing/submissions/sub-1/grading');
    expect(retryWritingGrade).toHaveBeenCalledTimes(1);
  });

  it('offers no Retry when the server does not allow it (non-retryable failure, or an old API without the field)', async () => {
    getWritingSubmission.mockResolvedValue({ ...SUBMISSION, failureCode: 'letter_invalid', canRetry: false });
    const { unmount } = render(<WritingSubmissionDetailPage />);
    expect(await screen.findByText('writing.submissions.detail.status.failed')).toBeInTheDocument();
    expect(screen.queryByRole('button', RETRY)).not.toBeInTheDocument();
    unmount();

    getWritingSubmission.mockResolvedValue({ ...SUBMISSION, canRetry: undefined, failureCode: undefined });
    render(<WritingSubmissionDetailPage />);
    expect(await screen.findByText('writing.submissions.detail.status.failed')).toBeInTheDocument();
    expect(screen.queryByRole('button', RETRY)).not.toBeInTheDocument();
  });

  it('shows the safe-to-leave reassurance while the server is retrying by itself, and keeps Wait for grade', async () => {
    getWritingSubmission.mockResolvedValue({ ...SUBMISSION, status: 'queued', canRetry: false, autoRetrying: true });
    render(<WritingSubmissionDetailPage />);

    expect(await screen.findByText('writing.myWork.delayed')).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'writing.submissions.detail.waitForGrade' })).toHaveAttribute('href', '/writing/submissions/sub-1/grading');
    expect(screen.queryByRole('button', RETRY)).not.toBeInTheDocument();
  });

  it('a failed retry shows a candidate-safe error and lets the learner try again', async () => {
    retryWritingGrade.mockRejectedValueOnce(Object.assign(new Error('upstream exploded'), { status: 503 }));
    render(<WritingSubmissionDetailPage />);

    fireEvent.click(await screen.findByRole('button', RETRY));

    expect(await screen.findByText('writing.myWork.error.retry')).toBeInTheDocument();
    expect(push).not.toHaveBeenCalled();
    expect(screen.getByRole('button', RETRY)).toBeEnabled();
  });
});
