'use client';

import { type KeyboardEvent as ReactKeyboardEvent, type MouseEvent as ReactMouseEvent, type ReactNode } from 'react';
import { motion, useReducedMotion } from 'motion/react';
import { getMotionDelay, getSurfaceTransition, getSurfaceVariants, prefersReducedMotion } from '@/lib/motion';

import { cn } from '@/lib/utils';

import { Card } from './card';

export interface Column<T> {
  key: string;
  header: string;
  render: (row: T, index: number) => ReactNode;
  className?: string;
  hideOnMobile?: boolean;
}

type RowActivationEvent = ReactMouseEvent<HTMLElement> | ReactKeyboardEvent<HTMLElement>;

interface DataTableProps<T> {
  columns: Column<T>[];
  data: T[];
  keyExtractor: (row: T, index: number) => string;
  onRowClick?: (row: T, event: RowActivationEvent) => void;
  mobileCardRender?: (row: T, index: number) => ReactNode;
  emptyMessage?: string;
  className?: string;
  /**
   * Table row density.
   * - `default` (44px rows): standard learner + general admin tables.
   * - `compact` (30px rows): admin data-heavy tables (users, audit-logs, ai-usage, billing).
   */
  density?: 'default' | 'compact';
  'aria-label'?: string;
}

export function DataTable<T>({
  columns,
  data,
  keyExtractor,
  onRowClick,
  mobileCardRender,
  emptyMessage = 'No data',
  className,
  density = 'default',
  'aria-label': ariaLabel,
}: DataTableProps<T>) {
  const reducedMotion = prefersReducedMotion(useReducedMotion());
  const rowVariants = getSurfaceVariants('item', reducedMotion);
  const rowTransition = getSurfaceTransition('item', reducedMotion);

  if (data.length === 0) {
    return (
      <div className="rounded-[20px] border border-dashed border-gray-200 bg-background-light px-6 py-10 text-center text-sm text-muted">
        {emptyMessage}
      </div>
    );
  }

  const hasMobileCardView = Boolean(mobileCardRender);
  const mobileColumns = columns.filter((column) => !column.hideOnMobile);
  const displayColumns = mobileColumns.length > 0 ? mobileColumns : columns;
  const primaryColumn = displayColumns[0] ?? columns[0];

  const renderDefaultMobileCard = (row: T, index: number) => {
    const rowKey = keyExtractor(row, index);

    return (
      <Card
        key={rowKey}
        hoverable={Boolean(onRowClick)}
        padding="md"
        role={onRowClick ? 'button' : 'group'}
        tabIndex={onRowClick ? 0 : undefined}
        data-testid="data-table-mobile-card"
        data-row-key={rowKey}
        aria-labelledby={`${rowKey}-mobile-title`}
        onClick={(event) => onRowClick?.(row, event)}
        onKeyDown={(event) => {
          if (!onRowClick) return;
          if (event.key === 'Enter' || event.key === ' ') {
            event.preventDefault();
            onRowClick(row, event);
          }
        }}
        className={cn(onRowClick && 'cursor-pointer focus-visible:bg-primary/[0.03]')}
      >
        <div className="space-y-3">
          <div className="flex items-start justify-between gap-3">
            <div className="min-w-0 flex-1">
              <p className="text-[11px] font-semibold uppercase tracking-[0.16em] text-muted">
                {primaryColumn?.header ?? 'Item'}
              </p>
              <div id={`${rowKey}-mobile-title`} className="mt-1 break-words text-sm font-semibold text-navy">
                {primaryColumn?.render(row, index)}
              </div>
            </div>
          </div>

          {displayColumns.length > 1 ? (
            <div className="grid gap-2">
              {displayColumns.slice(1).map((column) => (
                <div
                  key={column.key}
                  className={cn(
                    'flex items-start justify-between gap-3 rounded-xl bg-background-light px-3 py-2',
                    column.className,
                  )}
                >
                  <span className="text-[11px] font-semibold uppercase tracking-[0.16em] text-muted">
                    {column.header}
                  </span>
                  <div className="text-right text-sm text-navy">{column.render(row, index)}</div>
                </div>
              ))}
            </div>
          ) : null}
        </div>
      </Card>
    );
  };

  return (
    <div className={cn('overflow-hidden rounded-[24px] border border-gray-200 bg-surface shadow-sm', className)}>
      <div className="md:hidden p-3">
        {data.map((row, idx) => {
          const rowKey = keyExtractor(row, idx);

          if (hasMobileCardView) {
            return (
              <Card
                key={rowKey}
                hoverable={Boolean(onRowClick)}
                padding="md"
                role={onRowClick ? 'button' : 'group'}
                tabIndex={onRowClick ? 0 : undefined}
                data-row-key={rowKey}
                onClick={(event) => onRowClick?.(row, event)}
                onKeyDown={(event) => {
                  if (!onRowClick) return;
                  if (event.key === 'Enter' || event.key === ' ') {
                    event.preventDefault();
                    onRowClick(row, event);
                  }
                }}
                className={cn(onRowClick && 'cursor-pointer focus-visible:bg-primary/[0.03]')}
              >
                {mobileCardRender?.(row, idx)}
              </Card>
            );
          }

          return renderDefaultMobileCard(row, idx);
        })}
      </div>

      <div className="hidden md:block">
        <div className="overflow-x-auto">
          <table className="w-full min-w-full text-sm" aria-label={ariaLabel}>
            <thead className="bg-background-light">
              <tr className="border-b border-gray-200/60">
                {columns.map((column) => (
                  <th
                    key={column.key}
                    scope="col"
                    className={cn(
                      'text-left text-[11px] font-semibold uppercase tracking-[0.16em] text-muted',
                      density === 'compact' ? 'px-4 py-2.5' : 'px-5 py-3.5',
                      column.hideOnMobile && 'hidden md:table-cell',
                      column.className,
                    )}
                  >
                    {column.header}
                  </th>
                ))}
              </tr>
            </thead>

            <tbody className="divide-y divide-gray-100/90 bg-surface">
              {data.map((row, idx) => (
                <motion.tr
                  key={keyExtractor(row, idx)}
                  data-row-key={keyExtractor(row, idx)}
                  variants={rowVariants}
                  initial="hidden"
                  animate="visible"
                  transition={{ ...rowTransition, delay: getMotionDelay(idx, reducedMotion) }}
                  onClick={(event) => onRowClick?.(row, event)}
                  onKeyDown={(event) => {
                    if (onRowClick && (event.key === 'Enter' || event.key === ' ')) {
                      event.preventDefault();
                      onRowClick(row, event);
                    }
                  }}
                  tabIndex={onRowClick ? 0 : undefined}
                  role={onRowClick ? 'button' : undefined}
                  className={cn(
                    'transition-colors duration-200',
                    onRowClick && 'cursor-pointer hover:bg-primary/[0.03] focus-visible:bg-primary/[0.03] focus-visible:outline-none',
                  )}
                >
                  {columns.map((column) => (
                    <td
                      key={column.key}
                      className={cn(
                        'align-top text-sm text-navy',
                        density === 'compact' ? 'px-4 py-2.5' : 'px-5 py-4',
                        column.hideOnMobile && 'hidden md:table-cell',
                        column.className,
                      )}
                    >
                      {column.render(row, idx)}
                    </td>
                  ))}
                </motion.tr>
              ))}
            </tbody>
          </table>
        </div>
      </div>
    </div>
  );
}
