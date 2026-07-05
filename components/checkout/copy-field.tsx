'use client';

import { useState } from 'react';
import { Check, Copy } from 'lucide-react';
import { cn } from '@/lib/utils';

/**
 * A labelled read-only value with a one-tap copy button. Used for bank / wallet
 * details on the checkout page so learners never have to hand-retype an IBAN or
 * account number. Clipboard failures are swallowed — the value stays visible for
 * manual selection.
 */
export function CopyField({
  label,
  value,
  mono = true,
  className,
}: {
  label: string;
  value: string;
  mono?: boolean;
  className?: string;
}) {
  const [copied, setCopied] = useState(false);

  const copy = async () => {
    try {
      await navigator.clipboard.writeText(value);
      setCopied(true);
      window.setTimeout(() => setCopied(false), 1600);
    } catch {
      /* clipboard unavailable — the value is on screen to copy manually */
    }
  };

  return (
    <div
      className={cn(
        'flex items-center justify-between gap-3 rounded-xl border border-border bg-surface px-3.5 py-2.5',
        className,
      )}
    >
      <div className="min-w-0">
        <p className="text-[11px] font-semibold uppercase tracking-wide text-muted">{label}</p>
        <p
          className={cn('mt-0.5 truncate text-sm text-navy', mono ? 'font-mono tracking-tight' : 'font-medium')}
          title={value}
        >
          {value}
        </p>
      </div>
      <button
        type="button"
        onClick={copy}
        aria-label={`Copy ${label}`}
        className={cn(
          'inline-flex shrink-0 items-center gap-1.5 rounded-lg border px-2.5 py-1.5 text-xs font-semibold transition',
          copied
            ? 'border-success/40 bg-success/10 text-success'
            : 'border-border text-muted hover:border-primary/50 hover:text-primary',
        )}
      >
        {copied ? <Check className="h-3.5 w-3.5" /> : <Copy className="h-3.5 w-3.5" />}
        {copied ? 'Copied' : 'Copy'}
      </button>
    </div>
  );
}
