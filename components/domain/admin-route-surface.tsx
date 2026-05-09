'use client';

import { cloneElement, isValidElement } from 'react';
import Link from 'next/link';
import type { ComponentProps, ElementType, HTMLAttributes, ReactNode } from 'react';
import { Sparkles } from 'lucide-react';
import { Button } from '@/components/ui/button';
import { LearnerSurfaceSectionHeader } from '@/components/domain/learner-surface';
import type {
  LearnerPageHeroModel,
  LearnerPageHeroHighlight,
  LearnerSurfaceAccent,
  LearnerSurfaceAction,
} from '@/lib/learner-surface';
import { sanitizeLearnerPageHeroHighlights } from '@/lib/learner-surface';
import { cn } from '@/lib/utils';

type LearnerSurfaceSectionHeaderProps = ComponentProps<typeof LearnerSurfaceSectionHeader>;
type AdminRouteIcon = ElementType | ReactNode;

const accentClassMap: Record<LearnerSurfaceAccent, { icon: string; chip: string }> = {
  primary: { icon: 'bg-violet-500/20 text-violet-400', chip: 'bg-violet-500/10 text-violet-300 border-violet-500/20' },
  navy: { icon: 'bg-violet-500/20 text-violet-400', chip: 'bg-zinc-800/80 text-zinc-300 border-zinc-700' },
  amber: { icon: 'bg-amber-500/15 text-amber-400', chip: 'bg-amber-500/10 text-amber-300 border-amber-500/20' },
  blue: { icon: 'bg-blue-500/15 text-blue-400', chip: 'bg-blue-500/10 text-blue-300 border-blue-500/20' },
  indigo: { icon: 'bg-indigo-500/15 text-indigo-400', chip: 'bg-indigo-500/10 text-indigo-300 border-indigo-500/20' },
  purple: { icon: 'bg-purple-500/15 text-purple-400', chip: 'bg-purple-500/10 text-purple-300 border-purple-500/20' },
  rose: { icon: 'bg-rose-500/15 text-rose-400', chip: 'bg-rose-500/10 text-rose-300 border-rose-500/20' },
  emerald: { icon: 'bg-emerald-500/15 text-emerald-400', chip: 'bg-emerald-500/10 text-emerald-300 border-emerald-500/20' },
  slate: { icon: 'bg-slate-500/15 text-slate-300', chip: 'bg-slate-500/10 text-slate-300 border-slate-500/20' },
};

function renderAdminRouteIcon(icon: AdminRouteIcon | undefined, className: string) {
  if (!icon) return null;
  if (typeof icon === 'function' || typeof icon === 'string') {
    const Icon = icon as ElementType;
    return <Icon className={className} />;
  }
  if (isValidElement<{ className?: string }>(icon)) {
    return cloneElement(icon, { className: cn(icon.props.className, className) });
  }
  return null;
}

export function AdminRouteWorkspace({ className, children, ...props }: HTMLAttributes<HTMLDivElement>) {
  return (
    <div className={cn('flex flex-col gap-3 min-h-0', className)} {...props}>
      {children}
    </div>
  );
}

function renderAdminAction(action: LearnerSurfaceAction | undefined) {
  if (!action) return null;

  if (action.href) {
    return (
      <Link
        href={action.href}
        className={cn(
          'inline-flex min-h-10 items-center justify-center rounded-lg px-4 text-sm font-bold transition-colors focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-violet-400',
          action.variant === 'outline' || action.variant === 'secondary'
            ? 'border border-zinc-700 text-zinc-100 hover:bg-zinc-900'
            : action.variant === 'ghost'
              ? 'text-zinc-300 hover:bg-zinc-900'
              : 'bg-violet-500 text-white hover:bg-violet-400',
        )}
      >
        {action.label}
      </Link>
    );
  }

  return (
    <Button variant={action.variant === 'ghost' ? 'ghost' : action.variant === 'outline' ? 'outline' : 'primary'} onClick={action.onClick}>
      {action.label}
    </Button>
  );
}

export function AdminRouteHero({
  title,
  description,
  eyebrow = 'Admin Workspace',
  icon: Icon = Sparkles,
  accent = 'navy',
  highlights: rawHighlights,
  aside,
  primaryAction,
  secondaryAction,
}: LearnerPageHeroModel & {
  primaryAction?: LearnerSurfaceAction;
  secondaryAction?: LearnerSurfaceAction;
}) {
  const palette = accentClassMap[accent];
  const highlights = sanitizeLearnerPageHeroHighlights(rawHighlights);
  const hasActions = Boolean(primaryAction) || Boolean(secondaryAction);

  return (
    <section className="rounded-xl border border-zinc-800 bg-zinc-950 px-5 py-4 shadow-sm">
      <div className="flex flex-col gap-4 xl:flex-row xl:items-start xl:justify-between">
        <div className="min-w-0 flex-1">
          <div className="flex items-start gap-3">
            <div className={cn('mt-0.5 flex h-10 w-10 shrink-0 items-center justify-center rounded-lg', palette.icon)}>
              {renderAdminRouteIcon(Icon, 'h-5 w-5')}
            </div>
            <div className="min-w-0">
              {eyebrow && <p className="text-[10px] font-extrabold uppercase tracking-[0.18em] text-zinc-500">{eyebrow}</p>}
              <h1 className="mt-1 text-xl font-bold leading-tight tracking-tight text-white">{title}</h1>
              <p className="mt-1.5 max-w-4xl text-sm leading-6 text-zinc-400">{description}</p>
            </div>
          </div>
          {highlights.length > 0 && (
            <div className="mt-4 flex flex-wrap gap-2">
              {highlights.map((item) => {
                const HighlightIcon = item.icon;
                return (
                  <div key={`${item.label}-${item.value}`} className="min-w-0 rounded-lg border border-zinc-800 bg-zinc-900/60 px-3 py-2">
                    <div className="flex items-center gap-2 text-[10px] font-extrabold uppercase tracking-[0.16em] text-zinc-500">
                      {HighlightIcon && <HighlightIcon className="h-3.5 w-3.5" />}
                      {item.label}
                    </div>
                    <p className="mt-1 text-sm font-bold text-zinc-100">{item.value}</p>
                  </div>
                );
              })}
            </div>
          )}
          {hasActions && (
            <div className="mt-4 flex flex-wrap gap-2">
              {renderAdminAction(primaryAction)}
              {renderAdminAction(secondaryAction)}
            </div>
          )}
        </div>
        {aside ? <div className="min-w-0 shrink-0 xl:max-w-sm">{aside}</div> : null}
      </div>
    </section>
  );
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
  icon?: AdminRouteIcon;
  accent?: LearnerSurfaceAccent;
  highlights?: LearnerPageHeroHighlight[];
}) {
  const hasActions = Boolean(action) || Boolean(actions);
  const palette = accentClassMap[accent];
  const safeHighlights = sanitizeLearnerPageHeroHighlights(highlights);
  return (
    <div className={cn('flex flex-col gap-4 rounded-xl border border-zinc-800 bg-zinc-950 px-5 py-4 shadow-sm sm:flex-row sm:items-center sm:justify-between', className)}>
      <div className="flex min-w-0 flex-1 items-start gap-3">
        <div className={cn('shrink-0 rounded-lg p-2', palette.icon)}>
          {renderAdminRouteIcon(Icon, 'h-5 w-5')}
        </div>
        <div className="min-w-0">
          <div className="flex flex-wrap items-center gap-2">
            {eyebrow && <span className={cn('rounded border px-2 py-0.5 text-[10px] font-bold uppercase tracking-widest', palette.chip)}>{eyebrow}</span>}
            {title && <h1 className="text-xl font-bold tracking-tight text-white leading-none">{title}</h1>}
            {meta && <span className="rounded bg-zinc-800/80 px-2 py-0.5 text-[10px] uppercase tracking-widest text-zinc-400 font-bold">{meta}</span>}
          </div>
          {description && <p className="mt-1.5 max-w-4xl text-xs leading-snug text-zinc-400">{description}</p>}
          {safeHighlights.length > 0 && (
            <div className="mt-3 flex flex-wrap gap-2">
              {safeHighlights.map((item) => (
                <span key={`${item.label}-${item.value}`} className="rounded border border-zinc-800 bg-zinc-900 px-2 py-1 text-[10px] font-bold uppercase tracking-wider text-zinc-400">
                  {item.label}: <span className="text-zinc-100">{item.value}</span>
                </span>
              ))}
            </div>
          )}
        </div>
      </div>
      {hasActions && (
        <div className="flex w-full flex-wrap items-center gap-2 sm:w-auto sm:justify-end">
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
  label: string;
  value: string | number;
  hint?: string;
  accent?: LearnerSurfaceAccent;
  icon?: AdminRouteIcon;
  tone?: 'default' | 'success' | 'warning' | 'danger';
  statusLabel?: string;
  primaryAction?: LearnerSurfaceAction;
  secondaryAction?: LearnerSurfaceAction;
  className?: string;
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

  return (
    <div data-slot="summary-card" className={cn('flex items-center gap-3 rounded-xl border px-4 py-3 flex-1 min-w-0 shadow-sm', bg[tone], className)}>
      {renderAdminRouteIcon(icon, cn('h-5 w-5 shrink-0', icoMap[tone]))}
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

export function AdminRouteRedirectNotice({
  title,
  description,
  className,
}: {
  title: string;
  description: string;
  className?: string;
}) {
  return (
    <AdminRouteWorkspace className={cn('p-6', className)}>
      <AdminRoutePanel>
        <div className="flex items-center gap-3">
          <div className="flex h-10 w-10 shrink-0 items-center justify-center rounded-lg bg-violet-500/15 text-violet-500">
            <Sparkles className="h-5 w-5" />
          </div>
          <div className="min-w-0">
            <p className="text-sm font-bold text-zinc-900 dark:text-zinc-100">{title}</p>
            <p className="mt-1 text-xs text-zinc-500 dark:text-zinc-400">{description}</p>
          </div>
        </div>
      </AdminRoutePanel>
    </AdminRouteWorkspace>
  );
}
