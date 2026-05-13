import { describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { useState } from 'react';
import { BCQuestionRenderer } from '@/components/domain/listening/BCQuestionRenderer';

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
});
