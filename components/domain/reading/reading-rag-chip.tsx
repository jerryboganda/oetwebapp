'use client';

import { cn } from '@/lib/utils';

/**
 * ReadingRagChip — RAG (Red / Amber / Green) status chip for cohort analytics.
 *
 * The colour is driven entirely by the `rag` value returned by the backend
 * (`green` = pass, `amber` = one band below, `red` = below). Thresholds are
 * NEVER computed on the client — we only map the server verdict to a colour.
 *
 * Light + dark parity is achieved with neutral Tailwind tokens so the chip
 * reads correctly inside both the admin (`--admin-*`) and expert/learner
 * surfaces without leaking either token system.
 */

export type ReadingRag = 'green' | 'amber' | 'red' | 'unknown';

function normalizeRag(rag: string): ReadingRag {
  const value = rag.trim().toLowerCase();
  if (value === 'green' || value === 'pass') return 'green';
  if (value === 'amber' || value === 'warning') return 'amber';
  if (value === 'red' || value === 'fail') return 'red';
  return 'unknown';
}

const RAG_STYLES: Record<ReadingRag, { dot: string; chip: string; label: string }> = {
  green: {
    dot: 'bg-emerald-500',
    chip: 'bg-emerald-50 text-emerald-700 border-emerald-200 dark:bg-emerald-950/40 dark:text-emerald-300 dark:border-emerald-800',
    label: 'Green',
  },
  amber: {
    dot: 'bg-amber-500',
    chip: 'bg-amber-50 text-amber-800 border-amber-200 dark:bg-amber-950/40 dark:text-amber-300 dark:border-amber-800',
    label: 'Amber',
  },
  red: {
    dot: 'bg-red-500',
    chip: 'bg-red-50 text-red-700 border-red-200 dark:bg-red-950/40 dark:text-red-300 dark:border-red-800',
    label: 'Red',
  },
  unknown: {
    dot: 'bg-slate-400',
    chip: 'bg-slate-50 text-slate-600 border-slate-200 dark:bg-slate-900 dark:text-slate-400 dark:border-slate-700',
    label: 'No attempt',
  },
};

export interface ReadingRagChipProps {
  /** Raw RAG verdict from the API. */
  rag: string;
  /** Optional override label; defaults to the canonical RAG word. */
  label?: string;
  className?: string;
}

export function ReadingRagChip({ rag, label, className }: ReadingRagChipProps) {
  const variant = normalizeRag(rag);
  const styles = RAG_STYLES[variant];
  const text = label ?? styles.label;

  return (
    <span
      data-rag={variant}
      role="status"
      className={cn(
        'inline-flex items-center gap-1.5 rounded-full border px-2.5 py-0.5 text-xs font-medium leading-none whitespace-nowrap',
        styles.chip,
        className,
      )}
    >
      <span className={cn('h-1.5 w-1.5 rounded-full', styles.dot)} aria-hidden="true" />
      {text}
    </span>
  );
}
