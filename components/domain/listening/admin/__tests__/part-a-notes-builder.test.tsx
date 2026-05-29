import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { useState } from 'react';
import {
  PART_A_GAP_MARKER,
  PartANotesBuilder,
  sanitizePastedStem,
} from '@/components/domain/listening/admin/PartANotesBuilder';

// Mirror of the renderer's BLANK_PATTERN — the marker the builder emits MUST
// be recognised by this so the learner renderer splits a gap field on it.
const BLANK_PATTERN = /(____+|\[\s*\]|\{\{\s*blank\s*\}\})/i;

function Harness({ initial = '', onValue }: { initial?: string; onValue?: (v: string) => void }) {
  const [value, setValue] = useState(initial);
  return (
    <PartANotesBuilder
      value={value}
      onChange={(next) => {
        setValue(next);
        onValue?.(next);
      }}
      questionNumber={3}
      partLabel="Part A1"
    />
  );
}

describe('PartANotesBuilder', () => {
  // The builder restores the caret inside a requestAnimationFrame after each
  // toolbar insert. Run it synchronously so the manual setSelectionRange in
  // these tests is always the final caret write before the next click — keeps
  // multi-gap insertion deterministic and frame-timing independent.
  beforeEach(() => {
    vi.spyOn(window, 'requestAnimationFrame').mockImplementation((cb) => {
      cb(0);
      return 0;
    });
  });

  afterEach(() => {
    vi.restoreAllMocks();
  });

  it('emits the canonical gap marker that matches the renderer BLANK_PATTERN', () => {
    expect(PART_A_GAP_MARKER).toBe('____');
    expect(BLANK_PATTERN.test(PART_A_GAP_MARKER)).toBe(true);
  });

  it('inserts a gap marker at the caret and fires onChange with the updated text', async () => {
    const user = userEvent.setup();
    const onValue = vi.fn();
    render(<Harness initial="Pain located in " onValue={onValue} />);

    const editor = screen.getByLabelText(/stem \(note-completion\)/i) as HTMLTextAreaElement;
    // Caret at end of the seeded text.
    editor.setSelectionRange(editor.value.length, editor.value.length);

    await user.click(screen.getByRole('button', { name: /insert gap/i }));

    expect(onValue).toHaveBeenCalledWith('Pain located in ____');
    expect(editor.value).toBe('Pain located in ____');
    expect(BLANK_PATTERN.test(editor.value)).toBe(true);
  });

  it('supports multiple, separated gaps', async () => {
    const user = userEvent.setup();
    render(<Harness initial="Onset  dose " />);

    const editor = screen.getByLabelText(/stem \(note-completion\)/i) as HTMLTextAreaElement;
    const gap = screen.getByRole('button', { name: /insert gap/i });

    // Insert after "Onset " (position 6) …
    editor.setSelectionRange(6, 6);
    await user.click(gap);
    // … then at the very end, leaving text between the two markers.
    editor.setSelectionRange(editor.value.length, editor.value.length);
    await user.click(gap);

    // Two distinct gaps separated by " dose ".
    expect(editor.value).toBe('Onset ____ dose ____');
    // The renderer would split this into two gap fields, not one merged run.
    const segments = editor.value.split(BLANK_PATTERN).filter(Boolean);
    expect(segments.filter((s) => BLANK_PATTERN.test(s))).toHaveLength(2);
  });

  it('renders a live preview whose gap count tracks the authored markers', async () => {
    const user = userEvent.setup();
    render(<Harness initial="Pain in " />);

    const preview = screen.getByTestId('part-a-notes-preview');
    // No authored gap yet → renderer shows the fallback single answer box.
    expect(within(preview).getAllByRole('textbox')).toHaveLength(1);

    const editor = screen.getByLabelText(/stem \(note-completion\)/i) as HTMLTextAreaElement;
    const gap = screen.getByRole('button', { name: /insert gap/i });

    editor.setSelectionRange(editor.value.length, editor.value.length);
    await user.click(gap);
    // One authored gap → one inline answer field.
    expect(within(preview).getAllByRole('textbox')).toHaveLength(1);

    // Insert a second gap at the start so the two markers stay separated by
    // text (adjacent ____ runs would merge into a single blank).
    editor.setSelectionRange(0, 0);
    await user.click(gap);
    // Two authored gaps → two inline answer fields inside the preview.
    expect(within(preview).getAllByRole('textbox')).toHaveLength(2);
  });

  it('keeps the live preview non-interactive (pointer-events disabled)', () => {
    render(<Harness initial="Pain in ____" />);
    const preview = screen.getByTestId('part-a-notes-preview');
    expect(preview.className).toContain('pointer-events-none');
  });

  it('inserts a heading on its own line', async () => {
    const user = userEvent.setup();
    render(<Harness initial="Patient notes" />);

    const editor = screen.getByLabelText(/stem \(note-completion\)/i) as HTMLTextAreaElement;
    editor.setSelectionRange(editor.value.length, editor.value.length);
    await user.click(screen.getByRole('button', { name: /heading/i }));

    expect(editor.value).toBe('Patient notes\n## Heading');
  });

  it('sanitizes pasted rich content into safe plain text with structure preserved', () => {
    const dirty = '<p>Pain located in</p><script>alert(1)</script><p>the chest</p>';
    const cleaned = sanitizePastedStem(dirty);
    expect(cleaned).not.toMatch(/<script/i);
    expect(cleaned).not.toMatch(/alert\(1\)/);
    expect(cleaned).toContain('Pain located in');
    expect(cleaned).toContain('the chest');
  });

  it('neutralises a pasted script via the paste handler', () => {
    const onValue = vi.fn();
    render(<Harness onValue={onValue} />);
    const editor = screen.getByLabelText(/stem \(note-completion\)/i) as HTMLTextAreaElement;

    const data = {
      getData: (type: string) =>
        type === 'text/html'
          ? '<p>Dose: <strong>5mg</strong></p><script>steal()</script>'
          : 'Dose: 5mg',
    };
    fireEvent.paste(editor, { clipboardData: data });

    const last = onValue.mock.calls.at(-1)?.[0] as string;
    expect(last).toContain('Dose: 5mg');
    expect(last).not.toMatch(/<script|steal\(\)/i);
  });
});
