import { act, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

interface MockTimerProps {
  mode?: 'countdown' | 'elapsed';
  initialSeconds?: number;
  onComplete?: () => void;
}

interface MockWritingEditorProps {
  saveStatus?: string;
  disabled?: boolean;
  spellCheck?: boolean;
}

const fetchWritingTaskMock = vi.fn();
const fetchWritingChecklistMock = vi.fn();
const ensureWritingAttemptMock = vi.fn();
const submitWritingDraftMock = vi.fn();
const submitWritingTaskMock = vi.fn();
const fetchWritingEntitlementMock = vi.fn();
const lintWritingViaApiMock = vi.fn();
const uploadMediaMock = vi.fn();
const attachWritingPaperAssetsMock = vi.fn();
const fetchWritingPaperAssetsMock = vi.fn();
const fetchAuthorizedObjectUrlMock = vi.fn();
const analyticsTrackMock = vi.fn();
let latestTimerOnComplete: (() => void) | undefined;

beforeAll(() => {
  Object.defineProperty(window, 'matchMedia', {
    writable: true,
    value: vi.fn().mockImplementation((query: string) => ({
      matches: query.includes('prefers-reduced-motion'),
      media: query,
      onchange: null,
      addListener: vi.fn(),
      removeListener: vi.fn(),
      addEventListener: vi.fn(),
      removeEventListener: vi.fn(),
      dispatchEvent: vi.fn(),
    })),
  });
});

vi.mock('@/hooks/use-mobile', () => ({
  useIsMobile: () => true,
}));

vi.mock('@/lib/api', () => {
  class ApiError extends Error {
    status: number;
    code: string;
    retryable: boolean;
    userMessage: string;
    fieldErrors: Array<{ field: string; code: string; message: string }>;
    constructor(status: number, code: string, message: string) {
      super(message);
      this.status = status;
      this.code = code;
      this.retryable = false;
      this.userMessage = message;
      this.fieldErrors = [];
    }
  }
  return {
    fetchWritingTask: (...args: unknown[]) => fetchWritingTaskMock(...args),
    fetchWritingChecklist: (...args: unknown[]) => fetchWritingChecklistMock(...args),
    ensureWritingAttempt: (...args: unknown[]) => ensureWritingAttemptMock(...args),
    submitWritingDraft: (...args: unknown[]) => submitWritingDraftMock(...args),
    submitWritingTask: (...args: unknown[]) => submitWritingTaskMock(...args),
    completeMockSection: vi.fn(),
    fetchWritingEntitlement: (...args: unknown[]) => fetchWritingEntitlementMock(...args),
    lintWritingViaApi: (...args: unknown[]) => lintWritingViaApiMock(...args),
    uploadMedia: (...args: unknown[]) => uploadMediaMock(...args),
    attachWritingPaperAssets: (...args: unknown[]) => attachWritingPaperAssetsMock(...args),
    fetchWritingPaperAssets: (...args: unknown[]) => fetchWritingPaperAssetsMock(...args),
    fetchAuthorizedObjectUrl: (...args: unknown[]) => fetchAuthorizedObjectUrlMock(...args),
    ApiError,
  };
});

vi.mock('@/lib/analytics', () => ({
  analytics: {
    track: (...args: unknown[]) => analyticsTrackMock(...args),
  },
}));

vi.mock('@/components/domain/writing-case-notes-panel', () => ({
  WritingCaseNotesPanel: ({ activeTab, readingWindowLocked }: { activeTab?: string; readingWindowLocked?: boolean }) => (
    <div data-testid="case-notes-panel">Panel: {activeTab} locked:{String(Boolean(readingWindowLocked))}</div>
  ),
}));

vi.mock('@/components/domain/writing-editor', () => ({
  WritingEditor: ({ saveStatus, disabled, spellCheck }: MockWritingEditorProps) => (
    <div data-testid="writing-editor-shell" data-disabled={String(Boolean(disabled))} data-spellcheck={String(Boolean(spellCheck))}>
      <label htmlFor="writing-editor">Writing editor</label>
      <textarea id="writing-editor" aria-label="Writing editor" />
      <div aria-label="save-status">{saveStatus}</div>
    </div>
  ),
}));

vi.mock('@/components/ui/timer', () => ({
  Timer: ({ mode, initialSeconds, onComplete }: MockTimerProps) => {
    latestTimerOnComplete = onComplete;
    return <div data-testid="timer" data-mode={mode} data-seconds={initialSeconds} />;
  },
}));

vi.mock('@/components/ui/skeleton', () => ({
  Skeleton: ({ className }: { className?: string }) => <div className={className} data-testid="skeleton" />,
}));

import WritingPlayer from './page';
import { renderWithRouter } from '@/tests/test-utils';

describe('WritingPlayer', () => {
  afterEach(() => {
    vi.clearAllMocks();
  });

  const mockTask = {
    id: 'wt-001',
    title: 'Practice Writing Request',
    profession: 'Nursing',
    scenarioType: 'Referral',
    letterType: 'Referral',
    caseNotes: 'Patient details and case notes.',
  };

  beforeEach(() => {
    latestTimerOnComplete = undefined;
    fetchWritingTaskMock.mockResolvedValue(mockTask);
    fetchWritingChecklistMock.mockResolvedValue([
      { id: 1, text: 'Review the prompt', completed: false },
      { id: 2, text: 'Check tone and accuracy', completed: false },
    ]);
    ensureWritingAttemptMock.mockResolvedValue({
      attemptId: 'wa-1',
      contentId: 'wt-001',
      context: 'exam',
      mode: 'exam',
      state: 'in_progress',
      startedAt: new Date().toISOString(),
      draftVersion: 1,
      draftContent: '',
    });
    submitWritingDraftMock.mockResolvedValue(undefined);
    submitWritingTaskMock.mockResolvedValue({ id: 'result-1' });
    lintWritingViaApiMock.mockResolvedValue({ findings: [] });
    uploadMediaMock.mockResolvedValue({ id: 'media-paper-1' });
    attachWritingPaperAssetsMock.mockResolvedValue({ assets: [], extractedText: '', extractionState: 'empty' });
    fetchWritingPaperAssetsMock.mockResolvedValue({ assets: [], extractedText: '', extractionState: 'empty' });
    fetchAuthorizedObjectUrlMock.mockResolvedValue('blob:paper-page');
    fetchWritingEntitlementMock.mockResolvedValue({
      allowed: true,
      tier: 'premium',
      remaining: null,
      limitPerWindow: null,
      windowDays: 7,
      resetAt: null,
      reason: 'allowed',
    });
  });

  it('starts strict exam mode with reading window locks and no live rulebook assistant', async () => {
    fetchWritingTaskMock.mockResolvedValue({
      ...mockTask,
      title: 'Strict Exam Writing Request',
    });

    renderWithRouter(<WritingPlayer />, { searchParams: new URLSearchParams('taskId=wt-001&mode=exam') });

    await waitFor(() => expect(screen.getByText('Strict Exam Writing Request')).toBeInTheDocument());
    expect(ensureWritingAttemptMock).toHaveBeenCalledWith('wt-001', 'exam');
    expect(screen.getByText('Reading window — 5 minutes')).toBeInTheDocument();
    expect(screen.getAllByTestId('timer')[0]).toHaveAttribute('data-mode', 'countdown');
    expect(Number(screen.getAllByTestId('timer')[0].getAttribute('data-seconds'))).toBeGreaterThanOrEqual(299);
    expect(screen.getAllByTestId('case-notes-panel').every(panel => panel.textContent?.includes('locked:true'))).toBe(true);
    expect(screen.queryByText('Rulebook Review')).not.toBeInTheDocument();

    await userEvent.click(screen.getByRole('button', { name: /editor/i }));
    expect(screen.getAllByTestId('writing-editor-shell').every(editor => editor.getAttribute('data-disabled') === 'true')).toBe(true);
    expect(screen.getAllByTestId('writing-editor-shell').every(editor => editor.getAttribute('data-spellcheck') === 'false')).toBe(true);
  });

  it('uses the server attempt start time to resume directly into the writing phase', async () => {
    ensureWritingAttemptMock.mockResolvedValue({
      attemptId: 'wa-older',
      contentId: 'wt-001',
      context: 'exam',
      mode: 'exam',
      state: 'in_progress',
      startedAt: new Date(Date.now() - (5 * 60 + 42) * 1000).toISOString(),
      draftVersion: 1,
      draftContent: '',
    });

    renderWithRouter(<WritingPlayer />, { searchParams: new URLSearchParams('taskId=wt-001&mode=exam') });

    await waitFor(() => expect(screen.getByText('Practice Writing Request')).toBeInTheDocument());
    expect(screen.queryByText('Reading window — 5 minutes')).not.toBeInTheDocument();
    expect(Number(screen.getAllByTestId('timer')[0].getAttribute('data-seconds'))).toBeLessThan(2400);

    await userEvent.click(screen.getByRole('button', { name: /editor/i }));
    expect(screen.getAllByTestId('writing-editor-shell').every(editor => editor.getAttribute('data-disabled') === 'false')).toBe(true);
  });

  it('unlocks writing when the server-aligned reading timer completes', async () => {
    renderWithRouter(<WritingPlayer />, { searchParams: new URLSearchParams('taskId=wt-001&mode=exam') });

    await waitFor(() => expect(screen.getByText('Practice Writing Request')).toBeInTheDocument());
    expect(screen.getByText('Reading window — 5 minutes')).toBeInTheDocument();
    expect(latestTimerOnComplete).toBeTypeOf('function');

    act(() => latestTimerOnComplete?.());

    await waitFor(() => expect(screen.queryByText('Reading window — 5 minutes')).not.toBeInTheDocument());
    await userEvent.click(screen.getByRole('button', { name: /editor/i }));
    expect(screen.getAllByTestId('writing-editor-shell').every(editor => editor.getAttribute('data-disabled') === 'false')).toBe(true);
    expect(analyticsTrackMock).toHaveBeenCalledWith('writing_reading_window_ended', { taskId: 'wt-001', mode: 'exam' });
  });

  it('supports learning mode with elapsed timer, rulebook support, and one-tap submit (no confirmation modal)', async () => {
    renderWithRouter(<WritingPlayer />, { searchParams: new URLSearchParams('taskId=wt-001&mode=learning') });

    await waitFor(() => expect(screen.getByText('Practice Writing Request')).toBeInTheDocument());
    expect(ensureWritingAttemptMock).not.toHaveBeenCalled();
    expect(screen.getAllByTestId('timer')[0]).toHaveAttribute('data-mode', 'elapsed');
    expect(screen.getAllByTestId('timer')[0]).toHaveAttribute('data-seconds', '0');
    expect(screen.queryByText('Reading window — 5 minutes')).not.toBeInTheDocument();
    expect(screen.getAllByTestId('case-notes-panel').every(panel => panel.textContent?.includes('locked:false'))).toBe(true);
    expect(screen.getByRole('button', { name: /case notes/i })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /editor/i })).toBeInTheDocument();

    await userEvent.click(screen.getByRole('button', { name: /editor/i }));
    expect(screen.getAllByRole('textbox', { name: /writing editor/i }).length).toBeGreaterThan(0);
    expect(screen.getAllByTestId('writing-editor-shell').every(editor => editor.getAttribute('data-disabled') === 'false')).toBe(true);
    expect(screen.getAllByTestId('writing-editor-shell').every(editor => editor.getAttribute('data-spellcheck') === 'true')).toBe(true);
    expect(screen.getAllByText('Rulebook Review').length).toBeGreaterThan(0);

    // Per Writing Module Spec v1.0: clicking Submit calls submitWritingTask
    // directly without showing any "Submit Your Response?" confirmation modal.
    await userEvent.click(screen.getByRole('button', { name: /submit/i }));
    await waitFor(() => expect(submitWritingTaskMock).toHaveBeenCalled());
    expect(screen.queryByRole('dialog', { name: /submit your response/i })).not.toBeInTheDocument();
    expect(screen.queryByText(/word count/i)).not.toBeInTheDocument();
    expect(screen.queryByText(/likely too short/i)).not.toBeInTheDocument();
  });

  it('blocks paper-mode submission until uploaded pages have OCR text', async () => {
    renderWithRouter(<WritingPlayer />, { searchParams: new URLSearchParams('taskId=wt-001&mode=exam&examMode=paper&assessor=ai') });

    await waitFor(() => expect(screen.getByText('Practice Writing Request')).toBeInTheDocument());

    const submit = screen.getByRole('button', { name: /submit/i });
    expect(submit).toBeDisabled();
    expect(screen.getByText('Upload the handwritten response before submitting this paper-mode attempt.')).toBeInTheDocument();

    await userEvent.click(submit);
    expect(submitWritingTaskMock).not.toHaveBeenCalled();
  });
});
