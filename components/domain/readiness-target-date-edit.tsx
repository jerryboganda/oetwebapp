'use client';

import { useState } from 'react';
import { Calendar, Check, Pencil, X } from 'lucide-react';
import { updateUserProfile } from '@/lib/api';

interface ReadinessTargetDateEditProps {
  initialDate: string;
  onSaved?: (newDate: string) => void;
}

export function ReadinessTargetDateEdit({ initialDate, onSaved }: ReadinessTargetDateEditProps) {
  const [editing, setEditing] = useState(false);
  const [value, setValue] = useState(initialDate);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState('');

  async function handleSave() {
    setSaving(true);
    setError('');
    try {
      await updateUserProfile({ examDate: value });
      setEditing(false);
      onSaved?.(value);
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Could not save target date.');
    } finally {
      setSaving(false);
    }
  }

  if (!editing) {
    return (
      <button
        type="button"
        onClick={() => setEditing(true)}
        className="-ms-2 inline-flex min-h-11 items-center gap-1.5 rounded-control px-2 text-xs font-bold text-primary transition-colors hover-primary focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary"
      >
        <Pencil className="h-3 w-3" aria-hidden="true" />
        Edit target date
      </button>
    );
  }

  return (
    <div className="flex flex-col gap-2">
      <div className="flex flex-wrap items-center gap-2">
        <Calendar className="h-3.5 w-3.5 text-muted" aria-hidden="true" />
        <input
          type="date"
          value={value}
          onChange={(e) => setValue(e.target.value)}
          aria-label="Target date"
          className="min-h-11 rounded-control border border-border bg-surface px-2 text-sm text-navy focus:border-primary focus:outline-none focus:ring-2 focus:ring-primary/20 sm:min-h-9"
        />
        <button
          type="button"
          onClick={handleSave}
          disabled={saving || !value}
          className="inline-flex h-11 w-11 items-center justify-center rounded-control bg-success/10 text-success-strong transition-colors hover:bg-success/20 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary disabled:opacity-50 sm:h-9 sm:w-9"
          aria-label="Save target date"
        >
          <Check className="h-3.5 w-3.5" aria-hidden="true" />
        </button>
        <button
          type="button"
          onClick={() => { setEditing(false); setValue(initialDate); setError(''); }}
          disabled={saving}
          className="inline-flex h-11 w-11 items-center justify-center rounded-control bg-danger/10 text-danger-strong transition-colors hover:bg-danger/20 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary sm:h-9 sm:w-9"
          aria-label="Cancel"
        >
          <X className="h-3.5 w-3.5" aria-hidden="true" />
        </button>
      </div>
      {error && <p className="text-2xs text-danger-strong" role="alert">{error}</p>}
    </div>
  );
}
