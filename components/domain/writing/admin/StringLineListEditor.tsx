'use client';

import { useCallback } from 'react';

import { Button } from '@/components/admin/ui/button';
import { IconButton } from './IconButton';

interface StringLineListEditorProps {
  lines: string[];
  onChange: (next: string[]) => void;
  /** Label for the add button, e.g. "Add note" / "Add instruction line". */
  addLabel: string;
  /** Placeholder for each row input. */
  placeholder?: string;
  /** Shown when the list is empty. */
  emptyHint?: string;
  /** Use a multi-line textarea per row instead of a single-line input. */
  multiline?: boolean;
  /** Accessible noun for aria-labels, e.g. "note". */
  itemNoun?: string;
}

/**
 * Reorderable editor for a flat list of strings. Powers case-note items and
 * the fixed instruction lines. Up/down move buttons keep it keyboard-friendly
 * (no drag-and-drop dependency). Uses raw inputs (not the labelled
 * form-controls) since each row is a bare field, not a labelled control.
 */
export function StringLineListEditor({
  lines,
  onChange,
  addLabel,
  placeholder,
  emptyHint,
  multiline = false,
  itemNoun = 'line',
}: StringLineListEditorProps) {
  const update = useCallback(
    (idx: number, value: string) => {
      onChange(lines.map((l, i) => (i === idx ? value : l)));
    },
    [lines, onChange],
  );

  const add = useCallback(() => {
    onChange([...lines, '']);
  }, [lines, onChange]);

  const remove = useCallback(
    (idx: number) => {
      onChange(lines.filter((_, i) => i !== idx));
    },
    [lines, onChange],
  );

  const move = useCallback(
    (idx: number, dir: -1 | 1) => {
      const next = idx + dir;
      if (next < 0 || next >= lines.length) return;
      const copy = [...lines];
      const [item] = copy.splice(idx, 1);
      copy.splice(next, 0, item);
      onChange(copy);
    },
    [lines, onChange],
  );

  const sharedInputClass =
    'block w-full rounded-xl border border-slate-300 bg-white px-3 py-2 text-sm text-slate-900 shadow-sm outline-none transition-[border-color,box-shadow] duration-150 placeholder:text-slate-400 focus:border-violet-500 focus:ring-2 focus:ring-violet-200 motion-reduce:transition-none';

  return (
    <div className="space-y-2">
      {lines.length === 0 && emptyHint && (
        <p className="rounded-lg border border-dashed border-slate-300 bg-slate-50 px-3 py-4 text-center text-xs text-slate-500">
          {emptyHint}
        </p>
      )}
      {lines.map((line, idx) => (
        <div key={idx} className="flex items-start gap-2">
          <span className="mt-2.5 w-5 shrink-0 text-right text-xs font-medium tabular-nums text-slate-400">
            {idx + 1}
          </span>
          {multiline ? (
            <textarea
              value={line}
              onChange={(e) => update(idx, e.target.value)}
              placeholder={placeholder}
              rows={2}
              aria-label={`${itemNoun} ${idx + 1}`}
              className={sharedInputClass}
            />
          ) : (
            <input
              value={line}
              onChange={(e) => update(idx, e.target.value)}
              placeholder={placeholder}
              aria-label={`${itemNoun} ${idx + 1}`}
              className={sharedInputClass}
            />
          )}
          <div className="flex shrink-0 items-center gap-0.5 pt-1">
            <IconButton
              onClick={() => move(idx, -1)}
              disabled={idx === 0}
              aria-label={`Move ${itemNoun} up`}
            >
              ↑
            </IconButton>
            <IconButton
              onClick={() => move(idx, 1)}
              disabled={idx === lines.length - 1}
              aria-label={`Move ${itemNoun} down`}
            >
              ↓
            </IconButton>
            <IconButton
              tone="danger"
              onClick={() => remove(idx)}
              aria-label={`Remove ${itemNoun}`}
            >
              ✕
            </IconButton>
          </div>
        </div>
      ))}
      <Button variant="secondary" size="sm" onClick={add}>
        + {addLabel}
      </Button>
    </div>
  );
}
