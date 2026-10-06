'use client';

import * as React from 'react';

import { Textarea, type TextareaProps } from './textarea';

/** Split raw textarea text into trimmed, non-blank lines (one list entry per line). */
export function parseLines(raw: string): string[] {
  return raw
    .split(/\r?\n/)
    .map((line) => line.trim())
    .filter(Boolean);
}

export interface LinesTextareaProps
  extends Omit<TextareaProps, 'value' | 'defaultValue' | 'onChange'> {
  /** Initial lines. Read ONCE on mount: remount with a new `key` to re-seed after a reset or reload. */
  lines: readonly string[] | undefined;
  /** Called on every change with the live parsed lines, so a save never depends on blur having run. */
  onLinesChange: (lines: string[]) => void;
}

/**
 * Admin Textarea for "one entry per line" lists.
 *
 * The displayed value is the RAW draft the person is typing, never re-derived from the
 * parsed array: Enter, trailing spaces, blank separator lines and pasted multi-line text
 * all survive while typing. The parsed (trimmed, blank-free) lines are propagated live,
 * and blur normalises the draft to exactly what will be saved. No form, no Enter handler.
 */
export const LinesTextarea = React.forwardRef<HTMLTextAreaElement, LinesTextareaProps>(
  function LinesTextarea({ lines, onLinesChange, onBlur, enterKeyHint = 'enter', ...rest }, ref) {
    const [draft, setDraft] = React.useState(() => (lines ?? []).join('\n'));

    return (
      <Textarea
        {...rest}
        ref={ref}
        enterKeyHint={enterKeyHint}
        value={draft}
        onChange={(event) => {
          const raw = event.target.value;
          setDraft(raw);
          onLinesChange(parseLines(raw));
        }}
        onBlur={(event) => {
          setDraft((current) => parseLines(current).join('\n'));
          onBlur?.(event);
        }}
      />
    );
  },
);
LinesTextarea.displayName = 'LinesTextarea';
