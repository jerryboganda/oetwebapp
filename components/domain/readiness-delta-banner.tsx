'use client';

import Link from 'next/link';
import { TrendingUp, TrendingDown, ArrowRight, Minus } from 'lucide-react';

interface ReadinessDeltaBannerProps {
  before: number | null;
  after?: number | null;
  risk?: string;
  loading?: boolean;
}

export function ReadinessDeltaBanner({
  before,
  after = null,
  risk = 'Unknown',
  loading = false,
}: ReadinessDeltaBannerProps) {
  if (before == null) return null;
  const delta = after != null && before != null ? Math.round(after - before) : 0;
  const current = after ?? before;

  const Icon = delta > 0 ? TrendingUp : delta < 0 ? TrendingDown : Minus;
  const tone = delta > 0
    ? 'border-success/20 bg-success/5 text-success-strong'
    : delta < 0
    ? 'border-danger/20 bg-danger/5 text-danger-strong'
    : 'border-border bg-background-light text-muted';

  return (
    <div className={`flex items-center justify-between gap-3 rounded-2xl border p-4 shadow-sm ${tone}`}>
      <div className="flex min-w-0 items-center gap-3">
        <Icon className="h-5 w-5 shrink-0" aria-hidden="true" />
        <div className="min-w-0">
          <p className="text-sm font-bold tabular-nums text-navy">
            Your readiness {delta === 0 ? 'is now' : delta > 0 ? 'went up to' : 'dipped to'} {Math.round(current)}
            {delta !== 0 && <span className="ms-2 text-xs font-bold">({delta > 0 ? '+' : ''}{delta})</span>}
          </p>
          <p className="text-xs text-muted">
            {loading ? 'Updating after this mock…' : `${risk} risk. See what changed and what to do next.`}
          </p>
        </div>
      </div>
      <Link
        href="/readiness"
        className="hover-primary -me-2 inline-flex min-h-11 shrink-0 items-center gap-1.5 rounded-control px-2 text-xs font-bold text-primary transition-colors"
      >
        See why <ArrowRight className="h-3.5 w-3.5 rtl:rotate-180" aria-hidden="true" />
      </Link>
    </div>
  );
}
