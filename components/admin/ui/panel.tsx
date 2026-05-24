// Re-skinned 2026-05-24 for admin redesign — uses --admin-* token system
import Link from 'next/link';
import { cn } from '@/lib/utils';
import React from 'react';

export function Panel({
  title, icon: Icon, href, className, children,
}: { title: string; icon: React.ElementType; href?: string; className?: string; children: React.ReactNode }) {
  return (
    <div
      className={cn(
        'flex flex-col overflow-hidden rounded-admin border border-admin-border bg-admin-bg-surface shadow-admin-md',
        className,
      )}
    >
      <div className="flex shrink-0 items-center justify-between border-b border-admin-border bg-admin-bg-subtle/50 p-4 sm:p-5">
        <div className="flex items-center gap-2">
          <Icon className="h-4 w-4 text-admin-primary" />
          <h5 className="text-base font-semibold leading-none text-admin-fg-strong">{title}</h5>
        </div>
        {href && (
          <Link
            href={href}
            aria-label={`Open ${title}`}
            className="text-sm font-medium leading-none text-admin-primary hover:underline"
          >
            View all
          </Link>
        )}
      </div>
      <div className="min-h-0 flex-1 p-4 sm:p-5">{children}</div>
    </div>
  );
}
