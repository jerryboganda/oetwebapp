'use client';

import { Badge, type BadgeProps } from '@/components/ui/badge';
import { cn } from '@/lib/utils';
import type { RateLimit } from '@/lib/owner-agent/types';

const STATUS_VARIANT: Record<RateLimit['status'], NonNullable<BadgeProps['variant']>> = {
  ok: 'success',
  warning: 'warning',
  limited: 'danger',
  unknown: 'muted',
};

function formatReset(resetsAt: string | undefined): string | null {
  if (!resetsAt) return null;
  const parsed = Date.parse(resetsAt);
  if (!Number.isFinite(parsed)) return null;
  return new Date(parsed).toLocaleString();
}

export interface RateLimitBadgeProps {
  /** null/undefined = the engine has not reported limits yet. */
  limits: RateLimit[] | null | undefined;
  label?: string;
  className?: string;
}

/** Subscription rate-limit windows; shows "unknown" until the first report. */
export function RateLimitBadge({ limits, label, className }: RateLimitBadgeProps) {
  if (!limits || limits.length === 0) {
    return (
      <Badge variant="muted" className={className} data-testid="rate-limit-unknown">
        {label ? `${label}: ` : ''}limits unknown
      </Badge>
    );
  }
  return (
    <span className={cn('inline-flex flex-wrap items-center gap-1', className)}>
      {limits.map((limit, index) => {
        const reset = formatReset(limit.resetsAt);
        const pct = typeof limit.usedPercent === 'number' && Number.isFinite(limit.usedPercent)
          ? ` ${Math.round(limit.usedPercent)}%`
          : '';
        return (
          <Badge
            key={`${limit.label}-${index}`}
            variant={STATUS_VARIANT[limit.status] ?? 'muted'}
            title={reset ? `Resets ${reset}` : undefined}
          >
            {label ? `${label} · ` : ''}
            {limit.label}
            {pct}
          </Badge>
        );
      })}
    </span>
  );
}
