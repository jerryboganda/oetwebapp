import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

import { WordCounter } from '../WordCounter';
import { WritingTimerV2 } from '../WritingTimerV2';
import { SubmitBar } from '../SubmitBar';
import { ReadinessWidget } from '../ReadinessWidget';
import { CanonViolationCard } from '../CanonViolationCard';
import { BandHistoryChart } from '../BandHistoryChart';

describe('writing UI primitives', () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it('renders the word counter tone and aria hint for each band', () => {
    const { rerender } = render(<WordCounter count={149} />);

    expect(screen.getByText('149')).toHaveClass('text-muted');
    expect(screen.getByLabelText('149 words, keep writing, target 180 to 220')).toBeInTheDocument();

    rerender(<WordCounter count={180} />);

    expect(screen.getByText('180')).toHaveClass('text-success');
    expect(screen.getByLabelText('180 words, in target range, target 180 to 220')).toBeInTheDocument();

    rerender(<WordCounter count={251} />);

    expect(screen.getByText('251')).toHaveClass('text-danger');
    expect(screen.getByLabelText('251 words, over-length, target 180 to 220')).toBeInTheDocument();
  });

  it('moves from reading to writing when reading time expires', async () => {
    const onPhaseChange = vi.fn();

    const { rerender } = render(
      <WritingTimerV2
        phase="reading"
        readingSecondsRemaining={5}
        writingSecondsRemaining={2400}
        onPhaseChange={onPhaseChange}
      />,
    );

    expect(screen.getByRole('timer', { name: 'Reading window: 00:05 remaining' })).toBeInTheDocument();

    rerender(
      <WritingTimerV2
        phase="reading"
        readingSecondsRemaining={0}
        writingSecondsRemaining={2400}
        onPhaseChange={onPhaseChange}
      />,
    );

    await waitFor(() => {
      expect(onPhaseChange).toHaveBeenCalledWith('writing');
    });
  });

  it('marks the writing phase as completed when the timer reaches zero', async () => {
    const onPhaseChange = vi.fn();

    render(
      <WritingTimerV2
        phase="writing"
        readingSecondsRemaining={300}
        writingSecondsRemaining={0}
        onPhaseChange={onPhaseChange}
      />,
    );

    expect(screen.getByRole('timer', { name: 'Writing window: 00:00 remaining' })).toBeInTheDocument();

    await waitFor(() => {
      expect(onPhaseChange).toHaveBeenCalledWith('completed');
    });
  });

  it('disables submit until ready and still wires secondary actions', async () => {
    const user = userEvent.setup();
    const onSubmit = vi.fn();
    const onReset = vi.fn();

    const { rerender } = render(
      <SubmitBar
        canSubmit={false}
        submitLabel="Submit draft"
        onSubmit={onSubmit}
        secondaryActions={[{ label: 'Reset', onClick: onReset }]}
        helperText="Need at least 180 words"
      />,
    );

    expect(screen.getByRole('button', { name: 'Submit draft' })).toBeDisabled();
    expect(screen.getByText('Need at least 180 words')).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Reset' }));
    expect(onReset).toHaveBeenCalledTimes(1);

    rerender(
      <SubmitBar
        canSubmit
        submitLabel="Submit draft"
        onSubmit={onSubmit}
        secondaryActions={[{ label: 'Reset', onClick: onReset }]}
        helperText="Need at least 180 words"
      />,
    );

    await user.click(screen.getByRole('button', { name: 'Submit draft' }));
    expect(onSubmit).toHaveBeenCalledTimes(1);
  });

  it('shows readiness score context and sub-score bars', () => {
    render(
      <ReadinessWidget
        score={92}
        deltaVsLastWeek={3}
        predictedBand="B"
        subScores={{
          mockAverage: 86,
          trajectory: 91,
          canonCleanRate: 94,
          timeMgmt: 88,
          typeConsistency: 97,
        }}
      />,
    );

    expect(screen.getByLabelText('Readiness score widget')).toHaveTextContent('92');
    expect(screen.getByText('Exam-ready')).toBeInTheDocument();
    expect(screen.getByText('+3 vs last week')).toBeInTheDocument();
    expect(screen.getByText(/Likely band on exam day: B/i)).toBeInTheDocument();
    expect(screen.getByRole('progressbar', { name: 'Mock average: 86 percent' })).toHaveAttribute(
      'aria-valuenow',
      '86',
    );
  });

  it('opens the canon violation link and supports optimistic dispute feedback', async () => {
    const user = userEvent.setup();
    const onDispute = vi.fn().mockResolvedValue(undefined);

    render(
      <CanonViolationCard
        violation={{
          id: 'violation-1',
          ruleId: 'R09.2',
          ruleText: 'Avoid direct reference to the patient.',
          severity: 'medium',
          lineNumber: 4,
          snippet: 'the patient',
          suggestedFix: 'Use the patient name or pronoun instead.',
          disputed: false,
        }}
        onDispute={onDispute}
      />,
    );

    expect(screen.getByRole('link', { name: /r09.2/i })).toHaveAttribute('href', '/writing/canon/R09.2');
    expect(screen.getByText('Medium severity')).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Mark this detection as incorrect' }));

    await waitFor(() => {
      expect(onDispute).toHaveBeenCalledWith('R09.2', 'violation-1');
    });
    expect(await screen.findByText('Flagged for review')).toBeInTheDocument();
  });

  it('shows an empty chart state before any graded letters exist', () => {
    render(<BandHistoryChart data={[]} />);

    expect(screen.getByRole('img', { name: 'Band history chart: no data yet' })).toBeInTheDocument();
    expect(screen.getByText('No graded letters yet. Your band history will appear here.')).toBeInTheDocument();
  });
});