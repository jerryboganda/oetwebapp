import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import type { CriteriaDelta } from '@/lib/mock-data';

const { mockFetchWritingEntitlement, mockFetchWritingRevisionData, mockSubmitWritingRevision, mockPush, mockTrack } = vi.hoisted(() => ({
  mockFetchWritingEntitlement: vi.fn(),
  mockFetchWritingRevisionData: vi.fn(),
  mockSubmitWritingRevision: vi.fn(),
  mockPush: vi.fn(),
  mockTrack: vi.fn(),
}));

vi.mock('next/navigation', () => ({
  useSearchParams: () => new URLSearchParams('id=we-1'),
  useRouter: () => ({ push: mockPush }),
  usePathname: () => '/writing/revision',
}));

vi.mock('@/components/layout', () => ({
  LearnerDashboardShell: ({ children }: { children: React.ReactNode }) => <div>{children}</div>,
}));

vi.mock('@/components/domain', () => ({
  LearnerPageHero: ({ title, aside }: { title: string; aside?: React.ReactNode }) => (
    <header>
      <h1>{title}</h1>
      {aside}
    </header>
  ),
  LearnerSurfaceSectionHeader: ({ title, description }: { title: string; description?: string }) => (
    <div>
      <h2>{title}</h2>
      {description ? <p>{description}</p> : null}
    </div>
  ),
}));

vi.mock('@/components/domain/revision-diff-viewer', () => ({
  RevisionDiffViewer: ({ original, revised }: { original: string; revised: string }) => (
    <div data-testid="revision-diff">
      <p>{original}</p>
      <p>{revised}</p>
    </div>
  ),
}));

vi.mock('@/components/domain/writing-improvement-banner', () => ({
  WritingImprovementBanner: () => <div data-testid="improvement-banner" />,
}));

vi.mock('@/lib/analytics', () => ({ analytics: { track: mockTrack } }));

vi.mock('@/lib/api', () => ({
  fetchWritingEntitlement: mockFetchWritingEntitlement,
  fetchWritingRevisionData: mockFetchWritingRevisionData,
  isApiError: (error: unknown) => error instanceof Error && error.name === 'ApiError',
  submitWritingRevision: mockSubmitWritingRevision,
}));

import WritingRevisionMode from './page';

describe('Writing revision page', () => {
  const deltas: CriteriaDelta[] = [
    { name: 'Purpose', original: 2, revised: 3, max: 3 },
  ];

  beforeEach(() => {
    vi.clearAllMocks();
    mockFetchWritingEntitlement.mockResolvedValue({
      allowed: true,
      tier: 'premium',
      remaining: null,
      limitPerWindow: null,
      windowDays: 7,
      resetAt: null,
      reason: 'active_subscription',
    });
    mockFetchWritingRevisionData.mockResolvedValue({
      attemptId: 'wa-base',
      originalText: 'Original referral letter.',
      revisedText: 'Draft revised referral letter.',
      deltas,
      unresolvedIssues: ['Clarify the request.'],
    });
    mockSubmitWritingRevision.mockResolvedValue({ attemptId: 'wa-revision', evaluationId: 'we-revision', state: 'queued' });
  });

  it('submits edited revision content and redirects to the new evaluation', async () => {
    const user = userEvent.setup();
    render(<WritingRevisionMode />);

    const revisedText = 'Updated revised referral letter with a clearer purpose.';
    const editor = await screen.findByLabelText(/revised letter/i);
    await waitFor(() => expect((editor as HTMLTextAreaElement).value).toBe('Draft revised referral letter.'));
    await user.clear(editor);
    await user.type(editor, revisedText);
    await waitFor(() => expect((editor as HTMLTextAreaElement).value).toBe(revisedText));

    const submitButtons = screen.getAllByRole('button', { name: /submit revision/i });
    const submitButton = submitButtons[submitButtons.length - 1] as HTMLButtonElement;
    await waitFor(() => expect(submitButton.disabled).toBe(false));
    await user.click(submitButton);

    await waitFor(() => {
      expect(mockSubmitWritingRevision).toHaveBeenCalledWith('wa-base', revisedText, expect.stringMatching(/^wr-/));
    });
    expect(mockPush).toHaveBeenCalledWith('/writing/result?id=we-revision');
    expect(mockTrack).toHaveBeenCalledWith('writing_revision_submitted', expect.objectContaining({
      attemptId: 'wa-base',
      revisionAttemptId: 'wa-revision',
      evaluationId: 'we-revision',
    }));
  });

  it('blocks submission when writing entitlement is unavailable', async () => {
    const user = userEvent.setup();
    mockFetchWritingEntitlement.mockResolvedValueOnce({
      allowed: false,
      tier: 'free',
      remaining: 0,
      limitPerWindow: 3,
      windowDays: 7,
      resetAt: null,
      reason: 'premium_required',
    });

    render(<WritingRevisionMode />);

    const editor = await screen.findByLabelText(/revised letter/i);
    await user.clear(editor);
    await user.type(editor, 'A revised letter ready for grading.');

    const submitButtons = screen.getAllByRole('button', { name: /submit revision/i });
    await user.click(submitButtons[submitButtons.length - 1]);

    expect(await screen.findByText('AI grading is a premium feature')).toBeInTheDocument();
    expect(mockSubmitWritingRevision).not.toHaveBeenCalled();
    expect(mockPush).not.toHaveBeenCalled();
  });
});