'use client';

import { Ban } from 'lucide-react';

import { Card, CardContent, CardHeader, CardTitle } from '@/components/admin/ui/card';

/**
 * The one honest way to render a signal that has no data source.
 *
 * SAMI §13.2 asks for several things the platform does not instrument (per-answer
 * correctness, a persisted hallucination flag, a per-turn retrieval trace, a
 * confidence score). The temptation is to print `0` for each and move on — that
 * reads as "we measured this and it is fine", which is the opposite of true, and
 * it is exactly the failure mode a quality dashboard must not have.
 *
 * So each uninstrumented signal is listed with the concrete reason it cannot be
 * measured, the table/column that would be needed, and — where it exists — the
 * measurable proxy that stands in for it. The heading says "Not instrumented"
 * rather than "No data", because those are different claims.
 */
export interface NotInstrumentedSignal {
  signal: string;
  detail: string;
}

function humaniseSignal(signal: string): string {
  const words = signal.replace(/_/g, ' ');
  return words.charAt(0).toUpperCase() + words.slice(1);
}

export function NotInstrumentedPanel({
  signals,
  title = 'Not instrumented',
  description = 'These signals have no data source, so no number is shown for them. A zero would read as a measurement.',
}: {
  signals: NotInstrumentedSignal[];
  title?: string;
  description?: string;
}) {
  if (signals.length === 0) return null;

  return (
    <Card data-slot="not-instrumented" className="border-amber-300 dark:border-amber-900/60">
      <CardHeader className="items-start gap-3">
        <div className="min-w-0">
          <CardTitle className="flex items-center gap-2 text-sm">
            <Ban className="h-4 w-4 shrink-0 text-amber-700 dark:text-amber-300" aria-hidden="true" />
            {title}
            <span className="rounded-full bg-[var(--admin-bg-subtle)] px-2 py-0.5 text-xs font-medium tabular-nums text-admin-fg-muted">
              {signals.length}
            </span>
          </CardTitle>
          <p className="mt-1 text-xs text-admin-fg-muted">{description}</p>
        </div>
      </CardHeader>
      <CardContent className="pt-0">
        <dl className="divide-y divide-admin-border border-t border-admin-border">
          {signals.map((item) => (
            <div key={item.signal} className="grid gap-1 py-3 sm:grid-cols-[14rem_1fr] sm:gap-4">
              <dt className="font-mono text-xs font-medium text-admin-fg-strong">
                {humaniseSignal(item.signal)}
              </dt>
              <dd className="text-xs leading-relaxed text-admin-fg-muted">{item.detail}</dd>
            </div>
          ))}
        </dl>
      </CardContent>
    </Card>
  );
}
