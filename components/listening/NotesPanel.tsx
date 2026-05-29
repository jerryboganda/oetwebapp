'use client';

/**
 * NotesPanel — auto-saving scratch-notes textarea for the diagnostic (§6.2, §25.7).
 *
 * Consumed by:
 *   - app/listening/diagnostic — Part A notes alongside the gap-fill stems.
 *   - app/listening/practice/{sessionId} — scratch notes during practice runs.
 *
 * Wraps {@link useAutoSavingNotes} which debounces writes by ~700ms and posts
 * to `POST /v1/listening-pathway/practice/sessions/{sessionId}/notes`. Shows
 * an inline "Saved Xs ago" / "Saving…" indicator and a live character count
 * so the learner has confidence their notes are persisted.
 */

import { useEffect, useState } from 'react';
import { Save, Loader2, AlertTriangle } from 'lucide-react';
import { useAutoSavingNotes } from '@/hooks/useAutoSavingNotes';

export interface NotesPanelProps {
  sessionId: string;
  questionId?: string;
  placeholder?: string;
  disabled?: boolean;
  initialValue?: string;
  className?: string;
  /** Optional cap for the live character counter. Default 5000. */
  maxLength?: number;
}

const DEFAULT_PLACEHOLDER =
  'Jot down details you hear: names, dates, doses…\n• bullets, - dashes, line breaks all work';

function formatSavedAgo(lastSavedAt: Date | null, now: number): string {
  if (!lastSavedAt) return '';
  const diffSec = Math.max(0, Math.round((now - lastSavedAt.getTime()) / 1000));
  if (diffSec < 5) return 'Saved just now';
  if (diffSec < 60) return `Saved ${diffSec}s ago`;
  const diffMin = Math.round(diffSec / 60);
  if (diffMin < 60) return `Saved ${diffMin}m ago`;
  const diffHr = Math.round(diffMin / 60);
  return `Saved ${diffHr}h ago`;
}

export function NotesPanel({
  sessionId,
  questionId,
  placeholder = DEFAULT_PLACEHOLDER,
  disabled = false,
  initialValue = '',
  className,
  maxLength = 5000,
}: NotesPanelProps) {
  const { value, setValue, isSaving, lastSavedAt, error } = useAutoSavingNotes({
    sessionId,
    questionId,
    initialValue,
    disabled,
  });

  // Re-render every second so the "Saved Xs ago" label stays fresh without
  // requiring the parent to tick.
  const [now, setNow] = useState<number>(() => Date.now());
  useEffect(() => {
    if (!lastSavedAt) return;
    const id = setInterval(() => setNow(Date.now()), 1000);
    return () => clearInterval(id);
  }, [lastSavedAt]);

  const charCount = value.length;
  const overLimit = charCount > maxLength;

  return (
    <div
      className={[
        'flex flex-col gap-2 rounded-xl border border-border bg-surface p-3',
        className ?? '',
      ]
        .filter(Boolean)
        .join(' ')}
    >
      <label className="sr-only" htmlFor={`notes-${questionId ?? sessionId}`}>
        Scratch notes
      </label>
      <textarea
        id={`notes-${questionId ?? sessionId}`}
        rows={8}
        value={value}
        onChange={(e) => setValue(e.target.value)}
        placeholder={placeholder}
        disabled={disabled}
        spellCheck={false}
        className={[
          'w-full resize-y rounded-lg border border-border bg-background-light px-3 py-2',
          'font-mono text-sm leading-relaxed text-navy dark:bg-background-dark',
          'placeholder:text-muted focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary',
          'disabled:cursor-not-allowed disabled:opacity-60',
        ].join(' ')}
        aria-describedby={`notes-status-${questionId ?? sessionId}`}
      />
      <div
        id={`notes-status-${questionId ?? sessionId}`}
        className="flex flex-wrap items-center justify-between gap-2 text-[11px]"
      >
        <span className={overLimit ? 'text-danger font-medium' : 'text-muted'}>
          {charCount} / {maxLength} characters
        </span>
        <span className="inline-flex items-center gap-1 text-muted">
          {error ? (
            <>
              <AlertTriangle aria-hidden="true" className="h-3 w-3 text-warning" />
              <span className="text-warning">Save failed, retrying</span>
            </>
          ) : isSaving ? (
            <>
              <Loader2 aria-hidden="true" className="h-3 w-3 animate-spin" />
              <span>Saving…</span>
            </>
          ) : lastSavedAt ? (
            <>
              <Save aria-hidden="true" className="h-3 w-3 text-success" />
              <span>{formatSavedAgo(lastSavedAt, now)}</span>
            </>
          ) : (
            <span className="opacity-60">Notes auto-save as you type</span>
          )}
        </span>
      </div>
    </div>
  );
}

export default NotesPanel;
