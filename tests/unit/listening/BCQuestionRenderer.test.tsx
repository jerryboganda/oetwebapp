import { describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { useState } from 'react';
import { BCQuestionRenderer } from '@/components/domain/listening/BCQuestionRenderer';
import type { ListeningQuestionAnnotation } from '@/hooks/use-listening-annotations';

const OPTIONS = ['Ask the nurse to repeat the dose', 'Check the medication chart', 'Call the patient later'];

function BCHarness({ locked = false }: { locked?: boolean }) {
  const [value, setValue] = useState('');
  return (
    <BCQuestionRenderer
      questionNumber={14}
      partLabel="Part B"
      prompt="What should the clinician do first?"
      options={OPTIONS}
      value={value}
      onChange={setValue}
      locked={locked}
    />
  );
}

/**
 * Drives the renderer entirely through the controlled `annotation` /
 * `onAnnotationChange` props — the exact contract the Listening player uses to
 * persist strikethroughs via useListeningAnnotations. No internal fallback
 * state is exercised here.
 */
function ControlledBCHarness() {
  const [value, setValue] = useState('');
  const [annotation, setAnnotation] = useState<ListeningQuestionAnnotation>({});
  return (
    <BCQuestionRenderer
      questionNumber={3}
      partLabel="Part B"
      prompt="What should the clinician do first?"
      options={OPTIONS}
      value={value}
      onChange={setValue}
      annotation={annotation}
      onAnnotationChange={(mutator) => setAnnotation((current) => mutator(current))}
    />
  );
}

describe('BCQuestionRenderer', () => {
  it('selects options and toggles stem highlight', async () => {
    const user = userEvent.setup();
    render(<BCHarness />);

    const highlight = screen.getByRole('button', { name: /highlight question 14 stem/i });
    await user.click(highlight);
    await waitFor(() => expect(screen.getByRole('button', { name: /remove highlight from question 14 stem/i })).toHaveAttribute('aria-pressed', 'true'));

    await user.click(screen.getByRole('radio', { name: /check the medication chart/i }));
    expect(screen.getByRole('radio', { name: /check the medication chart/i })).toHaveAttribute('aria-checked', 'true');
  });

  it('supports right-click and keyboard-accessible option strikethrough', async () => {
    const user = userEvent.setup();
    render(<BCHarness />);

    const firstOption = screen.getByRole('radio', { name: /ask the nurse/i });
    fireEvent.contextMenu(firstOption);
    expect(screen.getByText('Ask the nurse to repeat the dose')).toHaveClass('line-through');

    const strikeSecond = screen.getByRole('button', { name: /strike out option b/i });
    await user.click(strikeSecond);
    await waitFor(() => expect(screen.getByRole('button', { name: /remove strikethrough from option b/i })).toHaveAttribute('aria-pressed', 'true'));
    expect(screen.getByText('Check the medication chart')).toHaveClass('line-through');
  });

  it('supports arrow-key navigation within the radio group', async () => {
    const user = userEvent.setup();
    render(<BCHarness />);

    const firstOption = screen.getByRole('radio', { name: /ask the nurse/i });
    firstOption.focus();
    await user.keyboard('{ArrowDown}');

    const secondOption = screen.getByRole('radio', { name: /check the medication chart/i });
    expect(secondOption).toHaveAttribute('aria-checked', 'true');
    expect(secondOption).toHaveFocus();

    await user.keyboard('{End}');
    const thirdOption = screen.getByRole('radio', { name: /call the patient later/i });
    expect(thirdOption).toHaveAttribute('aria-checked', 'true');
    expect(thirdOption).toHaveFocus();
  });

  it('disables answer and annotation controls when locked', async () => {
    const user = userEvent.setup();
    const onError = vi.spyOn(console, 'error').mockImplementation(() => undefined);
    try {
      render(<BCHarness locked />);

      expect(screen.getByRole('button', { name: /highlight question 14 stem/i })).toBeDisabled();
      expect(screen.getByRole('button', { name: /strike out option a/i })).toBeDisabled();
      await user.click(screen.getByRole('radio', { name: /ask the nurse/i }));
      expect(screen.getByRole('radio', { name: /ask the nurse/i })).toHaveAttribute('aria-checked', 'false');
    } finally {
      onError.mockRestore();
    }
  });

  it('round-trips strikethrough through controlled annotation props', async () => {
    const user = userEvent.setup();
    render(<ControlledBCHarness />);

    // The struck state is driven by the controlled `annotation` prop: the
    // click fires onAnnotationChange, the parent applies the mutator, and the
    // new prop value re-renders the line-through.
    await user.click(screen.getByRole('button', { name: /strike out option a/i }));
    await waitFor(() => expect(screen.getByText(OPTIONS[0])).toHaveClass('line-through'));
    expect(screen.getByRole('button', { name: /remove strikethrough from option a/i })).toHaveAttribute('aria-pressed', 'true');

    // Toggling again clears it back through the same controlled path.
    await user.click(screen.getByRole('button', { name: /remove strikethrough from option a/i }));
    await waitFor(() => expect(screen.getByText(OPTIONS[0])).not.toHaveClass('line-through'));
  });

  // --- Flag-for-Review ---

  it('flag button toggles aria-pressed and calls onAnnotationChange with flagged:true (controlled)', async () => {
    const user = userEvent.setup();
    const onAnnotationChange = vi.fn();
    render(
      <BCQuestionRenderer
        questionNumber={3}
        partLabel="Part B"
        prompt="What is the priority action?"
        options={OPTIONS}
        value=""
        onChange={vi.fn()}
        annotation={{ flagged: false }}
        onAnnotationChange={onAnnotationChange}
      />,
    );

    const flagBtn = screen.getByRole('button', { name: /flag question 3 for review/i });
    expect(flagBtn).toHaveAttribute('aria-pressed', 'false');

    await user.click(flagBtn);

    expect(onAnnotationChange).toHaveBeenCalledTimes(1);
    // Verify the mutator produces {flagged: true} when current is {flagged: false}.
    const mutator = onAnnotationChange.mock.calls[0]![0] as (
      current: ListeningQuestionAnnotation,
    ) => ListeningQuestionAnnotation;
    expect(mutator({ flagged: false })).toMatchObject({ flagged: true });
  });

  it('toggling flag twice leaves it unflagged (controlled round-trip)', async () => {
    const user = userEvent.setup();
    render(<ControlledBCHarness />);

    const flagBtn = screen.getByRole('button', { name: /flag question 3 for review/i });
    await user.click(flagBtn);
    await waitFor(() => expect(screen.getByRole('button', { name: /remove review flag/i })).toHaveAttribute('aria-pressed', 'true'));

    await user.click(screen.getByRole('button', { name: /remove review flag/i }));
    await waitFor(() => expect(screen.getByRole('button', { name: /flag question 3 for review/i })).toHaveAttribute('aria-pressed', 'false'));
  });

  it('flagged question card shows visual flagged indicator', async () => {
    const user = userEvent.setup();
    render(<ControlledBCHarness />);

    // Before flagging: no flagged indicator.
    expect(screen.queryByTestId('bc-flagged-indicator')).not.toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: /flag question 3 for review/i }));
    await waitFor(() => expect(screen.getByTestId('bc-flagged-indicator')).toBeInTheDocument());
  });

  it('flag button is disabled when locked', () => {
    render(<BCHarness locked />);
    // The flag button should be disabled when the question is locked.
    const flagBtn = screen.getByRole('button', { name: /flag question 14 for review/i });
    expect(flagBtn).toBeDisabled();
  });
});
