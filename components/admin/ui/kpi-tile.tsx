'use client';

import * as React from 'react';
import Link from 'next/link';
import { ArrowDown, ArrowUp, Minus } from 'lucide-react';

import { cn } from '@/lib/utils';
import { Card, CardContent } from './card';

/**
 * Dashboard KPI tile — replaces the legacy PulseTile pattern with the
 * Axelit/Aria-Labs metric chrome (uppercase eyebrow, tabular value, trend row).
 *
 * Tokens: `app/admin/_design/admin-tokens.css`.
 * Spec:   docs/admin-redesign/axelit-study/04-COMPONENT_ARCHITECTURE.md
 */

export type KpiTone = 'default' | 'primary' | 'success' | 'warning' | 'danger' | 'info';
export type KpiSize = 'sm' | 'md';

export type KpiTrend = {
  value: string;
  direction: 'up' | 'down' | 'flat';
  /** Marks the trend as "good" (green) or "bad" (red). Defaults map by direction. */
  positive?: boolean;
};

export type KpiTileProps = {
  label: string;
  value: string | number;
  trend?: KpiTrend;
  icon?: React.ReactNode;
  tone?: KpiTone;
  sparkline?: React.ReactNode;
  /** When provided, the whole tile becomes a clickable Next link. */
  href?: string;
  loading?: boolean;
  size?: KpiSize;
  className?: string;
  /**
   * Optional short subtitle / footnote rendered below the value (e.g.
   * "vs last week", "across all cohorts"). Backward-compat for the
   * pre-migration KpiTile that exposed a `hint` slot.
   */
  hint?: React.ReactNode;
};

const toneIconStyles: Record<KpiTone, string> = {
  default: 'bg-admin-bg-subtle text-admin-fg-muted',
  primary: 'bg-[var(--admin-primary-tint)] text-[var(--admin-primary)]',
  success: 'bg-[var(--admin-success-tint)] text-[var(--admin-success)]',
  warning: 'bg-[var(--admin-warning-tint)] text-[var(--admin-warning)]',
  danger: 'bg-[var(--admin-danger-tint)] text-[var(--admin-danger)]',
  info: 'bg-[var(--admin-info-tint)] text-[var(--admin-info)]',
};

const valueSizeStyles: Record<KpiSize, string> = {
  sm: 'text-xl sm:text-2xl',
  md: 'text-2xl sm:text-3xl',
};

function TrendBadge({ trend }: { trend: KpiTrend }) {
  const Icon = trend.direction === 'up' ? ArrowUp : trend.direction === 'down' ? ArrowDown : Minus;

  // Resolve color: explicit `positive` wins; otherwise infer from direction.
  let color = 'text-admin-fg-muted';
  if (trend.direction === 'flat') {
    color = 'text-admin-fg-muted';
  } else if (typeof trend.positive === 'boolean') {
    color = trend.positive ? 'text-[var(--admin-success)]' : 'text-[var(--admin-danger)]';
  } else {
    color =
      trend.direction === 'up' ? 'text-[var(--admin-success)]' : 'text-[var(--admin-danger)]';
  }

  return (
    <div className="mt-2 flex items-center gap-1.5 text-xs">
      <span className={cn('inline-flex items-center gap-0.5 font-medium tabular-nums', color)}>
        <Icon className="h-3.5 w-3.5" aria-hidden="true" />
        {trend.value}
      </span>
      <span className="text-admin-fg-muted">vs last period</span>
    </div>
  );
}

function KpiSkeleton({ size }: { size: KpiSize }) {
  return (
    <div className="space-y-2" aria-hidden="true">
      <div className="flex items-center gap-2">
        <div className="h-8 w-8 rounded-full bg-admin-bg-subtle motion-safe:animate-pulse" />
        <div className="h-3 w-24 rounded bg-admin-bg-subtle motion-safe:animate-pulse" />
      </div>
      <div
        className={cn(
          'mt-3 rounded bg-admin-bg-subtle motion-safe:animate-pulse',
          size === 'sm' ? 'h-7 w-20' : 'h-9 w-28',
        )}
      />
      <div className="mt-2 h-3 w-32 rounded bg-admin-bg-subtle motion-safe:animate-pulse" />
    </div>
  );
}

export const KpiTile = React.forwardRef<HTMLDivElement, KpiTileProps>(
  (
    { label, value, trend, icon, tone = 'default', sparkline, href, loading, size = 'md', className, hint },
    ref,
  ) => {
    const body = (
      <CardContent className="p-4 sm:p-5 pt-4 sm:pt-5">
        {loading ? (
          <KpiSkeleton size={size} />
        ) : (
          <>
            <div className="flex items-center gap-2.5">
              {icon ? (
                <span
                  className={cn(
                    'inline-flex h-8 w-8 shrink-0 items-center justify-center rounded-full',
                    toneIconStyles[tone],
                  )}
                  aria-hidden="true"
                >
                  {icon}
                </span>
              ) : null}
              <p className="min-w-0 truncate text-xs font-semibold uppercase tracking-wider text-admin-fg-muted">
                {label}
              </p>
            </div>

            <p
              className={cn(
                'mt-3 font-bold tabular-nums tracking-tight text-admin-fg-strong leading-none',
                valueSizeStyles[size],
              )}
            >
              {value}
            </p>

            {trend ? <TrendBadge trend={trend} /> : null}

            {hint ? (
              <p className="mt-2 text-xs text-admin-fg-muted leading-snug">
                {hint}
              </p>
            ) : null}

            {sparkline ? (
              <div className="mt-2 h-8" aria-hidden="true">
                {sparkline}
              </div>
            ) : null}
          </>
        )}
      </CardContent>
    );

    // Clickable variant — wrap the whole tile in a next/link. Use Card.asChild
    // so the surface styles and ring apply to the anchor.
    if (href && !loading) {
      return (
        <Card
          ref={ref}
          asChild
          interactive
          className={cn('block focus-visible:outline-none', className)}
        >
          <Link href={href} aria-label={typeof value === 'string' ? `${label}: ${value}` : label}>
            {body}
          </Link>
        </Card>
      );
    }

    return (
      <Card ref={ref} className={className} aria-busy={loading || undefined}>
        {body}
      </Card>
    );
  },
);
KpiTile.displayName = 'KpiTile';
