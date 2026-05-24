'use client';

import * as React from 'react';
import { cva, type VariantProps } from 'class-variance-authority';

import { cn } from '@/lib/utils';

/* ─────────────────────────────────────────────────────────────────────
 * Skeleton — content loading placeholder for the OET admin DS.
 *
 * Partner pattern to <EmptyState>: rendered during fetch (NOT for error
 * or zero-data scenarios). Match the eventual content shape — spec 19 §2.20.
 *
 * Accessibility:
 *   - `aria-hidden` by default; surrounding container should expose
 *     `aria-busy="true"` while loading so SR users get one announcement
 *     rather than per-cell noise.
 *   - Animation is gated on `motion-safe` (Tailwind respects
 *     `prefers-reduced-motion: reduce` automatically).
 * ───────────────────────────────────────────────────────────────────── */

const skeletonVariants = cva(
  [
    'block rounded-md bg-admin-bg-subtle',
    // animate-pulse only when motion is permitted.
    'motion-safe:animate-pulse',
  ],
  {
    variants: {
      variant: {
        text: 'h-4 w-full',
        circle: 'aspect-square rounded-full w-full',
        avatar: 'h-10 w-10 rounded-full',
        card: 'h-32 w-full',
        // `bare` = no preset dimensions, caller controls via className.
        bare: '',
      },
    },
    defaultVariants: { variant: 'bare' },
  },
);

export interface SkeletonProps
  extends React.HTMLAttributes<HTMLDivElement>,
    VariantProps<typeof skeletonVariants> {}

function Skeleton({ className, variant, ...props }: SkeletonProps) {
  return (
    <div
      aria-hidden="true"
      className={cn(skeletonVariants({ variant }), className)}
      {...props}
    />
  );
}
Skeleton.displayName = 'Skeleton';

/* ─────────────────────────────────────────────────────────────────────
 * TableSkeleton — N rows × M cols of text skeletons with a header bar.
 *
 * Use inside <table>-shaped surfaces while data is fetching. Width
 * variation per column gives a more natural shimmer than uniform cells.
 * ───────────────────────────────────────────────────────────────────── */

export interface TableSkeletonProps extends React.HTMLAttributes<HTMLDivElement> {
  rows?: number;
  columns?: number;
  /** Show a separate <thead>-like header strip. */
  showHeader?: boolean;
}

function TableSkeleton({
  rows = 5,
  columns = 4,
  showHeader = true,
  className,
  ...props
}: TableSkeletonProps) {
  // Deterministic-ish width pattern so the shimmer doesn't look like a grid.
  const widthClass = (rowIdx: number, colIdx: number) => {
    if (colIdx === 0) return 'w-3/4';
    const pattern = ['w-full', 'w-5/6', 'w-2/3', 'w-1/2', 'w-4/5'];
    return pattern[(rowIdx + colIdx) % pattern.length];
  };

  return (
    <div
      role="status"
      aria-busy="true"
      aria-label="Loading table data"
      className={cn(
        'w-full overflow-hidden rounded-admin border border-admin-border bg-admin-bg-surface',
        className,
      )}
      {...props}
    >
      {showHeader ? (
        <div
          className={cn(
            'grid gap-3 border-b border-admin-border bg-admin-bg-subtle px-4 py-3',
          )}
          style={{ gridTemplateColumns: `repeat(${columns}, minmax(0, 1fr))` }}
        >
          {Array.from({ length: columns }).map((_, idx) => (
            <Skeleton
              key={`th-${idx}`}
              variant="text"
              className={cn('h-3', idx === 0 ? 'w-1/2' : 'w-1/3')}
            />
          ))}
        </div>
      ) : null}

      <div className="divide-y divide-admin-border">
        {Array.from({ length: rows }).map((_, rowIdx) => (
          <div
            key={`tr-${rowIdx}`}
            className="grid gap-3 px-4 py-3.5"
            style={{ gridTemplateColumns: `repeat(${columns}, minmax(0, 1fr))` }}
          >
            {Array.from({ length: columns }).map((_, colIdx) => (
              <Skeleton
                key={`td-${rowIdx}-${colIdx}`}
                variant="text"
                className={widthClass(rowIdx, colIdx)}
              />
            ))}
          </div>
        ))}
      </div>
      <span className="sr-only">Loading…</span>
    </div>
  );
}
TableSkeleton.displayName = 'TableSkeleton';

/* ─────────────────────────────────────────────────────────────────────
 * ListSkeleton — activity-feed shape: avatar + 2 text lines per row.
 * ───────────────────────────────────────────────────────────────────── */

export interface ListSkeletonProps extends React.HTMLAttributes<HTMLDivElement> {
  items?: number;
  /** Show a leading avatar circle on each row. */
  showAvatar?: boolean;
}

function ListSkeleton({
  items = 5,
  showAvatar = true,
  className,
  ...props
}: ListSkeletonProps) {
  return (
    <div
      role="status"
      aria-busy="true"
      aria-label="Loading list"
      className={cn('space-y-4', className)}
      {...props}
    >
      {Array.from({ length: items }).map((_, idx) => (
        <div key={`row-${idx}`} className="flex items-center gap-3">
          {showAvatar ? <Skeleton variant="avatar" /> : null}
          <div className="flex-1 space-y-2">
            <Skeleton variant="text" className="h-4 w-3/4" />
            <Skeleton variant="text" className="h-3 w-1/2" />
          </div>
        </div>
      ))}
      <span className="sr-only">Loading…</span>
    </div>
  );
}
ListSkeleton.displayName = 'ListSkeleton';

export { Skeleton, TableSkeleton, ListSkeleton, skeletonVariants };
