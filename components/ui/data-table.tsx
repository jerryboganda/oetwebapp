'use client';

import { useMemo, useRef, type KeyboardEvent as ReactKeyboardEvent, type MouseEvent as ReactMouseEvent, type ReactNode } from 'react';
import { useVirtualizer, type VirtualItem } from '@tanstack/react-virtual';

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
  /** Show row-selection checkboxes. */
  selectable?: boolean;
  /** Controlled set of selected row keys. */
  selectedKeys?: Set<string>;
  /** Called when row selection changes. */
  onSelectionChange?: (keys: Set<string>) => void;
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
  selectable = false,
  selectedKeys,
  onSelectionChange,
}: DataTableProps<T>) {
  const rowKeys = useMemo(() => data.map((row, idx) => keyExtractor(row, idx)), [data, keyExtractor]);
  const selectedRowKeys = selectedKeys ?? new Set<string>();
  const allSelected = selectable && rowKeys.length > 0 && rowKeys.every((key) => selectedRowKeys.has(key));
  const someSelected = selectable && rowKeys.some((key) => selectedRowKeys.has(key)) && !allSelected;

  const toggleAll = () => {
    if (!onSelectionChange) return;
    const next = new Set(selectedRowKeys);
    if (allSelected) {
      rowKeys.forEach((key) => next.delete(key));
    } else {
      rowKeys.forEach((key) => next.add(key));
    }
    onSelectionChange(next);
  };

  const toggleRow = (rowKey: string) => {
    if (!onSelectionChange) return;
    const next = new Set(selectedRowKeys);
    if (next.has(rowKey)) {
      next.delete(rowKey);
    } else {
      next.add(rowKey);
    }
    onSelectionChange(next);
  };

  if (data.length === 0) {
    return (
      <div className="rounded-2xl border border-dashed border-border bg-background-light px-6 py-10 text-center text-sm text-muted">
        {emptyMessage}
      </div>
    );
  }

  const hasMobileCardView = Boolean(mobileCardRender);
  const mobileColumns = columns.filter((column) => !column.hideOnMobile);
  const displayColumns = mobileColumns.length > 0 ? mobileColumns : columns;
  const primaryColumn = displayColumns[0] ?? columns[0];

  const renderSelectionCheckbox = (rowKey: string, checked: boolean, label: string) => (
    <input
      type="checkbox"
      checked={checked}
      onChange={() => toggleRow(rowKey)}
      onClick={(event) => event.stopPropagation()}
      aria-label={label}
      className="h-4 w-4 cursor-pointer rounded border-border text-primary focus:ring-primary/30"
    />
  );

  const renderDefaultMobileCard = (row: T, index: number) => {
    const rowKey = keyExtractor(row, index);

    const isSelected = selectedRowKeys.has(rowKey);

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
        className={cn(
          onRowClick && 'cursor-pointer focus-visible:bg-primary/[0.03]',
          isSelected && 'ring-2 ring-primary/40',
        )}
      >
        <div className="space-y-3">
          <div className="flex items-start justify-between gap-3">
            {selectable ? (
              <div className="pt-1">
                {renderSelectionCheckbox(rowKey, isSelected, `Select row ${rowKey}`)}
              </div>
            ) : null}
            <div className="min-w-0 flex-1">
              <p className="text-[11px] font-bold uppercase tracking-[0.16em] text-muted">
                {primaryColumn?.header ?? 'Item'}
              </p>
              <div id={`${rowKey}-mobile-title`} className="mt-1 break-words text-sm font-bold text-navy">
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
                  <span className="text-[11px] font-bold uppercase tracking-[0.16em] text-muted">
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
    <div className={cn('overflow-hidden rounded-2xl border border-border bg-surface shadow-sm', className)}>
      <div className="md:hidden p-3">
        {selectable ? (
          <label className="mb-3 flex items-center gap-2 rounded-xl bg-background-light px-3 py-2 text-sm font-semibold text-navy">
            <input
              type="checkbox"
              checked={allSelected}
              ref={(element) => {
                if (element) element.indeterminate = someSelected;
              }}
              onChange={toggleAll}
              aria-label="Select all rows"
              className="h-4 w-4 cursor-pointer rounded border-border text-primary focus:ring-primary/30"
            />
            Select all
          </label>
        ) : null}
        {data.map((row, idx) => {
          const rowKey = keyExtractor(row, idx);
          const isSelected = selectedRowKeys.has(rowKey);

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
                className={cn(
                  onRowClick && 'cursor-pointer focus-visible:bg-primary/[0.03]',
                  isSelected && 'ring-2 ring-primary/40',
                )}
              >
                <div className="flex items-start gap-3">
                  {selectable ? (
                    <div className="pt-1">
                      {renderSelectionCheckbox(rowKey, isSelected, `Select row ${rowKey}`)}
                    </div>
                  ) : null}
                  <div className="min-w-0 flex-1">{mobileCardRender?.(row, idx)}</div>
                </div>
              </Card>
            );
          }

          return renderDefaultMobileCard(row, idx);
        })}
      </div>

      <div className="hidden md:block">
        {virtualize ? (
          <VirtualizedDesktopView
            columns={columns}
            data={data}
            keyExtractor={keyExtractor}
            onRowClick={onRowClick}
            ariaLabel={ariaLabel}
            options={virtualize}
            selectable={selectable}
            selectedKeys={selectedRowKeys}
            allSelected={allSelected}
            someSelected={someSelected}
            onToggleAll={toggleAll}
            onToggleRow={toggleRow}
          />
        ) : (
          <div className="overflow-x-auto">
            <table className="w-full min-w-full text-sm" aria-label={ariaLabel}>
              <thead className="bg-background-light">
                <tr className="border-b border-border/60">
                  {selectable ? (
                    <th scope="col" className="w-10 border-b border-border py-2 px-3 text-left">
                      <input
                        type="checkbox"
                        checked={allSelected}
                        ref={(element) => {
                          if (element) element.indeterminate = someSelected;
                        }}
                        onChange={toggleAll}
                        aria-label="Select all rows"
                        className="h-4 w-4 cursor-pointer rounded border-border text-primary focus:ring-primary/30"
                      />
                    </th>
                  ) : null}
                  {columns.map((column) => (
                    <th
                      key={column.key}
                      scope="col"
                      className={cn(
                        'border-b border-border py-2 px-4 text-left text-[11px] font-bold uppercase tracking-[0.18em] text-muted whitespace-nowrap',
                        column.hideOnMobile && 'hidden md:table-cell',
                        column.className,
                      )}
                    >
                      {column.header}
                    </th>
                  ))}
                </tr>
              </thead>

              <tbody className="divide-y divide-border bg-surface">
                {data.map((row, idx) => {
                  const rowKey = keyExtractor(row, idx);
                  const isSelected = selectedRowKeys.has(rowKey);

                  return (
                  <tr
                    key={rowKey}
                    data-row-key={rowKey}
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
                      'border-b border-border/30 hover:bg-background-light/70 transition-colors focus:outline-none',
                      onRowClick && 'cursor-pointer hover:bg-primary/[0.03] focus-visible:bg-primary/[0.03] focus-visible:outline-none',
                      isSelected && 'bg-primary/[0.05]',
                    )}
                  >
                    {selectable ? (
                      <td className="w-10 px-3 py-3 align-middle">
                        {renderSelectionCheckbox(rowKey, isSelected, `Select row ${rowKey}`)}
                      </td>
                    ) : null}
                    {columns.map((column) => (
                      <td
                        key={column.key}
                        className={cn(
                          'px-4 py-3 text-sm whitespace-nowrap align-middle',
                          column.hideOnMobile && 'hidden md:table-cell',
                          column.className,
                        )}
                      >
                        {column.render(row, idx)}
                      </td>
                    ))}
                  </tr>
                  );
                })}
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
  selectable: boolean;
  selectedKeys: Set<string>;
  allSelected: boolean;
  someSelected: boolean;
  onToggleAll: () => void;
  onToggleRow: (rowKey: string) => void;
}

function VirtualizedDesktopView<T>({
  columns,
  data,
  keyExtractor,
  onRowClick,
  ariaLabel,
  options,
  selectable,
  selectedKeys,
  allSelected,
  someSelected,
  onToggleAll,
  onToggleRow,
}: VirtualizedDesktopViewProps<T>) {
  const { rowHeight, containerHeight = 640, overscan = 8 } = options;
  const parentRef = useRef<HTMLDivElement>(null);
  // `useVirtualizer` is a third-party hook whose dependencies are managed
  // internally by TanStack Virtual; the eslint react-hooks rule's
  // incompatible-library check flags it in Next.js strict mode.
  // eslint-disable-next-line react-hooks/incompatible-library
  const virtualizer = useVirtualizer({
    count: data.length,
    getScrollElement: () => parentRef.current,
    estimateSize: () => rowHeight,
    overscan,
  });

  const columnCount = columns.length + (selectable ? 1 : 0);
  const gridTemplate = selectable ? `40px repeat(${columns.length}, minmax(0, 1fr))` : `repeat(${columnCount}, minmax(0, 1fr))`;

  return (
    <div
      ref={parentRef}
      className="overflow-auto"
      style={{ maxHeight: containerHeight }}
      role="table"
      aria-label={ariaLabel}
      aria-rowcount={data.length + 1}
    >
      <div
        role="rowgroup"
        className="sticky top-0 z-10 border-b border-border bg-background-light"
      >
        <div role="row" className="grid" style={{ gridTemplateColumns: gridTemplate }}>
          {selectable ? (
            <div role="columnheader" className="border-b border-border py-2 px-3">
              <input
                type="checkbox"
                checked={allSelected}
                ref={(element) => {
                  if (element) element.indeterminate = someSelected;
                }}
                onChange={onToggleAll}
                aria-label="Select all rows"
                className="h-4 w-4 cursor-pointer rounded border-border text-primary focus:ring-primary/30"
              />
            </div>
          ) : null}
          {columns.map((column) => (
            <div
              key={column.key}
              role="columnheader"
              className={cn(
                'border-b border-border py-2 px-4 text-left text-[11px] font-bold uppercase tracking-[0.18em] text-muted whitespace-nowrap',
                column.className,
              )}
            >
              {column.header}
            </div>
          ))}
        </div>
      </div>

      <div role="rowgroup" style={{ height: virtualizer.getTotalSize(), position: 'relative' }}>
        {virtualizer.getVirtualItems().map((virtualRow: VirtualItem) => {
          const row = data[virtualRow.index];
          if (!row) return null;
          const rowKey = keyExtractor(row, virtualRow.index);
          const isSelected = selectedKeys.has(rowKey);
          return (
            <div
              key={rowKey}
              role="row"
              aria-rowindex={virtualRow.index + 2}
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
                'grid border-b border-border/30 hover:bg-background-light/70 transition-colors focus:outline-none',
                onRowClick && 'cursor-pointer focus-visible:bg-primary/[0.03]',
                isSelected && 'bg-primary/[0.05]',
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
              {selectable ? (
                <div role="cell" className="px-3 py-3 align-middle">
                  <input
                    type="checkbox"
                    checked={isSelected}
                    onChange={() => onToggleRow(rowKey)}
                    onClick={(event) => event.stopPropagation()}
                    aria-label={`Select row ${rowKey}`}
                    className="h-4 w-4 cursor-pointer rounded border-border text-primary focus:ring-primary/30"
                  />
                </div>
              ) : null}
              {columns.map((column) => (
                <div
                  key={column.key}
                  role="cell"
                  className={cn('px-4 py-3 text-sm whitespace-nowrap align-middle', column.className)}
                >
                  {column.render(row, virtualRow.index)}
                </div>
              ))}
            </div>
          );
        })}
      </div>
    </div>
  );
}
