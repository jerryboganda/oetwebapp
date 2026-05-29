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
import { paletteFor } from '@/lib/writing-criterion-colors';
import { fetchWritingWeaknesses, type WritingWeaknessSummary } from '@/lib/api';
import { analytics } from '@/lib/analytics';

// Maps the canonical writing-error tags to their best-fit drill route. Used by
// the spec §14 recommendation card; surface the link only when a tag has a
// natural drill anchor.
const TAG_TO_DRILL: Record<string, string> = {
  missing_key_content: '/writing/drills/relevance',
  irrelevant_content: '/writing/drills/relevance',
  unclear_purpose: '/writing/drills/opening',
  informal_tone: '/writing/drills/tone',
  abbreviation_issue: '/writing/drills/abbreviation',
  poor_paragraphing: '/writing/drills/ordering',
};

function useWeaknessData(): {
  status: 'loading' | 'success' | 'error';
  summary: WritingWeaknessSummary | null;
  error: string | null;
} {
  const [state, setState] = useState<{
    status: 'loading' | 'success' | 'error';
    summary: WritingWeaknessSummary | null;
    error: string | null;
  }>({ status: 'loading', summary: null, error: null });

  useEffect(() => {
    let cancelled = false;
    fetchWritingWeaknesses(14)
      .then((s) => {
        if (cancelled) return;
        setState({ status: 'success', summary: s, error: null });
      })
      .catch((e: Error) => {
        if (cancelled) return;
        setState({ status: 'error', summary: null, error: e.message || 'Failed to load analytics.' });
      });
    return () => { cancelled = true; };
  }, []);

  return state;
}

function pct(n: number): string {
  return `${Math.round(n * 100)}%`;
}

export default function WritingAnalyticsPage() {
  const { status, summary, error } = useWeaknessData();

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

  const topTagDrillLink = useMemo(() => {
    const top = summary?.topTags[0];
    if (!top) return null;
    const route = TAG_TO_DRILL[top.tag];
    return route ? { route, label: top.label, tag: top.tag } : null;
  }, [summary]);

  const latestGrade = summary?.gradeTrend[summary.gradeTrend.length - 1] ?? null;
  const latestPurpose = summary?.purposeTrend[summary.purposeTrend.length - 1] ?? null;

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
                className="inline-flex items-center justify-center rounded-xl bg-primary px-4 py-2 text-sm font-medium text-white shadow-sm transition-[color,background-color,transform] duration-200 hover:bg-primary/90 active:scale-[0.98] motion-reduce:active:scale-100 dark:bg-violet-700 dark:hover:bg-violet-600"
              >
                Practise drills
              </Link>
            </div>
          }
        />

        {status === 'error' ? (
          <Card className="border-danger/40 bg-danger/5 p-6 text-sm text-danger">
            <p className="font-semibold">Failed to load analytics.</p>
            <p className="mt-1 text-xs">{error}</p>
          </Card>
        ) : !summary ? (
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
              className="mt-4 inline-flex items-center justify-center rounded-xl bg-primary px-4 py-2 text-sm font-medium text-white shadow-sm transition-[color,background-color,transform] duration-200 hover:bg-primary/90 active:scale-[0.98] motion-reduce:active:scale-100 dark:bg-violet-700 dark:hover:bg-violet-600"
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
                  description="Daily count of issues. Flat or downward is good."
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

            {/* Spec §14: estimated grade + purpose score snapshots */}
            {(latestGrade || latestPurpose) && (
              <MotionSection delayIndex={2.5}>
                <Card className="border-border bg-surface p-6">
                  <LearnerSurfaceSectionHeader
                    eyebrow="Score snapshot"
                    title="Latest AI estimate"
                    description="Estimate only, not an official OET score. Tutor review may differ."
                    className="mb-5"
                  />
                  <div className="grid grid-cols-1 gap-3 sm:grid-cols-2">
                    {latestGrade && (
                      <div className="rounded-2xl border border-border bg-background-light p-4">
                        <div className="text-xs font-bold uppercase tracking-wider text-muted">Estimated grade</div>
                        <div className="mt-1 flex items-baseline gap-2">
                          <span className="text-3xl font-bold text-navy">{latestGrade.gradeRange}</span>
                          <span className="text-sm text-muted">({latestGrade.scoreRange})</span>
                        </div>
                        <div className="mt-1 text-[11px] text-muted">{latestGrade.date}</div>
                      </div>
                    )}
                    {latestPurpose && (
                      <div className="rounded-2xl border border-border bg-background-light p-4">
                        <div className="text-xs font-bold uppercase tracking-wider text-muted">Purpose score</div>
                        <div className="mt-1 flex items-baseline gap-2">
                          <span className="text-3xl font-bold text-navy">
                            {latestPurpose.score}<span className="text-sm text-muted">/{latestPurpose.maxScore}</span>
                          </span>
                        </div>
                        <div className="mt-1 text-[11px] text-muted">{latestPurpose.date}</div>
                      </div>
                    )}
                  </div>
                </Card>
              </MotionSection>
            )}

            {/* Spec §14: recommendation card linking to the top-tag drill */}
            {topTagDrillLink && (
              <MotionSection delayIndex={3}>
                <Card className="border-primary/30 bg-primary/5 p-6">
                  <LearnerSurfaceSectionHeader
                    eyebrow="Recommendation"
                    title={`Practise: ${topTagDrillLink.label}`}
                    description="Your most common weakness has a matching drill. Targeting it now usually moves the needle faster than general practice."
                    className="mb-4"
                  />
                  <Link
                    href={topTagDrillLink.route}
                    onClick={() => analytics.track('writing_analytics_recommendation_clicked', {
                      tag: topTagDrillLink.tag,
                      route: topTagDrillLink.route,
                    })}
                    className="inline-flex items-center justify-center rounded-xl bg-primary px-4 py-2 text-sm font-medium text-white shadow-sm transition-[color,background-color,transform] duration-200 hover:bg-primary/90 active:scale-[0.98] motion-reduce:active:scale-100 dark:bg-violet-700 dark:hover:bg-violet-600"
                  >
                    Open drill
                  </Link>
                </Card>
              </MotionSection>
            )}
          </>
        )}
      </main>
    </LearnerDashboardShell>
  );
}
