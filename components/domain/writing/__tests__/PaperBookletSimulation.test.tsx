import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent, createEvent } from '@testing-library/react';
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

const ANSWER_LABEL = 'writing.paper.answerBookletLabel';
const QUESTION_BOOKLET_LABEL = 'writing.paper.questionBookletLabel';

const content: PaperBookletContent = {
  title: 'Discharge letter — Mr Brown',
  professionLabel: 'Medicine',
  caseNoteSections: [
    { heading: 'Admission', items: ['65-year-old male', 'Community-acquired pneumonia'] },
  ],
  caseNotesMarkdown: '',
  writerRole: 'You are the charge nurse on the ward.',
  todayDate: '14 March 2026',
  taskPromptMarkdown: 'Using the case notes, write a discharge letter.',
  recipient: { name: 'Dr Smith', role: 'GP', organisation: 'Parkview Clinic', address: '1 High St' },
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
    onPhaseChange: vi.fn(),
    onSubmit: vi.fn(),
    onAutosave: vi.fn(),
    ...overrides,
  };
}

describe('PaperBookletSimulation', () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it('renders the booklet with the task title and case notes', () => {
    render(<PaperBookletSimulation {...baseProps()} />);

    // The authored task title is real content (not translated).
    expect(
      screen.getByRole('heading', { name: /discharge letter — mr brown/i }),
    ).toBeInTheDocument();
    // Structured case-note content is rendered in the question booklet.
    expect(screen.getByText(/community-acquired pneumonia/i)).toBeInTheDocument();
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
});
