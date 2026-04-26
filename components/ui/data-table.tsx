'use client';

<<<<<<< Updated upstream
<<<<<<< Updated upstream
import { useRef, type KeyboardEvent as ReactKeyboardEvent, type MouseEvent as ReactMouseEvent, type ReactNode } from 'react';
import { useVirtualizer, type VirtualItem } from '@tanstack/react-virtual';
=======
import { type KeyboardEvent as ReactKeyboardEvent, type MouseEvent as ReactMouseEvent, type ReactNode } from 'react';
<<<<<<< Updated upstream
>>>>>>> Stashed changes
=======
>>>>>>> Stashed changes
=======
import { useRef, type KeyboardEvent as ReactKeyboardEvent, type MouseEvent as ReactMouseEvent, type ReactNode } from 'react';
import { useVirtualizer, type VirtualItem } from '@tanstack/react-virtual';
>>>>>>> Stashed changes

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

/**
 * Opt-in row virtualization for very large tables (>500 rows).
 * - When set, the desktop view uses a `<div role="table">` scroller instead
 *   of a native `<table>`, rendering only visible rows via `@tanstack/react-virtual`.
 * - When NOT set (default), the desktop view keeps the native `<table>` layout
 *   so sort/sticky/print semantics and consumer CSS keep working untouched.
 */
export interface DataTableVirtualizeOptions {
  /** Fixed pixel height per row. Required — virtualization needs a size oracle. */
  rowHeight: number;
  /** Max scroll-container height (defaults to 640px). */
  containerHeight?: number;
  /** Extra rows rendered above/below viewport (defaults to 8). */
  overscan?: number;
}

interface DataTableProps<T> {
  columns: Column<T>[];
  data: T[];
  keyExtractor: (row: T, index: number) => string;
  onRowClick?: (row: T, event: RowActivationEvent) => void;
  mobileCardRender?: (row: T, index: number) => ReactNode;
  emptyMessage?: string;
  className?: string;
  'aria-label'?: string;
  /** Enable row virtualization for large datasets. See {@link DataTableVirtualizeOptions}. */
  virtualize?: DataTableVirtualizeOptions;
}

export function DataTable<T>({
  columns,
  data,
  keyExtractor,
  onRowClick,
  mobileCardRender,
  emptyMessage = 'No data',
  className,
  'aria-label': ariaLabel,
  virtualize,
}: DataTableProps<T>) {
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
<<<<<<< Updated upstream
<<<<<<< Updated upstream
=======
>>>>>>> Stashed changes
        {virtualize ? (
          <VirtualizedDesktopView
            columns={columns}
            data={data}
            keyExtractor={keyExtractor}
            onRowClick={onRowClick}
            ariaLabel={ariaLabel}
            options={virtualize}
          />
        ) : (
          <div className="overflow-x-auto">
            <table className="w-full min-w-full text-sm" aria-label={ariaLabel}>
              <thead className="bg-background-light">
                <tr className="border-b border-gray-200/60">
<<<<<<< Updated upstream
=======
        <div className="overflow-x-auto">
          <table className="w-full min-w-full text-sm" aria-label={ariaLabel}>
            <thead className="bg-background-light">
              <tr className="border-b border-gray-200/60">
                {columns.map((column) => (
                  <th
                    key={column.key}
                    scope="col"
                    className={cn(
                      'px-5 py-3.5 text-left text-[11px] font-semibold uppercase tracking-[0.16em] text-muted',
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
                <tr
                  key={keyExtractor(row, idx)}
                  data-row-key={keyExtractor(row, idx)}
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
>>>>>>> Stashed changes
=======
>>>>>>> Stashed changes
                  {columns.map((column) => (
                    <th
                      key={column.key}
                      scope="col"
                      className={cn(
                        'px-5 py-3.5 text-left text-[11px] font-semibold uppercase tracking-[0.16em] text-muted',
                        column.hideOnMobile && 'hidden md:table-cell',
                        column.className,
                      )}
                    >
                      {column.header}
                    </th>
                  ))}
                </tr>
<<<<<<< Updated upstream
<<<<<<< Updated upstream
<<<<<<< Updated upstream
=======
>>>>>>> Stashed changes
              </thead>

              <tbody className="divide-y divide-gray-100/90 bg-surface">
                {data.map((row, idx) => (
                  <tr
                    key={keyExtractor(row, idx)}
                    data-row-key={keyExtractor(row, idx)}
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
                          'px-5 py-4 align-top text-sm text-navy',
                          column.hideOnMobile && 'hidden md:table-cell',
                          column.className,
                        )}
                      >
                        {column.render(row, idx)}
                      </td>
                    ))}
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </div>
    </div>
  );
}

interface VirtualizedDesktopViewProps<T> {
  columns: Column<T>[];
  data: T[];
  keyExtractor: (row: T, index: number) => string;
  onRowClick?: (row: T, event: RowActivationEvent) => void;
  ariaLabel?: string;
  options: DataTableVirtualizeOptions;
}

function VirtualizedDesktopView<T>({
  columns,
  data,
  keyExtractor,
  onRowClick,
  ariaLabel,
  options,
}: VirtualizedDesktopViewProps<T>) {
  const { rowHeight, containerHeight = 640, overscan = 8 } = options;
  const parentRef = useRef<HTMLDivElement>(null);
<<<<<<< Updated upstream
  // `useVirtualizer` is a third-party hook whose dependencies are managed
  // internally by TanStack Virtual; the eslint react-hooks rule's
  // incompatible-library check flags it in Next.js strict mode.
  // eslint-disable-next-line react-hooks/incompatible-library
=======
>>>>>>> Stashed changes
  const virtualizer = useVirtualizer({
    count: data.length,
    getScrollElement: () => parentRef.current,
    estimateSize: () => rowHeight,
    overscan,
  });

<<<<<<< Updated upstream
=======
  // Column template so rows line up with the sticky header grid.
>>>>>>> Stashed changes
  const columnCount = columns.length;
  const gridTemplate = `repeat(${columnCount}, minmax(0, 1fr))`;

  return (
    <div
      ref={parentRef}
      className="overflow-auto"
      style={{ maxHeight: containerHeight }}
      role="table"
      aria-label={ariaLabel}
<<<<<<< Updated upstream
      aria-rowcount={data.length + 1}
=======
      aria-rowcount={data.length + 1 /* header */}
>>>>>>> Stashed changes
    >
      <div
        role="rowgroup"
        className="sticky top-0 z-10 bg-background-light border-b border-gray-200/60"
      >
        <div role="row" className="grid" style={{ gridTemplateColumns: gridTemplate }}>
          {columns.map((column) => (
            <div
              key={column.key}
              role="columnheader"
              className={cn(
                'px-5 py-3.5 text-left text-[11px] font-semibold uppercase tracking-[0.16em] text-muted',
                column.className,
              )}
            >
              {column.header}
            </div>
          ))}
        </div>
      </div>

<<<<<<< Updated upstream
      <div role="rowgroup" style={{ height: virtualizer.getTotalSize(), position: 'relative' }}>
=======
      <div
        role="rowgroup"
        style={{ height: virtualizer.getTotalSize(), position: 'relative' }}
      >
>>>>>>> Stashed changes
        {virtualizer.getVirtualItems().map((virtualRow: VirtualItem) => {
          const row = data[virtualRow.index];
          if (!row) return null;
          const rowKey = keyExtractor(row, virtualRow.index);
          return (
            <div
              key={rowKey}
              role="row"
<<<<<<< Updated upstream
              aria-rowindex={virtualRow.index + 2}
=======
              aria-rowindex={virtualRow.index + 2 /* header is row 1 */}
>>>>>>> Stashed changes
              data-row-key={rowKey}
              onClick={(event) => onRowClick?.(row, event)}
              onKeyDown={(event) => {
                if (onRowClick && (event.key === 'Enter' || event.key === ' ')) {
                  event.preventDefault();
                  onRowClick(row, event);
                }
              }}
              tabIndex={onRowClick ? 0 : undefined}
              className={cn(
                'grid border-b border-gray-100/90 bg-surface transition-colors duration-200',
                onRowClick && 'cursor-pointer hover:bg-primary/[0.03] focus-visible:bg-primary/[0.03] focus-visible:outline-none',
              )}
              style={{
                position: 'absolute',
                top: 0,
                left: 0,
                width: '100%',
                height: virtualRow.size,
                transform: `translateY(${virtualRow.start}px)`,
                gridTemplateColumns: gridTemplate,
              }}
            >
              {columns.map((column) => (
                <div
                  key={column.key}
                  role="cell"
                  className={cn('px-5 py-4 align-top text-sm text-navy', column.className)}
                >
                  {column.render(row, virtualRow.index)}
                </div>
              ))}
            </div>
          );
        })}
<<<<<<< Updated upstream
=======
=======
>>>>>>> Stashed changes
              ))}
            </tbody>
          </table>
        </div>
<<<<<<< Updated upstream
>>>>>>> Stashed changes
=======
>>>>>>> Stashed changes
=======
>>>>>>> Stashed changes
      </div>
    </div>
  );
}
