import Link from 'next/link';
import { cn } from '@/lib/utils';
import React from 'react';

export function Panel({
  title, icon: Icon, href, className, children,
}: { title: string; icon: React.ElementType; href?: string; className?: string; children: React.ReactNode }) {
  return (
    <div className={cn('rounded-2xl border border-admin-border bg-admin-surface shadow-sm overflow-hidden flex flex-col', className)}>
      <div className="flex items-center justify-between px-4 py-2.5 border-b border-admin-border/60 bg-admin-surface-raised/40 shrink-0">
        <div className="flex items-center gap-2">
          <Icon className="h-4 w-4 text-admin-text-muted" />
          <span className="text-xs font-bold uppercase tracking-[0.18em] text-admin-text-muted leading-none">{title}</span>
        </div>
        {href && (
          <Link href={href} aria-label={`Open ${title}`} className="text-xs font-bold text-violet-400 hover:text-violet-300 hover:underline leading-none">
            Open
          </Link>
        )}
      </div>
      <div className="flex-1 min-h-0">{children}</div>
    </div>
  );
}
