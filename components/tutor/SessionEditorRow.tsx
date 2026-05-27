'use client';

import { useState } from 'react';
import { CalendarClock, Pencil, Save, Trash2, X } from 'lucide-react';

import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/form-controls';
import type { LiveClassSessionSummary } from '@/lib/api';

function toLocalInputValue(iso: string): string {
  const d = new Date(iso);
  const offset = d.getTimezoneOffset() * 60_000;
  return new Date(d.getTime() - offset).toISOString().slice(0, 16);
}

function formatDate(iso: string) {
  return new Intl.DateTimeFormat('en-AU', {
    weekday: 'short',
    day: 'numeric',
    month: 'short',
    hour: '2-digit',
    minute: '2-digit',
  }).format(new Date(iso));
}

function statusVariant(status: string) {
  switch (status) {
    case 'Scheduled':
      return 'info' as const;
    case 'Live':
      return 'success' as const;
    case 'Completed':
      return 'muted' as const;
    case 'Cancelled':
      return 'danger' as const;
    default:
      return 'outline' as const;
  }
}

export interface SessionEditorRowProps {
  session: LiveClassSessionSummary;
  onSave: (payload: { scheduledStartAt?: string; capacity?: number }) => Promise<void>;
  onCancel: () => Promise<void>;
}

export function SessionEditorRow({ session, onSave, onCancel }: SessionEditorRowProps) {
  const [editing, setEditing] = useState(false);
  const [busy, setBusy] = useState(false);
  const [startsAt, setStartsAt] = useState(toLocalInputValue(session.scheduledStartAt));
  const [capacity, setCapacity] = useState(session.capacity);

  const cancelled = session.status === 'Cancelled';

  async function handleSave() {
    setBusy(true);
    try {
      await onSave({
        scheduledStartAt: new Date(startsAt).toISOString(),
        capacity,
      });
      setEditing(false);
    } finally {
      setBusy(false);
    }
  }

  async function handleCancelSession() {
    if (!window.confirm('Cancel this session? Enrolled learners will be notified.')) return;
    setBusy(true);
    try {
      await onCancel();
    } finally {
      setBusy(false);
    }
  }

  if (editing) {
    return (
      <tr className="bg-background-light">
        <td className="px-4 py-3">
          <Input
            type="datetime-local"
            value={startsAt}
            onChange={(e) => setStartsAt(e.target.value)}
            className="text-xs"
          />
        </td>
        <td className="px-4 py-3">
          <Input
            type="number"
            min={1}
            max={500}
            value={capacity}
            onChange={(e) => setCapacity(Number(e.target.value))}
            className="w-20 text-xs"
          />
        </td>
        <td className="px-4 py-3 text-muted">{session.enrolledCount}</td>
        <td className="px-4 py-3">
          <Badge variant={statusVariant(session.status)}>{session.status}</Badge>
        </td>
        <td className="px-4 py-3">
          <div className="flex gap-2">
            <Button type="button" variant="primary" size="sm" loading={busy} onClick={() => void handleSave()}>
              <Save className="h-3.5 w-3.5" /> Save
            </Button>
            <Button type="button" variant="ghost" size="sm" onClick={() => setEditing(false)} disabled={busy}>
              <X className="h-3.5 w-3.5" /> Cancel
            </Button>
          </div>
        </td>
      </tr>
    );
  }

  return (
    <tr>
      <td className="px-4 py-3">
        <span className="flex items-center gap-1.5 text-sm text-navy">
          <CalendarClock className="h-4 w-4 text-muted" />
          {formatDate(session.scheduledStartAt)}
        </span>
      </td>
      <td className="px-4 py-3 text-muted">{session.capacity}</td>
      <td className="px-4 py-3 text-muted">{session.enrolledCount}</td>
      <td className="px-4 py-3">
        <Badge variant={statusVariant(session.status)}>{session.status}</Badge>
      </td>
      <td className="px-4 py-3">
        <div className="flex gap-2">
          {!cancelled ? (
            <>
              <Button type="button" variant="outline" size="sm" onClick={() => setEditing(true)} disabled={busy}>
                <Pencil className="h-3.5 w-3.5" /> Edit
              </Button>
              <Button
                type="button"
                variant="ghost"
                size="sm"
                loading={busy}
                onClick={() => void handleCancelSession()}
              >
                <Trash2 className="h-3.5 w-3.5" /> Cancel
              </Button>
            </>
          ) : (
            <span className="text-xs text-muted">No actions</span>
          )}
        </div>
      </td>
    </tr>
  );
}
