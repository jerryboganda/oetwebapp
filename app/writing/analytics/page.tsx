'use client';

/**
 * ============================================================================
 * /writing/analytics — Weakness Dashboard (Phase C)
 * ============================================================================
 *
 * Aggregates the learner's drill error tags + criterion mistakes and surfaces:
 *   • Top weakness tags (count + share)
 *   • Per-criterion breakdown (colour-coded)
 *   • 14-day trend
 *
 * Uses deterministic seed data while the backend endpoint is being built.
 * The render path is identical once the data source moves to the API — only
 * `useWeaknessData()` needs to change.
 * ============================================================================
 */

import { useEffect, useMemo, useState } from 'react';
import Link from 'next/link';
import { ChevronLeft, TrendingDown, AlertTriangle, Target } from 'lucide-react';
import { LearnerDashboardShell } from '@/components/layout';
import { LearnerPageHero, LearnerSurfaceSectionHeader } from '@/components/domain';
import { Card } from '@/components/ui/card';
import { Skeleton } from '@/components/ui/skeleton';
import { MotionSection } from '@/components/ui/motion-primitives';
import { aggregateWeaknesses } from '@/lib/writing-analytics/aggregate';
import {
  SAMPLE_WEAKNESS_OBSERVATIONS,
  SAMPLE_WEAKNESS_REFERENCE_DATE,
} from '@/lib/writing-analytics/mock';
import { paletteFor } from '@/lib/writing-criterion-colors';
import type { WeaknessDataPoint } from '@/lib/writing-analytics/types';
import { analytics } from '@/lib/analytics';

function useWeaknessData() {
  const [points, setPoints] = useState<WeaknessDataPoint[] | null>(null);

  useEffect(() => {
    // Deterministic seed; swap to API call when /v1/writing/analytics/weaknesses ships.
    const handle = setTimeout(() => setPoints(SAMPLE_WEAKNESS_OBSERVATIONS), 250);
    return () => clearTimeout(handle);
  }, []);

  return points;
}

function pct(n: number): string {
  return `${Math.round(n * 100)}%`;
}

export default function WritingAnalyticsPage() {
  const points = useWeaknessData();

  const summary = useMemo(() => {
    if (!points) return null;
    return aggregateWeaknesses(points, {
      endDate: new Date(SAMPLE_WEAKNESS_REFERENCE_DATE),
      trendDays: 14,
      topN: 5,
    });
  }, [points]);

  useEffect(() => {
    if (!summary) return;
    analytics.track('writing_analytics_viewed', {
      totalObservations: summary.totalObservations,
      topTag: summary.topTags[0]?.tag ?? null,
    });
  }, [summary]);

  const maxTrendCount = summary
    ? Math.max(1, ...summary.trend.map((b) => b.count))
    : 1;

  return (
    <LearnerDashboardShell pageTitle="Writing Analytics">
      <main className="space-y-8">
        <LearnerPageHero
          eyebrow="Writing Analytics"
          icon={TrendingDown}
          accent="amber"
          title="Your weakness map"
          description="See exactly where you keep losing marks across drills, rewrites and expert feedback. Use this to choose what to practise next."
          highlights={summary
            ? [
                { icon: AlertTriangle, label: 'Observations (14d)', value: `${summary.totalObservations}` },
                { icon: Target, label: 'Top weakness', value: summary.topTags[0]?.label ?? '—' },
                { icon: TrendingDown, label: 'Tracked tags', value: `${summary.topTags.length}` },
              ]
            : []}
          aside={
            <div className="flex flex-col gap-3">
              <Link
                href="/writing"
                className="inline-flex items-center justify-center gap-2 rounded-xl border border-border bg-surface px-4 py-2 text-sm font-medium text-navy shadow-sm transition-colors hover:border-primary/30 hover:bg-background-light"
              >
                <ChevronLeft className="h-4 w-4" /> Back to Writing
              </Link>
              <Link
                href="/writing/drills"
                className="inline-flex items-center justify-center rounded-xl bg-primary px-4 py-2 text-sm font-medium text-white shadow-sm transition-colors hover:bg-primary/90"
              >
                Practise drills
              </Link>
            </div>
          }
        />

        {!summary ? (
          <div className="space-y-6">
            <Skeleton className="h-44 rounded-2xl" />
            <Skeleton className="h-64 rounded-2xl" />
          </div>
        ) : summary.totalObservations === 0 ? (
          <Card className="border-border bg-surface p-8 text-center">
            <h2 className="text-lg font-semibold text-navy">No observations yet</h2>
            <p className="mt-2 text-sm text-muted">
              Complete some drills or get expert feedback and your weakness map will appear here.
            </p>
            <Link
              href="/writing/drills"
              className="mt-4 inline-flex items-center justify-center rounded-xl bg-primary px-4 py-2 text-sm font-medium text-white shadow-sm transition-colors hover:bg-primary/90"
            >
              Start drills
            </Link>
          </Card>
        ) : (
          <>
            {/* Top weakness tags */}
            <MotionSection>
              <Card className="border-border bg-surface p-6">
                <LearnerSurfaceSectionHeader
                  eyebrow="Top weaknesses"
                  title="Where you keep losing marks"
                  description="Counted across drill grading, expert feedback, and AI rule-engine findings."
                  className="mb-5"
                />
                <ul className="space-y-3">
                  {summary.topTags.map((tag) => (
                    <li key={tag.tag} className="rounded-xl border border-border bg-background-light p-4">
                      <div className="mb-2 flex items-center justify-between gap-3">
                        <p className="text-sm font-semibold text-navy">{tag.label}</p>
                        <span className="text-xs font-bold tabular-nums text-muted">
                          {tag.count} · {pct(tag.share)}
                        </span>
                      </div>
                      <div
                        className="h-2 overflow-hidden rounded-full bg-surface"
                        role="progressbar"
                        aria-valuenow={Math.round(tag.share * 100)}
                        aria-valuemin={0}
                        aria-valuemax={100}
                        aria-label={`${tag.label} share`}
                      >
                        <div
                          className="h-full bg-warning transition-[width]"
                          style={{ width: `${Math.max(4, Math.round(tag.share * 100))}%` }}
                        />
                      </div>
                    </li>
                  ))}
                </ul>
              </Card>
            </MotionSection>

            {/* Per-criterion breakdown */}
            <MotionSection delayIndex={1}>
              <Card className="border-border bg-surface p-6">
                <LearnerSurfaceSectionHeader
                  eyebrow="By criterion"
                  title="Where the marks are leaking"
                  description="Issues grouped by the OET Writing criterion they affect."
                  className="mb-5"
                />
                {summary.byCriterion.length === 0 ? (
                  <p className="text-sm text-muted">No criterion-tagged observations yet.</p>
                ) : (
                  <div className="grid grid-cols-1 gap-3 sm:grid-cols-2 lg:grid-cols-3">
                    {summary.byCriterion.map((c) => {
                      const palette = paletteFor(c.criterion);
                      return (
                        <div
                          key={c.criterion}
                          className={`rounded-2xl border p-3 ${palette.bgClass} ${palette.borderClass}`}
                        >
                          <div className={`mb-1 text-xs font-bold uppercase tracking-wider ${palette.textClass}`}>
                            {palette.label}
                          </div>
                          <div className="flex items-baseline gap-2">
                            <span className={`text-2xl font-bold tabular-nums ${palette.textClass}`}>{c.count}</span>
                            <span className={`text-xs font-medium ${palette.textClass}`}>{pct(c.share)}</span>
                          </div>
                        </div>
                      );
                    })}
                  </div>
                )}
              </Card>
            </MotionSection>

            {/* 14-day trend */}
            <MotionSection delayIndex={2}>
              <Card className="border-border bg-surface p-6">
                <LearnerSurfaceSectionHeader
                  eyebrow="Trend"
                  title="Last 14 days"
                  description="Daily count of issues — flat or downward is good."
                  className="mb-5"
                />
                <div className="flex h-32 items-end gap-1.5" role="img" aria-label="14 day trend chart">
                  {summary.trend.map((bucket) => {
                    const height = `${Math.max(4, (bucket.count / maxTrendCount) * 100)}%`;
                    return (
                      <div
                        key={bucket.date}
                        className="flex-1"
                        title={`${bucket.date}: ${bucket.count} issues`}
                        aria-label={`${bucket.date}: ${bucket.count} issues`}
                      >
                        <div
                          className="w-full rounded-t-md bg-warning/70 transition-[height]"
                          style={{ height }}
                        />
                      </div>
                    );
                  })}
                </div>
                <div className="mt-2 flex justify-between text-[10px] text-muted">
                  <span>{summary.trend[0]?.date.slice(5)}</span>
                  <span>{summary.trend[summary.trend.length - 1]?.date.slice(5)}</span>
                </div>
              </Card>
            </MotionSection>
          </>
        )}
      </main>
    </LearnerDashboardShell>
  );
}
