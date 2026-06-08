import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent, createEvent, waitFor } from '@testing-library/react';
import {
  PaperBookletSimulation,
  type PaperBookletContent,
  type PaperBookletSimulationProps,
} from '../PaperBookletSimulation';

/**
 * Vitest spec for the PAPER-mode booklet simulation (WS-F3).
 *
 * The booklet emits fire-and-forget attempt-telemetry through exam-api. Mock it
 * so no network is attempted and we can assert the paste guard fired.
 *
 * NOTE on i18n: the global next-intl mock (vitest.setup.ts) returns the message
 * KEY verbatim (mirroring the production missing-key fallback). The component
 * labels its answer textarea + booklet regions via `t('writing.paper.*')`, so in
 * tests the accessible names are those keys. We query by the keys (and the
 * stable textarea id) rather than the human English copy.
 */
const recordWritingAttemptEvent = vi.fn().mockResolvedValue(undefined);
vi.mock('@/lib/writing/exam-api', () => ({
  recordWritingAttemptEvent: (...args: unknown[]) => recordWritingAttemptEvent(...args),
}));

// ── Mock @/lib/api + pdfjs-dist for the stimulus-PDF case ────────────────────
// Copied from WritingStimulusViewer.test.tsx: the booklet's case-notes page
// embeds <WritingStimulusViewer> when a stimulus PDF is provided, which fetches
// an authenticated blob URL and renders pdf.js pages to <canvas>.
vi.mock('@/lib/api', () => ({
  fetchAuthorizedObjectUrl: vi.fn().mockResolvedValue('blob:fake-url'),
}));

vi.mock('pdfjs-dist/legacy/build/pdf.mjs', () => {
  const fakePage = {
    getViewport: vi.fn(() => ({ width: 100, height: 100 })),
    render: vi.fn(() => ({ promise: Promise.resolve() })),
  };
  const fakePdf = {
    numPages: 1,
    getPage: vi.fn().mockResolvedValue(fakePage),
  };
  return {
    GlobalWorkerOptions: { workerSrc: '' },
    version: '3.x',
    getDocument: vi.fn(() => ({ promise: Promise.resolve(fakePdf) })),
  };
});

const ANSWER_LABEL = 'writing.paper.answerBookletLabel';
const QUESTION_BOOKLET_LABEL = 'writing.paper.questionBookletLabel';

const content: PaperBookletContent = {
  title: 'Discharge letter — Mr Brown',
  professionLabel: 'Medicine',
  writerRole: 'You are the charge nurse on the ward.',
  todayDate: '14 March 2026',
  taskPromptMarkdown: 'Using the case notes, write a discharge letter.',
  fixedInstructions: ['Use letter format.', 'Do not use note form.'],
  wordGuideMin: 180,
  wordGuideMax: 200,
};

function baseProps(
  overrides: Partial<PaperBookletSimulationProps> = {},
): PaperBookletSimulationProps {
  return {
    attemptId: 'attempt-1',
    scenarioId: 'scenario-1',
    submissionId: null,
    content,
    phase: 'reading',
    readingSecondsRemaining: 300,
    writingSecondsRemaining: 2400,
    resultsHref: '/writing/submissions/sub-1/results',
    onContentChange: vi.fn(),
    onSubmit: vi.fn(),
    onAutosave: vi.fn(),
    ...overrides,
  };
}

// jsdom does not implement canvas 2D context. Stub getContext so the embedded
// PDF viewer's render path does not throw (it already guards a null ctx).
beforeEach(() => {
  vi.clearAllMocks();
  HTMLCanvasElement.prototype.getContext = vi.fn().mockReturnValue({
    clearRect: vi.fn(),
    drawImage: vi.fn(),
    fillRect: vi.fn(),
    putImageData: vi.fn(),
    createImageData: vi.fn(),
    scale: vi.fn(),
    save: vi.fn(),
    restore: vi.fn(),
    transform: vi.fn(),
    fillText: vi.fn(),
  }) as unknown as typeof HTMLCanvasElement.prototype.getContext;
});

describe('PaperBookletSimulation', () => {
  it('renders the booklet with the task title and task prompt', () => {
    render(<PaperBookletSimulation {...baseProps()} />);

    // The authored task title is real content (not translated).
    expect(
      screen.getByRole('heading', { name: /discharge letter — mr brown/i }),
    ).toBeInTheDocument();
    // The task prompt is rendered in the question booklet.
    expect(screen.getByText(/write a discharge letter/i)).toBeInTheDocument();
    expect(
      screen.getByRole('region', { name: QUESTION_BOOKLET_LABEL }),
    ).toBeInTheDocument();
  });

  it('disables the answer textarea during the reading phase', () => {
    render(<PaperBookletSimulation {...baseProps({ phase: 'reading' })} />);

    // The textarea is the booklet's only textbox; query by role + accessible
    // name (its aria-label is the i18n key under the test mock). A role query
    // dedupes the element even though it also has an associated <label>.
    const textarea = screen.getByRole('textbox', { name: ANSWER_LABEL });
    expect(textarea).toBeDisabled();
  });

  it('enables the answer textarea during the writing phase', () => {
    render(<PaperBookletSimulation {...baseProps({ phase: 'writing' })} />);

    const textarea = screen.getByRole('textbox', { name: ANSWER_LABEL });
    expect(textarea).not.toBeDisabled();
  });

  it('prevents pasting into the answer textarea during the writing phase', () => {
    render(<PaperBookletSimulation {...baseProps({ phase: 'writing' })} />);

    const textarea = screen.getByRole('textbox', { name: ANSWER_LABEL }) as HTMLTextAreaElement;

    // Build a cancelable paste event and dispatch it; the handler must call
    // preventDefault so the browser would never insert the clipboard content.
    const pasteEvent = createEvent.paste(textarea, {
      clipboardData: {
        getData: () => 'pasted contraband text',
      },
    });
    fireEvent(textarea, pasteEvent);

    expect(pasteEvent.defaultPrevented).toBe(true);
    // The value must remain empty (nothing inserted).
    expect(textarea.value).toBe('');
    // A `paste` attempt event is emitted for invigilation telemetry.
    expect(recordWritingAttemptEvent).toHaveBeenCalledWith(
      expect.objectContaining({ eventType: 'paste', mode: 'paper' }),
    );
  });

  it('renders the stimulus PDF viewer in the case-notes page and hides printed case notes when a stimulus download path is provided', async () => {
    const { fetchAuthorizedObjectUrl } = await import('@/lib/api');

    render(
      <PaperBookletSimulation
        {...baseProps({ stimulus: { downloadPath: '/v1/media/x/content' } })}
      />,
    );

    // The embedded WritingStimulusViewer fetches the authenticated blob URL with
    // the provided download path (its presence proves the viewer mounted).
    await waitFor(() => {
      expect(fetchAuthorizedObjectUrl).toHaveBeenCalledWith('/v1/media/x/content');
    });

    // The viewer's toolbar title is shown…
    expect(screen.getByText('Question paper')).toBeInTheDocument();
    // …and the printed structured case-note text is NOT rendered (PDF replaces it).
    expect(screen.queryByText(/community-acquired pneumonia/i)).not.toBeInTheDocument();
  });

  it('shows the task prompt when no stimulus download path is provided', () => {
    // Explicitly-null download path behaves like the prop being absent.
    render(<PaperBookletSimulation {...baseProps({ stimulus: { downloadPath: null } })} />);

    expect(screen.getByText(/write a discharge letter/i)).toBeInTheDocument();
    expect(screen.queryByText('Question paper')).not.toBeInTheDocument();
  });
});
