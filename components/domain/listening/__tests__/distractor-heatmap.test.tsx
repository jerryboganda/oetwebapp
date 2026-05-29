import { describe, it, expect } from 'vitest';
import { render, screen, within } from '@testing-library/react';
import {
  DistractorHeatmap,
  type DistractorHeatmapRow,
} from '@/components/domain/listening/DistractorHeatmap';

const rows: DistractorHeatmapRow[] = [
  {
    paperId: 'lt-001',
    questionNumber: 12,
    correctAnswer: 'C',
    wrongAnswerHistogram: { A: 7, B: 12, D: 3 },
  },
  {
    paperId: 'lt-001',
    questionNumber: 18,
    correctAnswer: 'A',
    wrongAnswerHistogram: { B: 1 },
  },
];

describe('DistractorHeatmap', () => {
  it('renders the empty state when there are no rows', () => {
    render(<DistractorHeatmap rows={[]} />);
    expect(screen.getByText(/no mcq distractor noise/i)).toBeInTheDocument();
  });

  it('honours a custom empty label', () => {
    render(<DistractorHeatmap rows={[]} emptyLabel="Nothing to show here." />);
    expect(screen.getByText('Nothing to show here.')).toBeInTheDocument();
  });

  it('renders a row per question and unions the option columns', () => {
    render(<DistractorHeatmap rows={rows} />);
    // One row header per question.
    expect(screen.getByRole('rowheader', { name: 'Q12' })).toBeInTheDocument();
    expect(screen.getByRole('rowheader', { name: 'Q18' })).toBeInTheDocument();
    // Column headers are the union of all option labels (A, B, C, D), sorted.
    expect(screen.getByRole('columnheader', { name: 'A' })).toBeInTheDocument();
    expect(screen.getByRole('columnheader', { name: 'B' })).toBeInTheDocument();
    expect(screen.getByRole('columnheader', { name: 'C' })).toBeInTheDocument();
    expect(screen.getByRole('columnheader', { name: 'D' })).toBeInTheDocument();
  });

  it('marks the correct option cell and labels it for a11y', () => {
    render(<DistractorHeatmap rows={[rows[0]]} />);
    // The correct answer (C) is surfaced as a "correct answer" cell.
    expect(screen.getByLabelText('C — correct answer')).toBeInTheDocument();
    // A heavy-noise distractor reports its learner count (plural).
    expect(screen.getByLabelText('B — 12 learners chose this')).toBeInTheDocument();
  });

  it('uses singular phrasing when exactly one learner chose an option', () => {
    // Row 18's only wrong pick is B:1.
    render(<DistractorHeatmap rows={[rows[1]]} />);
    expect(screen.getByLabelText('B — 1 learner chose this')).toBeInTheDocument();
  });

  it('shows the wrong-pick count inside each heat cell', () => {
    render(<DistractorHeatmap rows={[rows[0]]} />);
    const cellB = screen.getByLabelText('B — 12 learners chose this');
    expect(cellB).toHaveTextContent('12');
    const cellA = screen.getByLabelText('A — 7 learners chose this');
    expect(cellA).toHaveTextContent('7');
  });

  it('exposes an accessible table and legend', () => {
    render(<DistractorHeatmap rows={rows} />);
    expect(
      screen.getByRole('table', { name: /distractor heatmap/i }),
    ).toBeInTheDocument();
    expect(screen.getByText('Correct option')).toBeInTheDocument();
    expect(screen.getByText('Heavy noise')).toBeInTheDocument();
  });

  it('does not animate position (reduced-motion safe — colour-only transition)', () => {
    const { container } = render(<DistractorHeatmap rows={[rows[0]]} />);
    // Heat cells fade colour only behind the motion-safe gate; they must not
    // carry a blanket transition-all or any transform/translate utility.
    const animatedCells = container.querySelectorAll('[class*="transition-all"]');
    expect(animatedCells.length).toBe(0);
    const motionSafe = container.querySelector('[class*="motion-safe:transition-colors"]');
    expect(motionSafe).not.toBeNull();
  });

  it('keeps each row scaled to its own peak so a quiet row still reads', () => {
    // Row 18 has a single wrong pick (B:1); that cell is its own peak and must
    // render the hottest class, independent of row 12's larger counts.
    render(<DistractorHeatmap rows={rows} />);
    const q18Row = screen.getByRole('rowheader', { name: 'Q18' }).closest('tr');
    expect(q18Row).not.toBeNull();
    const hotCell = within(q18Row as HTMLElement).getByLabelText('B — 1 learner chose this');
    expect(hotCell.className).toContain('var(--admin-danger)');
  });
});
