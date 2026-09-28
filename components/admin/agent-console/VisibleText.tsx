'use client';

import { Fragment, useMemo } from 'react';
import { cn } from '@/lib/utils';
import { revealHiddenCharacters, type RevealLevel } from '@/lib/owner-agent/text-safety';

const CATEGORY_STYLES: Record<string, string> = {
  bidi: 'bg-red-600 text-white',
  tag: 'bg-red-600 text-white',
  control: 'bg-amber-500 text-slate-950',
  invisible: 'bg-amber-500 text-slate-950',
  space: 'bg-slate-500 text-white',
};

export interface VisibleTextProps {
  text: string;
  level?: RevealLevel;
  className?: string;
}

/**
 * Renders untrusted text with every control, bidirectional-override,
 * zero-width and tag character replaced by a labelled marker such as
 * `U+202E RLO`. Newlines keep their line break after the marker. Output is
 * plain React text (escaped).
 * - `strict` (commands, paths, ids): forced left-to-right in logical order
 *   (`unicode-bidi: bidi-override`) so right-to-left letters cannot visually
 *   reorder a command, and long tokens break anywhere.
 * - `prose` (messages, reasons): bidi controls are already revealed as
 *   markers, so the text is only isolated from its surroundings; natural
 *   right-to-left prose (e.g. Arabic) still reads correctly.
 */
export function VisibleText({ text, level = 'strict', className }: VisibleTextProps) {
  const segments = useMemo(() => revealHiddenCharacters(text ?? '', level), [text, level]);
  const strict = level === 'strict';
  return (
    <span
      className={cn('whitespace-pre-wrap', strict ? 'break-all' : 'break-words', className)}
      dir="ltr"
      style={{ unicodeBidi: strict ? 'bidi-override' : 'isolate' }}
    >
      {segments.map((segment, index) =>
        segment.kind === 'text' ? (
          <Fragment key={index}>{segment.value}</Fragment>
        ) : (
          <Fragment key={index}>
            <span
              className={cn(
                'mx-px inline-block rounded px-1 align-baseline font-mono text-3xs font-semibold leading-4',
                CATEGORY_STYLES[segment.category] ?? CATEGORY_STYLES.control,
              )}
              title={`${segment.label} ${segment.name}`}
              data-hidden-char={segment.label}
            >
              {segment.label}
            </span>
            {segment.lineBreak ? '\n' : null}
          </Fragment>
        ),
      )}
    </span>
  );
}
