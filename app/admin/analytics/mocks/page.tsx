'use client';

import { useEffect, useState } from 'react';
import {
  AlertTriangle,
  BarChart3,
  BookOpen,
  CheckCircle2,
  Clock,
  DollarSign,
  FileWarning,
  Gauge,
  Target,
  Users,
} from 'lucide-react';
import { Badge } from '@/components/admin/ui/badge';
import { Skeleton } from '@/components/admin/ui/skeleton';
import { Card, CardContent, CardHeader, CardTitle, CardDescription } from '@/components/admin/ui/card';
import { KpiTile } from '@/components/admin/ui/kpi-tile';
import { AdminOperationsLayout, KpiStrip, BentoGrid, BentoCell } from '@/components/admin/layout/admin-operations-layout';
import { useAdminAuth } from '@/lib/hooks/use-admin-auth';
import {
  fetchAdminMocksAnalytics,
  type AdminMocksAnalyticsAttemptsCompletion,
  type AdminMocksAnalyticsAverageReadiness,
  type AdminMocksAnalyticsLowQualityRow,
  type AdminMocksAnalyticsMarkingDelay,
  type AdminMocksAnalyticsPassPrediction,
  type AdminMocksAnalyticsReadingSection,
  type AdminMocksAnalyticsResponse,
  type AdminMocksAnalyticsRevenueRow,
  type AdminMocksAnalyticsTutorWorkloadRow,
} from '@/lib/api';

type PageStatus = 'loading' | 'success' | 'empty' | 'error';

function formatRevenue(amount: number, currency: string) {
  try {
    return new Intl.NumberFormat(undefined, {
      style: 'currency',
      currency: currency || 'USD',
      maximumFractionDigits: 0,
    }).format(amount);
  } catch {
    return `${currency} ${amount.toFixed(0)}`;
  }
}

function sumRevenue(rows: AdminMocksAnalyticsRevenueRow[]): { total: number; currency: string } {
  if (rows.length === 0) return { total: 0, currency: 'USD' };
  const currency = rows[0].currency || 'USD';
  let total = 0;
  for (const row of rows) {
    if ((row.currency || 'USD') === currency) {
      total += row.totalRevenue;
    }
  }
  return { total, currency };
}

function sumWorkload(rows: AdminMocksAnalyticsTutorWorkloadRow[]): { pending: number; completed: number } {
  let pending = 0;
  let completed = 0;
  for (const row of rows) {
    pending += row.pendingBookings;
    completed += row.completedThisWeek;
  }
  return { pending, completed };
}

function RevenuePanel({ rows }: { rows: AdminMocksAnalyticsRevenueRow[] }) {
  if (rows.length === 0) {
    return (
      <p className="text-sm text-admin-text-muted">
        No mock package revenue recorded yet. Once learners purchase mock bundles, totals will appear here.
      </p>
    );
  }

  return (
    <div className="overflow-x-auto">
      <table className="min-w-full divide-y divide-admin-border text-sm">
        <thead>
          <tr className="text-left text-xs font-bold uppercase tracking-[0.14em] text-admin-text-muted">
            <th className="py-3 pr-4">Package</th>
            <th className="px-4 py-3">Code</th>
            <th className="px-4 py-3 text-right">Total revenue</th>
          </tr>
        </thead>
        <tbody className="divide-y divide-admin-border/70">
          {rows.map((row) => (
            <tr key={row.packageCode}>
              <td className="py-3 pr-4">
                <p className="font-semibold text-admin-text">{row.packageName}</p>
              </td>
              <td className="px-4 py-3">
                <code className="rounded bg-admin-surface-raised px-1.5 py-0.5 text-xs text-admin-text-muted">
                  {row.packageCode}
                </code>
              </td>
              <td className="px-4 py-3 text-right font-semibold tabular-nums text-admin-text">
                {formatRevenue(row.totalRevenue, row.currency)}
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}

function WorkloadPanel({ rows }: { rows: AdminMocksAnalyticsTutorWorkloadRow[] }) {
  if (rows.length === 0) {
    return (
      <p className="text-sm text-admin-text-muted">
        No tutor activity in the current window. Workload appears once mock bookings flow through.
      </p>
    );
  }

  return (
    <div className="overflow-x-auto">
      <table className="min-w-full divide-y divide-admin-border text-sm">
        <thead>
          <tr className="text-left text-xs font-bold uppercase tracking-[0.14em] text-admin-text-muted">
            <th className="py-3 pr-4">Tutor</th>
            <th className="px-4 py-3 text-right">Pending</th>
            <th className="px-4 py-3 text-right">Completed (7d)</th>
          </tr>
        </thead>
        <tbody className="divide-y divide-admin-border/70">
          {rows.map((row) => (
            <tr key={row.tutorId}>
              <td className="py-3 pr-4">
                <p className="font-semibold text-admin-text">{row.tutorName}</p>
                <p className="text-xs text-admin-text-muted">{row.tutorId}</p>
              </td>
              <td className="px-4 py-3 text-right">
                <Badge
                  variant={row.pendingBookings > 5 ? 'warning' : 'outline'}
                  className="text-[10px] tabular-nums"
                >
                  {row.pendingBookings}
                </Badge>
              </td>
              <td className="px-4 py-3 text-right font-semibold tabular-nums text-admin-text">
                {row.completedThisWeek}
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}

function LowQualityPanel({ rows }: { rows: AdminMocksAnalyticsLowQualityRow[] }) {
  if (rows.length === 0) {
    return (
      <p className="text-sm text-admin-text-muted">
        No bundles have been flagged. Item analysis raises a flag when discrimination or distractor signals fall outside policy.
      </p>
    );
  }

  return (
    <ul className="space-y-2">
      {rows.map((row) => (
        <li
          key={row.bundleId}
          className="flex flex-col gap-2 rounded-2xl border border-admin-border bg-admin-surface-raised/40 p-3 sm:flex-row sm:items-center sm:justify-between"
        >
          <div className="min-w-0">
            <p className="truncate font-semibold text-admin-text">{row.bundleTitle}</p>
            <p className="text-xs text-admin-text-muted">{row.itemCount} item{row.itemCount === 1 ? '' : 's'} flagged</p>
          </div>
          <div className="flex flex-wrap gap-1.5">
            {row.flags.length === 0 ? (
              <Badge variant="muted" className="text-[10px]">no flags</Badge>
            ) : (
              row.flags.map((flag) => (
                <Badge key={`${row.bundleId}-${flag}`} variant="warning" className="text-[10px]">
                  {flag}
                </Badge>
              ))
            )}
          </div>
        </li>
      ))}
    </ul>
  );
}

function formatPercent(value: number | null): string {
  if (value === null || Number.isNaN(value)) return '—';
  return `${Math.round(value * 100)}%`;
}

function formatHours(value: number): string {
  if (!Number.isFinite(value)) return '—';
  if (value >= 100) return `${Math.round(value)}h`;
  return `${value.toFixed(1)}h`;
}

function AttemptsCompletionPanel({ data }: { data: AdminMocksAnalyticsAttemptsCompletion }) {
  if (data.started === 0) {
    return (
      <p className="text-sm text-admin-text-muted">
        No mock sub-test attempts in the current window. Once learners start sitting mocks the completion rate will populate.
      </p>
    );
  }

  return (
    <div className="grid gap-3 sm:grid-cols-3">
      <div className="rounded-2xl border border-admin-border bg-admin-surface-raised/40 p-3">
        <p className="text-xs font-bold uppercase tracking-[0.14em] text-admin-text-muted">Started</p>
        <p className="mt-1 text-lg font-semibold tabular-nums text-admin-text">{data.started}</p>
      </div>
      <div className="rounded-2xl border border-admin-border bg-admin-surface-raised/40 p-3">
        <p className="text-xs font-bold uppercase tracking-[0.14em] text-admin-text-muted">Completed</p>
        <p className="mt-1 text-lg font-semibold tabular-nums text-admin-text">{data.completed}</p>
      </div>
      <div className="rounded-2xl border border-admin-border bg-admin-surface-raised/40 p-3">
        <p className="text-xs font-bold uppercase tracking-[0.14em] text-admin-text-muted">Completion rate</p>
        <p className="mt-1 text-lg font-semibold tabular-nums text-admin-text">
          {formatPercent(data.completionRate)}
        </p>
      </div>
    </div>
  );
}

function AverageReadinessPanel({ data }: { data: AdminMocksAnalyticsAverageReadiness }) {
  if (data.sampleSize === 0) {
    return (
      <p className="text-sm text-admin-text-muted">
        No mock reports were generated in the current window. Average readiness fills once reports are aggregated.
      </p>
    );
  }

  const total = data.sampleSize;
  const bars: Array<{ key: string; label: string; count: number; barClass: string }> = [
    { key: 'red', label: 'Red (<320)', count: data.distribution.red, barClass: 'bg-rose-500' },
    { key: 'amber', label: 'Amber (320–349)', count: data.distribution.amber, barClass: 'bg-amber-500' },
    { key: 'green', label: 'Green (350–399)', count: data.distribution.green, barClass: 'bg-emerald-500' },
    { key: 'darkGreen', label: 'Dark green (≥400)', count: data.distribution.darkGreen, barClass: 'bg-emerald-700' },
  ];

  return (
    <div className="space-y-3">
      <div className="flex flex-wrap items-baseline gap-2">
        <span className="text-2xl font-semibold tabular-nums text-admin-text">
          {data.averageScore !== null ? data.averageScore.toFixed(1) : '—'}
        </span>
        <span className="text-xs text-admin-text-muted">average overall ({data.sampleSize} report{data.sampleSize === 1 ? '' : 's'})</span>
      </div>
      <ul className="space-y-1.5">
        {bars.map((bar) => {
          const pct = total > 0 ? (bar.count / total) * 100 : 0;
          return (
            <li key={bar.key} className="space-y-1">
              <div className="flex items-center justify-between text-xs text-admin-text-muted">
                <span>{bar.label}</span>
                <span className="tabular-nums">{bar.count} · {pct.toFixed(0)}%</span>
              </div>
              <div className="h-1.5 w-full overflow-hidden rounded-full bg-admin-surface-raised">
                <div
                  className={`h-full ${bar.barClass}`}
                  style={{ width: `${Math.max(2, pct)}%` }}
                  aria-hidden
                />
              </div>
            </li>
          );
        })}
      </ul>
    </div>
  );
}

function PassPredictionPanel({ data }: { data: AdminMocksAnalyticsPassPrediction }) {
  if (data.sampleSize === 0) {
    return (
      <p className="text-sm text-admin-text-muted">
        No mock reports with overall scores in the current window. Pass-rate trends populate once reports are generated.
      </p>
    );
  }

  if (data.byProfession.length === 0) {
    return (
      <p className="text-sm text-admin-text-muted">
        Profession breakdown unavailable — global predicted pass rate: {formatPercent(data.predictedPassRate)}.
      </p>
    );
  }

  return (
    <div className="overflow-x-auto">
      <table className="min-w-full divide-y divide-admin-border text-sm">
        <thead>
          <tr className="text-left text-xs font-bold uppercase tracking-[0.14em] text-admin-text-muted">
            <th className="py-3 pr-4">Profession</th>
            <th className="px-4 py-3 text-right">Sample size</th>
            <th className="px-4 py-3 text-right">Predicted pass rate</th>
          </tr>
        </thead>
        <tbody className="divide-y divide-admin-border/70">
          {data.byProfession.map((row) => (
            <tr key={row.profession}>
              <td className="py-3 pr-4 capitalize">
                <p className="font-semibold text-admin-text">{row.profession}</p>
              </td>
              <td className="px-4 py-3 text-right tabular-nums text-admin-text">{row.sampleSize}</td>
              <td className="px-4 py-3 text-right">
                <Badge
                  variant={row.predictedPassRate < 0.5 ? 'warning' : 'outline'}
                  className="text-[10px] tabular-nums"
                >
                  {formatPercent(row.predictedPassRate)}
                </Badge>
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}

function MarkingDelayPanel({ data }: { data: AdminMocksAnalyticsMarkingDelay }) {
  if (data.perSubtest.length === 0) {
    return (
      <p className="text-sm text-admin-text-muted">
        No tutor-reviewed mocks closed inside the current window. Delay metrics appear once reviewers consume reservations.
      </p>
    );
  }

  return (
    <div className="overflow-x-auto">
      <table className="min-w-full divide-y divide-admin-border text-sm">
        <thead>
          <tr className="text-left text-xs font-bold uppercase tracking-[0.14em] text-admin-text-muted">
            <th className="py-3 pr-4">Sub-test</th>
            <th className="px-4 py-3 text-right">Sample size</th>
            <th className="px-4 py-3 text-right">Avg delay</th>
            <th className="px-4 py-3 text-right">P95 delay</th>
          </tr>
        </thead>
        <tbody className="divide-y divide-admin-border/70">
          {data.perSubtest.map((row) => (
            <tr key={row.subtest}>
              <td className="py-3 pr-4 capitalize">
                <p className="font-semibold text-admin-text">{row.subtest}</p>
              </td>
              <td className="px-4 py-3 text-right tabular-nums text-admin-text">{row.sampleSize}</td>
              <td className="px-4 py-3 text-right tabular-nums text-admin-text">{formatHours(row.avgDelayHours)}</td>
              <td className="px-4 py-3 text-right">
                <Badge
                  variant={row.p95DelayHours > 48 ? 'warning' : 'outline'}
                  className="text-[10px] tabular-nums"
                >
                  {formatHours(row.p95DelayHours)}
                </Badge>
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}

/**
 * Phase 2 closure — Reading subtest cross-reference panel. Reads from
 * `/v1/admin/analytics/mocks` root payload. Lets operators see how
 * Reading is performing inside the mock-session funnel without
 * leaving the mocks dashboard.
 */
function ReadingSectionPanel({ data }: { data: AdminMocksAnalyticsReadingSection }) {
  if (data.started === 0) {
    return (
      <p className="text-sm text-admin-text-muted">
        No Reading sub-test attempts inside mocks yet. Once learners launch
        Reading from a mock booking, the cross-reference populates.
      </p>
    );
  }

  return (
    <div className="grid gap-3 sm:grid-cols-2 xl:grid-cols-4">
      <div className="rounded-2xl border border-admin-border bg-admin-surface-raised/40 p-3">
        <p className="text-xs font-bold uppercase tracking-[0.14em] text-admin-text-muted">Started</p>
        <p className="mt-1 text-lg font-semibold tabular-nums text-admin-text">{data.started}</p>
      </div>
      <div className="rounded-2xl border border-admin-border bg-admin-surface-raised/40 p-3">
        <p className="text-xs font-bold uppercase tracking-[0.14em] text-admin-text-muted">Submitted</p>
        <p className="mt-1 text-lg font-semibold tabular-nums text-admin-text">{data.submitted}</p>
        {data.completionRatePercent != null ? (
          <p className="mt-1 text-xs text-admin-text-muted tabular-nums">
            {data.completionRatePercent.toFixed(1)}% completion
          </p>
        ) : null}
      </div>
      <div className="rounded-2xl border border-admin-border bg-admin-surface-raised/40 p-3">
        <p className="text-xs font-bold uppercase tracking-[0.14em] text-admin-text-muted">Avg scaled</p>
        <p className="mt-1 text-lg font-semibold tabular-nums text-admin-text">
          {data.averageScaledScore != null ? data.averageScaledScore.toFixed(0) : '—'}
        </p>
        {data.averageRawScore != null ? (
          <p className="mt-1 text-xs text-admin-text-muted tabular-nums">
            raw {data.averageRawScore.toFixed(1)} / 42
          </p>
        ) : null}
      </div>
      <div className="rounded-2xl border border-admin-border bg-admin-surface-raised/40 p-3">
        <p className="text-xs font-bold uppercase tracking-[0.14em] text-admin-text-muted">Avg time</p>
        <p className="mt-1 text-lg font-semibold tabular-nums text-admin-text">
          {data.averageCompletionSeconds != null
            ? `${Math.round(data.averageCompletionSeconds / 60)}m`
            : '—'}
        </p>
      </div>
    </div>
  );
}

export default function AdminMocksAnalyticsPage() {
  const { isAuthenticated, role } = useAdminAuth();
  const [status, setStatus] = useState<PageStatus>('loading');
  const [analytics, setAnalytics] = useState<AdminMocksAnalyticsResponse | null>(null);

  useEffect(() => {
    if (!isAuthenticated || role !== 'admin') {
      return;
    }

    let cancelled = false;

    async function load() {
      setStatus('loading');
      try {
        const result = await fetchAdminMocksAnalytics();
        if (cancelled) return;

        setAnalytics(result);
        const hasData =
          result.revenueByPackage.length > 0 ||
          result.tutorWorkload.length > 0 ||
          result.lowQualityFlags.length > 0 ||
          result.attemptsCompletion.started > 0 ||
          result.averageReadiness.sampleSize > 0 ||
          result.passPrediction.sampleSize > 0 ||
          result.markingDelay.perSubtest.length > 0 ||
          result.readingSection.started > 0;
        setStatus(hasData ? 'success' : 'empty');
      } catch (error) {
        console.error(error);
        if (!cancelled) {
          setStatus('error');
        }
      }
    }

    load();
    return () => {
      cancelled = true;
    };
  }, [isAuthenticated, role]);

  if (!isAuthenticated || role !== 'admin') return null;

  const revenueSummary = analytics ? sumRevenue(analytics.revenueByPackage) : { total: 0, currency: 'USD' };
  const workloadSummary = analytics ? sumWorkload(analytics.tutorWorkload) : { pending: 0, completed: 0 };
  const lowQualityCount = analytics?.lowQualityFlags.length ?? 0;

  const breadcrumbs = [
    { label: 'Admin', href: '/admin' },
    { label: 'Analytics', href: '/admin' },
    { label: 'Mocks' },
  ];

  return (
    <AdminOperationsLayout
      title="Mocks Analytics"
      description="Revenue by mock package, tutor workload, item-analysis flags, completion rate, readiness, pass-rate, and tutor marking turnaround."
      eyebrow="Analytics"
      breadcrumbs={breadcrumbs}
      kpis={
        status === 'success' && analytics ? (
          <KpiStrip>
            <KpiTile
              label="Total mock revenue"
              value={formatRevenue(revenueSummary.total, revenueSummary.currency)}
              icon={<DollarSign className="h-4 w-4" />}
              tone={revenueSummary.total > 0 ? 'success' : 'default'}
            />
            <KpiTile
              label="Tutor workload"
              value={workloadSummary.pending}
              icon={<Users className="h-4 w-4" />}
              tone={workloadSummary.pending > 20 ? 'warning' : 'default'}
            />
            <KpiTile
              label="Flagged bundles"
              value={lowQualityCount}
              icon={<AlertTriangle className="h-4 w-4" />}
              tone={lowQualityCount > 0 ? 'warning' : 'success'}
            />
            <KpiTile
              label="Completion rate (30d)"
              value={formatPercent(analytics.attemptsCompletion.completionRate)}
              icon={<CheckCircle2 className="h-4 w-4" />}
              tone={analytics.attemptsCompletion.completionRate < 0.6 ? 'warning' : 'success'}
            />
          </KpiStrip>
        ) : null
      }
      primaryGrid={
        <div className="space-y-6">
          {status === 'loading' ? (
            <>
              <div className="grid gap-4 md:grid-cols-2 xl:grid-cols-3">
                {Array.from({ length: 3 }).map((_, index) => (
                  <Skeleton key={index} className="h-24 rounded-admin" />
                ))}
              </div>
              <div className="grid gap-6 xl:grid-cols-2">
                <Skeleton className="h-64 rounded-admin" />
                <Skeleton className="h-64 rounded-admin" />
              </div>
              <Skeleton className="h-48 rounded-admin" />
            </>
          ) : null}

          {status === 'error' ? (
            <Card>
              <CardHeader>
                <CardTitle>Mocks analytics unavailable</CardTitle>
              </CardHeader>
              <CardContent>
                <p className="text-sm text-admin-fg-muted">
                  The mocks analytics service could not be loaded. Try again after the API is available.
                </p>
              </CardContent>
            </Card>
          ) : null}

          {status === 'empty' && analytics ? (
            <Card>
              <CardHeader>
                <CardTitle>No mocks analytics yet</CardTitle>
              </CardHeader>
              <CardContent>
                <p className="text-sm text-admin-fg-muted">
                  Mock revenue, tutor workload, and low-quality flags will populate once mock bundles see traffic.
                </p>
              </CardContent>
            </Card>
          ) : null}

          {status === 'success' && analytics ? (
            <>
              <BentoGrid>
                <BentoCell span={{ default: 12, xl: 6 }}>
                  <Card>
                    <CardHeader>
                      <CardTitle>Revenue by package</CardTitle>
                      <CardDescription>Totals are aggregated from BillingAddOn purchases tied to mock entitlement codes.</CardDescription>
                    </CardHeader>
                    <CardContent>
                      <RevenuePanel rows={analytics.revenueByPackage} />
                    </CardContent>
                  </Card>
                </BentoCell>
                <BentoCell span={{ default: 12, xl: 6 }}>
                  <Card>
                    <CardHeader>
                      <CardTitle>Tutor workload</CardTitle>
                      <CardDescription>Pending Speaking bookings and completed sessions in the last seven days.</CardDescription>
                    </CardHeader>
                    <CardContent>
                      <WorkloadPanel rows={analytics.tutorWorkload} />
                    </CardContent>
                  </Card>
                </BentoCell>
              </BentoGrid>

              <Card>
                <CardHeader>
                  <div className="flex items-start justify-between gap-3">
                    <div>
                      <CardTitle>Low-quality flagged bundles</CardTitle>
                      <CardDescription>Bundles whose item analysis surfaced low discrimination, weak distractors, or other quality signals.</CardDescription>
                    </div>
                    <span className="inline-flex items-center gap-1 text-xs text-admin-fg-muted">
                      <FileWarning className="h-3.5 w-3.5" />
                      MockItemAnalysisSnapshot.Flag
                    </span>
                  </div>
                </CardHeader>
                <CardContent>
                  <LowQualityPanel rows={analytics.lowQualityFlags} />
                </CardContent>
              </Card>

              <KpiStrip>
                <KpiTile
                  label="Avg readiness (30d)"
                  value={
                    analytics.averageReadiness.averageScore !== null
                      ? analytics.averageReadiness.averageScore.toFixed(0)
                      : '—'
                  }
                  icon={<Gauge className="h-4 w-4" />}
                  tone={
                    analytics.averageReadiness.averageScore !== null && analytics.averageReadiness.averageScore < 350
                      ? 'warning'
                      : 'default'
                  }
                />
                <KpiTile
                  label="Predicted pass rate (30d)"
                  value={formatPercent(analytics.passPrediction.predictedPassRate)}
                  icon={<Target className="h-4 w-4" />}
                  tone={
                    analytics.passPrediction.predictedPassRate !== null
                    && analytics.passPrediction.predictedPassRate < 0.5
                      ? 'warning'
                      : 'success'
                  }
                />
                <KpiTile
                  label="Marking turnaround (avg)"
                  value={
                    analytics.markingDelay.perSubtest.length > 0
                      ? formatHours(
                          analytics.markingDelay.perSubtest.reduce(
                            (acc, row) => acc + row.avgDelayHours * row.sampleSize,
                            0,
                          ) /
                            Math.max(
                              1,
                              analytics.markingDelay.perSubtest.reduce((acc, row) => acc + row.sampleSize, 0),
                            ),
                        )
                      : '—'
                  }
                  icon={<Clock className="h-4 w-4" />}
                  tone={
                    analytics.markingDelay.perSubtest.some((row) => row.p95DelayHours > 48)
                      ? 'warning'
                      : 'default'
                  }
                />
              </KpiStrip>

              <BentoGrid>
                <BentoCell span={{ default: 12, xl: 6 }}>
                  <Card>
                    <CardHeader>
                      <CardTitle>Attempts completion</CardTitle>
                      <CardDescription>Sub-test attempts started vs. submitted/completed within the 30-day window.</CardDescription>
                    </CardHeader>
                    <CardContent>
                      <AttemptsCompletionPanel data={analytics.attemptsCompletion} />
                    </CardContent>
                  </Card>
                </BentoCell>
                <BentoCell span={{ default: 12, xl: 6 }}>
                  <Card>
                    <CardHeader>
                      <CardTitle>Average readiness</CardTitle>
                      <CardDescription>Distribution of overallScore across mock reports generated in the last 30 days.</CardDescription>
                    </CardHeader>
                    <CardContent>
                      <AverageReadinessPanel data={analytics.averageReadiness} />
                    </CardContent>
                  </Card>
                </BentoCell>
                <BentoCell span={{ default: 12, xl: 6 }}>
                  <Card>
                    <CardHeader>
                      <CardTitle>Pass prediction by profession</CardTitle>
                      <CardDescription>Reports with overall ≥ 350 (OET Grade-B anchor) counted as predicted pass.</CardDescription>
                    </CardHeader>
                    <CardContent>
                      <PassPredictionPanel data={analytics.passPrediction} />
                    </CardContent>
                  </Card>
                </BentoCell>
                <BentoCell span={{ default: 12, xl: 6 }}>
                  <Card>
                    <CardHeader>
                      <CardTitle>Tutor marking delay</CardTitle>
                      <CardDescription>Hours from review reservation to marker consumption, per sub-test.</CardDescription>
                    </CardHeader>
                    <CardContent>
                      <MarkingDelayPanel data={analytics.markingDelay} />
                    </CardContent>
                  </Card>
                </BentoCell>
              </BentoGrid>

              <Card>
                <CardHeader>
                  <div className="flex items-start justify-between gap-3">
                    <div>
                      <CardTitle>Reading inside mocks</CardTitle>
                      <CardDescription>Reading subtest performance across all mock sessions. Cross-reference of MockSectionAttempt rows where subtest code is reading.</CardDescription>
                    </div>
                    <span className="inline-flex items-center gap-1 text-xs text-admin-fg-muted">
                      <BookOpen className="h-3.5 w-3.5" />
                      /admin/analytics/reading
                    </span>
                  </div>
                </CardHeader>
                <CardContent>
                  <ReadingSectionPanel data={analytics.readingSection} />
                </CardContent>
              </Card>
            </>
          ) : null}
        </div>
      }
    />
  );
}
