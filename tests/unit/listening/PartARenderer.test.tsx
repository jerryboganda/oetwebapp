import { describe, expect, it } from 'vitest';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { useState } from 'react';
import { PartARenderer } from '@/components/domain/listening/PartARenderer';

function PartAHarness({ locked = false }: { locked?: boolean }) {
  const [value, setValue] = useState('');
  return (
    <PartARenderer
      questionNumber={3}
      partLabel="Part A1"
      prompt="Pain located in ____ after the fall"
      inputId="listening-answer-q-3"
      value={value}
      onChange={setValue}
      locked={locked}
    />
  );
}

describe('PartARenderer', () => {
  it('renders an OET-style clinical note blank for short-answer questions', () => {
    render(<PartAHarness />);

    expect(screen.getByTestId('part-a-clinical-note')).toBeInTheDocument();
    expect(screen.getByText('Pain located in')).toBeInTheDocument();
    expect(screen.getByText('after the fall')).toBeInTheDocument();
    expect(screen.getByRole('textbox', { name: /answer for question 3/i })).toHaveAttribute('spellcheck', 'false');
  });

  it('updates the controlled Part A answer value', async () => {
    const user = userEvent.setup();
    render(<PartAHarness />);

    const input = screen.getByRole('textbox', { name: /answer for question 3/i });
    await user.type(input, 'right leg');

    expect(input).toHaveValue('right leg');
  });

  it('respects locked mode by making the answer read-only', () => {
    render(<PartAHarness locked />);

    expect(screen.getByRole('textbox', { name: /answer for question 3/i })).toHaveAttribute('readonly');
  });

  it('does not expose Part B/C highlight or strikethrough tools', () => {
    render(<PartAHarness />);

    expect(screen.queryByRole('button', { name: /highlight/i })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /strike out/i })).not.toBeInTheDocument();
  });
});