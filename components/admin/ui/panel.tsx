import Link from 'next/link';
import { cn } from '@/lib/utils';
import React from 'react';

export function Panel({
  title, icon: Icon, href, className, children,
}: { title: string; icon: React.ElementType; href?: string; className?: string; children: React.ReactNode }) {
  return (
    <div className={cn('rounded-xl border border-zinc-200 bg-white shadow-sm overflow-hidden dark:border-zinc-800 dark:bg-zinc-950 flex flex-col', className)}>
      <div className="flex items-center justify-between px-4 py-2.5 border-b border-zinc-100 dark:border-zinc-800/60 bg-zinc-50/60 dark:bg-zinc-900/40 shrink-0">
        <div className="flex items-center gap-2">
          <Icon className="h-4 w-4 text-zinc-500 dark:text-zinc-400" />
          <span className="text-[11px] font-extrabold uppercase tracking-[0.18em] text-zinc-600 dark:text-zinc-300 leading-none">{title}</span>
        </div>
        {href && (
          <Link href={href} aria-label={`Open ${title}`} className="text-[11px] font-bold text-violet-600 hover:text-violet-700 dark:text-violet-400 hover:underline leading-none">
            Open
          </Link>
        )}
      </div>
      <div className="flex-1 min-h-0">{children}</div>
    </div>
  );
}
