'use client';

import { cn } from '@/lib/utils';
import { EmptyState } from '@/components/ui/empty-error';
import { Activity } from 'lucide-react';

/**
 * Readiness heatmap for a batch of learners taking a mock.
 *
 * Rows are learners, columns are OET sub-tests (Listening / Reading / Writing / Speaking).
 * Each cell is colored by a RAG (Red / Amber / Green) signal that indicates
 * whether the learner's draft is ready, in-flight, or at risk for that sub-test.
 *
 * The component is intentionally presentational — fetching, batching, and
 * grouping live in the consuming page.
 */

export type MockBatchHeatmapRag = 'green' | 'amber' | 'red' | 'grey' | string;

export interface MockBatchHeatmapCell {
  subtest: string;
  rag: MockBatchHeatmapRag;
  score?: number | null;
}

export interface MockBatchHeatmapRow {
  learnerId: string;
  learnerName: string;
  cells: MockBatchHeatmapCell[];
}

export interface MockBatchHeatmapProps {
  rows: MockBatchHeatmapRow[];
  /** Override the column ordering / labels. Defaults to L/R/W/S. */
  columns?: Array<{ id: string; label: string }>;
  className?: string;
}

const DEFAULT_COLUMNS: Array<{ id: string; label: string }> = [
  { id: 'listening', label: 'L' },
  { id: 'reading', label: 'R' },
  { id: 'writing', label: 'W' },
  { id: 'speaking', label: 'S' },
];

function normalizeRag(rag: MockBatchHeatmapRag): 'green' | 'amber' | 'red' | 'grey' {
  const value = String(rag).toLowerCase();
  if (value === 'green' || value === 'g' || value === 'ready' || value === 'pass') return 'green';
  if (value === 'amber' || value === 'a' || value === 'yellow' || value === 'at_risk' || value === 'borderline') return 'amber';
  if (value === 'red' || value === 'r' || value === 'fail' || value === 'risk') return 'red';
  return 'grey';
}

function ragClasses(rag: 'green' | 'amber' | 'red' | 'grey') {
  switch (rag) {
    case 'green':
      return 'bg-emerald-100 text-emerald-800 border-emerald-200 dark:bg-emerald-900/30 dark:text-emerald-200 dark:border-emerald-800';
    case 'amber':
      return 'bg-amber-100 text-amber-800 border-amber-200 dark:bg-amber-900/30 dark:text-amber-200 dark:border-amber-800';
    case 'red':
      return 'bg-red-100 text-red-800 border-red-200 dark:bg-red-900/30 dark:text-red-200 dark:border-red-800';
    case 'grey':
    default:
      return 'bg-background-light text-muted border-border dark:bg-background-dark dark:text-muted';
  }
}

function ragLabel(rag: 'green' | 'amber' | 'red' | 'grey') {
  switch (rag) {
    case 'green':
      return 'On track';
    case 'amber':
      return 'At risk';
    case 'red':
      return 'Off track';
    case 'grey':
    default:
      return 'No data';
  }
}

export function MockBatchHeatmap({ rows, columns = DEFAULT_COLUMNS, className }: MockBatchHeatmapProps) {
  if (!rows || rows.length === 0) {
    return (
      <EmptyState
        icon={<Activity className="h-10 w-10 text-muted" />}
        title="No mock attempts in this batch"
        description="Once learners begin or submit attempts, their readiness will show here grouped by sub-test."
        className={className}
      />
    );
  }

  return (
    <div
      className={cn('overflow-x-auto rounded-2xl border border-border bg-surface', className)}
      role="region"
      aria-label="Mock batch readiness heatmap"
    >
      <table className="w-full min-w-[420px] text-sm">
        <thead className="bg-background-light">
          <tr>
            <th scope="col" className="sticky left-0 z-10 bg-background-light px-4 py-3 text-left text-xs font-bold uppercase tracking-wide text-muted">
              Learner
            </th>
            {columns.map((col) => (
              <th
                key={col.id}
                scope="col"
                className="px-3 py-3 text-center text-xs font-bold uppercase tracking-wide text-muted"
                aria-label={col.id}
              >
                {col.label}
              </th>
            ))}
          </tr>
        </thead>
        <tbody>
          {rows.map((row) => (
            <tr key={row.learnerId} className="border-t border-border">
              <th
                scope="row"
                className="sticky left-0 z-10 max-w-[180px] truncate bg-surface px-4 py-2 text-left text-sm font-semibold text-navy"
                title={row.learnerName}
              >
                {row.learnerName}
              </th>
              {columns.map((col) => {
                const cell = row.cells.find((c) => c.subtest.toLowerCase() === col.id.toLowerCase());
                const rag = normalizeRag(cell?.rag ?? 'grey');
                const tooltip = cell
                  ? `${row.learnerName} – ${col.id}: ${ragLabel(rag)}${cell.score != null ? ` (${cell.score})` : ''}`
                  : `${row.learnerName} – ${col.id}: ${ragLabel('grey')}`;
                return (
                  <td key={`${row.learnerId}-${col.id}`} className="px-2 py-2 text-center">
                    <span
                      className={cn(
                        'inline-flex h-10 w-12 items-center justify-center rounded-md border text-xs font-bold tabular-nums',
                        ragClasses(rag),
                      )}
                      title={tooltip}
                      aria-label={tooltip}
                      data-rag={rag}
                    >
                      {cell?.score != null ? cell.score : 'N/A'}
                    </span>
                  </td>
                );
              })}
            </tr>
          ))}
        </tbody>
      </table>
      <div className="flex flex-wrap items-center gap-3 border-t border-border bg-background-light px-4 py-3 text-xs text-muted">
        <span className="font-semibold uppercase tracking-wide">Legend</span>
        <span className="inline-flex items-center gap-1.5">
          <span className={cn('h-3 w-3 rounded-sm border', ragClasses('green'))} aria-hidden="true" />
          On track
        </span>
        <span className="inline-flex items-center gap-1.5">
          <span className={cn('h-3 w-3 rounded-sm border', ragClasses('amber'))} aria-hidden="true" />
          At risk
        </span>
        <span className="inline-flex items-center gap-1.5">
          <span className={cn('h-3 w-3 rounded-sm border', ragClasses('red'))} aria-hidden="true" />
          Off track
        </span>
        <span className="inline-flex items-center gap-1.5">
          <span className={cn('h-3 w-3 rounded-sm border', ragClasses('grey'))} aria-hidden="true" />
          No data
        </span>
      </div>
    </div>
  );
}

export default MockBatchHeatmap;
