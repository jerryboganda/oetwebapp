import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import type { ListeningAuthoredQuestion } from '@/lib/listening-authoring-api';

const {
  mockReplaceListeningStructure,
  mockGetListeningExtracts,
  mockSetListeningExtractContext,
} = vi.hoisted(() => ({
  mockReplaceListeningStructure: vi.fn(),
  mockGetListeningExtracts: vi.fn(),
  mockSetListeningExtractContext: vi.fn(),
}));

vi.mock('@/lib/listening-authoring-api', () => ({
  replaceListeningStructure: mockReplaceListeningStructure,
  getListeningExtracts: mockGetListeningExtracts,
  setListeningExtractContext: mockSetListeningExtractContext,
}));

import { ListeningAnswerSheetBuilder } from './ListeningAnswerSheetBuilder';

const noop = () => {};

function mcq(number: number, partCode: string, correctAnswer = 'A'): ListeningAuthoredQuestion {
  return {
    id: `lq-${number}`,
    number,
    partCode: partCode as ListeningAuthoredQuestion['partCode'],
    type: 'multiple_choice_3',
    stem: 'See PDF',
    options: ['Option A', 'Option B', 'Option C'],
    correctAnswer,
    acceptedAnswers: [],
    explanation: null,
    skillTag: null,
    transcriptExcerpt: null,
    distractorExplanation: null,
    points: 1,
  };
}

/** The questions array passed to replaceListeningStructure on the latest call. */
function savedQuestions(): ListeningAuthoredQuestion[] {
  const calls = mockReplaceListeningStructure.mock.calls;
  return calls[calls.length - 1][1] as ListeningAuthoredQuestion[];
}

beforeEach(() => {
  vi.clearAllMocks();
  // Echo the saved list back as the new structure so onSaved gets it.
  mockReplaceListeningStructure.mockImplementation((_paperId: string, questions: ListeningAuthoredQuestion[]) =>
    Promise.resolve({ questions, counts: { partACount: 0, partBCount: 0, partCCount: 0, totalItems: questions.length } }),
  );
  mockGetListeningExtracts.mockResolvedValue({ extracts: [] });
  mockSetListeningExtractContext.mockResolvedValue({ extracts: [] });
});

describe('ListeningAnswerSheetBuilder', () => {
  it('generates one MCQ-3 item for a Part B section at its printed number (B3 → 27)', async () => {
    const user = userEvent.setup();
    render(
      <ListeningAnswerSheetBuilder
        paperId="paper-1"
        partCode="B"
        activeSection="B3"
        allQuestions={[]}
        onSaved={noop}
        onNotify={noop}
      />,
    );

    await user.click(screen.getByRole('button', { name: /generate/i }));
    await user.selectOptions(screen.getByRole('combobox'), 'A');
    await user.click(screen.getByRole('button', { name: /save all/i }));

    expect(mockReplaceListeningStructure).toHaveBeenCalledTimes(1);
    const saved = savedQuestions();
    expect(saved).toHaveLength(1);
    expect(saved[0]).toMatchObject({
      number: 27,
      partCode: 'B3',
      type: 'multiple_choice_3',
      stem: 'See PDF',
      options: ['Option A', 'Option B', 'Option C'],
      correctAnswer: 'A',
    });
  });

  it('generates six MCQ-3 items for Part C section C2 at numbers 37..42 (3 options)', async () => {
    const user = userEvent.setup();
    render(
      <ListeningAnswerSheetBuilder
        paperId="paper-1"
        partCode="C"
        activeSection="C2"
        allQuestions={[]}
        onSaved={noop}
        onNotify={noop}
      />,
    );

    await user.click(screen.getByRole('button', { name: /generate/i }));
    for (const select of screen.getAllByRole('combobox')) {
      await user.selectOptions(select, 'B');
    }
    await user.click(screen.getByRole('button', { name: /save all/i }));

    const saved = savedQuestions().sort((a, b) => a.number - b.number);
    expect(saved.map((q) => q.number)).toEqual([37, 38, 39, 40, 41, 42]);
    expect(saved.every((q) => q.type === 'multiple_choice_3' && q.options.length === 3)).toBe(true);
    expect(saved.every((q) => q.correctAnswer === 'B')).toBe(true);
  });

  it('preserves questions from other sub-sections when saving (merge, not replace)', async () => {
    const user = userEvent.setup();
    const b1 = mcq(25, 'B1', 'C');
    render(
      <ListeningAnswerSheetBuilder
        paperId="paper-1"
        partCode="B"
        activeSection="B3"
        allQuestions={[b1]}
        onSaved={noop}
        onNotify={noop}
      />,
    );

    await user.click(screen.getByRole('button', { name: /generate/i }));
    await user.selectOptions(screen.getByRole('combobox'), 'A');
    await user.click(screen.getByRole('button', { name: /save all/i }));

    const saved = savedQuestions();
    // B1 (25) preserved untouched + new B3 (27).
    expect(saved.map((q) => q.number).sort((a, b) => a - b)).toEqual([25, 27]);
    expect(saved.find((q) => q.number === 25)).toMatchObject({ partCode: 'B1', correctAnswer: 'C' });
  });

  it('blocks save and notifies when a row has no correct answer', async () => {
    const user = userEvent.setup();
    const onNotify = vi.fn();
    render(
      <ListeningAnswerSheetBuilder
        paperId="paper-1"
        partCode="C"
        activeSection="C1"
        allQuestions={[]}
        onSaved={noop}
        onNotify={onNotify}
      />,
    );

    await user.click(screen.getByRole('button', { name: /generate/i }));
    await user.click(screen.getByRole('button', { name: /save all/i }));

    expect(mockReplaceListeningStructure).not.toHaveBeenCalled();
    expect(onNotify).toHaveBeenCalledWith('error', expect.stringMatching(/correct answer/i));
  });

  it('seeds rows from existing questions and updates them in place (idempotent)', async () => {
    const user = userEvent.setup();
    const existing = mcq(27, 'B3', 'B');
    render(
      <ListeningAnswerSheetBuilder
        paperId="paper-1"
        partCode="B"
        activeSection="B3"
        allQuestions={[existing]}
        onSaved={noop}
        onNotify={noop}
      />,
    );

    // Seeded immediately (no Generate needed) with the existing correct answer.
    await user.click(screen.getByRole('button', { name: /save all/i }));

    const saved = savedQuestions();
    expect(saved).toHaveLength(1);
    expect(saved[0]).toMatchObject({ id: 'lq-27', number: 27, correctAnswer: 'B' });
  });

  it('saves an authored rationale as the question explanation', async () => {
    const user = userEvent.setup();
    render(
      <ListeningAnswerSheetBuilder
        paperId="paper-1"
        partCode="B"
        activeSection="B3"
        allQuestions={[]}
        onSaved={noop}
        onNotify={noop}
      />,
    );

    await user.click(screen.getByRole('button', { name: /generate/i }));
    await user.selectOptions(screen.getByRole('combobox'), 'A');
    await user.type(screen.getByLabelText(/rationale for question 27/i), 'The speaker confirms option A.');
    await user.click(screen.getByRole('button', { name: /save all/i }));

    expect(savedQuestions()[0]).toMatchObject({ explanation: 'The speaker confirms option A.' });
  });

  it('blocks the builder when a section holds non-MCQ (advanced-authored) items', async () => {
    const freeText: ListeningAuthoredQuestion = { ...mcq(27, 'B3'), type: 'short_answer', options: [] };
    render(
      <ListeningAnswerSheetBuilder
        paperId="paper-1"
        partCode="B"
        activeSection="B3"
        allQuestions={[freeText]}
        onSaved={noop}
        onNotify={noop}
      />,
    );

    expect(screen.getByText(/advanced editor/i)).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /save all/i })).not.toBeInTheDocument();
  });

  it('saves the typed stem and option texts inline (no See PDF placeholder)', async () => {
    const user = userEvent.setup();
    render(
      <ListeningAnswerSheetBuilder
        paperId="paper-1"
        partCode="B"
        activeSection="B3"
        allQuestions={[]}
        onSaved={noop}
        onNotify={noop}
      />,
    );

    await user.click(screen.getByRole('button', { name: /generate/i }));
    await user.type(screen.getByLabelText(/question 27 stem/i), 'What does the nurse advise about the dose?');
    await user.type(screen.getByLabelText(/question 27 option a/i), 'Increase the dose');
    await user.type(screen.getByLabelText(/question 27 option b/i), 'Reduce the dose');
    await user.type(screen.getByLabelText(/question 27 option c/i), 'Keep the dose unchanged');
    await user.selectOptions(screen.getByRole('combobox'), 'B');
    await user.click(screen.getByRole('button', { name: /save all/i }));

    expect(savedQuestions()[0]).toMatchObject({
      number: 27,
      stem: 'What does the nurse advise about the dose?',
      options: ['Increase the dose', 'Reduce the dose', 'Keep the dose unchanged'],
      correctAnswer: 'B',
    });
  });

  it('persists the per-extract scenario/context line for the section', async () => {
    const user = userEvent.setup();
    render(
      <ListeningAnswerSheetBuilder
        paperId="paper-1"
        partCode="B"
        activeSection="B3"
        allQuestions={[]}
        onSaved={noop}
        onNotify={noop}
      />,
    );

    await user.click(screen.getByRole('button', { name: /generate/i }));
    await user.type(screen.getByLabelText(/scenario or context/i), 'You hear a charge nurse briefing a colleague.');
    await user.selectOptions(screen.getByRole('combobox'), 'A');
    await user.click(screen.getByRole('button', { name: /save all/i }));

    expect(mockSetListeningExtractContext).toHaveBeenCalledWith(
      'paper-1',
      'B3',
      'You hear a charge nurse briefing a colleague.',
    );
  });

  it('preserves real authored stem + options on re-save (no clobber back to See PDF)', async () => {
    const user = userEvent.setup();
    const existing: ListeningAuthoredQuestion = {
      ...mcq(27, 'B3', 'B'),
      stem: 'What does the nurse advise?',
      options: ['Increase fluids', 'Reduce the dose', 'Refer on'],
    };
    render(
      <ListeningAnswerSheetBuilder
        paperId="paper-1"
        partCode="B"
        activeSection="B3"
        allQuestions={[existing]}
        onSaved={noop}
        onNotify={noop}
      />,
    );

    // Seeded from the existing question — save without touching any field.
    await user.click(screen.getByRole('button', { name: /save all/i }));

    expect(savedQuestions()[0]).toMatchObject({
      stem: 'What does the nurse advise?',
      options: ['Increase fluids', 'Reduce the dose', 'Refer on'],
      correctAnswer: 'B',
    });
  });
});
