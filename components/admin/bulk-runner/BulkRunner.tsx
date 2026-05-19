'use client';

/**
 * Reusable progress runner for SUBAGENT_E bulk admin pages.
 * Runs an async `run(item)` callback for each row, displays a progress bar
 * + per-row status table, and (best-effort) cancels remaining work when the
 * "Cancel" button is pressed.
 *
 * Used by:
 *   - app/admin/content/mocks/bulk/page.tsx
 *   - app/admin/content/vocabulary/publish-batch/page.tsx
 *   - app/admin/content/papers/republish-drafts/page.tsx
 */

import { useCallback, useMemo, useRef, useState, type ReactNode } from 'react';
import { Button } from '@/components/ui/button';
import { Badge } from '@/components/ui/badge';
import type { BulkRunnerOutcome, BulkRunnerRowState } from '@/lib/types/admin/bulk-ops';

export interface BulkRunnerProps<T> {
  items: T[];
  /** Stable React key per item (must be unique within `items`). */
  getKey: (item: T, index: number) => string;
  /** Renders the descriptive cell for an item row. */
  renderRow: (item: T) => ReactNode;
  /** Per-item async worker. Throwing is treated as `{ ok: false, error }`. */
  run: (item: T) => Promise<BulkRunnerOutcome>;
  /** Optional label for the start button. Default: "Run". */
  startLabel?: string;
  /** Optional callback fired once the run finishes (or is cancelled). */
  onFinished?: (summary: BulkRunnerSummary) => void;
  /**
   * Maximum concurrent in-flight workers. Default 1 — admin write endpoints
   * are rate-limited (PerUserWrite ≈ 60/min) so sequential is the safe default.
   */
  concurrency?: number;
}

export interface BulkRunnerSummary {
  total: number;
  ok: number;
  failed: number;
  warnings: number;
  cancelled: boolean;
}

export function BulkRunner<T>({
  items,
  getKey,
  renderRow,
  run,
  startLabel = 'Run',
  onFinished,
  concurrency = 1,
}: BulkRunnerProps<T>) {
  const [states, setStates] = useState<Record<string, BulkRunnerRowState>>({});
  const [running, setRunning] = useState(false);
  const cancelRef = useRef(false);

  const totals = useMemo<BulkRunnerSummary>(() => {
    let ok = 0;
    let failed = 0;
    let warnings = 0;
    for (const k of Object.keys(states)) {
      const s = states[k];
      if (s.status !== 'done') continue;
      if (s.outcome.ok) ok++; else failed++;
      if (s.outcome.warnings && s.outcome.warnings.length > 0) warnings++;
    }
    return { total: items.length, ok, failed, warnings, cancelled: false };
  }, [states, items.length]);

  const completedCount = useMemo(
    () => Object.values(states).filter(s => s.status === 'done').length,
    [states],
  );

  const start = useCallback(async () => {
    if (running || items.length === 0) return;
    cancelRef.current = false;
    setRunning(true);

    // Seed every row as pending up front so the table renders immediately.
    const initial: Record<string, BulkRunnerRowState> = {};
    items.forEach((item, idx) => {
      initial[getKey(item, idx)] = { status: 'pending' };
    });
    setStates(initial);

    // Sequential or bounded-concurrency processing.
    const queue: Array<{ key: string; item: T }> = items.map((item, idx) => ({
      key: getKey(item, idx),
      item,
    }));

    let cursor = 0;
    const workers: Promise<void>[] = [];
    const workerCount = Math.max(1, Math.min(concurrency, queue.length));

    const next = async (): Promise<void> => {
      while (cursor < queue.length) {
        if (cancelRef.current) return;
        const myIndex = cursor++;
        const { key, item } = queue[myIndex];
        setStates(prev => ({ ...prev, [key]: { status: 'running' } }));
        let outcome: BulkRunnerOutcome;
        try {
          outcome = await run(item);
        } catch (e: unknown) {
          const msg = e instanceof Error ? e.message : String(e);
          outcome = { ok: false, error: msg };
        }
        setStates(prev => ({ ...prev, [key]: { status: 'done', outcome } }));
      }
    };

    for (let i = 0; i < workerCount; i++) workers.push(next());
    await Promise.all(workers);

    setRunning(false);
    const cancelled = cancelRef.current;
    cancelRef.current = false;

    if (onFinished) {
      // Snapshot current states for the summary.
      setStates(prev => {
        let ok = 0;
        let failed = 0;
        let warnings = 0;
        for (const s of Object.values(prev)) {
          if (s.status !== 'done') continue;
          if (s.outcome.ok) ok++; else failed++;
          if (s.outcome.warnings && s.outcome.warnings.length > 0) warnings++;
        }
        onFinished({ total: items.length, ok, failed, warnings, cancelled });
        return prev;
      });
    }
  }, [items, getKey, run, running, concurrency, onFinished]);

  const cancel = useCallback(() => {
    cancelRef.current = true;
  }, []);

  const percent = items.length === 0
    ? 0
    : Math.round((completedCount / items.length) * 100);

  return (
    <div className="space-y-4">
      <div className="flex items-center justify-between gap-3">
        <div className="flex items-center gap-2">
          <Button
            type="button"
            variant="primary"
            onClick={start}
            disabled={running || items.length === 0}
          >
            {running ? 'Running…' : `${startLabel} (${items.length})`}
          </Button>
          {running && (
            <Button type="button" variant="secondary" onClick={cancel}>
              Cancel
            </Button>
          )}
        </div>
        <div className="text-sm text-slate-600 dark:text-slate-300">
          {completedCount}/{items.length} · ok {totals.ok} · failed {totals.failed}
          {totals.warnings > 0 ? ` · warnings ${totals.warnings}` : ''}
        </div>
      </div>

      <div
        className="h-2 w-full overflow-hidden rounded-full bg-slate-200 dark:bg-slate-800"
        role="progressbar"
        aria-valuenow={percent}
        aria-valuemin={0}
        aria-valuemax={100}
      >
        <div
          className="h-full bg-emerald-500 transition-all duration-300 ease-out"
          style={{ width: `${percent}%` }}
        />
      </div>

      <div className="overflow-x-auto rounded-lg border border-slate-200 dark:border-slate-700">
        <table className="w-full text-sm">
          <thead className="bg-slate-50 text-left text-xs uppercase text-slate-600 dark:bg-slate-900/50 dark:text-slate-400">
            <tr>
              <th className="px-3 py-2">Item</th>
              <th className="w-32 px-3 py-2">Status</th>
              <th className="px-3 py-2">Detail</th>
            </tr>
          </thead>
          <tbody>
            {items.map((item, idx) => {
              const key = getKey(item, idx);
              const state = states[key] ?? { status: 'pending' as const };
              return (
                <tr key={key} className="border-t border-slate-100 dark:border-slate-800">
                  <td className="px-3 py-2 align-top">{renderRow(item)}</td>
                  <td className="px-3 py-2 align-top">
                    {state.status === 'pending' && <Badge variant="muted">Pending</Badge>}
                    {state.status === 'running' && <Badge variant="info">Running…</Badge>}
                    {state.status === 'done' && state.outcome.ok && (
                      <Badge variant="success">
                        {state.outcome.warnings && state.outcome.warnings.length > 0 ? 'OK + warnings' : 'OK'}
                      </Badge>
                    )}
                    {state.status === 'done' && !state.outcome.ok && (
                      <Badge variant="danger">Failed</Badge>
                    )}
                  </td>
                  <td className="px-3 py-2 align-top text-xs text-slate-600 dark:text-slate-300">
                    {state.status === 'done' && state.outcome.detail && (
                      <div className="mb-1">{state.outcome.detail}</div>
                    )}
                    {state.status === 'done' && state.outcome.error && (
                      <div className="text-rose-600 dark:text-rose-400">{state.outcome.error}</div>
                    )}
                    {state.status === 'done' && state.outcome.warnings && state.outcome.warnings.length > 0 && (
                      <ul className="list-inside list-disc text-amber-700 dark:text-amber-300">
                        {state.outcome.warnings.map((w, i) => (
                          <li key={i}>{w}</li>
                        ))}
                      </ul>
                    )}
                  </td>
                </tr>
              );
            })}
            {items.length === 0 && (
              <tr>
                <td colSpan={3} className="px-3 py-6 text-center text-slate-500">
                  No items to run.
                </td>
              </tr>
            )}
          </tbody>
        </table>
      </div>
    </div>
  );
}
