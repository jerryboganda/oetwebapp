import { act, fireEvent, screen, waitFor } from '@testing-library/react';
const routerPush = vi.fn();
const fetchWritingTaskMock = vi.fn();
const fetchWritingChecklistMock = vi.fn();
const resolveWritingAttemptMock = vi.fn();
const heartbeatWritingAttemptMock = vi.fn();
const submitWritingDraftMock = vi.fn();
const submitWritingTaskMock = vi.fn();
const analyticsTrackMock = vi.fn();

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

vi.mock('@/lib/api', () => ({
  ApiError: class ApiError extends Error {
    status: number;
    code: string;
    retryable: boolean;

    constructor(status: number, code: string, message: string, retryable = false) {
      super(message);
      this.name = 'ApiError';
      this.status = status;
      this.code = code;
      this.retryable = retryable;
    }
  },
  fetchWritingTask: (...args: unknown[]) => fetchWritingTaskMock(...args),
  fetchWritingChecklist: (...args: unknown[]) => fetchWritingChecklistMock(...args),
  resolveWritingAttempt: (...args: unknown[]) => resolveWritingAttemptMock(...args),
  heartbeatWritingAttempt: (...args: unknown[]) => heartbeatWritingAttemptMock(...args),
  submitWritingDraft: (...args: unknown[]) => submitWritingDraftMock(...args),
  submitWritingTask: (...args: unknown[]) => submitWritingTaskMock(...args),
}));

vi.mock('@/lib/analytics', () => ({
  analytics: {
    track: (...args: unknown[]) => analyticsTrackMock(...args),
  },
}));

vi.mock('@/components/domain/writing-case-notes-panel', () => ({
  WritingCaseNotesPanel: ({ activeTab, scratchpad, onScratchpadChange }: { activeTab?: string; scratchpad?: string; onScratchpadChange?: (value: string) => void }) => (
    <div data-testid="case-notes-panel">
      Panel: {activeTab}
      <label htmlFor="scratchpad">Scratchpad</label>
      <textarea id="scratchpad" aria-label="Scratchpad" value={scratchpad ?? ''} onChange={(event) => onScratchpadChange?.(event.target.value)} />
    </div>
  ),
}));

vi.mock('@/components/domain/writing-editor', () => ({
  WritingEditor: ({ saveStatus, value, onChange }: { saveStatus?: string; value?: string; onChange?: (value: string) => void }) => (
    <div>
      <label htmlFor="writing-editor">Writing editor</label>
      <textarea id="writing-editor" aria-label="Writing editor" value={value ?? ''} onChange={(event) => onChange?.(event.target.value)} />
      <div aria-label="save-status">{saveStatus}</div>
    </div>
  ),
}));

vi.mock('@/components/ui/timer', () => ({
  Timer: () => <div data-testid="timer" />,
}));

vi.mock('@/components/ui/skeleton', () => ({
  Skeleton: ({ className }: { className?: string }) => <div className={className} data-testid="skeleton" />,
}));

import WritingPlayer from './page';
import { renderWithRouter } from '@/tests/test-utils';

describe('WritingPlayer', () => {
  beforeEach(() => {
    fetchWritingTaskMock.mockResolvedValue({
      id: 'wt-001',
      contentId: 'wt-001',
      title: 'Practice Writing Request',
      profession: 'Medicine',
      professionId: 'medicine',
      scenarioType: 'referral',
      caseNotes: 'Patient details and case notes.',
      estimatedDurationMinutes: 40,
      durationSeconds: 2400,
    });
    fetchWritingChecklistMock.mockResolvedValue([
      { id: 1, text: 'Review the prompt', completed: false },
      { id: 2, text: 'Check tone and accuracy', completed: false },
    ]);
    resolveWritingAttemptMock.mockResolvedValue({
      attemptId: 'wa-001',
      contentId: 'wt-001',
      draftVersion: 3,
      elapsedSeconds: 120,
      content: '',
      scratchpad: '',
      checklist: {},
      state: 'in_progress',
    });
    heartbeatWritingAttemptMock.mockResolvedValue({ attemptId: 'wa-001', elapsedSeconds: 120 });
    submitWritingDraftMock.mockResolvedValue({ attemptId: 'wa-001', saved: true, draftVersion: 4, lastSavedAt: '2026-04-20T10:00:00Z' });
    submitWritingTaskMock.mockResolvedValue({ id: 'result-1' });
  });

  afterEach(() => {
    vi.clearAllMocks();
    vi.useRealTimers();
  });

  it('supports the reduced-motion learner flow and opens the submit overlay on mobile', async () => {
    renderWithRouter(<WritingPlayer />);

    await waitFor(() => expect(screen.getByText('Practice Writing Request')).toBeInTheDocument());
    expect(screen.getByRole('button', { name: /case notes/i })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /editor/i })).toBeInTheDocument();

    fireEvent.click(screen.getByRole('button', { name: /editor/i }));
    expect(screen.getAllByRole('textbox', { name: /writing editor/i }).length).toBeGreaterThan(0);

    fireEvent.click(screen.getByRole('button', { name: /submit/i }));
    expect(await screen.findByRole('dialog', { name: /submit your response/i })).toBeInTheDocument();

    fireEvent.click(screen.getByRole('button', { name: /cancel/i }));
    await waitFor(() => {
      expect(screen.queryByRole('dialog', { name: /submit your response/i })).not.toBeInTheDocument();
    });
  });

  it('hydrates an existing draft and sends draftVersion on autosave', async () => {
    resolveWritingAttemptMock.mockResolvedValueOnce({
      attemptId: 'wa-existing',
      contentId: 'wt-001',
      draftVersion: 7,
      elapsedSeconds: 180,
      content: 'Existing saved letter',
      scratchpad: 'Important notes',
      checklist: { '1': true },
      state: 'in_progress',
    });
    submitWritingDraftMock.mockResolvedValueOnce({ attemptId: 'wa-existing', saved: true, draftVersion: 8, lastSavedAt: '2026-04-20T10:02:00Z' });

    renderWithRouter(<WritingPlayer />);

    await waitFor(() => expect(screen.getByText('Practice Writing Request')).toBeInTheDocument());
    vi.useFakeTimers();
    fireEvent.click(screen.getByRole('button', { name: /editor/i }));

    const editor = screen.getAllByRole('textbox', { name: /writing editor/i })[0];
    expect(editor).toHaveValue('Existing saved letter');

    fireEvent.change(editor, { target: { value: 'Existing saved letter updated' } });
    await act(async () => {
      vi.advanceTimersByTime(3000);
    });
    vi.useRealTimers();

    await waitFor(() => {
      expect(submitWritingDraftMock).toHaveBeenCalledWith('wt-001', expect.objectContaining({
        attemptId: 'wa-existing',
        content: 'Existing saved letter updated',
        scratchpad: 'Important notes',
        checklist: { '1': true, '2': false },
        draftVersion: 7,
      }));
    });
  });

  it('shows a learner-safe recovery message for stale draft conflicts', async () => {
    const ConflictError = (await import('@/lib/api')).ApiError;
    submitWritingDraftMock.mockRejectedValueOnce(new ConflictError(409, 'draft_version_conflict', 'Stale draft', false));

    renderWithRouter(<WritingPlayer />);

    await waitFor(() => expect(screen.getByText('Practice Writing Request')).toBeInTheDocument());
    vi.useFakeTimers();
    fireEvent.click(screen.getByRole('button', { name: /editor/i }));
    fireEvent.change(screen.getAllByRole('textbox', { name: /writing editor/i })[0], { target: { value: 'Conflicting update' } });

    await act(async () => {
      vi.advanceTimersByTime(3000);
    });
    vi.useRealTimers();

    expect(await screen.findByText(/This draft changed in another tab or device/i)).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /Reload saved draft/i })).toBeInTheDocument();
  });
});
