'use client';

import Link from 'next/link';
import { ChevronRight, Home } from 'lucide-react';
import { cn } from '@/lib/utils';
import { Fragment, type ReactNode } from 'react';

// ─────────────────────────────────────────────────────────────────────────────
// Breadcrumbs — chevron-separated trail for nested admin pages.
//
// Design contract: DESIGN.md §5.18. Last item non-clickable.
// ─────────────────────────────────────────────────────────────────────────────

export interface BreadcrumbItem {
  label: string;
  href?: string;
  icon?: ReactNode;
}

export interface BreadcrumbsProps {
  items: BreadcrumbItem[];
  className?: string;
  /** Optional home link shown as the first item when true. */
  showHome?: boolean;
  homeHref?: string;
}

export function Breadcrumbs({ items, className, showHome = false, homeHref = '/' }: BreadcrumbsProps) {
  if (items.length === 0 && !showHome) return null;

  const full: BreadcrumbItem[] = showHome
    ? [{ label: 'Home', href: homeHref, icon: <Home className="h-3.5 w-3.5" aria-hidden /> }, ...items]
    : items;

  return (
    <nav aria-label="Breadcrumb" className={cn('min-w-0 flex-1', className)}>
      <ol className="flex items-center gap-1 overflow-hidden text-sm">
        {full.map((item, index) => {
          const isLast = index === full.length - 1;
          const content = (
            <span className="inline-flex items-center gap-1.5">
              {item.icon}
              <span className="truncate">{item.label}</span>
            </span>
          );
          return (
            <Fragment key={`${item.label}-${index}`}>
              <li className="min-w-0">
                {isLast || !item.href ? (
                  <span
                    className={cn(
                      'truncate font-semibold',
                      isLast ? 'text-navy' : 'text-muted',
                    )}
                    aria-current={isLast ? 'page' : undefined}
                  >
                    {content}
                  </span>
                ) : (
                  <Link
                    href={item.href}
                    className="truncate text-muted transition-colors hover:text-navy focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary focus-visible:ring-offset-2 focus-visible:rounded-md"
                  >
                    {content}
                  </Link>
                )}
              </li>
              {!isLast && (
                <li aria-hidden className="text-muted/60">
                  <ChevronRight className="h-3.5 w-3.5" />
                </li>
              )}
            </Fragment>
          );
        })}
      </ol>
    </nav>
  );
}
