'use client';

/**
 * FeedbackTemplateMenu — tiny quick-insert menu of reusable feedback snippets
 * for a single criterion (spec §14). Clicking a snippet inserts it into the
 * associated comment field (appending to existing text). Origin-aware: the menu
 * opens from the trigger button and closes on outside click / Escape.
 */

import { useEffect, useRef, useState } from 'react';
import { MessageSquareText } from 'lucide-react';
import { cn } from '@/lib/utils';

export interface FeedbackTemplateMenuProps {
  templates: string[];
  onInsert: (snippet: string) => void;
  buttonLabel?: string;
  disabled?: boolean;
}

export function FeedbackTemplateMenu({
  templates,
  onInsert,
  buttonLabel = 'Templates',
  disabled = false,
}: FeedbackTemplateMenuProps) {
  const [open, setOpen] = useState(false);
  const ref = useRef<HTMLDivElement>(null);

  useEffect(() => {
    if (!open) return;
    const onPointerDown = (e: PointerEvent) => {
      if (ref.current && !ref.current.contains(e.target as Node)) setOpen(false);
    };
    const onKey = (e: KeyboardEvent) => {
      if (e.key === 'Escape') setOpen(false);
    };
    document.addEventListener('pointerdown', onPointerDown, true);
    document.addEventListener('keydown', onKey);
    return () => {
      document.removeEventListener('pointerdown', onPointerDown, true);
      document.removeEventListener('keydown', onKey);
    };
  }, [open]);

  if (templates.length === 0) return null;

  return (
    <div ref={ref} className="relative">
      <button
        type="button"
        disabled={disabled}
        onClick={() => setOpen((o) => !o)}
        aria-haspopup="menu"
        aria-expanded={open}
        className="inline-flex items-center gap-1 rounded-md border border-border bg-surface px-2 py-1 text-xs font-semibold text-muted transition-colors hover:bg-background-light disabled:opacity-50 focus:outline-none focus-visible:ring-2 focus-visible:ring-primary/40"
      >
        <MessageSquareText className="h-3.5 w-3.5" aria-hidden="true" /> {buttonLabel}
      </button>
      {open ? (
        <div
          role="menu"
          className={cn(
            'absolute right-0 z-20 mt-1 w-72 origin-top-right rounded-xl border border-border bg-surface p-1 shadow-lg',
            'motion-safe:animate-in motion-safe:fade-in motion-safe:zoom-in-95 motion-safe:duration-150',
          )}
        >
          {templates.map((snippet, i) => (
            <button
              key={i}
              type="button"
              role="menuitem"
              onClick={() => {
                onInsert(snippet);
                setOpen(false);
              }}
              className="block w-full rounded-lg px-2.5 py-1.5 text-left text-xs text-navy transition-colors hover:bg-background-light focus:outline-none focus-visible:bg-background-light"
            >
              {snippet}
            </button>
          ))}
        </div>
      ) : null}
    </div>
  );
}
