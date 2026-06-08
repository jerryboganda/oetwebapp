import { describe, expect, it } from 'vitest';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { useState } from 'react';
import { PartANotesDocument } from '@/components/domain/listening/PartANotesDocument';

// ── Fixtures ──────────────────────────────────────────────────────────────────

/** Minimal notes body with heading, bullet, sub-bullet, context, and divider. */
const MULTI_NODE_BODY = [
  '## Background',
  '- onset of ____',
  '  - also noted ____',
  'Context: patient reports ____',
  '---',
  '## Current symptoms',
  '- experiencing ____',
].join('\n');

/** 12-gap notes body that mirrors a real OET Part A extract. */
const TWELVE_GAP_BODY = [
  '## Patient history',
  '- condition: ____',
  '- onset: ____',
  '- location: ____',
  '- severity: ____',
  '## Current treatment',
  '- medication: ____',
  '- dosage: ____',
  '- frequency: ____',
  '- duration: ____',
  '## Examination findings',
  '- blood pressure: ____',
  '- heart rate: ____',
  '- temperature: ____',
  '- weight: ____',
].join('\n');

/** 12 questions numbered 1-12 (A1 extract). */
const Q1_12 = Array.from({ length: 12 }, (_, i) => ({
  id: `q${i + 1}`,
  number: i + 1,
}));

/** 12 questions numbered 13-24 (A2 extract). */
const Q13_24 = Array.from({ length: 12 }, (_, i) => ({
  id: `q${i + 13}`,
  number: i + 13,
}));

// ── Stateful harness ───────────────────────────────────────────────────────────

function NotesHarness({
  body = MULTI_NODE_BODY,
  questions = Q1_12.slice(0, 4),
  locked = false,
  highlightingEnabled,
}: {
  body?: string;
  questions?: Array<{ id: string; number: number }>;
  locked?: boolean;
  highlightingEnabled?: boolean;
}) {
  const [answers, setAnswers] = useState<Record<string, string>>({});
  const handleChange = (questionId: string, value: string) => {
    setAnswers((prev) => ({ ...prev, [questionId]: value }));
  };
  return (
    <PartANotesDocument
      partLabel="Part A1"
      notesBody={body}
      questions={questions}
      answers={answers}
      onAnswerChange={handleChange}
      locked={locked}
      highlightingEnabled={highlightingEnabled}
    />
  );
}

// ── Tests ─────────────────────────────────────────────────────────────────────

describe('PartANotesDocument', () => {
  it('renders the container with the expected testid', () => {
    render(<NotesHarness />);
    expect(screen.getByTestId('part-a-notes-document')).toBeInTheDocument();
  });

  it('renders headings from ## lines', () => {
    render(<NotesHarness />);
    expect(screen.getByRole('heading', { name: /background/i })).toBeInTheDocument();
    expect(screen.getByRole('heading', { name: /current symptoms/i })).toBeInTheDocument();
  });

  it('renders level-1 bullet text', () => {
    render(<NotesHarness />);
    // "onset of" is bullet text before the gap
    expect(screen.getByText(/onset of/i)).toBeInTheDocument();
  });

  it('renders level-2 sub-bullet text', () => {
    render(<NotesHarness />);
    expect(screen.getByText(/also noted/i)).toBeInTheDocument();
  });

  it('renders context line text', () => {
    render(<NotesHarness />);
    expect(screen.getByText(/context: patient reports/i)).toBeInTheDocument();
  });

  it('renders a divider element', () => {
    render(<NotesHarness />);
    const hr = screen.getByTestId('part-a-notes-document').querySelector('hr');
    expect(hr).toBeInTheDocument();
  });

  it('a 12-gap body + 12 questions renders exactly 12 textboxes', () => {
    render(<NotesHarness body={TWELVE_GAP_BODY} questions={Q1_12} />);
    const inputs = screen.getAllByRole('textbox');
    expect(inputs).toHaveLength(12);
  });

  it('only the typed gap changes value (independent editability)', async () => {
    const user = userEvent.setup();
    render(<NotesHarness body={TWELVE_GAP_BODY} questions={Q1_12} />);

    // Type into gap 3 (question index 2, number 3)
    const gap3 = screen.getByRole('textbox', { name: /answer for question 3/i });
    await user.type(gap3, 'fever');

    expect(gap3).toHaveValue('fever');

    // All other gaps must still be empty
    const allInputs = screen.getAllByRole('textbox');
    for (const input of allInputs) {
      if (input !== gap3) {
        expect(input).toHaveValue('');
      }
    }
  });

  it('each input is bound to the correct questionId via the id attribute', () => {
    render(<NotesHarness body={TWELVE_GAP_BODY} questions={Q1_12} />);
    for (const q of Q1_12) {
      expect(document.getElementById(`listening-answer-${q.id}`)).toBeInTheDocument();
    }
  });

  it('shows the correct question number label in parentheses for A1 (1)…(12)', () => {
    render(<NotesHarness body={TWELVE_GAP_BODY} questions={Q1_12} />);
    for (const q of Q1_12) {
      expect(screen.getByText(`(${q.number})`)).toBeInTheDocument();
    }
  });

  it('shows the correct question number labels for A2 harness (13)…(24)', () => {
    render(<NotesHarness body={TWELVE_GAP_BODY} questions={Q13_24} />);
    for (const q of Q13_24) {
      expect(screen.getByText(`(${q.number})`)).toBeInTheDocument();
    }
  });

  it('exam-mode default: container has userSelect:none and data-highlighting-enabled="false"', () => {
    render(<NotesHarness />);
    const container = screen.getByTestId('part-a-notes-document');
    const lockTarget = container.querySelector('[data-highlighting-enabled]') as HTMLElement | null;
    expect(lockTarget).not.toBeNull();
    expect(lockTarget!.dataset.highlightingEnabled).toBe('false');
    expect(lockTarget!.style.userSelect).toBe('none');
  });

  it('gaps are still typeable in exam-mode (inputs inside have userSelect:auto)', async () => {
    const user = userEvent.setup();
    render(<NotesHarness />);
    // The first gap is bound to questions[0] which is Q1_12[0]
    const input = screen.getByRole('textbox', { name: /answer for question 1/i });
    await user.type(input, 'answer');
    expect(input).toHaveValue('answer');
  });

  it('highlightingEnabled=true: container has data-highlighting-enabled="true" and no userSelect:none', () => {
    render(<NotesHarness highlightingEnabled />);
    const container = screen.getByTestId('part-a-notes-document');
    const lockTarget = container.querySelector('[data-highlighting-enabled]') as HTMLElement | null;
    expect(lockTarget).not.toBeNull();
    expect(lockTarget!.dataset.highlightingEnabled).toBe('true');
    expect(lockTarget!.style.userSelect).toBe('');
  });

  it('gaps are typeable when highlightingEnabled=true', async () => {
    const user = userEvent.setup();
    render(<NotesHarness highlightingEnabled />);
    const input = screen.getByRole('textbox', { name: /answer for question 1/i });
    await user.type(input, 'test value');
    expect(input).toHaveValue('test value');
  });

  it('no highlight or strikethrough buttons exist (Part A has no annotation tools)', () => {
    render(<NotesHarness />);
    expect(screen.queryByRole('button', { name: /highlight/i })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /strike out/i })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /strikethrough/i })).not.toBeInTheDocument();
  });

  it('respects locked=true by setting inputs to readOnly', () => {
    render(<NotesHarness body={TWELVE_GAP_BODY} questions={Q1_12} locked />);
    const inputs = screen.getAllByRole('textbox');
    for (const input of inputs) {
      expect(input).toHaveAttribute('readonly');
    }
  });

  it('renders a disabled fallback input when gapIndex >= questions.length (more gaps than questions)', () => {
    // 2-question array but body has 4 gaps — gaps at index 2 and 3 should get disabled fallback inputs
    render(<NotesHarness body={MULTI_NODE_BODY} questions={Q1_12.slice(0, 2)} />);
    const allInputs = screen.getAllByRole('textbox');
    // Should still render all 4 gaps (2 bound + 2 fallback)
    expect(allInputs.length).toBeGreaterThanOrEqual(4);
    // The first two are bound (have an id with listening-answer-)
    expect(document.getElementById('listening-answer-q1')).toBeInTheDocument();
    expect(document.getElementById('listening-answer-q2')).toBeInTheDocument();
  });

  it('leftover questions (index >= gap count) appear as fallback answer fields', () => {
    // Body has 1 gap but we pass 3 questions — questions[1] and questions[2] are leftover
    const oneGapBody = '- main condition: ____';
    render(<NotesHarness body={oneGapBody} questions={Q1_12.slice(0, 3)} />);
    // All 3 question ids should be reachable
    expect(document.getElementById('listening-answer-q1')).toBeInTheDocument();
    expect(document.getElementById('listening-answer-q2')).toBeInTheDocument();
    expect(document.getElementById('listening-answer-q3')).toBeInTheDocument();
  });

  // ── Fix 1: a11y — (N) label must be programmatically associated with its input ──

  it('Fix 1 a11y: each gap label <label> has htmlFor equal to the input id (per-gap association)', () => {
    render(<NotesHarness body={TWELVE_GAP_BODY} questions={Q1_12} />);
    // Check at least two distinct gaps so we know association is per-gap, not shared
    for (const q of [Q1_12[0], Q1_12[4]]) {
      const inputId = `listening-answer-${q.id}`;
      const label = document.querySelector(`label[for="${inputId}"]`);
      expect(label, `label[for="${inputId}"] should exist`).not.toBeNull();
      expect(label!.textContent).toContain(`(${q.number})`);
    }
  });

  it('Fix 1 a11y: getByRole textbox resolves each gap by exact accessible name for at least two gaps', () => {
    render(<NotesHarness body={TWELVE_GAP_BODY} questions={Q1_12} />);
    // Each textbox must be resolvable by its exact aria-label
    for (const q of [Q1_12[1], Q1_12[7]]) {
      expect(
        screen.getByRole('textbox', { name: `Answer for question ${q.number}` }),
      ).toBeInTheDocument();
    }
  });
});
