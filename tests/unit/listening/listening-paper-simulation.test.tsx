/**
 * Unit tests for the ListeningPaperBooklet component within
 * ListeningPaperSimulation — specifically the Part A notes-document wiring:
 *   - A1 page with notesBody → renders a single PartANotesDocument (not 12 cards)
 *   - A1 page without notesBody (legacy) → falls back to PartARenderer per question
 *   - Answer changes route through onAnswerChange with the correct questionId
 */
import { describe, expect, it } from 'vitest';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { useState } from 'react';
import { ListeningPaperBooklet } from '@/components/domain/listening/ListeningPaperSimulation';
import type { ListeningPaperBookletPage } from '@/lib/listening-paper-simulation';
import type { ListeningSessionQuestionDto } from '@/lib/listening-api';

// ── Shared fixtures ───────────────────────────────────────────────────────────

/** 12 questions numbered 1-12, all Part A1 short-answer (no options). */
const QUESTIONS_A1: ListeningSessionQuestionDto[] = Array.from({ length: 12 }, (_, i) => ({
  id: `a1q${i + 1}`,
  number: i + 1,
  partCode: 'A1',
  text: `Question ${i + 1} stem ____`,
  type: 'short_answer',
  options: [],
  points: 1,
}));

const QUESTION_BY_ID = new Map(QUESTIONS_A1.map((q) => [q.id, q] as const));

/** 12-gap notes body (one gap per bullet line). */
const NOTES_BODY_12 = Array.from({ length: 12 }, (_, i) => `- item ${i + 1}: ____`).join('\n');

/** A1 page WITH notesBody. */
const A1_PAGE_WITH_NOTES: ListeningPaperBookletPage = {
  id: 'listening-part-a-a1',
  label: 'Part A — Extract 1 — Consultation',
  section: 'A1',
  extract: {
    partCode: 'A1',
    displayOrder: 1,
    kind: 'consultation',
    title: 'Consultation',
    accentCode: null,
    speakers: [],
    audioStartMs: null,
    audioEndMs: null,
    notesBody: NOTES_BODY_12,
  },
  questionIds: QUESTIONS_A1.map((q) => q.id),
  kind: 'notes',
};

/** A1 page WITHOUT notesBody (legacy paper — extract exists but notesBody is null). */
const A1_PAGE_LEGACY: ListeningPaperBookletPage = {
  id: 'listening-part-a-a1',
  label: 'Part A — Extract 1 — Consultation',
  section: 'A1',
  extract: {
    partCode: 'A1',
    displayOrder: 1,
    kind: 'consultation',
    title: 'Consultation',
    accentCode: null,
    speakers: [],
    audioStartMs: null,
    audioEndMs: null,
    notesBody: null,
  },
  questionIds: QUESTIONS_A1.map((q) => q.id),
  kind: 'notes',
};

/** A1 page with no extract at all (older legacy). */
const A1_PAGE_NO_EXTRACT: ListeningPaperBookletPage = {
  id: 'listening-part-a-a1',
  label: 'Part A — Extract 1',
  section: 'A1',
  extract: null,
  questionIds: QUESTIONS_A1.map((q) => q.id),
  kind: 'notes',
};

// ── Harness ───────────────────────────────────────────────────────────────────

function BookletHarness({
  page,
  initialAnswers = {},
}: {
  page: ListeningPaperBookletPage;
  initialAnswers?: Record<string, string>;
}) {
  const [answers, setAnswers] = useState<Record<string, string>>(initialAnswers);
  const handleChange = (questionId: string, value: string) => {
    setAnswers((prev) => ({ ...prev, [questionId]: value }));
  };
  return (
    <ListeningPaperBooklet
      page={page}
      pageIndex={0}
      pageCount={1}
      questionById={QUESTION_BY_ID}
      answers={answers}
      onPageChange={() => undefined}
      onAnswerChange={handleChange}
    />
  );
}

// ── Tests ─────────────────────────────────────────────────────────────────────

describe('ListeningPaperBooklet — Part A notes-document wiring', () => {
  it('A1 page with notesBody renders a single part-a-notes-document (NOT 12 cards)', () => {
    render(<BookletHarness page={A1_PAGE_WITH_NOTES} />);

    // Should have exactly ONE notes-document container
    const documents = screen.getAllByTestId('part-a-notes-document');
    expect(documents).toHaveLength(1);

    // Should NOT have the old per-question card testid
    expect(screen.queryByTestId('part-a-clinical-note')).not.toBeInTheDocument();
  });

  it('A1 page with notesBody renders exactly 12 answer textboxes', () => {
    render(<BookletHarness page={A1_PAGE_WITH_NOTES} />);
    const inputs = screen.getAllByRole('textbox');
    expect(inputs).toHaveLength(12);
  });

  it('answer changes from notesBody-backed gaps route through with the correct questionId', async () => {
    const user = userEvent.setup();
    render(<BookletHarness page={A1_PAGE_WITH_NOTES} />);

    // Type into question 5 (id='a1q5')
    const input = screen.getByRole('textbox', { name: /answer for question 5/i });
    await user.type(input, 'right leg');
    expect(input).toHaveValue('right leg');
    expect(document.getElementById('listening-answer-a1q5')).toHaveValue('right leg');
  });

  it('all 12 question ids are reachable as input ids', () => {
    render(<BookletHarness page={A1_PAGE_WITH_NOTES} />);
    for (const q of QUESTIONS_A1) {
      expect(document.getElementById(`listening-answer-${q.id}`)).toBeInTheDocument();
    }
  });

  it('legacy A1 page (notesBody == null) renders PartARenderer per question, not the notes-document', () => {
    render(<BookletHarness page={A1_PAGE_LEGACY} />);

    // Should fall back to per-question PartARenderer cards
    expect(screen.queryByTestId('part-a-notes-document')).not.toBeInTheDocument();
    // Each question should render its clinical note card
    const cards = screen.getAllByTestId('part-a-clinical-note');
    expect(cards).toHaveLength(12);
  });

  it('legacy A1 page with null extract also falls back to PartARenderer cards', () => {
    render(<BookletHarness page={A1_PAGE_NO_EXTRACT} />);

    expect(screen.queryByTestId('part-a-notes-document')).not.toBeInTheDocument();
    const cards = screen.getAllByTestId('part-a-clinical-note');
    expect(cards).toHaveLength(12);
  });

  it('notesBody-backed booklet passes highlightingEnabled=true to PartANotesDocument', () => {
    render(<BookletHarness page={A1_PAGE_WITH_NOTES} />);
    // When highlightingEnabled is true, data-highlighting-enabled attr should be "true"
    const doc = screen.getByTestId('part-a-notes-document');
    const lockTarget = doc.querySelector('[data-highlighting-enabled]') as HTMLElement | null;
    expect(lockTarget).not.toBeNull();
    expect(lockTarget!.dataset.highlightingEnabled).toBe('true');
  });
});
