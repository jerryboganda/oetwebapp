'use client';

import Link from 'next/link';
import { AlertTriangle, ArrowRight } from 'lucide-react';
import { Card } from '@/components/ui/card';
import { ProgressBar } from '@/components/ui/progress';
import type { ReadinessBlocker } from '@/lib/mock-data';
import { cn } from '@/lib/utils';

interface ReadinessBlockerCardProps {
  blocker: ReadinessBlocker;
}

const SEVERITY_TOKENS: Record<string, { chip: string; border: string; bar: 'danger' | 'warning' | 'success' }> = {
  high: { chip: 'bg-danger/10 text-danger-strong', border: 'border-danger/20', bar: 'danger' },
  medium: { chip: 'bg-warning/10 text-warning-strong', border: 'border-warning/20', bar: 'warning' },
  low: { chip: 'bg-success/10 text-success-strong', border: 'border-success/20', bar: 'success' },
};

export function ReadinessBlockerCard({ blocker }: ReadinessBlockerCardProps) {
  const severity = blocker.severity ?? 'medium';
  const tokens = SEVERITY_TOKENS[severity] ?? SEVERITY_TOKENS.medium;
  const impact = blocker.impactScore ?? 0;
  return (
    <Card className={cn('flex h-full flex-col gap-3', tokens.border)}>
      <div className="flex items-start justify-between gap-2">
        <div className="flex min-w-0 items-center gap-2">
          <span className={cn('inline-flex h-7 w-7 shrink-0 items-center justify-center rounded-lg', tokens.chip)}>
            <AlertTriangle className="h-3.5 w-3.5" aria-hidden="true" />
          </span>
          <h3 className="min-w-0 text-sm font-bold text-navy">{blocker.title}</h3>
        </div>
        <span className={cn('tile-label shrink-0 rounded-full px-2 py-0.5', tokens.chip)}>{severity}</span>
      </div>
      <p className="text-xs leading-relaxed text-muted">{blocker.description}</p>
      {impact > 0 && (
        <ProgressBar value={Math.min(100, impact)} color={tokens.bar} ariaLabel={`${blocker.title} impact ${Math.min(100, impact)}%`} />
      )}
      {blocker.actionHref && (
        <Link
          href={blocker.actionHref}
          className="mt-auto inline-flex min-h-11 items-center gap-1.5 self-start rounded-control text-xs font-bold text-primary hover:underline focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary"
        >
          {blocker.actionLabel ?? 'Take action'} <ArrowRight className="h-3.5 w-3.5 rtl:rotate-180" aria-hidden="true" />
        </Link>
      )}
    </Card>
  );
}
