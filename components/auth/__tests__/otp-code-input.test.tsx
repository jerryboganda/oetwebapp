import { useState } from 'react';
import { fireEvent, render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it } from 'vitest';
import { OtpCodeInput } from '@/components/auth/otp-code-input';

/**
 * Universal OTP compatibility (owner brief, 15 Sep 2026).
 *
 * The reported iPad/Safari failure was NOT focus or the keyboard — it was the
 * ASCII-only `/\D/g` filter deleting Arabic-Indic digits, so the boxes stayed
 * empty while the learner typed. These pin the closed behaviour across the
 * brief's acceptance list.
 */
function Harness({ initial = '' }: { initial?: string }) {
  const [value, setValue] = useState(initial);
  return (
    <>
      <OtpCodeInput id="code" value={value} onChange={setValue} />
      <output data-testid="value">{value}</output>
    </>
  );
}

const boxes = () => screen.getAllByRole('textbox') as HTMLInputElement[];
const currentValue = () => screen.getByTestId('value').textContent;

describe('OtpCodeInput', () => {
  it('accepts six digits typed one box at a time (English keyboard)', async () => {
    const user = userEvent.setup();
    render(<Harness />);
    await user.click(boxes()[0]);
    await user.keyboard('123456');
    expect(currentValue()).toBe('123456');
  });

  // The reported defect.
  it('accepts Arabic-Indic digits and normalizes them to ASCII', async () => {
    const user = userEvent.setup();
    render(<Harness />);
    await user.click(boxes()[0]);
    await user.keyboard('\u0664\u0665\u0666\u0667\u0668\u0669');
    expect(currentValue()).toBe('456789');
  });

  it('accepts Extended Arabic-Indic (Persian) digits', async () => {
    const user = userEvent.setup();
    render(<Harness />);
    await user.click(boxes()[0]);
    await user.keyboard('\u06F1\u06F2\u06F3\u06F4\u06F5\u06F6');
    expect(currentValue()).toBe('123456');
  });

  it('pastes a complete code into every box, whichever box receives it', async () => {
    const user = userEvent.setup();
    render(<Harness />);
    await user.click(boxes()[3]);
    await user.paste('123456');
    expect(currentValue()).toBe('123456');
  });

  it('pastes an Arabic-digit code', async () => {
    const user = userEvent.setup();
    render(<Harness />);
    await user.click(boxes()[0]);
    await user.paste('\u0661\u0662\u0663\u0664\u0665\u0666');
    expect(currentValue()).toBe('123456');
  });

  it('sanitizes separators out of a pasted code', async () => {
    const user = userEvent.setup();
    render(<Harness />);
    await user.click(boxes()[0]);
    await user.paste('12 34-56');
    expect(currentValue()).toBe('123456');
  });

  // Acceptance #9 — correcting a wrong digit must not get stuck or spill into
  // the next box. maxLength={1} used to block this outright.
  it('replaces a digit when the learner taps a filled box and retypes', async () => {
    const user = userEvent.setup();
    render(<Harness initial="123456" />);
    await user.click(boxes()[2]);
    await user.keyboard('9');
    expect(currentValue()).toBe('129456');
  });

  it('backspaces from an empty box into the previous one', async () => {
    const user = userEvent.setup();
    render(<Harness initial="1234" />);
    await user.click(boxes()[4]);
    await user.keyboard('{Backspace}');
    expect(currentValue()).toBe('123');
  });

  it('advertises one-time-code AutoFill on every box, with numeric intent', () => {
    render(<Harness />);
    for (const box of boxes()) {
      expect(box).toHaveAttribute('autocomplete', 'one-time-code');
      expect(box).toHaveAttribute('inputmode', 'numeric');
      expect(box).toHaveAttribute('type', 'text');
    }
  });

  // Guard against a future change reintroducing desktop keyboard assumptions:
  // digit entry must ride on input events, never on keydown/keyCode.
  it('commits a digit delivered as a raw input event (no keydown at all)', () => {
    render(<Harness />);
    const first = boxes()[0];
    fireEvent.input(first, { target: { value: '7' } });
    expect(currentValue()).toBe('7');
  });
});
