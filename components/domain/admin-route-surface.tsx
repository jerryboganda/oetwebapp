'use client';

import { cloneElement, isValidElement } from 'react';
import type { ComponentProps, ElementType, HTMLAttributes, ReactNode } from 'react';
import { Sparkles } from 'lucide-react';
import { Card } from '@/components/ui/card';
import { LearnerPageHero, LearnerSurfaceCard, LearnerSurfaceSectionHeader } from '@/components/domain/learner-surface';
import type {
  LearnerPageHeroModel,
  LearnerPageHeroHighlight,
  LearnerSurfaceAccent,
  LearnerSurfaceAction,
  LearnerSurfaceCardModel,
} from '@/lib/learner-surface';
import { cn } from '@/lib/utils';

type LearnerSurfaceSectionHeaderProps = ComponentProps<typeof LearnerSurfaceSectionHeader>;

const toneAccentMap: Record<'default' | 'success' | 'warning' | 'danger', LearnerSurfaceAccent> = {
  default: 'navy',
  success: 'emerald',
  warning: 'amber',
  danger: 'rose',
};

export function AdminRouteWorkspace({ className, children, ...props }: HTMLAttributes<HTMLDivElement>) {
  return (
    <div className={cn('flex flex-col gap-3 min-h-0', className)} {...props}>
      {children}
    </div>
  );
}

export function AdminRouteHero(props: LearnerPageHeroModel) {
  return <LearnerPageHero {...props} />;
}

export function AdminRouteSectionHeader({
  action,
  actions,
  meta,
  eyebrow = 'Admin Workspace',
  icon: Icon = Sparkles,
  accent = 'navy',
  highlights,
  className,
  title,
  description,
}: LearnerSurfaceSectionHeaderProps & {
  actions?: ReactNode;
  meta?: string;
  icon?: any;
  accent?: LearnerSurfaceAccent;
  highlights?: LearnerPageHeroHighlight[];
}) {
  const hasActions = Boolean(action) || Boolean(actions);
  return (
    <div className={cn('flex flex-col gap-4 sm:flex-row sm:items-center sm:justify-between rounded-xl border border-zinc-800 bg-zinc-950 px-5 py-4 shadow-sm', className)}>
      <div className="flex items-center gap-3 min-w-0 flex-1">
        <div className="rounded-lg bg-violet-500/20 p-2 shrink-0">
          <Icon className="h-5 w-5 text-violet-400" />
        </div>
        <div className="min-w-0">
          <div className="flex items-center gap-2">
            {title && <h1 className="text-xl font-bold tracking-tight text-white leading-none">{title}</h1>}
            {meta && <span className="rounded bg-zinc-800/80 px-2 py-0.5 text-[10px] uppercase tracking-widest text-zinc-400 font-bold">{meta}</span>}
          </div>
          {description && <p className="text-xs text-zinc-400 leading-snug mt-1.5 truncate">{description}</p>}
        </div>
      </div>
      {hasActions && (
        <div className="flex items-center gap-2 shrink-0">
          {action}
          {actions}
        </div>
      )}
    </div>
  );
}

export function AdminRouteFreshnessBadge({ value }: { value?: string | null }) {
  return (
    <span className="text-xs text-muted">
      {value ? `Updated ${new Date(value).toLocaleString()}` : 'Freshness unavailable'}
    </span>
  );
}

export function AdminRouteSummaryCard({
  label, value, hint, accent, icon, tone = 'default', statusLabel, primaryAction, secondaryAction, className,
}: {
  label: string; value: string | number; hint?: string; accent?: string; icon?: any; tone?: 'default'|'success'|'warning'|'danger'; statusLabel?: string; primaryAction?: any; secondaryAction?: any; className?: string;
}) {
  const bg: Record<string, string> = {
    default: 'bg-white border-zinc-200 dark:bg-zinc-950 dark:border-zinc-800',
    success: 'bg-emerald-50 border-emerald-200/60 dark:bg-emerald-950/40 dark:border-emerald-800/40',
    warning: 'bg-amber-50 border-amber-200/60 dark:bg-amber-950/40 dark:border-amber-800/40',
    danger: 'bg-rose-50 border-rose-200/60 dark:bg-rose-950/40 dark:border-rose-800/40',
  };
  const valMap: Record<string, string> = {
    default: 'text-zinc-900 dark:text-zinc-100', success: 'text-emerald-700 dark:text-emerald-400', warning: 'text-amber-700 dark:text-amber-400', danger: 'text-rose-700 dark:text-rose-400',
  };
  const icoMap: Record<string, string> = {
    default: 'text-zinc-400', success: 'text-emerald-500', warning: 'text-amber-500', danger: 'text-rose-500',
  };
  
  const IconToRender = typeof icon === 'function' ? icon : null;

  return (
    <div className={cn('flex items-center gap-3 rounded-xl border px-4 py-3 flex-1 min-w-0 shadow-sm', bg[tone], className)}>
      {IconToRender ? (
        <IconToRender className={cn('h-5 w-5 shrink-0', icoMap[tone])} />
      ) : icon ? (
        <div className={cn('h-5 w-5 shrink-0 flex items-center justify-center [&>svg]:w-full [&>svg]:h-full', icoMap[tone])}>
          {icon}
        </div>
      ) : null}
      <div className="min-w-0 flex-1">
        <p className="text-[10px] font-extrabold uppercase tracking-[0.18em] text-zinc-500 dark:text-zinc-400 truncate leading-none mb-1">{label}</p>
        <p className={cn('text-xl font-black leading-none tabular-nums tracking-tight', valMap[tone])}>{value}</p>
        {hint && <p className="mt-1 text-xs text-zinc-500 dark:text-zinc-400 leading-none">{hint}</p>}
      </div>
    </div>
  );
}

export function AdminRoutePanel({
  title,
  description,
  actions,
  children,
  className,
  contentClassName,
}: {
  title?: string;
  description?: string;
  actions?: ReactNode;
  children: ReactNode;
  className?: string;
  contentClassName?: string;
}) {
  return (
    <div className={cn('rounded-xl border border-zinc-200 bg-white shadow-sm overflow-hidden dark:border-zinc-800 dark:bg-zinc-950 flex flex-col', className)}>
      {(title || description || actions) && (
        <div className="flex flex-col gap-4 sm:flex-row sm:items-center sm:justify-between px-4 py-2.5 border-b border-zinc-100 dark:border-zinc-800/60 bg-zinc-50/60 dark:bg-zinc-900/40 shrink-0">
          <div className="flex flex-col">
            {title && <h2 className="text-[11px] font-extrabold uppercase tracking-[0.18em] text-zinc-600 dark:text-zinc-300 leading-none">{title}</h2>}
            {description && <p className="mt-1 text-xs text-zinc-500 dark:text-zinc-400">{description}</p>}
          </div>
          {actions && <div className="flex flex-wrap items-center gap-2">{actions}</div>}
        </div>
      )}
      <div className={cn('flex-1 min-h-0 p-4', contentClassName)}>{children}</div>
    </div>
  );
}
