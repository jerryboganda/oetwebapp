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
import { LearnerPageHero, LearnerSurfaceSectionHeader } from '@/components/domain';
import { LearnerSkeleton } from '@/components/domain/learner-skeletons';
import { Button } from '@/components/ui/button';
import { Card } from '@/components/ui/card';
import { CountUp } from '@/components/ui/count-up';
import { EmptyState, ErrorState } from '@/components/ui/empty-error';
import { ProgressBar } from '@/components/ui/progress';
import { MotionItem, MotionSection } from '@/components/ui/motion-primitives';
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
    <>
      <LearnerPageHero
        eyebrow="Writing Analytics"
        icon={TrendingDown}
        accent="writing"
        title="Your weakness map"
        description="See exactly where you keep losing marks across drills, rewrites and expert feedback. Use this to choose what to practise next."
        highlights={summary
          ? [
              { icon: AlertTriangle, label: 'Observations (14d)', value: `${summary.totalObservations}` },
              { icon: Target, label: 'Top weakness', value: summary.topTags[0]?.label ?? '-' },
              { icon: TrendingDown, label: 'Tracked tags', value: `${summary.topTags.length}` },
            ]
          : []}
        aside={
          <div className="flex flex-col gap-3">
            <Button asChild variant="outline" className="bg-surface">
              <Link href="/writing">
                <ChevronLeft className="h-4 w-4 rtl:rotate-180" aria-hidden /> Back to Writing
              </Link>
            </Button>
            <Button asChild>
              <Link href="/writing/drills">Practise drills</Link>
            </Button>
          </div>
        }
      />

      {status === 'error' ? (
        <ErrorState title="Failed to load analytics." message={error ?? undefined} />
      ) : !summary ? (
        <LearnerSkeleton variant="list" />
      ) : summary.totalObservations === 0 ? (
        <EmptyState
          icon={<Target className="h-8 w-8" />}
          title="No observations yet"
          description="Complete some drills or get expert feedback and your weakness map will appear here."
          action={{ label: 'Start drills', href: '/writing/drills' }}
        />
      ) : (
        <>
          {/* Top weakness tags */}
          <MotionSection delayIndex={0}>
            <Card padding="lg">
              <LearnerSurfaceSectionHeader
                eyebrow="Top weaknesses"
                title="Where you keep losing marks"
                description="Counted across drill grading, expert feedback, and AI rule-engine findings."
                className="mb-5"
              />
              <ul className="divide-y divide-border">
                {summary.topTags.map((tag) => (
                  <li key={tag.tag} className="py-3 first:pt-0 last:pb-0">
                    <div className="mb-2 flex items-center justify-between gap-3">
                      <p className="min-w-0 text-sm font-semibold text-navy">{tag.label}</p>
                      <span className="shrink-0 text-xs font-bold tabular-nums text-muted">
                        {tag.count} · {pct(tag.share)}
                      </span>
                    </div>
                    <ProgressBar value={Math.round(tag.share * 100)} color="warning" ariaLabel={`${tag.label} share`} />
                  </li>
                ))}
              </ul>
            </Card>
          </MotionSection>

          {/* Per-criterion breakdown */}
          <MotionSection delayIndex={1}>
            <Card padding="lg">
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
                  {summary.byCriterion.map((c, index) => {
                    const palette = paletteFor(c.criterion);
                    return (
                      <MotionItem
                        key={c.criterion}
                        delayIndex={Math.min(index, 5)}
                        className={`min-w-0 rounded-xl border p-3 ${palette.bgClass} ${palette.borderClass}`}
                      >
                        <p className={`tile-label mb-1 ${palette.textClass}`}>
                          {palette.label}
                        </p>
                        <div className="flex items-baseline gap-2">
                          <CountUp value={c.count} className={`text-2xl font-bold ${palette.textClass}`} />
                          <span className={`text-xs font-medium tabular-nums ${palette.textClass}`}>{pct(c.share)}</span>
                        </div>
                      </MotionItem>
                    );
                  })}
                </div>
              )}
            </Card>
          </MotionSection>

          {/* 14-day trend */}
          <MotionSection delayIndex={2}>
            <Card padding="lg">
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
                      <div className="w-full rounded-t-md bg-warning/70" style={{ height }} />
                    </div>
                  );
                })}
              </div>
              <div className="mt-2 flex justify-between text-3xs tabular-nums text-muted">
                <span>{summary.trend[0]?.date.slice(5)}</span>
                <span>{summary.trend[summary.trend.length - 1]?.date.slice(5)}</span>
              </div>
            </Card>
          </MotionSection>

          {/* Spec §14: estimated grade + purpose score snapshots */}
          {(latestGrade || latestPurpose) && (
            <MotionSection delayIndex={3}>
              <Card padding="lg">
                <LearnerSurfaceSectionHeader
                  eyebrow="Score snapshot"
                  title="Latest AI estimate"
                  description="Estimate only, not an official OET score. Tutor review may differ."
                  className="mb-5"
                />
                <div className="grid grid-cols-1 gap-3 sm:grid-cols-2">
                  {latestGrade && (
                    <div className="min-w-0 rounded-xl border border-border bg-background-light p-4">
                      <p className="tile-label text-muted">Estimated grade</p>
                      <div className="mt-1 flex flex-wrap items-baseline gap-2">
                        <span className="text-3xl font-bold tabular-nums text-navy">{latestGrade.gradeRange}</span>
                        <span className="text-sm tabular-nums text-muted">({latestGrade.scoreRange})</span>
                      </div>
                      <p className="mt-1 text-2xs tabular-nums text-muted">{latestGrade.date}</p>
                    </div>
                  )}
                  {latestPurpose && (
                    <div className="min-w-0 rounded-xl border border-border bg-background-light p-4">
                      <p className="tile-label text-muted">Purpose score</p>
                      <p className="mt-1 text-3xl font-bold text-navy">
                        <CountUp value={latestPurpose.score} /><span className="text-sm text-muted">/{latestPurpose.maxScore}</span>
                      </p>
                      <p className="mt-1 text-2xs tabular-nums text-muted">{latestPurpose.date}</p>
                    </div>
                  )}
                </div>
              </Card>
            </MotionSection>
          )}

          {/* Spec §14: recommendation card linking to the top-tag drill */}
          {topTagDrillLink && (
            <MotionSection delayIndex={4}>
              <Card padding="lg" className="border-primary/30 bg-primary/5">
                <LearnerSurfaceSectionHeader
                  eyebrow="Recommendation"
                  title={`Practise: ${topTagDrillLink.label}`}
                  description="Your most common weakness has a matching drill. Targeting it now usually moves the needle faster than general practice."
                  className="mb-4"
                />
                <Button asChild>
                  <Link
                    href={topTagDrillLink.route}
                    onClick={() => analytics.track('writing_analytics_recommendation_clicked', {
                      tag: topTagDrillLink.tag,
                      route: topTagDrillLink.route,
                    })}
                  >
                    Open drill
                  </Link>
                </Button>
              </Card>
            </MotionSection>
          )}
        </>
      )}
    </>
  );
}
