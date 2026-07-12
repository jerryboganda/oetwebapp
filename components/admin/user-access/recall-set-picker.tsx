'use client';

import type { RecallSetTagDto } from '@/lib/user-access';

interface RecallSetPickerProps {
  recallSets: RecallSetTagDto[];
  selectedCodes: string[];
  onChange: (codes: string[]) => void;
  disabled?: boolean;
}

/** Checkbox multiselect over recall-set tags (by `code`), storing selected codes. */
export function RecallSetPicker({ recallSets, selectedCodes, onChange, disabled }: RecallSetPickerProps) {
  const selectedSet = new Set(selectedCodes);

  function toggle(code: string) {
    const next = new Set(selectedSet);
    if (next.has(code)) {
      next.delete(code);
    } else {
      next.add(code);
    }
    onChange(Array.from(next));
  }

  if (recallSets.length === 0) {
    return <p className="text-sm text-muted">No recall set tags found.</p>;
  }

  return (
    <div className="grid max-h-64 grid-cols-1 gap-0.5 overflow-y-auto rounded-2xl border border-border bg-background-light p-3 sm:grid-cols-2">
      {recallSets.map((tag) => (
        <label key={tag.code} className="flex cursor-pointer items-center gap-2 rounded-lg py-1 text-sm hover:bg-admin-bg-subtle">
          <input
            type="checkbox"
            checked={selectedSet.has(tag.code)}
            onChange={() => toggle(tag.code)}
            disabled={disabled}
            className="h-4 w-4 rounded border-border text-primary focus:ring-primary"
          />
          <span className="truncate text-navy">{tag.displayName}</span>
        </label>
      ))}
    </div>
  );
}
