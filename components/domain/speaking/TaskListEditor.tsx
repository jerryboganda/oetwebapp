'use client';

/**
 * Editor for an ordered list of printed task bullets on a role-play card.
 *
 * Replaces the old fixed `task1..task5` / `patientTask1..patientTask5` input
 * blocks. Real printed OET cards carry up to nine candidate bullets and seven
 * roleplayer bullets, and the backend stores them unbounded (`TasksJson` /
 * `PatientTasksJson`), so a five-slot form silently truncated real content.
 *
 * Used by StepTasks (wizard), RolePlayCardEditor (candidate face) and
 * InterlocutorScriptEditor (roleplayer face).
 */

import { useCallback, useId } from 'react';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/form-controls';

export interface TaskListEditorProps {
  tasks: string[];
  onChange: (next: string[]) => void;
  /** Label for the add button, e.g. "Add task". */
  addLabel: string;
  /** Accessible noun used in row labels, e.g. "Task" / "Patient task". */
  itemNoun: string;
  placeholder?: string;
  emptyHint?: string;
  /**
   * When set, renders a live `n` count badge that turns green once at least
   * this many non-empty bullets exist (the backend publish gate is 3).
   */
  minRecommended?: number;
  disabled?: boolean;
}

/** Non-empty, trimmed bullets in printed order — what the API should receive. */
export function normaliseTasks(tasks: string[]): string[] {
  return tasks.map((t) => t.trim()).filter((t) => t.length > 0);
}

export function TaskListEditor({
  tasks,
  onChange,
  addLabel,
  itemNoun,
  placeholder,
  emptyHint,
  minRecommended,
  disabled = false,
}: TaskListEditorProps) {
  const fieldPrefix = useId();
  const filledCount = normaliseTasks(tasks).length;

  const update = useCallback(
    (idx: number, value: string) => onChange(tasks.map((t, i) => (i === idx ? value : t))),
    [onChange, tasks],
  );

  const add = useCallback(() => onChange([...tasks, '']), [onChange, tasks]);

  const remove = useCallback(
    (idx: number) => onChange(tasks.filter((_, i) => i !== idx)),
    [onChange, tasks],
  );

  const move = useCallback(
    (idx: number, dir: -1 | 1) => {
      const target = idx + dir;
      if (target < 0 || target >= tasks.length) return;
      const copy = [...tasks];
      const [item] = copy.splice(idx, 1);
      copy.splice(target, 0, item);
      onChange(copy);
    },
    [onChange, tasks],
  );

  const iconButtonClass = 'min-h-11 w-11 shrink-0 px-0 text-base';

  return (
    <div className="space-y-2">
      {minRecommended !== undefined && (
        <div className="flex justify-end">
          <span
            className={`rounded-full px-3 py-1 text-xs font-bold uppercase tracking-wider ${
              filledCount >= minRecommended
                ? 'bg-emerald-500/10 text-emerald-600'
                : 'bg-amber-500/10 text-amber-600'
            }`}
          >
            {filledCount} {filledCount === 1 ? itemNoun.toLowerCase() : `${itemNoun.toLowerCase()}s`}
          </span>
        </div>
      )}

      {tasks.length === 0 && emptyHint && (
        <p className="rounded-2xl border border-dashed border-border px-3 py-4 text-center text-xs text-muted">
          {emptyHint}
        </p>
      )}

      {tasks.map((task, idx) => (
        // eslint-disable-next-line react/no-array-index-key -- rows are positional; value comes from the array
        <div key={idx} className="flex items-start gap-2">
          <span className="mt-4 w-5 shrink-0 text-right text-xs font-semibold tabular-nums text-muted">
            {idx + 1}
          </span>
          <div className="min-w-0 flex-1">
            <Input
              id={`${fieldPrefix}-${idx}`}
              aria-label={`${itemNoun} ${idx + 1}`}
              value={task}
              onChange={(e) => update(idx, e.target.value)}
              placeholder={placeholder}
              maxLength={500}
              disabled={disabled}
            />
          </div>
          <div className="flex shrink-0 items-center gap-0.5">
            <Button
              type="button"
              variant="ghost"
              size="sm"
              className={iconButtonClass}
              onClick={() => move(idx, -1)}
              disabled={disabled || idx === 0}
              aria-label={`Move ${itemNoun.toLowerCase()} ${idx + 1} up`}
            >
              ↑
            </Button>
            <Button
              type="button"
              variant="ghost"
              size="sm"
              className={iconButtonClass}
              onClick={() => move(idx, 1)}
              disabled={disabled || idx === tasks.length - 1}
              aria-label={`Move ${itemNoun.toLowerCase()} ${idx + 1} down`}
            >
              ↓
            </Button>
            <Button
              type="button"
              variant="ghost"
              size="sm"
              className={`${iconButtonClass} text-danger hover:bg-danger/10`}
              onClick={() => remove(idx)}
              disabled={disabled}
              aria-label={`Remove ${itemNoun.toLowerCase()} ${idx + 1}`}
            >
              ✕
            </Button>
          </div>
        </div>
      ))}

      <Button type="button" variant="outline" size="sm" onClick={add} disabled={disabled}>
        + {addLabel}
      </Button>
    </div>
  );
}
