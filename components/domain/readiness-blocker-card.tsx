'use client';

import Link from 'next/link';
import { AlertTriangle, ArrowRight } from 'lucide-react';
import type { ReadinessBlocker } from '@/lib/mock-data';

interface ReadinessBlockerCardProps {
  blocker: ReadinessBlocker;
}

const SEVERITY_TOKENS: Record<string, { chip: string; border: string; bar: string }> = {
  high: { chip: 'bg-danger/10 text-danger', border: 'border-danger/20', bar: 'bg-danger' },
  medium: { chip: 'bg-warning/10 text-warning', border: 'border-warning/20', bar: 'bg-warning' },
  low: { chip: 'bg-success/10 text-success', border: 'border-success/20', bar: 'bg-success' },
};

export function ReadinessBlockerCard({ blocker }: ReadinessBlockerCardProps) {
  const severity = blocker.severity ?? 'medium';
  const tokens = SEVERITY_TOKENS[severity] ?? SEVERITY_TOKENS.medium;
  const impact = blocker.impactScore ?? 0;
  return (
    <div className={`bg-surface rounded-[24px] border ${tokens.border} p-5 shadow-sm flex flex-col gap-3`}>
      <div className="flex items-center justify-between gap-2">
        <div className="flex items-center gap-2">
          <span className={`inline-flex h-7 w-7 items-center justify-center rounded-lg ${tokens.chip}`}>
            <AlertTriangle className="w-3.5 h-3.5" />
          </span>
          <h3 className="text-sm font-bold text-navy">{blocker.title}</h3>
        </div>
        <span className={`text-[10px] font-bold uppercase tracking-widest px-2 py-0.5 rounded-full ${tokens.chip}`}>{severity}</span>
      </div>
      <p className="text-xs text-muted leading-relaxed">{blocker.description}</p>
      {impact > 0 && (
        <div className="h-1.5 w-full bg-background-light rounded-full overflow-hidden">
          <div className={`h-full rounded-full ${tokens.bar}`} style={{ width: `${Math.min(100, impact)}%` }} />
        </div>
      )}
      {blocker.actionHref && (
        <Link
          href={blocker.actionHref}
          className="inline-flex items-center gap-1.5 text-xs font-bold text-primary hover:underline mt-1"
        >
          {blocker.actionLabel ?? 'Take action'} <ArrowRight className="w-3.5 h-3.5" />
        </Link>
      )}
    </div>
  );
}
