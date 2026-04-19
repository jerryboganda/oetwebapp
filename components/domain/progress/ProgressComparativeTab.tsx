'use client';

import { Users, Trophy, Award } from 'lucide-react';
import type { ProgressComparativeBlock } from '@/lib/api';

const TIER_STYLES: Record<string, { label: string; classes: string }> = {
  top10: { label: 'Top 10%', classes: 'bg-emerald-100 text-emerald-800 border-emerald-200' },
  top25: { label: 'Top 25%', classes: 'bg-blue-100 text-blue-800 border-blue-200' },
  aboveMedian: { label: 'Above Median', classes: 'bg-amber-100 text-amber-800 border-amber-200' },
  belowMedian: { label: 'Below Median', classes: 'bg-rose-100 text-rose-800 border-rose-200' },
};

/**
 * Comparative cohort tab. Fixes the two bugs in the old /progress/comparative
 * route: (a) percentile no longer includes the learner in their own cohort,
 * (b) the block is gated by MinCohortSize so early adopters see an honest
 * "cohort insights unlock at N peers" state rather than noisy stats.
 */
export function ProgressComparativeTab({ comparative }: { comparative: ProgressComparativeBlock | null }) {
  if (!comparative) {
    return (
      <div className="flex items-center justify-center rounded-3xl border border-dashed border-gray-200 bg-background-light/60 p-8 text-center">
        <p className="text-sm text-muted">Comparative analytics are not available right now.</p>
      </div>
    );
  }

  if (!comparative.hasSufficientCohort) {
    const missing = Math.max(0, comparative.minCohortSize - comparative.cohortSize);
    return (
      <div className="rounded-3xl border border-dashed border-gray-200 bg-background-light/60 p-6 sm:p-8">
        <div className="flex items-start gap-3">
          <div className="rounded-xl bg-primary/10 p-2 text-primary">
            <Users className="w-5 h-5" />
          </div>
          <div>
            <p className="text-sm font-black uppercase tracking-widest text-muted">Cohort insights</p>
            <h3 className="text-lg font-black text-navy mt-1">Waiting for more peers</h3>
            <p className="mt-1 text-sm text-muted max-w-xl">
              {comparative.cohortScopeDescription}. We unlock comparative charts when at least{' '}
              <strong>{comparative.minCohortSize}</strong> peers have data in the last 90 days.
              {missing > 0 && (
                <>
                  {' '}
                  Currently at <strong>{comparative.cohortSize}</strong> — {missing} more needed.
                </>
              )}
            </p>
          </div>
        </div>
      </div>
    );
  }

  return (
    <div className="space-y-4">
      <div className="flex items-start gap-2 rounded-xl bg-blue-50 border border-blue-100 p-3 text-xs text-blue-800">
        <Users className="w-4 h-4 mt-0.5" />
        <p>
          <strong>{comparative.cohortSize}</strong> peers in scope · {comparative.cohortScopeDescription}. You are not included in your own percentile.
        </p>
      </div>

      <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
        {comparative.subtests.map((s) => {
          const tier = TIER_STYLES[s.tier] ?? TIER_STYLES.belowMedian;
          const delta = s.yourScaled - s.cohortAverage;
          return (
            <div key={s.subtestCode} className="rounded-2xl border border-gray-200 bg-surface p-4 sm:p-5">
              <div className="flex items-center justify-between">
                <div className="flex items-center gap-2">
                  <Trophy className="w-4 h-4 text-navy/50" />
                  <h3 className="text-sm font-black uppercase tracking-widest text-navy">{s.subtestCode}</h3>
                </div>
                <span className={`inline-flex items-center gap-1 rounded-md border px-2 py-1 text-xs font-bold ${tier.classes}`}>
                  <Award className="w-3 h-3" />
                  {tier.label}
                </span>
              </div>

              <div className="mt-3 grid grid-cols-3 gap-2 text-center">
                <div>
                  <p className="text-2xl font-black text-primary">{s.yourScaled}</p>
                  <p className="text-[11px] uppercase tracking-widest text-muted">You</p>
                </div>
                <div>
                  <p className="text-2xl font-black text-navy">{s.cohortAverage}</p>
                  <p className="text-[11px] uppercase tracking-widest text-muted">Cohort avg</p>
                </div>
                <div>
                  <p className="text-2xl font-black text-navy">{s.percentile.toFixed(1)}%</p>
                  <p className="text-[11px] uppercase tracking-widest text-muted">Percentile</p>
                </div>
              </div>

              <div className="mt-3 h-2 rounded-full bg-gray-100 overflow-hidden">
                <div
                  className="h-full rounded-full bg-gradient-to-r from-rose-400 via-amber-400 to-emerald-400"
                  style={{ width: `${Math.min(100, Math.max(0, s.percentile))}%` }}
                  role="progressbar"
                  aria-valuenow={Math.round(s.percentile)}
                  aria-valuemin={0}
                  aria-valuemax={100}
                  aria-label={`${s.subtestCode} percentile`}
                />
              </div>

              <p className="mt-2 text-xs text-muted">
                Median {s.cohortMedian} · {delta > 0 ? `+${delta}` : delta} vs average
              </p>
            </div>
          );
        })}
      </div>
    </div>
  );
}
