'use client';

import * as React from 'react';
import Link from 'next/link';
import { ArrowLeft, ChevronRight } from 'lucide-react';

import { cn } from '@/lib/utils';
import { Skeleton } from '@/components/admin/ui/skeleton';

/**
 * PageHeader — required atop every admin route per the macrostructure spec
 * (docs/admin-redesign/axelit-study/03-ADMIN_MACROSTRUCTURE.md).
 *
 * Layout owner for breadcrumbs, optional back link, title + description,
 * optional tinted icon, and a right-aligned action slot. Provides skeleton
 * fallback for in-flight pages.
 */

export type Breadcrumb = {
  label: string;
  href?: string;
};

export type PageHeaderProps = {
  title: string;
  description?: string;
  breadcrumbs?: Breadcrumb[];
  /**
   * Small uppercase kicker label rendered above the title (after
   * breadcrumbs / back link). Use for section identity or context cues —
   * e.g. "Catalog", "Operations", "Settings". Keep ≤ 24 chars.
   */
  eyebrow?: string;
  /** Right-aligned action slot (typically buttons). */
  actions?: React.ReactNode;
  /** Adds a "← Back" link above the breadcrumbs. */
  backHref?: string;
  /** Renders inside a tinted-circle to the left of the title. */
  icon?: React.ReactNode;
  loading?: boolean;
  className?: string;
};

function Breadcrumbs({ items }: { items: Breadcrumb[] }) {
  return (
    <nav aria-label="Breadcrumb" className="min-w-0">
      <ol className="flex flex-wrap items-center gap-1 text-xs text-admin-fg-muted">
        {items.map((item, i) => {
          const isLast = i === items.length - 1;
          return (
            <li key={`${item.label}-${i}`} className="flex items-center gap-1">
              {item.href && !isLast ? (
                <Link
                  href={item.href}
                  className="rounded-admin-sm px-0.5 hover:text-admin-fg-default hover:underline underline-offset-2 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[var(--admin-primary)] focus-visible:ring-offset-1 focus-visible:ring-offset-[var(--admin-bg-page)]"
                >
                  {item.label}
                </Link>
              ) : (
                <span
                  className={cn(
                    'px-0.5',
                    isLast && 'text-admin-fg-default font-medium',
                  )}
                  aria-current={isLast ? 'page' : undefined}
                >
                  {item.label}
                </span>
              )}
              {!isLast ? (
                <ChevronRight
                  className="h-3.5 w-3.5 shrink-0 text-admin-fg-muted/70"
                  aria-hidden="true"
                />
              ) : null}
            </li>
          );
        })}
      </ol>
    </nav>
  );
}

function PageHeaderSkeleton({ className }: { className?: string }) {
  return (
    <header
      className={cn('mb-6 sm:mb-8 flex flex-col gap-3', className)}
      aria-busy="true"
      aria-label="Loading page header"
    >
      <Skeleton className="h-3 w-40" />
      <Skeleton className="h-8 w-72 sm:h-9" />
      <Skeleton className="h-4 w-96 max-w-full" />
    </header>
  );
}

export const PageHeader = React.forwardRef<HTMLElement, PageHeaderProps>(
  (
    {
      title,
      description,
      breadcrumbs,
      eyebrow,
      actions,
      backHref,
      icon,
      loading,
      className,
    },
    ref,
  ) => {
    if (loading) {
      return <PageHeaderSkeleton className={className} />;
    }

    return (
      <header
        ref={ref}
        className={cn(
          'mb-6 sm:mb-8 flex flex-col gap-3 sm:flex-row sm:items-end sm:justify-between',
          className,
        )}
      >
        <div className="min-w-0 flex-1 space-y-2">
          {backHref ? (
            <Link
              href={backHref}
              className="inline-flex items-center gap-1 text-xs text-admin-fg-muted hover:text-admin-fg-default focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[var(--admin-primary)] focus-visible:ring-offset-1 focus-visible:ring-offset-[var(--admin-bg-page)] rounded-admin-sm"
            >
              <ArrowLeft className="h-3.5 w-3.5" aria-hidden="true" />
              <span>Back</span>
            </Link>
          ) : null}

          {breadcrumbs && breadcrumbs.length > 0 ? (
            <Breadcrumbs items={breadcrumbs} />
          ) : null}

          {eyebrow ? (
            <p className="mb-1 text-xs font-semibold uppercase tracking-wider text-[var(--admin-primary)]">
              {eyebrow}
            </p>
          ) : null}

          <div className="flex items-start gap-3">
            {icon ? (
              <span
                className="inline-flex h-10 w-10 shrink-0 items-center justify-center rounded-admin-md bg-[var(--admin-primary-tint)] p-2 text-[var(--admin-primary)]"
                aria-hidden="true"
              >
                {icon}
              </span>
            ) : null}
            <div className="min-w-0 flex-1">
              <h1 className="text-2xl sm:text-3xl font-bold tracking-tight text-admin-fg-strong leading-tight">
                {title}
              </h1>
              {description ? (
                <p className="mt-1 text-sm text-admin-fg-muted">{description}</p>
              ) : null}
            </div>
          </div>
        </div>

        {actions ? (
          <div className="flex w-full shrink-0 flex-wrap items-center gap-2 sm:w-auto sm:justify-end">
            {actions}
          </div>
        ) : null}
      </header>
    );
  },
);
PageHeader.displayName = 'PageHeader';
