'use client';

import { useEffect, useState } from 'react';
import Link from 'next/link';
import { TrendingUp, TrendingDown, ArrowRight, Minus } from 'lucide-react';
import { fetchReadiness } from '@/lib/api';

interface ReadinessDeltaBannerProps {
  /**
   * Mounted after a triggering event (mock submitted, tutor review applied).
   * Fetches readiness twice — once on mount, once after the configured delay —
   * so the second fetch can surface any change introduced by the background
   * compute that runs server-side after the trigger.
   */
  pollAfterMs?: number;
}

export function ReadinessDeltaBanner({ pollAfterMs = 4000 }: ReadinessDeltaBannerProps) {
  const [before, setBefore] = useState<number | null>(null);
  const [after, setAfter] = useState<number | null>(null);
  const [risk, setRisk] = useState<string>('Unknown');
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    let cancelled = false;
    fetchReadiness()
      .then((r) => {
        if (cancelled) return;
        setBefore(r.overallReadiness ?? null);
        setRisk(r.overallRisk);
      })
      .catch(() => { /* non-critical */ });

    const handle = setTimeout(() => {
      fetchReadiness()
        .then((r) => {
          if (cancelled) return;
          setAfter(r.overallReadiness ?? null);
          setRisk(r.overallRisk);
          setLoading(false);
        })
        .catch(() => { if (!cancelled) setLoading(false); });
    }, pollAfterMs);

    return () => { cancelled = true; clearTimeout(handle); };
  }, [pollAfterMs]);

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
