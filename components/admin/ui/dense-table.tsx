// Re-skinned 2026-05-24 for admin redesign — uses --admin-* token system
import { cn } from '@/lib/utils';
import React from 'react';

/**
 * DenseTable — compact tabular display.
 *
 * Public API preserved: `cols` (string[]) and `rows` (ReactNode[][]).
 * First column is left-aligned, subsequent columns right-aligned (numeric).
 */
export function DenseTable({
  cols, rows,
}: { cols: string[]; rows: React.ReactNode[][] }) {
  return (
    <div className="w-full overflow-x-auto">
      <table className="w-full min-w-[34rem] text-sm text-admin-fg-default">
        <thead className="border-b border-admin-border bg-admin-bg-subtle">
          <tr>
            {cols.map((c, i) => (
              <th
                key={i}
                className={cn(
                  'whitespace-nowrap px-3 py-2 text-left text-xs font-semibold uppercase tracking-wider text-admin-fg-muted',
                  i > 0 && 'text-right',
                )}
              >
                {c}
              </th>
            ))}
          </tr>
        </thead>
        <tbody>
          {rows.map((row, i) => (
            <tr
              key={i}
              className="border-b border-admin-border last:border-0 transition-colors hover:bg-[var(--admin-state-hover)]"
            >
              {row.map((cell, j) => (
                <td key={j} className={cn('px-3 py-2.5 align-middle', j > 0 && 'text-right')}>
                  {cell}
                </td>
              ))}
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}
