import { fireEvent, screen, waitFor } from '@testing-library/react';
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
  WritingCaseNotesPanel: ({ activeTab }: { activeTab?: string }) => (
    <div data-testid="case-notes-panel">Panel: {activeTab}</div>
  ),
}));

vi.mock('@/components/domain/writing-editor', () => ({
  WritingEditor: ({ saveStatus }: { saveStatus?: string }) => (
    <div>
      <label htmlFor="writing-editor">Writing editor</label>
      <textarea id="writing-editor" aria-label="Writing editor" />
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
  afterEach(() => {
    vi.clearAllMocks();
  });

  it('supports the reduced-motion learner flow and opens the submit overlay on mobile', async () => {
    fetchWritingTaskMock.mockResolvedValue({
      title: 'Practice Writing Request',
      profession: 'nursing',
      scenarioType: 'referral',
      caseNotes: 'Patient details and case notes.',
    });
    fetchWritingChecklistMock.mockResolvedValue([
      { id: 1, text: 'Review the prompt', completed: false },
      { id: 2, text: 'Check tone and accuracy', completed: false },
    ]);
    submitWritingDraftMock.mockResolvedValue(undefined);
    submitWritingTaskMock.mockResolvedValue({ id: 'result-1' });

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
});
