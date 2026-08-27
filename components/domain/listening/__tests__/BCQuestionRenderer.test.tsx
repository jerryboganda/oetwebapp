import { describe, expect, it, vi } from 'vitest';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { BCQuestionRenderer } from '../BCQuestionRenderer';

const OPTIONS = ['Increase fluids', 'Reduce the dose', 'Refer to a specialist'];

describe('BCQuestionRenderer', () => {
  it('renders the question prompt as a clean heading and displays option prose', () => {
    render(
      <BCQuestionRenderer
        questionNumber={25}
        partLabel="PART B"
        prompt="What does the nurse advise?"
        options={OPTIONS}
        optionKeys={['A', 'B', 'C']}
        value=""
        onChange={vi.fn()}
      />,
    );
    const heading = screen.getByRole('heading', { level: 3, name: 'What does the nurse advise?' });
    expect(heading).toBeInTheDocument();
    expect(screen.getByText('Reduce the dose')).toBeInTheDocument();
  });

  it('renders the Flag button and completely omits any Stem highlighter button', () => {
    render(
      <BCQuestionRenderer
        questionNumber={25}
        partLabel="PART B"
        prompt="What does the nurse advise?"
        options={OPTIONS}
        optionKeys={['A', 'B', 'C']}
        value=""
        onChange={vi.fn()}
      />,
    );
    // Flag button is present
    expect(screen.getByRole('button', { name: /flag question 25 for review/i })).toBeInTheDocument();
    // Stem highlighter button is absent
    expect(screen.queryByRole('button', { name: /stem/i })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /highlight/i })).not.toBeInTheDocument();
  });

  it('submits the option KEY (letter), not the display text', async () => {
    const onChange = vi.fn();
    const user = userEvent.setup();
    render(
      <BCQuestionRenderer
        questionNumber={25}
        partLabel="PART B"
        prompt="What does the nurse advise?"
        options={OPTIONS}
        optionKeys={['A', 'B', 'C']}
        value=""
        onChange={onChange}
      />,
    );
    await user.click(screen.getByText('Reduce the dose'));
    expect(onChange).toHaveBeenCalledWith('B');
    expect(onChange).not.toHaveBeenCalledWith('Reduce the dose');
  });

  it('marks the option selected by KEY value', () => {
    render(
      <BCQuestionRenderer
        questionNumber={25}
        partLabel="PART B"
        prompt="What does the nurse advise?"
        options={OPTIONS}
        optionKeys={['A', 'B', 'C']}
        value="B"
        onChange={vi.fn()}
      />,
    );
    const radios = screen.getAllByRole('radio');
    expect(radios[1].getAttribute('aria-checked')).toBe('true');
    expect(radios[0].getAttribute('aria-checked')).toBe('false');
  });

  it('falls back to the derived letter when optionKeys is absent (legacy DTO)', async () => {
    const onChange = vi.fn();
    const user = userEvent.setup();
    render(
      <BCQuestionRenderer
        questionNumber={25}
        partLabel="PART B"
        prompt="What does the nurse advise?"
        options={OPTIONS}
        value=""
        onChange={onChange}
      />,
    );
    await user.click(screen.getByText('Refer to a specialist'));
    expect(onChange).toHaveBeenCalledWith('C');
  });
});
