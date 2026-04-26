'use client';

import { useMemo, type ReactNode } from 'react';

import { cn } from '@/lib/utils';

import { Button } from './button';
import { Select } from './form-controls';

/**
 * Shared pagination primitive used across admin/expert/sponsor list pages.
 *
 * Replaces the inline pattern previously duplicated in app/admin/users/page.tsx,
 * app/admin/audit-logs/page.tsx, and 9 other surfaces. Visual + a11y semantics
 * match those originals exactly:
 * - Left side: "Showing {start}–{end} of {total} {label}".
 * - Right side: page-size <Select> + Previous / "Page X of Y" / Next.
 *
 * Behavior contract:
 * - `page` is 1-indexed.
 * - Changing page size resets to page 1 (caller responsibility — onPageSizeChange
 *   is invoked with the next page size; it is up to the parent to also reset
 *   page if desired). The original sites all reset to page 1 explicitly, and
 *   this component's default `onPageSizeChange` handler does the same when
 *   `resetOnPageSizeChange` is true (default).
 * - Disables Previous on page<=1 and Next on page>=totalPages.
 * - Hides itself entirely when total === 0 unless `showWhenEmpty` is set.
 */
export interface PaginationProps {
  /** Current 1-indexed page. */
  page: number;
  /** Rows per page. */
  pageSize: number;
  /** Total row count across all pages. */
  total: number;
  /** Setter for page (1-indexed). */
  onPageChange: (next: number) => void;
  /** Setter for page size. Defaults to also resetting page to 1 unless `resetOnPageSizeChange={false}`. */
  onPageSizeChange: (next: number) => void;
  /** Page size options. Defaults to [10, 20, 50]. */
  pageSizeOptions?: ReadonlyArray<number>;
  /** Singular noun describing rows ("user", "event", "lesson"). Defaults to "row". */
  itemLabel?: string;
  /** Plural noun. Defaults to `${itemLabel}s`. */
  itemLabelPlural?: string;
  /**
   * When true (default), changing page size will also call `onPageChange(1)`
   * before `onPageSizeChange`. Set to false if the parent handles reset itself.
   */
  resetOnPageSizeChange?: boolean;
  /** Render even when total is 0. Defaults to false. */
  showWhenEmpty?: boolean;
  /** Optional override for the summary line (replaces "Showing X-Y of N items"). */
  summary?: ReactNode;
  /** Extra classes for the wrapper. */
  className?: string;
}

const DEFAULT_PAGE_SIZES: ReadonlyArray<number> = [10, 20, 50];

export function Pagination({
  page,
  pageSize,
  total,
  onPageChange,
  onPageSizeChange,
  pageSizeOptions = DEFAULT_PAGE_SIZES,
  itemLabel = 'row',
  itemLabelPlural,
  resetOnPageSizeChange = true,
  showWhenEmpty = false,
  summary,
  className,
}: PaginationProps) {
  const totalPages = useMemo(
    () => Math.max(1, Math.ceil(total / Math.max(1, pageSize))),
    [total, pageSize],
  );
  const pageStart = total === 0 ? 0 : (page - 1) * pageSize + 1;
  const pageEnd = total === 0 ? 0 : Math.min(total, page * pageSize);
  const plural = itemLabelPlural ?? `${itemLabel}s`;
  const smallestPageSize = pageSizeOptions.length > 0 ? Math.min(...pageSizeOptions) : pageSize;
  const showSizeSelector = total > smallestPageSize;

  if (total === 0 && !showWhenEmpty) {
    return null;
  }

  const handlePageSizeChange = (next: number) => {
    if (resetOnPageSizeChange) {
      onPageChange(1);
    }
    onPageSizeChange(next);
  };

  return (
    <div
      className={cn(
        'flex flex-col gap-3 md:flex-row md:items-center md:justify-between',
        className,
      )}
    >
      <div className="text-sm text-muted">
        {summary ?? (
          <>
            Showing {pageStart}–{pageEnd} of {total} {total === 1 ? itemLabel : plural}
          </>
        )}
      </div>
      <div className="flex flex-col gap-3 md:flex-row md:items-center md:gap-4">
        {showSizeSelector ? (
          <div className="min-w-36">
            <Select
              label="Rows per page"
              value={String(pageSize)}
              onChange={(event) => handlePageSizeChange(Number(event.target.value))}
              options={pageSizeOptions.map((size) => ({
                value: String(size),
                label: String(size),
              }))}
            />
          </div>
        ) : null}
        <div className="flex items-center gap-2 pt-5 md:pt-0">
          <Button
            variant="outline"
            onClick={() => onPageChange(Math.max(1, page - 1))}
            disabled={page <= 1}
          >
            Previous
          </Button>
          <span className="text-sm text-muted">
            Page {page} of {totalPages}
          </span>
          <Button
            variant="outline"
            onClick={() => onPageChange(Math.min(totalPages, page + 1))}
            disabled={page >= totalPages}
          >
            Next
          </Button>
        </div>
      </div>
    </div>
  );
}
