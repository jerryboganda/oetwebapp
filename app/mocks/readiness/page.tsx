'use client';

import { useEffect, useMemo, useState } from 'react';
import Link from 'next/link';
import { useRouter } from 'next/navigation';
import { ArrowLeft, ArrowRight, CheckCircle2, Clock, Loader2, ShieldCheck, Target, TrendingDown, TrendingUp } from 'lucide-react';

import { LearnerDashboardShell } from '@/components/layout';
import { LearnerPageHero, LearnerSurfaceSectionHeader } from '@/components/domain';
import { Button } from '@/components/ui/button';
import { Badge } from '@/components/ui/badge';
import { Skeleton } from '@/components/ui/skeleton';
import { InlineAlert } from '@/components/ui/alert';
import {
  completeRemediationTask,
  fetchMockReadinessTrend,
  fetchRemediationPlan,
  type MockReadinessTrend,
  type RemediationTask,
} from '@/lib/api';

const SUBTEST_LABELS: Record<string, string> = {
  listening: 'Listening',
  reading: 'Reading',
  writing: 'Writing',
  speaking: 'Speaking',
};

type LoadState = 'loading' | 'ready' | 'error';

function trendBadgeVariant(trend: MockReadinessTrend | null): 'success' | 'warning' | 'info' | 'muted' {
  if (!trend) return 'muted';
  if (trend.consistentGreen) return 'success';
  if (trend.overallTrend === 'up') return 'info';
  if (trend.overallTrend === 'down') return 'warning';
  return 'muted';
}

function trendIcon(trend: MockReadinessTrend | null) {
  if (!trend) return Clock;
  if (trend.overallTrend === 'up') return TrendingUp;
  if (trend.overallTrend === 'down') return TrendingDown;
  return Target;
}

export default function MockReadinessPage() {
  const router = useRouter();
  const [trend, setTrend] = useState<MockReadinessTrend | null>(null);
  const [tasks, setTasks] = useState<RemediationTask[]>([]);
  const [status, setStatus] = useState<LoadState>('loading');
  const [error, setError] = useState<string | null>(null);
  const [completingTaskId, setCompletingTaskId] = useState<string | null>(null);

  useEffect(() => {
    let cancelled = false;
    setStatus('loading');
    setError(null);

    Promise.all([fetchMockReadinessTrend(), fetchRemediationPlan()])
      .then(([trendResult, planResult]) => {
        if (cancelled) return;
        setTrend(trendResult);
        setTasks(planResult.items);
        setStatus('ready');
      })
      .catch((err: unknown) => {
        if (cancelled) return;
        const message = err instanceof Error ? err.message : 'Could not load the readiness dashboard.';
        setError(message);
        setStatus('error');
      });

    return () => {
      cancelled = true;
    };
  }, []);

  const pendingTasks = useMemo(() => tasks.filter((task) => task.status === 'pending'), [tasks]);
  const completedTasks = useMemo(() => tasks.filter((task) => task.status === 'completed'), [tasks]);
  const examReady = trend?.consistentGreen === true;
  const TrendIcon = trendIcon(trend);

  async function handleCompleteTask(taskId: string) {
    setCompletingTaskId(taskId);
    try {
      const updated = await completeRemediationTask(taskId);
      setTasks((prev) => prev.map((task) => (task.id === taskId ? { ...task, ...updated } : task)));
    } catch (err) {
      const message = err instanceof Error ? err.message : 'Could not mark this task complete.';
      setError(message);
    } finally {
      setCompletingTaskId(null);
    }
  }

  return (
    <LearnerDashboardShell
      pageTitle="Mock Readiness"
      subtitle="Trend across recent mocks and the 7-day plan that closes your weakest gaps."
      backHref="/mocks"
    >
      <div className="space-y-8">
        <Button variant="ghost" className="gap-2" onClick={() => router.push('/mocks')}>
          <ArrowLeft className="h-4 w-4" />
          Back to mock center
        </Button>

        <LearnerPageHero
          eyebrow="Readiness"
          icon={ShieldCheck}
          accent="navy"
          title="Are you exam-ready?"
          description="Two consecutive Grade-B mocks unlock the booking gate. Until then, work the 7-day plan below."
        />

        {status === 'loading' ? (
          <div className="space-y-4">
            <Skeleton className="h-32 rounded-[24px]" />
            <Skeleton className="h-48 rounded-[24px]" />
            <Skeleton className="h-64 rounded-[24px]" />
          </div>
        ) : null}

        {status === 'error' && error ? <InlineAlert variant="error">{error}</InlineAlert> : null}

        {status === 'ready' ? (
          <>
            <section className="rounded-2xl border border-border bg-surface p-6 shadow-sm">
              <LearnerSurfaceSectionHeader
                eyebrow="Trend"
                title={examReady ? 'Exam-ready signal across recent mocks' : 'Trend across recent mocks'}
                description={
                  trend && trend.attemptsConsidered > 0
                    ? `Window: last ${trend.attemptsConsidered} mock${trend.attemptsConsidered === 1 ? '' : 's'}.`
                    : 'Complete a full mock to start the trend.'
                }
                className="mb-4"
              />

              <div className="flex flex-wrap items-center gap-3">
                <Badge variant={trendBadgeVariant(trend)} size="md">
                  <TrendIcon className="mr-1 inline h-3.5 w-3.5" aria-hidden />
                  {trend?.consistentGreen
                    ? 'Consistent green'
                    : trend?.overallTrend === 'up'
                      ? 'Improving'
                      : trend?.overallTrend === 'down'
                        ? 'Trending down'
                        : 'Flat'}
                </Badge>
                {trend ? (
                  <span className="text-sm text-muted">{trend.message}</span>
                ) : null}
              </div>

              <div className="mt-4 grid gap-3 sm:grid-cols-2">
                <Link
                  href="/exam-booking"
                  aria-disabled={!examReady}
                  className={`flex items-center justify-between rounded-2xl border p-4 transition-colors ${
                    examReady
                      ? 'border-success bg-success/10 hover:border-success/70'
                      : 'cursor-not-allowed border-border bg-background-light opacity-60'
                  }`}
                  onClick={(event) => {
                    if (!examReady) event.preventDefault();
                  }}
                >
                  <div>
                    <p className="text-sm font-bold text-navy">Book the official OET</p>
                    <p className="mt-1 text-xs text-muted">
                      {examReady
                        ? 'Two consecutive Grade-B mocks recorded. Booking unlocked.'
                        : 'Locked until two consecutive Grade-B mocks land.'}
                    </p>
                  </div>
                  <ArrowRight className="h-4 w-4 text-navy" aria-hidden />
                </Link>

                <Link
                  href="/mocks/setup"
                  className="flex items-center justify-between rounded-2xl border border-border bg-background-light p-4 hover:border-border-hover"
                >
                  <div>
                    <p className="text-sm font-bold text-navy">Schedule the next mock</p>
                    <p className="mt-1 text-xs text-muted">
                      Repeat full or sectional mocks to confirm the trend.
                    </p>
                  </div>
                  <ArrowRight className="h-4 w-4 text-navy" aria-hidden />
                </Link>
              </div>
            </section>

            <section className="rounded-2xl border border-border bg-surface p-6 shadow-sm">
              <LearnerSurfaceSectionHeader
                eyebrow="7-day plan"
                title={`${pendingTasks.length} task${pendingTasks.length === 1 ? '' : 's'} open`}
                description="The plan is seeded automatically when a mock report completes. Each task targets the weakest subtest first."
                className="mb-4"
              />

              {pendingTasks.length === 0 && completedTasks.length === 0 ? (
                <p className="rounded-2xl border border-border bg-background-light p-4 text-sm text-muted">
                  No remediation tasks yet. Submit a full mock to generate a 7-day plan.
                </p>
              ) : null}

              {pendingTasks.length > 0 ? (
                <ol className="grid gap-3">
                  {pendingTasks.map((task) => (
                    <li
                      key={task.id}
                      className="rounded-2xl border border-border bg-background-light p-4"
                    >
                      <div className="flex flex-wrap items-center gap-2">
                        <Badge variant="info" size="sm">
                          Day {task.dayIndex}
                        </Badge>
                        <Badge variant="muted" size="sm">
                          {SUBTEST_LABELS[task.subtestCode] ?? task.subtestCode}
                        </Badge>
                        <Badge variant="warning" size="sm">
                          {task.weaknessTag}
                        </Badge>
                      </div>
                      <p className="mt-2 text-sm font-bold text-navy">{task.title}</p>
                      <p className="mt-1 text-sm leading-6 text-muted">{task.description}</p>
                      <div className="mt-3 flex flex-wrap items-center gap-2">
                        {task.routeHref ? (
                          <Button variant="outline" onClick={() => router.push(task.routeHref!)}>
                            Start task
                            <ArrowRight className="ml-1 h-4 w-4" aria-hidden />
                          </Button>
                        ) : null}
                        <Button
                          variant="ghost"
                          loading={completingTaskId === task.id}
                          onClick={() => void handleCompleteTask(task.id)}
                        >
                          <CheckCircle2 className="mr-1 h-4 w-4" aria-hidden />
                          Mark complete
                        </Button>
                      </div>
                    </li>
                  ))}
                </ol>
              ) : null}

              {completedTasks.length > 0 ? (
                <details className="mt-4 rounded-2xl border border-border bg-background-light p-4">
                  <summary className="cursor-pointer text-sm font-bold text-navy">
                    {completedTasks.length} task{completedTasks.length === 1 ? '' : 's'} already done
                  </summary>
                  <ul className="mt-3 space-y-2 text-sm text-muted">
                    {completedTasks.map((task) => (
                      <li key={task.id} className="flex items-start gap-2">
                        <CheckCircle2 className="mt-0.5 h-4 w-4 shrink-0 text-success" aria-hidden />
                        <span>
                          <span className="font-semibold text-navy">Day {task.dayIndex}: {task.title}</span>
                          {task.completedAt ? (
                            <span className="ml-1 text-xs">
                              ({new Date(task.completedAt).toLocaleDateString()})
                            </span>
                          ) : null}
                        </span>
                      </li>
                    ))}
                  </ul>
                </details>
              ) : null}
            </section>
          </>
        ) : null}

        {status === 'loading' && completingTaskId ? (
          <span className="inline-flex items-center gap-2 text-xs text-muted">
            <Loader2 className="h-3 w-3 animate-spin" aria-hidden /> Updating task…
          </span>
        ) : null}
      </div>
    </LearnerDashboardShell>
  );
}
