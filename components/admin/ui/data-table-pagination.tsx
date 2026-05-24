'use client';

import * as React from 'react';
import { type Table } from '@tanstack/react-table';
import {
  ChevronLeft,
  ChevronRight,
  ChevronsLeft,
  ChevronsRight,
} from 'lucide-react';

import { cn } from '@/lib/utils';
import { Button } from './button';
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from './select';

/* ─────────────────────────────────────────────────────────────────────
 * DataTablePagination — footer for the DataTable primitive.
 *
 * Layout (lg+):
 *   ┌──────────────────────────────────────────────────────────────────┐
 *   │  X of Y row(s) selected.   Rows per page [20 ▾]   Page 1 of 12  │
 *   │                                          ⇤  ←  →  ⇥             │
 *   └──────────────────────────────────────────────────────────────────┘
 *
 * Mobile (< lg):
 *   Hides the per-page selector and first/last buttons; shows compact
 *   "Page X / Y" + prev / next only.
 *
 * Consumer wires the TanStack Table<TData> instance via props.
 * ───────────────────────────────────────────────────────────────────── */

export interface DataTablePaginationProps<TData> {
  table: Table<TData>;
  /** Page-size options for the dropdown. Default: [10, 20, 50, 100]. */
  pageSizeOptions?: number[];
  /** Hide the selection count label (e.g. when enableSelection is false). */
  hideSelectionCount?: boolean;
  className?: string;
}

function DataTablePagination<TData>({
  table,
  pageSizeOptions = [10, 20, 50, 100],
  hideSelectionCount = false,
  className,
}: DataTablePaginationProps<TData>) {
  const pageIndex = table.getState().pagination.pageIndex;
  const pageSize = table.getState().pagination.pageSize;
  const pageCount = Math.max(1, table.getPageCount());
  const filteredCount = table.getFilteredRowModel().rows.length;
  const selectedCount = table.getFilteredSelectedRowModel().rows.length;

  return (
    <div
      className={cn(
        'flex flex-col gap-3 px-3 py-3 lg:flex-row lg:items-center lg:justify-between',
        'border-t border-admin-border-default bg-admin-bg-surface',
        className,
      )}
    >
      {/* Selection count (left, hidden on small unless selections exist) */}
      <div className="flex min-w-0 items-center text-xs text-admin-fg-muted tabular-nums">
        {!hideSelectionCount ? (
          <span
            aria-live="polite"
            className={cn(selectedCount === 0 && 'hidden lg:inline-block')}
          >
            <span className="font-medium text-admin-fg-default">
              {selectedCount.toLocaleString()}
            </span>{' '}
            of{' '}
            <span className="font-medium text-admin-fg-default">
              {filteredCount.toLocaleString()}
            </span>{' '}
            row{filteredCount === 1 ? '' : 's'} selected.
          </span>
        ) : (
          <span className="hidden lg:inline-block">
            <span className="font-medium text-admin-fg-default">
              {filteredCount.toLocaleString()}
            </span>{' '}
            row{filteredCount === 1 ? '' : 's'}
          </span>
        )}
      </div>

      {/* Page-size + paging controls (right) */}
      <div className="flex items-center gap-2 lg:gap-6">
        {/* Rows per page — lg+ only */}
        <div className="hidden items-center gap-2 lg:flex">
          <p className="text-xs font-medium text-admin-fg-muted whitespace-nowrap">
            Rows per page
          </p>
          <Select
            value={String(pageSize)}
            onValueChange={(value) => table.setPageSize(Number(value))}
          >
            <SelectTrigger
              className="h-8 w-[4.5rem] text-xs"
              aria-label="Rows per page"
            >
              <SelectValue placeholder={String(pageSize)} />
            </SelectTrigger>
            <SelectContent side="top" className="min-w-[4.5rem]">
              {pageSizeOptions.map((opt) => (
                <SelectItem key={opt} value={String(opt)} className="text-xs">
                  {opt}
                </SelectItem>
              ))}
            </SelectContent>
          </Select>
        </div>

        {/* Page X of Y */}
        <div
          className="flex items-center text-xs font-medium text-admin-fg-muted whitespace-nowrap tabular-nums"
          aria-live="polite"
        >
          Page{' '}
          <span className="mx-1 text-admin-fg-default">
            {pageIndex + 1}
          </span>{' '}
          of <span className="ml-1 text-admin-fg-default">{pageCount}</span>
        </div>

        {/* Nav buttons */}
        <div className="flex items-center gap-1">
          <Button
            variant="outline"
            size="icon"
            className="hidden h-8 w-8 lg:inline-flex"
            onClick={() => table.setPageIndex(0)}
            disabled={!table.getCanPreviousPage()}
            aria-label="Go to first page"
          >
            <ChevronsLeft className="h-4 w-4" aria-hidden="true" />
          </Button>
          <Button
            variant="outline"
            size="icon"
            className="h-8 w-8"
            onClick={() => table.previousPage()}
            disabled={!table.getCanPreviousPage()}
            aria-label="Go to previous page"
          >
            <ChevronLeft className="h-4 w-4" aria-hidden="true" />
          </Button>
          <Button
            variant="outline"
            size="icon"
            className="h-8 w-8"
            onClick={() => table.nextPage()}
            disabled={!table.getCanNextPage()}
            aria-label="Go to next page"
          >
            <ChevronRight className="h-4 w-4" aria-hidden="true" />
          </Button>
          <Button
            variant="outline"
            size="icon"
            className="hidden h-8 w-8 lg:inline-flex"
            onClick={() => table.setPageIndex(pageCount - 1)}
            disabled={!table.getCanNextPage()}
            aria-label="Go to last page"
          >
            <ChevronsRight className="h-4 w-4" aria-hidden="true" />
          </Button>
        </div>
      </div>
    </div>
  );
}
DataTablePagination.displayName = 'DataTablePagination';

export { DataTablePagination };
