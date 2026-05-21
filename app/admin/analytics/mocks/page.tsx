'use client';

import { useEffect, useState } from 'react';
import { AlertTriangle, BarChart3, DollarSign, FileWarning, Users } from 'lucide-react';
import { Badge } from '@/components/ui/badge';
import { Skeleton } from '@/components/ui/skeleton';
import { MotionSection } from '@/components/ui/motion-primitives';
import {
  AdminRouteHero,
  AdminRoutePanel,
  AdminRouteSummaryCard,
  AdminRouteWorkspace,
} from '@/components/domain/admin-route-surface';
import { useAdminAuth } from '@/lib/hooks/use-admin-auth';
import {
  fetchAdminMocksAnalytics,
  type AdminMocksAnalyticsLowQualityRow,
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
          result.lowQualityFlags.length > 0;
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

  return (
    <AdminRouteWorkspace role="main" aria-label="Mocks analytics">
      <AdminRouteHero
        eyebrow="Analytics"
        icon={BarChart3}
        accent="indigo"
        title="Mocks Analytics"
        description="Revenue by mock package, tutor workload, and bundles flagged by item analysis for editorial follow-up."
      />

      {status === 'loading' ? (
        <>
          <div className="grid gap-4 md:grid-cols-2 xl:grid-cols-3">
            {Array.from({ length: 3 }).map((_, index) => (
              <Skeleton key={index} className="h-24 rounded-2xl" />
            ))}
          </div>
          <div className="grid gap-6 xl:grid-cols-2">
            <Skeleton className="h-64 rounded-2xl" />
            <Skeleton className="h-64 rounded-2xl" />
          </div>
          <Skeleton className="h-48 rounded-2xl" />
        </>
      ) : null}

      {status === 'error' ? (
        <AdminRoutePanel title="Mocks analytics unavailable">
          <p className="text-sm text-admin-text-muted">
            The mocks analytics service could not be loaded. Try again after the API is available.
          </p>
        </AdminRoutePanel>
      ) : null}

      {status === 'empty' && analytics ? (
        <AdminRoutePanel title="No mocks analytics yet">
          <p className="text-sm text-admin-text-muted">
            Mock revenue, tutor workload, and low-quality flags will populate once mock bundles see traffic.
          </p>
        </AdminRoutePanel>
      ) : null}

      {status === 'success' && analytics ? (
        <div className="space-y-6">
          <MotionSection delayIndex={0}>
            <div className="grid gap-4 md:grid-cols-2 xl:grid-cols-3">
              <AdminRouteSummaryCard
                label="Total mock revenue"
                value={formatRevenue(revenueSummary.total, revenueSummary.currency)}
                hint={`${analytics.revenueByPackage.length} package${analytics.revenueByPackage.length === 1 ? '' : 's'}`}
                icon={<DollarSign className="h-5 w-5" />}
                tone={revenueSummary.total > 0 ? 'success' : 'default'}
              />
              <AdminRouteSummaryCard
                label="Tutor workload"
                value={workloadSummary.pending}
                hint={`${workloadSummary.completed} completed last 7 days`}
                icon={<Users className="h-5 w-5" />}
                tone={workloadSummary.pending > 20 ? 'warning' : 'default'}
              />
              <AdminRouteSummaryCard
                label="Flagged bundles"
                value={lowQualityCount}
                hint="Item-analysis driven"
                icon={<AlertTriangle className="h-5 w-5" />}
                tone={lowQualityCount > 0 ? 'warning' : 'success'}
              />
            </div>
          </MotionSection>

          <MotionSection delayIndex={1}>
            <div className="grid gap-6 xl:grid-cols-2">
              <AdminRoutePanel
                title="Revenue by package"
                description="Totals are aggregated from BillingAddOn purchases tied to mock entitlement codes."
              >
                <RevenuePanel rows={analytics.revenueByPackage} />
              </AdminRoutePanel>

              <AdminRoutePanel
                title="Tutor workload"
                description="Pending Speaking bookings and completed sessions in the last seven days."
              >
                <WorkloadPanel rows={analytics.tutorWorkload} />
              </AdminRoutePanel>
            </div>
          </MotionSection>

          <MotionSection delayIndex={2}>
            <AdminRoutePanel
              title="Low-quality flagged bundles"
              description="Bundles whose item analysis surfaced low discrimination, weak distractors, or other quality signals."
              actions={
                <span className="inline-flex items-center gap-1 text-xs text-admin-text-muted">
                  <FileWarning className="h-3.5 w-3.5" />
                  Drives off MockItemAnalysisSnapshot.Flag
                </span>
              }
            >
              <LowQualityPanel rows={analytics.lowQualityFlags} />
            </AdminRoutePanel>
          </MotionSection>
        </div>
      ) : null}
    </AdminRouteWorkspace>
  );
}
