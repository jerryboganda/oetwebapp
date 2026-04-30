import { screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

interface MockTimerProps {
  mode?: 'countdown' | 'elapsed';
  initialSeconds?: number;
}

interface MockWritingEditorProps {
  saveStatus?: string;
  disabled?: boolean;
  spellCheck?: boolean;
  wordCountStatus?: { label: string };
}

const fetchWritingTaskMock = vi.fn();
const fetchWritingChecklistMock = vi.fn();
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
  fetchWritingTask: (...args: unknown[]) => fetchWritingTaskMock(...args),
  fetchWritingChecklist: (...args: unknown[]) => fetchWritingChecklistMock(...args),
  submitWritingDraft: (...args: unknown[]) => submitWritingDraftMock(...args),
  submitWritingTask: (...args: unknown[]) => submitWritingTaskMock(...args),
}));

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
  WritingEditor: ({ saveStatus, disabled, spellCheck, wordCountStatus }: MockWritingEditorProps) => (
    <div data-testid="writing-editor-shell" data-disabled={String(Boolean(disabled))} data-spellcheck={String(Boolean(spellCheck))}>
      <label htmlFor="writing-editor">Writing editor</label>
      <textarea id="writing-editor" aria-label="Writing editor" />
      <div aria-label="save-status">{saveStatus}</div>
      <div aria-label="word-count-status">{wordCountStatus?.label}</div>
    </div>
  ),
}));

vi.mock('@/components/ui/timer', () => ({
  Timer: ({ mode, initialSeconds }: MockTimerProps) => <div data-testid="timer" data-mode={mode} data-seconds={initialSeconds} />,
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
    targetWordRange: { min: 180, max: 200, warningMin: 170, warningMax: 220 },
  };

  beforeEach(() => {
    fetchWritingTaskMock.mockResolvedValue(mockTask);
    fetchWritingChecklistMock.mockResolvedValue([
      { id: 1, text: 'Review the prompt', completed: false },
      { id: 2, text: 'Check tone and accuracy', completed: false },
    ]);
    submitWritingDraftMock.mockResolvedValue(undefined);
    submitWritingTaskMock.mockResolvedValue({ id: 'result-1' });
  });

  it('starts strict exam mode with reading window locks and no live rulebook assistant', async () => {
    fetchWritingTaskMock.mockResolvedValue({
      ...mockTask,
      title: 'Strict Exam Writing Request',
    });

    renderWithRouter(<WritingPlayer />, { searchParams: new URLSearchParams('taskId=wt-001&mode=exam') });

    await waitFor(() => expect(screen.getByText('Strict Exam Writing Request')).toBeInTheDocument());
    expect(screen.getByText('Reading window — 5 minutes')).toBeInTheDocument();
    expect(screen.getAllByTestId('timer')[0]).toHaveAttribute('data-mode', 'countdown');
    expect(screen.getAllByTestId('timer')[0]).toHaveAttribute('data-seconds', '300');
    expect(screen.getAllByTestId('case-notes-panel').every(panel => panel.textContent?.includes('locked:true'))).toBe(true);
    expect(screen.queryByText('Rulebook Review')).not.toBeInTheDocument();

    await userEvent.click(screen.getByRole('button', { name: /editor/i }));
    expect(screen.getAllByTestId('writing-editor-shell').every(editor => editor.getAttribute('data-disabled') === 'true')).toBe(true);
    expect(screen.getAllByTestId('writing-editor-shell').every(editor => editor.getAttribute('data-spellcheck') === 'false')).toBe(true);
  });

  it('supports learning mode with elapsed timer, rulebook support, and submit overlay', async () => {
    renderWithRouter(<WritingPlayer />, { searchParams: new URLSearchParams('taskId=wt-001&mode=learning') });

    await waitFor(() => expect(screen.getByText('Practice Writing Request')).toBeInTheDocument());
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

    await userEvent.click(screen.getByRole('button', { name: /submit/i }));
    expect(await screen.findByRole('dialog', { name: /submit your response/i })).toBeInTheDocument();
    expect(screen.getByText(/writing correction workflow/i)).toBeInTheDocument();

    await userEvent.click(screen.getByRole('button', { name: /cancel/i }));
    await waitFor(() => {
      expect(screen.queryByRole('dialog', { name: /submit your response/i })).not.toBeInTheDocument();
    });
  });
});
