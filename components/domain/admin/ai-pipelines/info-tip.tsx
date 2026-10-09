'use client';

import { useId, useState, type ReactNode } from 'react';
import { Info } from 'lucide-react';

/**
 * A small (i) control that explains what a switch, field or figure does and what happens when it is changed
 * (owner directive 2026-10-10: every important control carries its own explanation). Opens on hover, focus or tap,
 * closes on Escape and blur, and is reachable by keyboard and screen readers.
 */
export function InfoTip({ label, children }: { label: string; children: ReactNode }) {
  const [open, setOpen] = useState(false);
  const id = useId();

  return (
    <span className="relative inline-flex align-middle print:hidden">
      <button
        type="button"
        aria-label={`About ${label}`}
        aria-expanded={open}
        aria-describedby={open ? id : undefined}
        onClick={() => setOpen((v) => !v)}
        onBlur={() => setOpen(false)}
        onMouseEnter={() => setOpen(true)}
        onMouseLeave={() => setOpen(false)}
        onKeyDown={(e) => {
          if (e.key === 'Escape') setOpen(false);
        }}
        className="inline-flex h-5 w-5 items-center justify-center rounded-full text-admin-fg-muted hover:text-admin-fg-strong focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[var(--admin-primary)]"
      >
        <Info className="h-4 w-4" aria-hidden="true" />
      </button>
      {open && (
        <span
          role="tooltip"
          id={id}
          className="absolute left-1/2 top-full z-30 mt-1 w-72 max-w-[80vw] -translate-x-1/2 rounded-admin-lg border border-admin-border bg-admin-bg-surface p-3 text-left text-xs font-normal normal-case leading-relaxed tracking-normal text-admin-fg-default shadow-lg"
        >
          {children}
        </span>
      )}
    </span>
  );
}
