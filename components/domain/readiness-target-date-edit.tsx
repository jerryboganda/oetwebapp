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
        className="inline-flex items-center gap-1.5 text-xs font-bold text-primary hover:underline"
      >
        <Pencil className="w-3 h-3" />
        Edit target date
      </button>
    );
  }

  return (
    <div className="flex flex-col gap-2">
      <div className="flex items-center gap-2">
        <Calendar className="w-3.5 h-3.5 text-muted" />
        <input
          type="date"
          value={value}
          onChange={(e) => setValue(e.target.value)}
          className="text-xs border border-border rounded-lg px-2 py-1"
        />
        <button
          type="button"
          onClick={handleSave}
          disabled={saving || !value}
          className="inline-flex items-center justify-center h-7 w-7 rounded-lg bg-success/10 text-success hover:bg-success/20 disabled:opacity-50"
          aria-label="Save target date"
        >
          <Check className="w-3.5 h-3.5" />
        </button>
        <button
          type="button"
          onClick={() => { setEditing(false); setValue(initialDate); setError(''); }}
          disabled={saving}
          className="inline-flex items-center justify-center h-7 w-7 rounded-lg bg-danger/10 text-danger hover:bg-danger/20"
          aria-label="Cancel"
        >
          <X className="w-3.5 h-3.5" />
        </button>
      </div>
      {error && <p className="text-[11px] text-danger">{error}</p>}
    </div>
  );
}
