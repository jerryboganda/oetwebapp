import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import type {
  ReadingPartAdminDto,
  ReadingSectionAdminDto,
  ReadingQuestionAdminDto,
} from '@/lib/reading-authoring-api';

const { mockUpsertReadingQuestion } = vi.hoisted(() => ({
  mockUpsertReadingQuestion: vi.fn(),
}));

vi.mock('@/lib/reading-authoring-api', () => ({
  upsertReadingQuestion: mockUpsertReadingQuestion,
}));

import { ReadingAnswerSheetBuilder } from './ReadingAnswerSheetBuilder';

function partA(questions: ReadingQuestionAdminDto[] = []): ReadingPartAdminDto {
  return { id: 'part-a', partCode: 'A', timeLimitMinutes: 15, maxRawScore: 20, instructions: null, texts: [], questions };
}

function partBC(partCode: 'B' | 'C', sections: ReadingSectionAdminDto[]): ReadingPartAdminDto {
  return {
    id: `part-${partCode.toLowerCase()}`,
    partCode,
    timeLimitMinutes: 45,
    maxRawScore: partCode === 'B' ? 6 : 16,
    instructions: null,
    texts: [],
    questions: sections.flatMap((s) => s.questions),
    sections,
  };
}

function section(sectionCode: ReadingSectionAdminDto['sectionCode'], displayOrder: number, questions: ReadingQuestionAdminDto[] = []): ReadingSectionAdminDto {
  return { id: `sec-${sectionCode.toLowerCase()}`, sectionCode, displayOrder, maxRawScore: sectionCode.startsWith('C') ? 8 : 1, contentPaperAssetId: null, questions };
}

const noop = () => {};

beforeEach(() => {
  vi.clearAllMocks();
  mockUpsertReadingQuestion.mockResolvedValue({ id: 'q-new' });
});

async function fillAllAnswers(user: ReturnType<typeof userEvent.setup>) {
  for (const select of screen.queryAllByRole('combobox')) {
    await user.selectOptions(select, 'A');
  }
  for (const input of screen.queryAllByRole('textbox')) {
    await user.type(input, 'answer');
  }
}

describe('ReadingAnswerSheetBuilder', () => {
  it('generates the official Part A layout and saves 20 answers with section-local order', async () => {
    const user = userEvent.setup();
    render(
      <ReadingAnswerSheetBuilder
        paperId="paper-1"
        partCode="A"
        activePart={partA()}
        activeSection={null}
        onSaved={noop}
        onNotify={noop}
      />,
    );

    await user.click(screen.getByRole('button', { name: /generate/i }));
    await fillAllAnswers(user);
    await user.click(screen.getByRole('button', { name: /save all/i }));

    expect(mockUpsertReadingQuestion).toHaveBeenCalledTimes(20);
    // Q1 is a matching item with the official Text A–D references.
    expect(mockUpsertReadingQuestion).toHaveBeenNthCalledWith(1, 'paper-1', expect.objectContaining({
      questionType: 'MatchingTextReference',
      readingSectionId: null,
      displayOrder: 1,
      optionsJson: '["Text A","Text B","Text C","Text D"]',
      stem: 'See PDF',
      distractorRationale: null,
    }));
    // Q8 is the first short-answer item.
    expect(mockUpsertReadingQuestion).toHaveBeenNthCalledWith(8, 'paper-1', expect.objectContaining({
      questionType: 'ShortAnswer',
      displayOrder: 8,
      optionsJson: '[]',
    }));
    // Q15 is the first sentence-completion item.
    expect(mockUpsertReadingQuestion).toHaveBeenNthCalledWith(15, 'paper-1', expect.objectContaining({
      questionType: 'SentenceCompletion',
      displayOrder: 15,
    }));
  });

  it('generates one MultipleChoice3 item for a Part B section at its section-local order', async () => {
    const user = userEvent.setup();
    render(
      <ReadingAnswerSheetBuilder
        paperId="paper-1"
        partCode="B"
        activePart={partBC('B', [section('B3', 3)])}
        activeSection={section('B3', 3)}
        onSaved={noop}
        onNotify={noop}
      />,
    );

    await user.click(screen.getByRole('button', { name: /generate/i }));
    await fillAllAnswers(user);
    await user.click(screen.getByRole('button', { name: /save all/i }));

    expect(mockUpsertReadingQuestion).toHaveBeenCalledTimes(1);
    expect(mockUpsertReadingQuestion).toHaveBeenNthCalledWith(1, 'paper-1', expect.objectContaining({
      questionType: 'MultipleChoice3',
      readingSectionId: 'sec-b3',
      displayOrder: 3,
      optionsJson: '["Option A","Option B","Option C"]',
      correctAnswerJson: '"A"',
      distractorRationale: null,
    }));
  });

  it('generates eight MultipleChoice4 items for Part C section C2 at orders 9..16', async () => {
    const user = userEvent.setup();
    render(
      <ReadingAnswerSheetBuilder
        paperId="paper-1"
        partCode="C"
        activePart={partBC('C', [section('C2', 2)])}
        activeSection={section('C2', 2)}
        onSaved={noop}
        onNotify={noop}
      />,
    );

    await user.click(screen.getByRole('button', { name: /generate/i }));
    await fillAllAnswers(user);
    await user.click(screen.getByRole('button', { name: /save all/i }));

    expect(mockUpsertReadingQuestion).toHaveBeenCalledTimes(8);
    expect(mockUpsertReadingQuestion).toHaveBeenNthCalledWith(1, 'paper-1', expect.objectContaining({
      questionType: 'MultipleChoice4',
      readingSectionId: 'sec-c2',
      displayOrder: 9,
      optionsJson: '["Option A","Option B","Option C","Option D"]',
    }));
    expect(mockUpsertReadingQuestion).toHaveBeenNthCalledWith(8, 'paper-1', expect.objectContaining({
      questionType: 'MultipleChoice4',
      displayOrder: 16,
    }));
  });

  it('blocks save and notifies when a generated row has no correct answer', async () => {
    const user = userEvent.setup();
    const onNotify = vi.fn();
    render(
      <ReadingAnswerSheetBuilder
        paperId="paper-1"
        partCode="C"
        activePart={partBC('C', [section('C1', 1)])}
        activeSection={section('C1', 1)}
        onSaved={noop}
        onNotify={onNotify}
      />,
    );

    await user.click(screen.getByRole('button', { name: /generate/i }));
    // Leave all answers blank.
    await user.click(screen.getByRole('button', { name: /save all/i }));

    expect(mockUpsertReadingQuestion).not.toHaveBeenCalled();
    expect(onNotify).toHaveBeenCalledWith('error', expect.stringMatching(/correct answer/i));
  });

  it('seeds rows from existing questions and updates them in place (idempotent)', async () => {
    const user = userEvent.setup();
    const existing: ReadingQuestionAdminDto = {
      id: 'q-b3-existing',
      readingPartId: 'part-b',
      readingSectionId: 'sec-b3',
      readingTextId: null,
      displayOrder: 3,
      points: 1,
      questionType: 'MultipleChoice3',
      stem: 'See PDF',
      optionsJson: '["Option A","Option B","Option C"]',
      correctAnswerJson: '"B"',
      acceptedSynonymsJson: null,
      caseSensitive: false,
      explanationMarkdown: null,
      skillTag: null,
    };
    render(
      <ReadingAnswerSheetBuilder
        paperId="paper-1"
        partCode="B"
        activePart={partBC('B', [section('B3', 3, [existing])])}
        activeSection={section('B3', 3, [existing])}
        onSaved={noop}
        onNotify={noop}
      />,
    );

    // Seeded immediately (no Generate needed) with the existing correct answer.
    await user.click(screen.getByRole('button', { name: /save all/i }));

    expect(mockUpsertReadingQuestion).toHaveBeenNthCalledWith(1, 'paper-1', expect.objectContaining({
      id: 'q-b3-existing',
      correctAnswerJson: '"B"',
    }));
  });

  it('saves an authored rationale as explanationMarkdown', async () => {
    const user = userEvent.setup();
    render(
      <ReadingAnswerSheetBuilder
        paperId="paper-1"
        partCode="B"
        activePart={partBC('B', [section('B3', 3)])}
        activeSection={section('B3', 3)}
        onSaved={noop}
        onNotify={noop}
      />,
    );

    await user.click(screen.getByRole('button', { name: /generate/i }));
    await user.selectOptions(screen.getByRole('combobox'), 'A');
    await user.type(screen.getByLabelText(/rationale for question 3/i), 'Text A states the diagnosis.');
    await user.click(screen.getByRole('button', { name: /save all/i }));

    expect(mockUpsertReadingQuestion).toHaveBeenNthCalledWith(1, 'paper-1', expect.objectContaining({
      explanationMarkdown: 'Text A states the diagnosis.',
    }));
  });

  it('sends explanationMarkdown null when no rationale is entered', async () => {
    const user = userEvent.setup();
    render(
      <ReadingAnswerSheetBuilder
        paperId="paper-1"
        partCode="B"
        activePart={partBC('B', [section('B3', 3)])}
        activeSection={section('B3', 3)}
        onSaved={noop}
        onNotify={noop}
      />,
    );

    await user.click(screen.getByRole('button', { name: /generate/i }));
    await user.selectOptions(screen.getByRole('combobox'), 'A');
    await user.click(screen.getByRole('button', { name: /save all/i }));

    expect(mockUpsertReadingQuestion).toHaveBeenNthCalledWith(1, 'paper-1', expect.objectContaining({
      explanationMarkdown: null,
    }));
  });

  it('seeds an existing rationale and preserves it on save', async () => {
    const user = userEvent.setup();
    const existing: ReadingQuestionAdminDto = {
      id: 'q-b3-existing',
      readingPartId: 'part-b',
      readingSectionId: 'sec-b3',
      readingTextId: null,
      displayOrder: 3,
      points: 1,
      questionType: 'MultipleChoice3',
      stem: 'See PDF',
      optionsJson: '["Option A","Option B","Option C"]',
      correctAnswerJson: '"B"',
      acceptedSynonymsJson: null,
      caseSensitive: false,
      explanationMarkdown: 'Existing reason for option B.',
      skillTag: null,
    };
    render(
      <ReadingAnswerSheetBuilder
        paperId="paper-1"
        partCode="B"
        activePart={partBC('B', [section('B3', 3, [existing])])}
        activeSection={section('B3', 3, [existing])}
        onSaved={noop}
        onNotify={noop}
      />,
    );

    // Rationale pre-fills from the existing question.
    expect(screen.getByLabelText(/rationale for question 3/i)).toHaveValue('Existing reason for option B.');

    await user.click(screen.getByRole('button', { name: /save all/i }));

    expect(mockUpsertReadingQuestion).toHaveBeenNthCalledWith(1, 'paper-1', expect.objectContaining({
      id: 'q-b3-existing',
      explanationMarkdown: 'Existing reason for option B.',
    }));
  });
});
