'use client';

// Phase 6 of LISTENING-MODULE-PLAN.md — learner-facing Listening analytics
// page. Shows per-part accuracy, top weaknesses, and a structured action
// plan based on submitted attempts. Anonymous users are redirected by the
// dashboard shell + middleware.

import { useEffect, useState } from 'react';
import Link from 'next/link';
import { Activity, AlertTriangle, ArrowRight, BarChart3, Target, TrendingUp } from 'lucide-react';
import { LearnerPageHero, LearnerSurfaceSectionHeader } from '@/components/domain';
import { Badge } from '@/components/ui/badge';
import { Skeleton } from '@/components/ui/skeleton';
import { MotionItem, MotionSection } from '@/components/ui/motion-primitives';
import { getListeningStudentAnalytics, type ListeningStudentAnalytics } from '@/lib/listening-authoring-api';
import { StatCard } from '@/components/ui/stat-card';
import { Card } from '@/components/ui/card';
import { CountUp } from '@/components/ui/count-up';
import { EmptyState, ErrorState } from '@/components/ui/empty-error';
import { ProgressBar } from '@/components/ui/progress';

function pct(value: number | null | undefined) {
  if (value == null) return '-';
  return Number.isInteger(value) ? `${value}%` : `${value.toFixed(1)}%`;
}

/** DESIGN.md §7: stat strips size to their container, so 3 or 4 tiles always fill the row. */
const STAT_GRID = 'grid grid-cols-[repeat(auto-fit,minmax(min(100%,8.5rem),1fr))] gap-2 sm:gap-3';

/** CountUp shows integers; an average such as 345.5 keeps its exact value. */
function scoreValue(value: number | null | undefined) {
  if (value == null) return '-';
  return Number.isInteger(value) ? <CountUp value={value} /> : value;
}

export default function ListeningAnalyticsPage() {
  const [data, setData] = useState<ListeningStudentAnalytics | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    let cancelled = false;
    (async () => {
      try {
        const result = await getListeningStudentAnalytics();
        if (!cancelled) setData(result);
      } catch (e) {
        console.error(e);
        if (!cancelled) setError('Could not load Listening analytics. Try again shortly.');
      } finally {
        if (!cancelled) setLoading(false);
      }
    })();
    return () => { cancelled = true; };
  }, []);

  const weakestPart = (data?.partBreakdown ?? [])
    .filter((part) => part.accuracyPercent != null && part.max > 0)
    .reduce<ListeningStudentAnalytics['partBreakdown'][number] | null>(
      (lowest, part) => (lowest == null || (part.accuracyPercent ?? 0) < (lowest.accuracyPercent ?? 0) ? part : lowest),
      null,
    );

  return (
    <>
      <LearnerPageHero
        eyebrow="Listening · Analytics"
        title="Where your Listening score is leaking"
        description="Per-part accuracy, the patterns behind your missed items, and a personalised action plan derived from your last 20 submitted attempts."
        icon={Activity}
      />

      {error ? <ErrorState title="Analytics unavailable" message={error} /> : null}

      {loading ? (
        <div className={STAT_GRID}>
          {[0, 1, 2].map((i) => <Skeleton key={i} className="h-32 rounded-xl" />)}
        </div>
      ) : null}

      {!loading && data && data.completedAttempts === 0 ? (
        <EmptyState
          icon={<Activity className="h-8 w-8" aria-hidden />}
          title="Take a Listening attempt to unlock analytics"
          description="Complete one published Listening paper to populate per-part accuracy, weakness detection, and your action plan."
          action={{ label: 'Open Listening Home', href: '/listening' }}
        />
      ) : null}

      {!loading && data && data.completedAttempts > 0 ? (
        <>
          <MotionSection delayIndex={0}>
            <div className={STAT_GRID}>
              <StatCard
                icon={<TrendingUp />}
                label="Best score"
                value={scoreValue(data.bestScaledScore)}
                hint={data.likelyPassing === true
                  ? 'Owner table: passing'
                  : data.likelyPassing === false
                    ? 'Owner table: not passing'
                    : 'Owner table: unavailable'}
                tone={data.likelyPassing === true ? 'success' : data.likelyPassing === false ? 'warning' : 'info'}
              />
              <StatCard
                icon={<Activity />}
                label="Avg score"
                value={scoreValue(data.averageScaledScore)}
                hint={`Across ${data.completedAttempts} attempts`}
                tone="info"
              />
              {/* Danger only when there is a weakness to show; "None" is not a warning. */}
              <StatCard
                icon={<Target />}
                label="Top weakness"
                value={data.weaknesses[0]?.label ?? 'None'}
                hint={data.weaknesses[0] ? `${data.weaknesses[0].count} recent` : 'More data needed'}
                tone={data.weaknesses[0] ? 'danger' : 'default'}
              />
              {/* Real data only: weakest part from the API's per-part accuracy
                  (replaces a hardcoded "Top 10% pacing" card — no timing data exists). */}
              {weakestPart ? (
                <StatCard
                  icon={<Target />}
                  label="Weakest part"
                  value={`Part ${weakestPart.partCode}`}
                  hint={`${pct(weakestPart.accuracyPercent)} accuracy`}
                  tone="warning"
                />
              ) : null}
            </div>
          </MotionSection>

          <MotionSection delayIndex={1}>
            <Card padding="lg">
              <LearnerSurfaceSectionHeader title="Accuracy by part" className="mb-4" />
              {data.partBreakdown.length > 0 ? (
                <div className="space-y-3">
                  {data.partBreakdown.map((part) => (
                    <div key={part.partCode} className="grid grid-cols-[6rem_minmax(0,1fr)_4.5rem] items-center gap-3">
                      <div>
                        <p className="text-sm font-semibold text-navy">Part {part.partCode}</p>
                        <p className="text-xs tabular-nums text-muted">{part.earned}/{part.max} pts</p>
                      </div>
                      <ProgressBar value={part.accuracyPercent ?? 0} ariaLabel={`Part ${part.partCode} accuracy`} />
                      <p className="text-end text-sm font-semibold tabular-nums text-navy">{pct(part.accuracyPercent)}</p>
                    </div>
                  ))}
                </div>
              ) : (
                <EmptyState className="py-6" icon={<BarChart3 className="h-7 w-7" aria-hidden />} title="No part telemetry recorded." />
              )}
            </Card>
          </MotionSection>

          {data.weaknesses.length > 0 ? (
            <MotionSection delayIndex={2}>
              <Card padding="lg">
                <LearnerSurfaceSectionHeader title="Top weaknesses" className="mb-3" />
                <ul className="divide-y divide-border">
                  {data.weaknesses.map((w, index) => (
                    <li key={w.errorType}>
                      <MotionItem delayIndex={Math.min(index, 5)} className="flex items-center justify-between gap-3 py-3">
                        <div className="flex min-w-0 items-center gap-2">
                          <AlertTriangle className="h-4 w-4 shrink-0 text-warning-strong" aria-hidden />
                          <span className="text-sm font-medium text-navy">{w.label}</span>
                        </div>
                        <Badge variant="muted" className="tabular-nums">{w.count}</Badge>
                      </MotionItem>
                    </li>
                  ))}
                </ul>
              </Card>
            </MotionSection>
          ) : null}

          <MotionSection delayIndex={3}>
            <Card padding="lg">
              <LearnerSurfaceSectionHeader title="Your action plan" className="mb-4" />
              <ol className="space-y-4">
                {data.actionPlan.map((item, i) => (
                  <li key={i} className="flex gap-3">
                    <span className="flex h-7 w-7 shrink-0 items-center justify-center rounded-full bg-primary/10 text-sm font-bold tabular-nums text-primary">
                      {i + 1}
                    </span>
                    <div className="min-w-0">
                      <p className="font-semibold text-navy">{item.headline}</p>
                      <p className="mt-0.5 text-sm text-muted">{item.detail}</p>
                      {item.route ? (
                        <Link
                          href={item.route}
                          className="inline-flex min-h-11 items-center gap-1 rounded-control text-sm font-medium text-primary transition-colors hover:text-primary-dark focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary"
                        >
                          Go now <ArrowRight className="h-3.5 w-3.5 rtl:rotate-180" aria-hidden />
                        </Link>
                      ) : null}
                    </div>
                  </li>
                ))}
              </ol>
            </Card>
          </MotionSection>
        </>
      ) : null}
    </>
  );
}

