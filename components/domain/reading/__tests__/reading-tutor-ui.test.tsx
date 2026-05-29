import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

import { ReadingRagChip } from '../reading-rag-chip';
import { ReadingOverridePanel } from '../reading-override-panel';
import type { ReadingPrivilegedAttemptReview } from '@/lib/reading-tutor-api';

const overrideReadingAttemptScore = vi.fn();
const clearReadingAttemptScoreOverride = vi.fn();

vi.mock('@/lib/reading-tutor-api', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@/lib/reading-tutor-api')>();
  return {
    ...actual,
    overrideReadingAttemptScore: (...args: unknown[]) => overrideReadingAttemptScore(...args),
    clearReadingAttemptScoreOverride: (...args: unknown[]) => clearReadingAttemptScoreOverride(...args),
  };
});

function buildReview(
  overrides: Partial<ReadingPrivilegedAttemptReview> = {},
): ReadingPrivilegedAttemptReview {
  return {
    attemptId: 'attempt-1',
    paperId: 'paper-1',
    paperTitle: 'Reading Paper 1',
    userId: 'learner-1',
    status: 'submitted',
    mode: 'exam',
    startedAt: '2026-05-01T10:00:00Z',
    submittedAt: '2026-05-01T11:00:00Z',
    gradedRawScore: 30,
    gradedScaledScore: 350,
    gradedGradeLetter: 'B',
    effectiveRawScore: 30,
    effectiveScaledScore: 350,
    effectiveGradeLetter: 'B',
    hasOverride: false,
    overrideRaw: null,
    overrideScaled: null,
    overrideReason: null,
    overriddenByUserId: null,
    overriddenAt: null,
    maxRawScore: 42,
    sections: [],
    questions: [],
    flaggedQuestionIds: [],
    ...overrides,
  };
}

describe('ReadingRagChip', () => {
  it('renders the canonical Green label and verdict for a pass', () => {
    render(<ReadingRagChip rag="green" />);
    const chip = screen.getByText('Green');
    expect(chip).toBeInTheDocument();
    expect(chip).toHaveAttribute('data-rag', 'green');
  });

  it('maps amber and red verdicts straight from the API value', () => {
    const { rerender } = render(<ReadingRagChip rag="amber" />);
    expect(screen.getByText('Amber')).toHaveAttribute('data-rag', 'amber');

    rerender(<ReadingRagChip rag="RED" />);
    expect(screen.getByText('Red')).toHaveAttribute('data-rag', 'red');
  });

  it('falls back to an unknown verdict for unrecognised values', () => {
    render(<ReadingRagChip rag="???" label="No attempt" />);
    const chip = screen.getByText('No attempt');
    expect(chip).toHaveAttribute('data-rag', 'unknown');
  });
});

describe('ReadingOverridePanel', () => {
  beforeEach(() => {
    overrideReadingAttemptScore.mockReset();
    clearReadingAttemptScoreOverride.mockReset();
  });

  it('submits a raw score override with the reason and area', async () => {
    const user = userEvent.setup();
    const review = buildReview();
    const updated = buildReview({ hasOverride: true, effectiveRawScore: 35 });
    overrideReadingAttemptScore.mockResolvedValue(updated);
    const onUpdated = vi.fn();

    render(
      <ReadingOverridePanel attemptId="attempt-1" area="admin" review={review} onUpdated={onUpdated} />,
    );

    await user.type(screen.getByLabelText('Raw score'), '35');
    await user.type(screen.getByLabelText(/reason/i), 'Manual remark after escalation');
    await user.click(screen.getByRole('button', { name: /apply override/i }));

    await waitFor(() => expect(overrideReadingAttemptScore).toHaveBeenCalledTimes(1));
    expect(overrideReadingAttemptScore).toHaveBeenCalledWith(
      'attempt-1',
      { reason: 'Manual remark after escalation', rawScore: 35, scaledScore: null },
      'admin',
    );
    expect(onUpdated).toHaveBeenCalledWith(updated);
  });

  it('blocks submission when the reason is missing', async () => {
    const user = userEvent.setup();
    render(
      <ReadingOverridePanel
        attemptId="attempt-1"
        area="expert"
        review={buildReview()}
        onUpdated={vi.fn()}
      />,
    );

    await user.type(screen.getByLabelText('Raw score'), '20');
    await user.click(screen.getByRole('button', { name: /apply override/i }));

    expect(overrideReadingAttemptScore).not.toHaveBeenCalled();
    expect(screen.getByRole('alert')).toHaveTextContent(/reason is required/i);
  });

  it('clears an existing override', async () => {
    const user = userEvent.setup();
    const review = buildReview({ hasOverride: true });
    clearReadingAttemptScoreOverride.mockResolvedValue(buildReview({ hasOverride: false }));
    const onUpdated = vi.fn();

    render(
      <ReadingOverridePanel attemptId="attempt-1" area="admin" review={review} onUpdated={onUpdated} />,
    );

    await user.click(screen.getByRole('button', { name: /clear override/i }));

    await waitFor(() => expect(clearReadingAttemptScoreOverride).toHaveBeenCalledWith('attempt-1', 'admin'));
    expect(onUpdated).toHaveBeenCalled();
  });
});
