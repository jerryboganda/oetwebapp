import { cn } from '@/lib/utils';
import React from 'react';

export function DenseTable({
  cols, rows,
}: { cols: string[]; rows: React.ReactNode[][] }) {
  return (
    <div className="w-full overflow-x-auto">
      <table className="w-full min-w-[34rem] text-sm">
        <thead>
          <tr className="border-b border-admin-border/60">
            {cols.map((c, i) => (
              <th key={i} className={cn('px-4 py-2 text-xs font-bold uppercase tracking-[0.18em] text-admin-text-muted whitespace-nowrap', i > 0 && 'text-right')}>
                {c}
              </th>
            ))}
          </tr>
        </thead>
        <tbody>
          {rows.map((row, i) => (
            <tr key={i} className="border-b border-admin-border/40 hover:bg-admin-surface-raised/40 transition-colors">
              {row.map((cell, j) => (
                <td key={j} className={cn('px-4 py-2.5 align-middle', j > 0 && 'text-right')}>
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
