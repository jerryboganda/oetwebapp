'use client';

import * as React from 'react';
import { type Column } from '@tanstack/react-table';
import {
  ArrowDown,
  ArrowUp,
  ArrowUpDown,
  EyeOff,
} from 'lucide-react';

import { cn } from '@/lib/utils';
import { Button } from './button';
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
} from './dropdown-menu';

/* ─────────────────────────────────────────────────────────────────────
 * DataTableColumnHeader — sortable column header helper
 *
 * Use inside a TanStack column definition:
 *
 *   {
 *     accessorKey: 'name',
 *     header: ({ column }) => (
 *       <DataTableColumnHeader column={column} title="Name" />
 *     ),
 *   }
 *
 * If the column is not sortable, renders the title text only. If
 * sortable, renders the title plus a sort-indicator icon button that
 * cycles asc → desc → none on each click, and an optional dropdown for
 * Asc / Desc / Hide actions (suppressed when `withMenu={false}`).
 *
 * Accessibility:
 *   - The clickable header sets `aria-sort` on its parent <th> via
 *     TanStack (consumer wires it through; see DataTable thead block).
 *   - Hide action is announced via the dropdown menu item label.
 * ───────────────────────────────────────────────────────────────────── */

export interface DataTableColumnHeaderProps<TData, TValue>
  extends Omit<React.HTMLAttributes<HTMLDivElement>, 'title'> {
  column: Column<TData, TValue>;
  title: React.ReactNode;
  /** Force right-alignment (useful for numeric / currency columns). */
  align?: 'left' | 'center' | 'right';
  /** When false, render plain title text without sort affordance. */
  withMenu?: boolean;
}

function DataTableColumnHeader<TData, TValue>({
  column,
  title,
  align = 'left',
  withMenu = true,
  className,
  ...rest
}: DataTableColumnHeaderProps<TData, TValue>) {
  const canSort = column.getCanSort();
  const canHide = column.getCanHide();
  const sorted = column.getIsSorted();

  // Container alignment (drives the inner flex).
  const justify =
    align === 'right'
      ? 'justify-end'
      : align === 'center'
        ? 'justify-center'
        : 'justify-start';

  // Non-sortable: plain text.
  if (!canSort) {
    return (
      <div
        className={cn(
          'flex items-center text-xs font-semibold uppercase tracking-wide',
          'text-admin-fg-muted',
          justify,
          className,
        )}
        {...rest}
      >
        <span className="truncate">{title}</span>
      </div>
    );
  }

  const SortIcon =
    sorted === 'asc' ? ArrowUp : sorted === 'desc' ? ArrowDown : ArrowUpDown;
  const sortLabel =
    sorted === 'asc'
      ? 'Sorted ascending. Click to sort descending.'
      : sorted === 'desc'
        ? 'Sorted descending. Click to clear sort.'
        : 'Click to sort ascending.';

  const triggerButton = (
    <button
      type="button"
      onClick={() => column.toggleSorting(sorted === 'asc')}
      aria-label={`${typeof title === 'string' ? title : 'Column'}: ${sortLabel}`}
      className={cn(
        'inline-flex items-center gap-1.5 rounded-md',
        'px-2 py-1 -mx-2 -my-1', // hit area
        'text-xs font-semibold uppercase tracking-wide',
        'text-admin-fg-muted',
        '[@media(hover:hover)]:hover:bg-admin-state-hover',
        '[@media(hover:hover)]:hover:text-admin-fg-strong',
        'focus-visible:outline-none focus-visible:ring-2',
        'focus-visible:ring-admin-primary focus-visible:ring-offset-1',
        'transition-colors duration-150 motion-reduce:transition-none',
        sorted && 'text-admin-fg-strong',
      )}
    >
      <span className="truncate">{title}</span>
      <SortIcon
        className={cn(
          'h-3.5 w-3.5 shrink-0',
          sorted ? 'opacity-100' : 'opacity-60',
        )}
        aria-hidden="true"
      />
    </button>
  );

  // No dropdown: just the sort toggle.
  if (!withMenu) {
    return (
      <div
        className={cn('flex items-center', justify, className)}
        {...rest}
      >
        {triggerButton}
      </div>
    );
  }

  // Sort toggle + dropdown for Asc / Desc / Hide.
  return (
    <div className={cn('flex items-center', justify, className)} {...rest}>
      <DropdownMenu>
        <DropdownMenuTrigger asChild>
          <button
            type="button"
            aria-label={`${typeof title === 'string' ? title : 'Column'} options`}
            className={cn(
              'inline-flex items-center gap-1.5 rounded-md',
              'px-2 py-1 -mx-2 -my-1',
              'text-xs font-semibold uppercase tracking-wide',
              'text-admin-fg-muted',
              '[@media(hover:hover)]:hover:bg-admin-state-hover',
              '[@media(hover:hover)]:hover:text-admin-fg-strong',
              'focus-visible:outline-none focus-visible:ring-2',
              'focus-visible:ring-admin-primary focus-visible:ring-offset-1',
              'transition-colors duration-150 motion-reduce:transition-none',
              sorted && 'text-admin-fg-strong',
            )}
          >
            <span className="truncate">{title}</span>
            <SortIcon
              className={cn(
                'h-3.5 w-3.5 shrink-0',
                sorted ? 'opacity-100' : 'opacity-60',
              )}
              aria-hidden="true"
            />
          </button>
        </DropdownMenuTrigger>
        <DropdownMenuContent align={align === 'right' ? 'end' : 'start'}>
          <DropdownMenuItem onSelect={() => column.toggleSorting(false)}>
            <ArrowUp className="mr-2 h-3.5 w-3.5 text-admin-fg-muted" aria-hidden="true" />
            <span>Asc</span>
          </DropdownMenuItem>
          <DropdownMenuItem onSelect={() => column.toggleSorting(true)}>
            <ArrowDown className="mr-2 h-3.5 w-3.5 text-admin-fg-muted" aria-hidden="true" />
            <span>Desc</span>
          </DropdownMenuItem>
          {sorted ? (
            <DropdownMenuItem onSelect={() => column.clearSorting()}>
              <ArrowUpDown className="mr-2 h-3.5 w-3.5 text-admin-fg-muted" aria-hidden="true" />
              <span>Clear sort</span>
            </DropdownMenuItem>
          ) : null}
          {canHide ? (
            <>
              <DropdownMenuSeparator />
              <DropdownMenuItem onSelect={() => column.toggleVisibility(false)}>
                <EyeOff className="mr-2 h-3.5 w-3.5 text-admin-fg-muted" aria-hidden="true" />
                <span>Hide</span>
              </DropdownMenuItem>
            </>
          ) : null}
        </DropdownMenuContent>
      </DropdownMenu>
    </div>
  );
}
DataTableColumnHeader.displayName = 'DataTableColumnHeader';

export { DataTableColumnHeader };
