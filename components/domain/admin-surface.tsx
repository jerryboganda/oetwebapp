'use client';

import type { ReactNode } from 'react';
import { Card, CardContent } from '@/components/ui/card';
import { cn } from '@/lib/utils';

export function AdminPageHeader({
  title,
  description,
  meta,
  actions,
}: {
  title: string;
  description: string;
  meta?: string;
  actions?: ReactNode;
}) {
  return (
    <div className="flex flex-col gap-4 lg:flex-row lg:items-start lg:justify-between">
      <div className="space-y-1">
        <h1 className="text-2xl font-semibold text-slate-900">{title}</h1>
        <p className="max-w-3xl text-sm text-slate-500">{description}</p>
        {meta ? <p className="text-xs font-medium uppercase tracking-[0.12em] text-slate-400">{meta}</p> : null}
      </div>
      {actions ? <div className="flex flex-wrap items-center gap-3">{actions}</div> : null}
    </div>
  );
}

export function AdminMetricCard({
  label,
  value,
  hint,
  icon,
  tone = 'default',
}: {
  label: string;
  value: string | number;
  hint?: string;
  icon?: ReactNode;
  tone?: 'default' | 'success' | 'warning' | 'danger';
}) {
  const toneClasses = {
    default: 'bg-white text-slate-900',
    success: 'bg-emerald-50 text-emerald-900 border-emerald-100',
    warning: 'bg-amber-50 text-amber-950 border-amber-100',
    danger: 'bg-rose-50 text-rose-950 border-rose-100',
  } as const;

  return (
    <Card className={cn('border shadow-sm', toneClasses[tone])}>
      <CardContent className="flex items-start justify-between gap-4 p-5">
        <div className="space-y-1">
          <p className="text-sm font-medium text-slate-500">{label}</p>
          <p className="text-2xl font-semibold tracking-tight">{value}</p>
          {hint ? <p className="text-xs text-slate-500">{hint}</p> : null}
        </div>
        {icon ? <div className="rounded-xl bg-slate-900/5 p-2.5 text-slate-500">{icon}</div> : null}
      </CardContent>
    </Card>
  );
}

export function AdminSectionPanel({
  title,
  description,
  actions,
  children,
  className,
}: {
  title: string;
  description?: string;
  actions?: ReactNode;
  children: ReactNode;
  className?: string;
}) {
  return (
    <Card className={cn('overflow-hidden border border-slate-200 shadow-sm', className)}>
      <CardContent className="space-y-4 p-5">
        <div className="flex flex-col gap-3 sm:flex-row sm:items-start sm:justify-between">
          <div className="space-y-1">
            <h2 className="text-lg font-semibold text-slate-900">{title}</h2>
            {description ? <p className="text-sm text-slate-500">{description}</p> : null}
          </div>
          {actions ? <div className="flex flex-wrap items-center gap-2">{actions}</div> : null}
        </div>
        {children}
      </CardContent>
    </Card>
  );
}

export function AdminFreshnessBadge({ value }: { value: string | null | undefined }) {
  if (!value) {
    return <span className="text-xs text-slate-400">Freshness unavailable</span>;
  }

  return <span className="text-xs text-slate-400">Updated {new Date(value).toLocaleString()}</span>;
}
