'use client';

import { cn } from '@/lib/utils';
import { type ReactNode } from 'react';

export interface Column<T> {
  key: string;
  header: string;
  render: (row: T, index: number) => ReactNode;
  className?: string;
  hideOnMobile?: boolean;
}

interface DataTableProps<T> {
  columns: Column<T>[];
  data: T[];
  keyExtractor: (row: T, index: number) => string;
  onRowClick?: (row: T) => void;
  emptyMessage?: string;
  className?: string;
  'aria-label'?: string;
}

export function DataTable<T>({ columns, data, keyExtractor, onRowClick, emptyMessage = 'No data', className, 'aria-label': ariaLabel }: DataTableProps<T>) {
  if (data.length === 0) {
    return (
      <div className="rounded-[20px] border border-dashed border-gray-200 bg-background-light px-6 py-10 text-center text-sm text-muted">
        {emptyMessage}
      </div>
    );
  }

  return (
    <div className={cn('overflow-hidden rounded-[24px] border border-gray-200 bg-surface shadow-sm', className)}>
      <div className="overflow-x-auto">
        <table className="w-full min-w-full text-sm" aria-label={ariaLabel}>
          <thead className="bg-background-light">
          <tr className="border-b border-gray-200/60">
            {columns.map((col) => (
              <th
                key={col.key}
                scope="col"
                className={cn(
                  'px-5 py-3.5 text-left text-[11px] font-semibold uppercase tracking-[0.16em] text-muted',
                  col.hideOnMobile && 'hidden md:table-cell',
                  col.className,
                )}
              >
                {col.header}
              </th>
            ))}
          </tr>
        </thead>
          <tbody className="divide-y divide-gray-100/90 bg-surface">
          {data.map((row, idx) => (
            <tr
              key={keyExtractor(row, idx)}
              data-row-key={keyExtractor(row, idx)}
              onClick={() => onRowClick?.(row)}
              onKeyDown={(e) => { if (onRowClick && (e.key === 'Enter' || e.key === ' ')) { e.preventDefault(); onRowClick(row); } }}
              tabIndex={onRowClick ? 0 : undefined}
              role={onRowClick ? 'button' : undefined}
              className={cn(
                'transition-colors duration-200',
                onRowClick && 'cursor-pointer hover:bg-primary/[0.03] focus-visible:bg-primary/[0.03] focus-visible:outline-none',
              )}
            >
              {columns.map((col) => (
                <td
                  key={col.key}
                  className={cn(
                    'px-5 py-4 align-top text-sm text-navy',
                    col.hideOnMobile && 'hidden md:table-cell',
                    col.className,
                  )}
                >
                  {col.render(row, idx)}
                </td>
              ))}
            </tr>
          ))}
        </tbody>
        </table>
      </div>
    </div>
  );
}
