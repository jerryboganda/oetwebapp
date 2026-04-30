'use client';

// Phase 6 of LISTENING-MODULE-PLAN.md — learner-facing Listening analytics
// page. Shows per-part accuracy, top weaknesses, and a structured action
// plan based on submitted attempts. Anonymous users are redirected by the
// dashboard shell + middleware.

import { useEffect, useState } from 'react';
import Link from 'next/link';
import { Activity, AlertTriangle, ArrowRight, Target, TrendingUp } from 'lucide-react';
import { LearnerDashboardShell } from '@/components/layout';
import { LearnerPageHero, LearnerSurfaceCard } from '@/components/domain';
import { Badge } from '@/components/ui/badge';
import { Skeleton } from '@/components/ui/skeleton';
import { InlineAlert } from '@/components/ui/alert';
import { MotionItem } from '@/components/ui/motion-primitives';
import { getListeningStudentAnalytics, type ListeningStudentAnalytics } from '@/lib/listening-authoring-api';

function pct(value: number | null | undefined) {
  if (value == null) return '--';
  return Number.isInteger(value) ? `${value}%` : `${value.toFixed(1)}%`;
}

function AccuracyBar({ value }: { value: number | null | undefined }) {
  const w = value == null ? 0 : Math.max(0, Math.min(100, value));
  return (
    <div className="h-2 w-full overflow-hidden rounded-full bg-slate-200 dark:bg-slate-800">
      <div className="h-full rounded-full bg-indigo-500" style={{ width: `${w}%` }} />
    </div>
  );
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

  return (
    <LearnerDashboardShell>
      <div className="space-y-8">
        <LearnerPageHero
          eyebrow="Listening · Analytics"
          title="Where your Listening score is leaking"
          description="Per-part accuracy, the patterns behind your missed items, and a personalised action plan derived from your last 20 submitted attempts."
          icon={Activity}
        />

        {error ? <InlineAlert variant="warning" title="Analytics unavailable">{error}</InlineAlert> : null}

        {loading ? (
          <div className="grid gap-4 md:grid-cols-3">
            {[0, 1, 2].map((i) => <Skeleton key={i} className="h-32 rounded-xl" />)}
          </div>
        ) : null}

        {!loading && data && data.completedAttempts === 0 ? (
          <LearnerSurfaceCard
            card={{
              kind: 'navigation',
              sourceType: 'frontend_navigation',
              accent: 'indigo',
              eyebrow: 'No data yet',
              title: 'Take a Listening attempt to unlock analytics',
              description: 'Complete one published Listening paper to populate per-part accuracy, weakness detection, and your action plan.',
              primaryAction: { label: 'Open Listening Home', href: '/listening' },
            }}
          />
        ) : null}

        {!loading && data && data.completedAttempts > 0 ? (
          <div className="space-y-8">
            <MotionItem>
              <div className="grid gap-4 md:grid-cols-3">
                <StatCard
                  icon={TrendingUp}
                  label="Best scaled score"
                  value={data.bestScaledScore ?? '--'}
                  hint={data.likelyPassing ? 'Above 350 — likely passing' : 'Below 350 — keep drilling'}
                  tone={data.likelyPassing ? 'success' : 'warning'}
                />
                <StatCard
                  icon={Activity}
                  label="Average scaled score"
                  value={data.averageScaledScore ?? '--'}
                  hint={`Across ${data.completedAttempts} submitted attempt${data.completedAttempts === 1 ? '' : 's'}`}
                />
                <StatCard
                  icon={Target}
                  label="Top weakness"
                  value={data.weaknesses[0]?.label ?? 'None detected'}
                  hint={data.weaknesses[0] ? `${data.weaknesses[0].count} recent occurrences` : 'Keep capturing details'}
                />
              </div>
            </MotionItem>

            <MotionItem>
              <div className="rounded-2xl border border-slate-200 bg-white p-6 shadow-sm dark:border-slate-800 dark:bg-slate-900">
                <h2 className="text-lg font-semibold text-slate-900 dark:text-slate-100">Accuracy by part</h2>
                <div className="mt-4 space-y-3">
                  {data.partBreakdown.map((part) => (
                    <div key={part.partCode} className="grid grid-cols-[6rem_minmax(0,1fr)_5rem] items-center gap-3">
                      <div>
                        <p className="text-sm font-semibold text-slate-900 dark:text-slate-100">Part {part.partCode}</p>
                        <p className="text-xs text-slate-500 dark:text-slate-400">{part.earned}/{part.max} pts</p>
                      </div>
                      <AccuracyBar value={part.accuracyPercent} />
                      <p className="text-right text-sm font-semibold text-slate-900 dark:text-slate-100">{pct(part.accuracyPercent)}</p>
                    </div>
                  ))}
                </div>
              </div>
            </MotionItem>

            {data.weaknesses.length > 0 ? (
              <MotionItem>
                <div className="rounded-2xl border border-slate-200 bg-white p-6 shadow-sm dark:border-slate-800 dark:bg-slate-900">
                  <h2 className="text-lg font-semibold text-slate-900 dark:text-slate-100">Top weaknesses</h2>
                  <ul className="mt-4 space-y-2">
                    {data.weaknesses.map((w) => (
                      <li key={w.errorType} className="flex items-center justify-between rounded-lg border border-slate-200 bg-slate-50 p-3 dark:border-slate-800 dark:bg-slate-800/50">
                        <div className="flex items-center gap-2">
                          <AlertTriangle className="h-4 w-4 text-amber-600" aria-hidden />
                          <span className="text-sm font-medium text-slate-900 dark:text-slate-100">{w.label}</span>
                        </div>
                        <Badge variant="muted">{w.count}</Badge>
                      </li>
                    ))}
                  </ul>
                </div>
              </MotionItem>
            ) : null}

            <MotionItem>
              <div className="rounded-2xl border border-slate-200 bg-white p-6 shadow-sm dark:border-slate-800 dark:bg-slate-900">
                <h2 className="text-lg font-semibold text-slate-900 dark:text-slate-100">Your action plan</h2>
                <ol className="mt-4 space-y-3">
                  {data.actionPlan.map((item, i) => (
                    <li key={i} className="flex gap-3">
                      <span className="flex h-7 w-7 shrink-0 items-center justify-center rounded-full bg-indigo-100 text-sm font-bold text-indigo-700 dark:bg-indigo-900/30 dark:text-indigo-300">
                        {i + 1}
                      </span>
                      <div className="min-w-0">
                        <p className="font-semibold text-slate-900 dark:text-slate-100">{item.headline}</p>
                        <p className="mt-0.5 text-sm text-slate-700 dark:text-slate-300">{item.detail}</p>
                        {item.route ? (
                          <Link href={item.route} className="mt-1 inline-flex items-center gap-1 text-sm font-medium text-indigo-600 hover:text-indigo-500">
                            Go now <ArrowRight className="h-3.5 w-3.5" aria-hidden />
                          </Link>
                        ) : null}
                      </div>
                    </li>
                  ))}
                </ol>
              </div>
            </MotionItem>
          </div>
        ) : null}
      </div>
    </LearnerDashboardShell>
  );
}

function StatCard({
  icon: Icon,
  label,
  value,
  hint,
  tone,
}: {
  icon: React.ComponentType<{ className?: string }>;
  label: string;
  value: number | string;
  hint?: string;
  tone?: 'success' | 'warning';
}) {
  const accent = tone === 'success' ? 'text-emerald-600' : tone === 'warning' ? 'text-amber-600' : 'text-indigo-600';
  return (
    <div className="rounded-2xl border border-slate-200 bg-white p-5 shadow-sm dark:border-slate-800 dark:bg-slate-900">
      <div className="flex items-center gap-2">
        <Icon className={`h-5 w-5 ${accent}`} aria-hidden />
        <p className="text-xs font-semibold uppercase tracking-wider text-slate-500 dark:text-slate-400">{label}</p>
      </div>
      <p className={`mt-3 text-3xl font-bold ${accent}`}>{value}</p>
      {hint ? <p className="mt-1 text-xs text-slate-500 dark:text-slate-400">{hint}</p> : null}
    </div>
  );
}
