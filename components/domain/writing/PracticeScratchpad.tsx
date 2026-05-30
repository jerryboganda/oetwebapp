'use client';

import { useState } from 'react';
import { cn } from '@/lib/utils';

export interface PracticeScratchpadProps {
  /** Scratch text (controlled by the host). */
  value: string;
  /** Change handler. */
  onChange: (value: string) => void;
  /** Start expanded. Defaults to false (collapsed). */
  defaultOpen?: boolean;
  /** Optional extra className. */
  className?: string;
}

/**
 * PracticeScratchpad — a collapsible planning / notes area.
 *
 * Practice-mode only (spec §20.2); never rendered inside a strict mock.
 * Purely presentational — the host owns the value, and this content is never
 * submitted or graded.
 */
export function PracticeScratchpad({
  value,
  onChange,
  defaultOpen = false,
  className,
}: PracticeScratchpadProps) {
  const [open, setOpen] = useState(defaultOpen);

  return (
    <div className={cn('rounded-2xl border border-dashed border-border bg-background-light/60', className)}>
      <button
        type="button"
        onClick={() => setOpen((o) => !o)}
        aria-expanded={open}
        className="flex w-full items-center justify-between px-3 py-2 text-left text-sm font-bold text-navy"
      >
        <span className="flex items-center gap-2">
          <svg viewBox="0 0 16 16" className="h-4 w-4 text-muted" fill="none" stroke="currentColor" strokeWidth={1.6} strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
            <path d="M2.5 11.5 11 3l2 2-8.5 8.5L2 14z" />
          </svg>
          Planning scratchpad
          <span className="rounded bg-navy/10 px-1.5 py-0.5 text-[10px] font-bold uppercase tracking-wide text-muted">
            Not graded
          </span>
        </span>
        <svg
          viewBox="0 0 16 16"
          className={cn('h-4 w-4 text-muted transition-transform duration-150 motion-reduce:transition-none', open && 'rotate-180')}
          fill="none"
          stroke="currentColor"
          strokeWidth={1.8}
          strokeLinecap="round"
          strokeLinejoin="round"
          aria-hidden="true"
        >
          <path d="m4 6 4 4 4-4" />
        </svg>
      </button>
      {open ? (
        <div className="px-3 pb-3">
          <textarea
            value={value}
            onChange={(e) => onChange(e.target.value)}
            rows={4}
            placeholder="Jot a quick plan — key points, order, who/what/when. This is never submitted."
            className="w-full resize-y rounded-xl border border-border bg-surface p-2.5 text-sm leading-6 text-navy outline-none transition-colors duration-150 placeholder:text-muted/60 focus:border-primary focus:ring-2 focus:ring-primary/20"
            aria-label="Planning scratchpad (not graded)"
          />
        </div>
      ) : null}
    </div>
  );
}
