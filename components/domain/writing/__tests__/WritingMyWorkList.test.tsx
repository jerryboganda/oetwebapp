import { act, fireEvent, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import type { WritingMyWorkItemDto } from '@/lib/writing/types';

const { mockGetMyWork, mockRetry } = vi.hoisted(() => ({
  mockGetMyWork: vi.fn(),
  mockRetry: vi.fn(),
}));

vi.mock('@/lib/writing/api', () => ({
  getWritingMyWork: mockGetMyWork,
  retryWritingGrade: mockRetry,
}));
// Echo the key plus its values (the global mock drops values) so counts are assertable.
vi.mock('next-intl', () => ({
  useTranslations: () => (key: string, values?: Record<string, unknown>) =>
    (values ? `${key} ${JSON.stringify(values)}` : key),
}));

import { WritingMyWorkList } from '../WritingMyWorkList';
import { renderWithRouter } from '@/tests/test-utils';

function item(overrides: Partial<WritingMyWorkItemDto> = {}): WritingMyWorkItemDto {
  return {
    key: 'submission:sub-1',
    kind: 'submission',
    state: 'graded',
    rawStatus: 'graded',
    scenarioId: 'scn-1',
    title: 'Discharge letter for Mr Smith',
    letterType: 'LT-DG',
    mode: 'practice',
    isRevision: false,
    isFreeSample: false,
    draftId: null,
    submissionId: 'sub-1',
    wordCount: 182,
    phase: null,
    readingSecondsRemaining: null,
    writingSecondsRemaining: null,
    lastActivityAt: '2026-10-02T09:30:00Z',
    canRetry: false,
    autoRetrying: false,
    actions: [{ kind: 'open_result', href: '/writing/submissions/sub-1/results' }],
    ...overrides,
  };
}

const DRAFT = item({
  key: 'draft:d-1',
  kind: 'draft',
  state: 'draft',
  rawStatus: 'active',
  scenarioId: 'scn-2',
  title: 'Referral to a physiotherapist',
  letterType: 'LT-RR',
  draftId: 'd-1',
  submissionId: null,
  wordCount: 96,
  phase: 'writing',
  writingSecondsRemaining: 1500,
  lastActivityAt: '2026-10-02T10:00:00Z',
  actions: [{ kind: 'resume', href: '/writing/practice/session/scn-2' }],
});
const FAILED = item({
  key: 'submission:sub-2',
  state: 'failed',
  rawStatus: 'failed',
  submissionId: 'sub-2',
  canRetry: true,
  lastActivityAt: '2026-10-01T08:00:00Z',
  actions: [
    { kind: 'retry', href: '/writing/submissions/sub-2/grading' },
    { kind: 'view_letter', href: '/writing/submissions/sub-2' },
  ],
});
const GRADED = item();

describe('WritingMyWorkList (Post Submissions)', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mockGetMyWork.mockResolvedValue({ items: [DRAFT, FAILED, GRADED], hasMore: false });
    mockRetry.mockResolvedValue({ id: 'sub-2', status: 'queued' });
  });

  it('renders one row per item with the harness data attributes, the real title and the server actions', async () => {
    renderWithRouter(<WritingMyWorkList />);

    const rows = await screen.findAllByTestId('post-submission-row');
    expect(rows).toHaveLength(3);
    expect(screen.getByTestId('post-submissions-list')).toBeInTheDocument();
    expect(mockGetMyWork).toHaveBeenCalledWith({ limit: 20 });

    const [draft, failed, graded] = rows;
    expect(draft).toHaveAttribute('data-state', 'draft');
    expect(draft).toHaveAttribute('data-scenario-id', 'scn-2');
    expect(draft).not.toHaveAttribute('data-submission-id');
    expect(draft).toHaveTextContent('Referral to a physiotherapist');
    expect(draft).toHaveTextContent('writing.myWork.state.draft');
    expect(draft).toHaveTextContent('writing.myWork.words {"count":96}');
    expect(draft).toHaveTextContent('writing.myWork.savedAt');
    expect(within(draft).getByTestId('post-submission-resume')).toHaveAttribute('href', '/writing/practice/session/scn-2');

    expect(failed).toHaveAttribute('data-state', 'failed');
    expect(failed).toHaveAttribute('data-submission-id', 'sub-2');
    expect(failed).toHaveTextContent('writing.myWork.state.failed');
    expect(within(failed).getByTestId('post-submission-retry')).toBeEnabled();
    expect(within(failed).getByTestId('post-submission-view')).toHaveAttribute('href', '/writing/submissions/sub-2');

    expect(graded).toHaveAttribute('data-state', 'graded');
    expect(graded).toHaveAttribute('data-submission-id', 'sub-1');
    expect(graded).toHaveTextContent('writing.myWork.submittedAt');
    expect(within(graded).getByTestId('post-submission-open')).toHaveAttribute('href', '/writing/submissions/sub-1/results');
    // The letter type reads as its label and the row never shows a raw id.
    expect(graded).toHaveTextContent('writing.practice.library.letterType.LT-DG');
    expect(graded).not.toHaveTextContent('sub-1');
  });

  it('shows a queued auto-retrying letter as Grading with the safe-to-leave reassurance', async () => {
    mockGetMyWork.mockResolvedValue({
      items: [
        item({ key: 'submission:sub-3', state: 'grading', rawStatus: 'queued', submissionId: 'sub-3', autoRetrying: true, actions: [{ kind: 'wait', href: '/writing/submissions/sub-3/grading' }] }),
        item({ key: 'submission:sub-4', state: 'grading', rawStatus: 'grading', submissionId: 'sub-4', actions: [{ kind: 'wait', href: '/writing/submissions/sub-4/grading' }] }),
      ],
      hasMore: false,
    });
    renderWithRouter(<WritingMyWorkList />);

    const [queued, grading] = await screen.findAllByTestId('post-submission-row');
    expect(queued).toHaveAttribute('data-state', 'grading');
    expect(queued).toHaveTextContent('writing.myWork.state.grading');
    expect(queued).toHaveTextContent('writing.myWork.delayed');
    expect(within(queued).getByTestId('post-submission-wait')).toHaveAttribute('href', '/writing/submissions/sub-3/grading');
    expect(grading).not.toHaveTextContent('writing.myWork.delayed');
  });

  it('Retry grading re-grades the SAME submission exactly once, then opens its grading page', async () => {
    let finishRetry!: () => void;
    mockRetry.mockReturnValue(new Promise((resolve) => { finishRetry = () => resolve({}); }));
    const push = vi.fn();
    renderWithRouter(<WritingMyWorkList />, { router: { push } });

    const retry = await screen.findByTestId('post-submission-retry');
    fireEvent.click(retry);
    fireEvent.click(retry);

    expect(mockRetry).toHaveBeenCalledTimes(1);
    expect(mockRetry).toHaveBeenCalledWith('sub-2');
    expect(retry).toBeDisabled();
    expect(retry).toHaveTextContent('writing.myWork.actions.retrying');
    expect(push).not.toHaveBeenCalled();

    await act(async () => { finishRetry(); });

    expect(push).toHaveBeenCalledWith('/writing/submissions/sub-2/grading');
    expect(mockRetry).toHaveBeenCalledTimes(1);
  });

  it('a failed retry shows a candidate-safe message on that row and allows a deliberate second try', async () => {
    const user = userEvent.setup();
    mockRetry.mockRejectedValueOnce(Object.assign(new Error('upstream exploded'), { status: 503 }));
    const push = vi.fn();
    renderWithRouter(<WritingMyWorkList />, { router: { push } });

    const retry = await screen.findByTestId('post-submission-retry');
    await user.click(retry);

    const row = retry.closest('li') as HTMLElement;
    expect(await within(row).findByRole('alert')).toHaveTextContent('writing.myWork.error.retry');
    expect(push).not.toHaveBeenCalled();
    expect(retry).toBeEnabled();

    await user.click(retry);
    expect(mockRetry).toHaveBeenCalledTimes(2);
    await waitFor(() => expect(push).toHaveBeenCalledWith('/writing/submissions/sub-2/grading'));
  });

  it('pages with Load more using the last row as the keyset cursor, without duplicating a boundary row', async () => {
    const user = userEvent.setup();
    mockGetMyWork
      .mockResolvedValueOnce({ items: [DRAFT, GRADED], hasMore: true })
      .mockResolvedValueOnce({ items: [GRADED, FAILED], hasMore: false });
    renderWithRouter(<WritingMyWorkList />);

    await user.click(await screen.findByRole('button', { name: 'writing.myWork.loadMore' }));

    await waitFor(() => expect(screen.getAllByTestId('post-submission-row')).toHaveLength(3));
    expect(mockGetMyWork).toHaveBeenLastCalledWith({ limit: 20, before: GRADED.lastActivityAt });
    expect(screen.queryByRole('button', { name: 'writing.myWork.loadMore' })).not.toBeInTheDocument();
  });

  it('keeps the loaded rows and offers Load more again when the next page fails', async () => {
    const user = userEvent.setup();
    mockGetMyWork
      .mockResolvedValueOnce({ items: [GRADED], hasMore: true })
      .mockRejectedValueOnce(new Error('offline'));
    renderWithRouter(<WritingMyWorkList />);

    await user.click(await screen.findByRole('button', { name: 'writing.myWork.loadMore' }));

    expect(await screen.findByText('writing.myWork.error.loadMore')).toBeInTheDocument();
    expect(screen.getAllByTestId('post-submission-row')).toHaveLength(1);
    expect(screen.getByRole('button', { name: 'writing.myWork.loadMore' })).toBeEnabled();
  });

  it('a failed load shows an honest error with Try again, never an empty list', async () => {
    const user = userEvent.setup();
    mockGetMyWork
      .mockRejectedValueOnce(new Error('offline'))
      .mockResolvedValueOnce({ items: [GRADED], hasMore: false });
    const onCountChange = vi.fn();
    renderWithRouter(<WritingMyWorkList onCountChange={onCountChange} />);

    expect(await screen.findByText('writing.myWork.error.load')).toBeInTheDocument();
    expect(screen.queryByTestId('post-submissions-list')).not.toBeInTheDocument();
    expect(onCountChange).not.toHaveBeenCalledWith(0);

    await user.click(screen.getByRole('button', { name: 'writing.myWork.tryAgain' }));

    expect(await screen.findAllByTestId('post-submission-row')).toHaveLength(1);
    expect(onCountChange).toHaveBeenLastCalledWith(1);
  });

  it('renders nothing for an account with no drafts or letters and reports zero', async () => {
    mockGetMyWork.mockResolvedValue({ items: [], hasMore: false });
    const onCountChange = vi.fn();
    const { container } = renderWithRouter(<WritingMyWorkList onCountChange={onCountChange} />);

    await waitFor(() => expect(onCountChange).toHaveBeenLastCalledWith(0));
    expect(container).toBeEmptyDOMElement();
  });
});
