'use client';

import * as React from 'react';
import {
  type ColumnDef,
  type ColumnFiltersState,
  type Row,
  type SortingState,
  type Table as TanstackTable,
  type VisibilityState,
  flexRender,
  getCoreRowModel,
  getFacetedRowModel,
  getFacetedUniqueValues,
  getFilteredRowModel,
  getPaginationRowModel,
  getSortedRowModel,
  useReactTable,
} from '@tanstack/react-table';
import { ListFilter, Search, X } from 'lucide-react';

import { cn } from '@/lib/utils';
import { Button } from './button';
import { Checkbox } from './checkbox';
import {
  DropdownMenu,
  DropdownMenuCheckboxItem,
  DropdownMenuContent,
  DropdownMenuLabel,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
} from './dropdown-menu';
import { EmptyState } from './empty-state';
import { Input } from './input';
import { TableSkeleton } from './skeleton';
import { DataTablePagination } from './data-table-pagination';

/* ─────────────────────────────────────────────────────────────────────
 * DataTable — generic admin data-table primitive (TanStack Table v8).
 *
 * Composes: Toolbar (search • bulk actions • column visibility •
 * custom actions) + Table chrome (sortable headers • selectable rows)
 * + Pagination footer.
 *
 * Loading: renders <TableSkeleton> matching column count.
 * Empty:   renders <EmptyState size="sm"> with optional action.
 *
 * Selection contract:
 *   - Header checkbox is the "select all on the current page"; goes
 *     indeterminate when only some rows on this page are selected.
 *   - Per-row checkbox selects that row; sets aria-selected on the <tr>.
 *
 * Accessibility:
 *   - <th scope="col">, aria-sort wired from column sort state.
 *   - Selected <tr aria-selected="true">.
 *   - Row-click rows expose role="button" + tabIndex=0 + Enter/Space
 *     keyboard activation when onRowClick is provided.
 * ───────────────────────────────────────────────────────────────────── */

export type DataTableProps<TData, TValue> = {
  columns: ColumnDef<TData, TValue>[];
  data: TData[];
  enableSorting?: boolean;
  enableFiltering?: boolean;
  enableSelection?: boolean;
  enablePagination?: boolean;
  enableColumnVisibility?: boolean;
  initialPageSize?: number;
  pageSizeOptions?: number[];
  loading?: boolean;
  emptyMessage?: string;
  emptyAction?: { label: string; href?: string; onClick?: () => void };
  onRowClick?: (row: TData) => void;
  searchPlaceholder?: string;
  /** Column ids included in the global-filter search. */
  searchableColumns?: string[];
  toolbarActions?: React.ReactNode;
  bulkActions?: (selectedRows: TData[]) => React.ReactNode;
  className?: string;
};

function DataTableInner<TData, TValue>({
  columns,
  data,
  enableSorting = true,
  enableFiltering = true,
  enableSelection = false,
  enablePagination = true,
  enableColumnVisibility = true,
  initialPageSize = 20,
  pageSizeOptions = [10, 20, 50, 100],
  loading = false,
  emptyMessage = 'No results found.',
  emptyAction,
  onRowClick,
  searchPlaceholder = 'Search…',
  searchableColumns,
  toolbarActions,
  bulkActions,
  className,
}: DataTableProps<TData, TValue>) {
  /* ── State ──────────────────────────────────────────────────────── */
  const [sorting, setSorting] = React.useState<SortingState>([]);
  const [columnFilters, setColumnFilters] = React.useState<ColumnFiltersState>(
    [],
  );
  const [columnVisibility, setColumnVisibility] =
    React.useState<VisibilityState>({});
  const [rowSelection, setRowSelection] = React.useState({});
  const [globalFilter, setGlobalFilter] = React.useState('');

  /* ── Inject selection checkbox column when enabled ──────────────── */
  const effectiveColumns = React.useMemo<ColumnDef<TData, TValue>[]>(() => {
    if (!enableSelection) return columns;
    const selectColumn: ColumnDef<TData, TValue> = {
      id: '__select__',
      enableSorting: false,
      enableHiding: false,
      size: 36,
      header: ({ table }) => {
        const allSelected = table.getIsAllPageRowsSelected();
        const someSelected = table.getIsSomePageRowsSelected();
        const checked: boolean | 'indeterminate' = allSelected
          ? true
          : someSelected
            ? 'indeterminate'
            : false;
        return (
          <Checkbox
            checked={checked}
            onCheckedChange={(value) =>
              table.toggleAllPageRowsSelected(value === true)
            }
            aria-label="Select all rows on this page"
          />
        );
      },
      cell: ({ row }) => (
        <Checkbox
          checked={row.getIsSelected()}
          onCheckedChange={(value) => row.toggleSelected(value === true)}
          aria-label={`Select row ${row.index + 1}`}
          // Prevent row-click from firing when the checkbox is clicked.
          onClick={(e) => e.stopPropagation()}
        />
      ),
    };
    return [selectColumn, ...columns];
  }, [columns, enableSelection]);

  /* ── Global filter resolver ─────────────────────────────────────── */
  // When searchableColumns is provided, restrict the filter to those
  // column ids; otherwise stringify every cell value.
  const globalFilterFn = React.useCallback<
    (row: Row<TData>, columnId: string, filterValue: string) => boolean
  >(
    (row, _columnId, filterValue) => {
      if (!filterValue) return true;
      const needle = String(filterValue).toLowerCase();
      const ids =
        searchableColumns && searchableColumns.length > 0
          ? searchableColumns
          : row.getAllCells().map((c) => c.column.id);
      for (const id of ids) {
        const value = row.getValue(id);
        if (value == null) continue;
        if (String(value).toLowerCase().includes(needle)) return true;
      }
      return false;
    },
    [searchableColumns],
  );

  /* ── TanStack Table instance ────────────────────────────────────── */
  const table = useReactTable({
    data,
    columns: effectiveColumns,
    state: {
      sorting,
      columnFilters,
      columnVisibility,
      rowSelection,
      globalFilter,
    },
    initialState: {
      pagination: { pageIndex: 0, pageSize: initialPageSize },
    },
    enableSorting,
    enableRowSelection: enableSelection,
    enableHiding: enableColumnVisibility,
    onSortingChange: setSorting,
    onColumnFiltersChange: setColumnFilters,
    onColumnVisibilityChange: setColumnVisibility,
    onRowSelectionChange: setRowSelection,
    onGlobalFilterChange: setGlobalFilter,
    globalFilterFn,
    getCoreRowModel: getCoreRowModel(),
    getSortedRowModel: enableSorting ? getSortedRowModel() : undefined,
    getFilteredRowModel: enableFiltering ? getFilteredRowModel() : undefined,
    getPaginationRowModel: enablePagination
      ? getPaginationRowModel()
      : undefined,
    getFacetedRowModel: getFacetedRowModel(),
    getFacetedUniqueValues: getFacetedUniqueValues(),
  });

  /* ── Derived values ─────────────────────────────────────────────── */
  const selectedRowModels = table.getFilteredSelectedRowModel().rows;
  const hasSelection = enableSelection && selectedRowModels.length > 0;
  const visibleColumnCount = table
    .getAllLeafColumns()
    .filter((c) => c.getIsVisible()).length;
  const rowsToRender = table.getRowModel().rows;
  const isEmpty = !loading && rowsToRender.length === 0;

  /* ── Toolbar (normal vs bulk-selection mode) ────────────────────── */
  const renderToolbar = () => {
    if (hasSelection) {
      const selectedData = selectedRowModels.map((r) => r.original);
      return (
        <div
          className={cn(
            'flex flex-wrap items-center gap-2 rounded-t-admin',
            'border-b border-admin-border-default',
            'bg-admin-primary-tint px-3 py-2',
          )}
          role="region"
          aria-label="Bulk actions"
        >
          <span className="text-sm font-medium text-admin-fg-strong tabular-nums">
            {selectedRowModels.length.toLocaleString()} selected
          </span>
          <span className="text-admin-fg-muted" aria-hidden="true">
            ·
          </span>
          <div className="flex flex-wrap items-center gap-2">
            {bulkActions ? bulkActions(selectedData) : null}
          </div>
          <Button
            variant="ghost"
            size="sm"
            className="ml-auto"
            onClick={() => table.resetRowSelection()}
            startIcon={<X className="h-3.5 w-3.5" />}
          >
            Clear selection
          </Button>
        </div>
      );
    }

    return (
      <div className="flex flex-wrap items-center gap-2 px-3 py-2">
        {enableFiltering ? (
          <div className="min-w-0 flex-1 sm:max-w-sm">
            <Input
              value={globalFilter}
              onChange={(e) => setGlobalFilter(e.target.value)}
              placeholder={searchPlaceholder}
              size="sm"
              startIcon={<Search className="h-3.5 w-3.5" />}
              aria-label="Search"
            />
          </div>
        ) : (
          <div className="flex-1" />
        )}

        {toolbarActions ? (
          <div className="flex flex-wrap items-center gap-2">
            {toolbarActions}
          </div>
        ) : null}

        {enableColumnVisibility ? (
          <DropdownMenu>
            <DropdownMenuTrigger asChild>
              <Button
                variant="outline"
                size="sm"
                startIcon={<ListFilter className="h-3.5 w-3.5" />}
                aria-label="Toggle columns"
              >
                <span className="hidden sm:inline">View</span>
              </Button>
            </DropdownMenuTrigger>
            <DropdownMenuContent align="end" className="w-[180px]">
              <DropdownMenuLabel>Toggle columns</DropdownMenuLabel>
              <DropdownMenuSeparator />
              {table
                .getAllLeafColumns()
                .filter(
                  (column) =>
                    column.getCanHide() && column.id !== '__select__',
                )
                .map((column) => {
                  const label =
                    typeof column.columnDef.header === 'string'
                      ? column.columnDef.header
                      : column.id;
                  return (
                    <DropdownMenuCheckboxItem
                      key={column.id}
                      checked={column.getIsVisible()}
                      onCheckedChange={(value) =>
                        column.toggleVisibility(!!value)
                      }
                      onSelect={(e) => e.preventDefault()}
                      className="capitalize"
                    >
                      {label}
                    </DropdownMenuCheckboxItem>
                  );
                })}
            </DropdownMenuContent>
          </DropdownMenu>
        ) : null}
      </div>
    );
  };

  /* ── Cell wrapping helper for click + keyboard semantics ─────────── */
  const handleRowKeyDown = (
    event: React.KeyboardEvent<HTMLTableRowElement>,
    row: TData,
  ) => {
    if (!onRowClick) return;
    if (event.key === 'Enter' || event.key === ' ') {
      event.preventDefault();
      onRowClick(row);
    }
  };

  /* ── Render ──────────────────────────────────────────────────────── */
  return (
    <div
      className={cn(
        'flex w-full flex-col overflow-hidden rounded-admin',
        'border border-admin-border-default bg-admin-bg-surface',
        'shadow-admin-sm',
        className,
      )}
    >
      {/* Toolbar */}
      {renderToolbar()}

      {/* Table */}
      <div className="relative w-full overflow-x-auto">
        {loading ? (
          <TableSkeleton
            rows={Math.min(initialPageSize, 8)}
            columns={visibleColumnCount}
            showHeader
            className="rounded-none border-0 border-t border-admin-border-default"
          />
        ) : isEmpty ? (
          <div className="border-t border-admin-border-default px-4 py-10">
            <EmptyState
              size="sm"
              title={emptyMessage}
              primaryAction={emptyAction}
            />
          </div>
        ) : (
          <table className="w-full text-sm" role="table">
            <thead className="bg-admin-bg-subtle">
              {table.getHeaderGroups().map((headerGroup) => (
                <tr
                  key={headerGroup.id}
                  className="border-t border-admin-border-default first:border-t-0"
                >
                  {headerGroup.headers.map((header) => {
                    const isSelectCol = header.column.id === '__select__';
                    const sortDir = header.column.getIsSorted();
                    const ariaSort: React.AriaAttributes['aria-sort'] =
                      !header.column.getCanSort()
                        ? undefined
                        : sortDir === 'asc'
                          ? 'ascending'
                          : sortDir === 'desc'
                            ? 'descending'
                            : 'none';
                    return (
                      <th
                        key={header.id}
                        scope="col"
                        aria-sort={ariaSort}
                        style={
                          isSelectCol
                            ? { width: 36, minWidth: 36 }
                            : undefined
                        }
                        className={cn(
                          'px-3 py-2.5 text-left',
                          'text-xs font-semibold uppercase tracking-wide',
                          'text-admin-fg-muted',
                          'whitespace-nowrap',
                        )}
                      >
                        {header.isPlaceholder
                          ? null
                          : flexRender(
                              header.column.columnDef.header,
                              header.getContext(),
                            )}
                      </th>
                    );
                  })}
                </tr>
              ))}
            </thead>
            <tbody>
              {rowsToRender.map((row) => {
                const selected = row.getIsSelected();
                const clickable = Boolean(onRowClick);
                return (
                  <tr
                    key={row.id}
                    aria-selected={selected || undefined}
                    role={clickable ? 'button' : undefined}
                    tabIndex={clickable ? 0 : undefined}
                    onClick={
                      clickable ? () => onRowClick!(row.original) : undefined
                    }
                    onKeyDown={
                      clickable
                        ? (e) => handleRowKeyDown(e, row.original)
                        : undefined
                    }
                    data-state={selected ? 'selected' : undefined}
                    className={cn(
                      'border-t border-admin-border-default first:border-t-0',
                      'transition-colors duration-150 motion-reduce:transition-none',
                      '[@media(hover:hover)]:hover:bg-admin-state-hover',
                      clickable && [
                        'cursor-pointer',
                        'focus:outline-none focus-visible:outline-none',
                        'focus-visible:ring-2 focus-visible:ring-inset',
                        'focus-visible:ring-admin-primary',
                      ],
                      selected && 'bg-admin-state-selected',
                    )}
                  >
                    {row.getVisibleCells().map((cell) => (
                      <td
                        key={cell.id}
                        className={cn(
                          'px-3 py-3 align-middle',
                          'text-sm text-admin-fg-default',
                        )}
                      >
                        {flexRender(
                          cell.column.columnDef.cell,
                          cell.getContext(),
                        )}
                      </td>
                    ))}
                  </tr>
                );
              })}
            </tbody>
          </table>
        )}
      </div>

      {/* Pagination footer */}
      {enablePagination && !loading && !isEmpty ? (
        <DataTablePagination
          table={table as TanstackTable<TData>}
          pageSizeOptions={pageSizeOptions}
          hideSelectionCount={!enableSelection}
        />
      ) : null}
    </div>
  );
}

// Exported wrapper so the generic signature is preserved at the call site.
function DataTable<TData, TValue>(props: DataTableProps<TData, TValue>) {
  return <DataTableInner {...props} />;
}
DataTable.displayName = 'DataTable';

export { DataTable };
