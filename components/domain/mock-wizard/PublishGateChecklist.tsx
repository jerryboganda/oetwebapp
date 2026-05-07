'use client';

import { Check, X } from 'lucide-react';
import { cn } from '@/lib/utils';

export interface PublishGateRow {
  label: string;
  ok: boolean;
  detail?: string;
}

export interface PublishGateChecklistProps {
  title: string;
  rows: PublishGateRow[];
}

export function PublishGateChecklist({ title, rows }: PublishGateChecklistProps) {
  return (
    <div className="rounded-2xl border border-border bg-surface p-4">
      <p className="text-sm font-bold text-navy">{title}</p>
      <ul className="mt-3 space-y-2">
        {rows.map((row, idx) => (
          <li
            key={`${row.label}-${idx}`}
            className="flex items-start gap-2 text-sm text-navy"
          >
            <span
              aria-hidden
              className={cn(
                'mt-0.5 inline-flex h-5 w-5 shrink-0 items-center justify-center rounded-full',
                row.ok ? 'bg-emerald-100 text-emerald-700' : 'bg-red-100 text-red-700',
              )}
            >
              {row.ok ? <Check className="h-3.5 w-3.5" /> : <X className="h-3.5 w-3.5" />}
            </span>
            <span className="flex-1">
              <span className={cn(row.ok ? 'text-navy' : 'text-red-700')}>{row.label}</span>
              {row.detail ? <span className="ml-1 text-xs text-muted">— {row.detail}</span> : null}
            </span>
          </li>
        ))}
      </ul>
    </div>
  );
}
