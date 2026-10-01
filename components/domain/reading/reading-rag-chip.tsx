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
    dot: 'bg-success',
    chip: 'bg-success/10 text-success-strong border-success/20',
    label: 'Green',
  },
  amber: {
    dot: 'bg-warning',
    chip: 'bg-warning/10 text-warning-strong border-warning/20',
    label: 'Amber',
  },
  red: {
    dot: 'bg-danger',
    chip: 'bg-danger/10 text-danger-strong border-danger/20',
    label: 'Red',
  },
  unknown: {
    dot: 'bg-slate-400',
    chip: 'bg-background-light text-muted border-border',
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
