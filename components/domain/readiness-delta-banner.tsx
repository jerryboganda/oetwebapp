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
    ? 'border-success/20 bg-success/5 text-success'
    : delta < 0
    ? 'border-danger/20 bg-danger/5 text-danger'
    : 'border-border bg-background-light text-muted';

  return (
    <div className={`rounded-2xl border ${tone} p-4 flex items-center justify-between gap-3`}>
      <div className="flex items-center gap-3">
        <Icon className="w-5 h-5 shrink-0" />
        <div>
          <p className="text-sm font-bold text-navy">
            Your readiness {delta === 0 ? 'is now' : delta > 0 ? 'went up to' : 'dipped to'} {Math.round(current)}
            {delta !== 0 && <span className="ml-2 text-xs font-bold">({delta > 0 ? '+' : ''}{delta})</span>}
          </p>
          <p className="text-xs text-muted">
            {loading ? 'Updating after this mock…' : `${risk} risk. See what changed and what to do next.`}
          </p>
        </div>
      </div>
      <Link
        href="/readiness"
        className="inline-flex items-center gap-1.5 text-xs font-bold text-primary hover:underline shrink-0"
      >
        See why <ArrowRight className="w-3.5 h-3.5" />
      </Link>
    </div>
  );
}
