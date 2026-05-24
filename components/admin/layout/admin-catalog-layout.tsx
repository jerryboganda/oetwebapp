'use client';

import * as React from 'react';
import { LayoutGrid, List as ListIcon } from 'lucide-react';

import { cn } from '@/lib/utils';
import { Button } from '@/components/admin/ui/button';
import { PageHeader, type PageHeaderProps } from '@/components/admin/ui/page-header';

import { AdminPageShell, type AdminPageShellProps } from './admin-page-shell';

/**
 * AdminCatalogLayout — Template B "Catalog / Index"
 *
 * Use for list-of-items pages where each row is a card (e.g. `/admin/content`,
 * `/admin/study-plan-templates`). Section order:
 *
 *   1. PageHeader (title + breadcrumbs + actions)
 *   2. Toolbar    (left: filters slot; right: grid/list view toggle)
 *   3. Items area (grid OR list — driven by `viewMode`)
 *   4. Pagination (optional, anchored to the bottom)
 *
 * The empty state is delegated to the caller — pass an `<EmptyState />` as
 * the only child when `items.length === 0`.
 *
 * See: docs/admin-redesign/axelit-study/03-ADMIN_MACROSTRUCTURE.md §5 Template B
 */

export type CatalogViewMode = 'grid' | 'list';

/* ------------------------------------------------------------------ */
/* View-mode toggle                                                    */
/* ------------------------------------------------------------------ */

interface ViewModeToggleProps {
  viewMode: CatalogViewMode;
  onViewModeChange: (mode: CatalogViewMode) => void;
  className?: string;
}

function ViewModeToggle({
  viewMode,
  onViewModeChange,
  className,
}: ViewModeToggleProps) {
  return (
    <div
      role="group"
      aria-label="Toggle item view"
      className={cn(
        'inline-flex items-center rounded-admin-lg border border-admin-border',
        'bg-admin-bg-surface p-1 gap-1',
        className,
      )}
    >
      <Button
        type="button"
        size="icon"
        variant={viewMode === 'grid' ? 'primary' : 'ghost'}
        aria-label="Grid view"
        aria-pressed={viewMode === 'grid'}
        onClick={() => onViewModeChange('grid')}
        className="h-8 w-8 min-w-0"
      >
        <LayoutGrid className="h-4 w-4" aria-hidden="true" />
      </Button>
      <Button
        type="button"
        size="icon"
        variant={viewMode === 'list' ? 'primary' : 'ghost'}
        aria-label="List view"
        aria-pressed={viewMode === 'list'}
        onClick={() => onViewModeChange('list')}
        className="h-8 w-8 min-w-0"
      >
        <ListIcon className="h-4 w-4" aria-hidden="true" />
      </Button>
    </div>
  );
}

/* ------------------------------------------------------------------ */
/* Layout                                                              */
/* ------------------------------------------------------------------ */

export interface AdminCatalogLayoutProps
  extends Omit<AdminPageShellProps, 'children' | 'title'> {
  /** Page title — required. */
  title: PageHeaderProps['title'];
  description?: PageHeaderProps['description'];
  breadcrumbs?: PageHeaderProps['breadcrumbs'];
  actions?: PageHeaderProps['actions'];
  eyebrow?: PageHeaderProps['eyebrow'];

  /** Filter controls (search, dropdowns, tags). Anchored to the left of the toolbar. */
  filters?: React.ReactNode;
  /** Current view mode. Controlled prop — parent owns state. */
  viewMode?: CatalogViewMode;
  /** Called when the user clicks the grid/list toggle. Omit to hide the toggle. */
  onViewModeChange?: (mode: CatalogViewMode) => void;
  /** Hide the grid/list toggle entirely. Useful when a layout is fixed. */
  hideViewModeToggle?: boolean;
  /** Pagination control rendered at the bottom of the items area. */
  pagination?: React.ReactNode;
  /** Extra controls aligned to the right of the toolbar (e.g. sort menu). */
  toolbarTrailing?: React.ReactNode;

  /** Override the items-area class — escape hatch for non-standard grids. */
  itemsClassName?: string;
  /** Cards / rows rendered inside the items area. */
  children: React.ReactNode;
}

const GRID_CLASSES = [
  'grid gap-4',
  'grid-cols-1 sm:grid-cols-2 lg:grid-cols-3 xl:grid-cols-4',
] as const;

const LIST_CLASSES = ['flex flex-col gap-2'] as const;

const AdminCatalogLayout = React.forwardRef<
  HTMLDivElement,
  AdminCatalogLayoutProps
>(
  (
    {
      title,
      description,
      breadcrumbs,
      actions,
      eyebrow,
      filters,
      viewMode = 'grid',
      onViewModeChange,
      hideViewModeToggle = false,
      pagination,
      toolbarTrailing,
      itemsClassName,
      className,
      children,
      ...shellProps
    },
    ref,
  ) => {
    const showToolbar =
      Boolean(filters) ||
      Boolean(toolbarTrailing) ||
      (!hideViewModeToggle && typeof onViewModeChange === 'function');

    const itemsClass =
      viewMode === 'grid'
        ? cn(GRID_CLASSES)
        : cn(LIST_CLASSES);

    return (
      <AdminPageShell ref={ref} className={className} {...shellProps}>
        <PageHeader
          title={title}
          description={description}
          breadcrumbs={breadcrumbs}
          actions={actions}
          eyebrow={eyebrow}
        />

        {showToolbar ? (
          <div
            role="toolbar"
            aria-label="Catalog filters"
            data-slot="catalog-toolbar"
            className={cn(
              'flex flex-col gap-3 sm:flex-row sm:items-center sm:justify-between',
              'rounded-admin-lg border border-admin-border bg-admin-bg-surface',
              'p-3 sm:p-4 shadow-admin-sm',
            )}
          >
            <div className="flex flex-1 flex-wrap items-center gap-2 min-w-0">
              {filters}
            </div>
            <div className="flex items-center gap-2 shrink-0">
              {toolbarTrailing}
              {!hideViewModeToggle && onViewModeChange ? (
                <ViewModeToggle
                  viewMode={viewMode}
                  onViewModeChange={onViewModeChange}
                />
              ) : null}
            </div>
          </div>
        ) : null}

        <section
          aria-label="Catalog items"
          data-slot="catalog-items"
          data-view-mode={viewMode}
          className={cn(itemsClass, itemsClassName)}
        >
          {children}
        </section>

        {pagination ? (
          <nav
            aria-label="Pagination"
            data-slot="catalog-pagination"
            className="flex justify-center pt-2"
          >
            {pagination}
          </nav>
        ) : null}
      </AdminPageShell>
    );
  },
);
AdminCatalogLayout.displayName = 'AdminCatalogLayout';

export { AdminCatalogLayout, ViewModeToggle };
