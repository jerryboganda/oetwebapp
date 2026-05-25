'use client';

/**
 * Admin Operations dashboard — the canonical demonstration of the new admin
 * design system. This page proves the new primitives end-to-end:
 *
 *   • AdminOperationsLayout / KpiStrip / BentoGrid (layout chrome)
 *   • KpiTile (metrics)
 *   • Card + DataTable (review-ops + billing tables)
 *   • Badge + statusToTone (status semantics)
 *   • EmptyState (error fallback inside the layout)
 *   • TableSkeleton (loading fallback inside the layout)
 *
 * Data + auth wiring is preserved verbatim from the legacy implementation:
 * `useAdminAuth`, `getAdminDashboardData`, `PrivilegedMfaBanner`. Only the
 * visual layer changed.
 */

import { useEffect, useState } from 'react';
import {
  AlertTriangle,
  ArrowRight,
  Flag,
  Inbox,
  RefreshCw,
  Shield,
  TrendingUp,
  Users,
} from 'lucide-react';

// ── Data + auth wiring (preserved) ────────────────────────────────────
import { PrivilegedMfaBanner } from '@/components/auth/privileged-mfa-banner';
import { useAdminAuth } from '@/lib/hooks/use-admin-auth';
import { getAdminDashboardData } from '@/lib/admin';
import type { AdminDashboardData } from '@/lib/types/admin';

// ── New admin design system primitives ────────────────────────────────
import {
  AdminOperationsLayout,
  KpiStrip,
  BentoGrid,
  BentoCell,
} from '@/components/admin/layout/admin-operations-layout';
import { Button } from '@/components/admin/ui/button';
import {
  Card,
  CardHeader,
  CardTitle,
  CardAction,
  CardContent,
} from '@/components/admin/ui/card';
import { KpiTile } from '@/components/admin/ui/kpi-tile';
import { Badge } from '@/components/admin/ui/badge';
import { DataTable, type ColumnDef } from '@/components/admin/ui/data-table';
import { EmptyState } from '@/components/admin/ui/empty-state';
import { TableSkeleton } from '@/components/admin/ui/skeleton';

/* ─────────────────────────────────────────────────────────────────────
 * Row types for the dashboard tables. We model these in-page rather than
 * leak local shapes into the global types module — the DataTable just
 * needs an inline ColumnDef array.
 * ───────────────────────────────────────────────────────────────────── */

type ReviewOpsRow = {
  id: string;
  workstream: string;
  badge: { tone: 'default' | 'warning' | 'danger' | 'success' | 'info'; label: string };
  count: number;
  emphasize: boolean;
};

type BillingRow = {
  id: string;
  category: string;
  badge: { tone: 'default' | 'warning' | 'danger' | 'success' | 'info'; label: string };
  count: string;
};

type QualityRow = {
  label: string;
  value: string;
  tone: 'default' | 'success' | 'warning' | 'danger' | 'info';
};

/* ─────────────────────────────────────────────────────────────────────
 * Pure projections from AdminDashboardData → table rows. Kept outside
 * the component so they can be unit-tested without rendering.
 * ───────────────────────────────────────────────────────────────────── */

function reviewOpsRows(d: AdminDashboardData): ReviewOpsRow[] {
  return [
    {
      id: 'backlog',
      workstream: 'Backlog',
      badge: d.reviewOps.backlog > 0
        ? { tone: 'warning', label: 'Action required' }
        : { tone: 'default', label: 'Normal' },
      count: d.reviewOps.backlog,
      emphasize: d.reviewOps.backlog > 0,
    },
    {
      id: 'overdue',
      workstream: 'Overdue',
      badge: d.reviewOps.overdue > 0
        ? { tone: 'danger', label: 'Critical' }
        : { tone: 'success', label: 'Clear' },
      count: d.reviewOps.overdue,
      emphasize: d.reviewOps.overdue > 0,
    },
    {
      id: 'inProgress',
      workstream: 'In progress',
      badge: { tone: 'info', label: 'Active' },
      count: d.reviewOps.inProgress,
      emphasize: false,
    },
    {
      id: 'failedReviews',
      workstream: 'Failed reviews',
      badge: d.reviewOps.failedReviews > 0
        ? { tone: 'danger', label: 'Failing' }
        : { tone: 'success', label: 'Clear' },
      count: d.reviewOps.failedReviews,
      emphasize: d.reviewOps.failedReviews > 0,
    },
    {
      id: 'failedJobs',
      workstream: 'Failed jobs',
      badge: d.reviewOps.failedJobs > 0
        ? { tone: 'danger', label: 'Failing' }
        : { tone: 'success', label: 'Healthy' },
      count: d.reviewOps.failedJobs,
      emphasize: d.reviewOps.failedJobs > 0,
    },
  ];
}

function billingRows(d: AdminDashboardData): BillingRow[] {
  return [
    {
      id: 'activeSubscribers',
      category: 'Active subscribers',
      badge: { tone: 'success', label: 'Stable' },
      count: d.billingRisk.activeSubscribers.toLocaleString(),
    },
    {
      id: 'pendingInvoices',
      category: 'Pending invoices',
      badge: d.billingRisk.pendingInvoices > 0
        ? { tone: 'warning', label: 'Pending' }
        : { tone: 'success', label: 'Clear' },
      count: d.billingRisk.pendingInvoices.toLocaleString(),
    },
    {
      id: 'failedInvoices',
      category: 'Failed invoices',
      badge: d.billingRisk.failedInvoices > 0
        ? { tone: 'danger', label: 'At risk' }
        : { tone: 'success', label: 'Clear' },
      count: d.billingRisk.failedInvoices.toLocaleString(),
    },
    {
      id: 'legacyPlans',
      category: 'Legacy plans',
      badge: { tone: 'warning', label: 'Deprecating' },
      count: d.billingRisk.legacyPlans.toLocaleString(),
    },
  ];
}

function qualityRows(d: AdminDashboardData): QualityRow[] {
  const agreementTone: QualityRow['tone'] =
    d.quality.agreementRate >= 80 ? 'success' : d.quality.agreementRate >= 65 ? 'warning' : 'danger';
  return [
    { label: 'AI/human agreement', value: `${d.quality.agreementRate}%`, tone: agreementTone },
    { label: 'Evaluations', value: d.quality.evaluationCount.toLocaleString(), tone: 'info' },
    { label: 'Avg review time', value: `${d.quality.avgReviewHours}h`, tone: 'default' },
    {
      label: 'Risk cases',
      value: d.quality.riskCases.toLocaleString(),
      tone: d.quality.riskCases > 0 ? 'danger' : 'success',
    },
  ];
}

/* ─────────────────────────────────────────────────────────────────────
 * Column definitions for the new DataTable. Inline because the rows
 * never leave this page.
 * ───────────────────────────────────────────────────────────────────── */

const reviewOpsColumns: ColumnDef<ReviewOpsRow>[] = [
  {
    id: 'workstream',
    header: 'Workstream',
    cell: ({ row }) => (
      <span className="font-medium text-admin-fg-strong">{row.original.workstream}</span>
    ),
  },
  {
    id: 'status',
    header: 'Status',
    cell: ({ row }) => (
      <Badge variant={row.original.badge.tone} intensity="tinted" size="sm">
        {row.original.badge.label}
      </Badge>
    ),
  },
  {
    id: 'count',
    header: () => <span className="block text-right">Count</span>,
    cell: ({ row }) => (
      <span
        className={
          row.original.emphasize
            ? 'block text-right font-mono font-semibold tabular-nums text-admin-fg-strong'
            : 'block text-right font-mono tabular-nums text-admin-fg-muted'
        }
      >
        {row.original.count.toLocaleString()}
      </span>
    ),
  },
];

const billingColumns: ColumnDef<BillingRow>[] = [
  {
    id: 'category',
    header: 'Category',
    cell: ({ row }) => (
      <span className="font-medium text-admin-fg-strong">{row.original.category}</span>
    ),
  },
  {
    id: 'status',
    header: 'Status',
    cell: ({ row }) => (
      <Badge variant={row.original.badge.tone} intensity="tinted" size="sm">
        {row.original.badge.label}
      </Badge>
    ),
  },
  {
    id: 'count',
    header: () => <span className="block text-right">Count</span>,
    cell: ({ row }) => (
      <span className="block text-right font-mono tabular-nums text-admin-fg-default">
        {row.original.count}
      </span>
    ),
  },
];

/* ─────────────────────────────────────────────────────────────────────
 * Page
 * ───────────────────────────────────────────────────────────────────── */

type FetchStatus = 'loading' | 'success' | 'error';

export default function AdminDashboardPage() {
  const { isAuthenticated, role } = useAdminAuth();
  const [status, setStatus] = useState<FetchStatus>('loading');
  const [data, setData] = useState<AdminDashboardData | null>(null);
  const [error, setError] = useState<Error | null>(null);

  useEffect(() => {
    let cancelled = false;
    setStatus('loading');
    setError(null);
    getAdminDashboardData()
      .then((d) => {
        if (cancelled) return;
        setData(d);
        setStatus('success');
      })
      .catch((err: unknown) => {
        if (cancelled) return;
        setError(err instanceof Error ? err : new Error('Failed to load dashboard'));
        setStatus('error');
      });
    return () => {
      cancelled = true;
    };
  }, []);

  // Auth gate — matches legacy behaviour. Server-side enforcement lives upstream;
  // this prevents an authenticated-but-wrong-role flash.
  if (!isAuthenticated || role !== 'admin') return null;

  return (
    <>
      <PrivilegedMfaBanner key="privileged-mfa-banner" />
      <h1 className="sr-only">Admin Operations Dashboard</h1>

      <AdminOperationsLayout
        title="Operations"
        description="Platform health · review risk · rollout signals · daily ops view"
        actions={
          <Button
            size="sm"
            variant="outline"
            startIcon={<RefreshCw className="h-4 w-4" />}
            onClick={() => window.location.reload()}
            aria-label="Refresh dashboard data"
          >
            Refresh
          </Button>
        }
      >
        {status === 'error' ? (
          <EmptyState
            variant="error"
            size="lg"
            illustration={<AlertTriangle aria-hidden="true" />}
            title="Couldn't load dashboard"
            description={
              error?.message ??
              'The dashboard data service did not respond. Reload to try again, or check your network.'
            }
            primaryAction={{
              label: 'Retry',
              onClick: () => window.location.reload(),
            }}
          />
        ) : status === 'loading' || !data ? (
          <DashboardSkeleton />
        ) : (
          <DashboardContent data={data} />
        )}
      </AdminOperationsLayout>
    </>
  );
}

/* ─────────────────────────────────────────────────────────────────────
 * Loading & success subtrees split out so they don't fight over the
 * Suspense boundary inside AdminOperationsLayout (when present).
 * ───────────────────────────────────────────────────────────────────── */

function DashboardSkeleton() {
  return (
    <div className="space-y-6" aria-busy="true" aria-label="Loading dashboard">
      <div className="grid grid-cols-2 gap-3 sm:grid-cols-3 lg:grid-cols-6">
        {Array.from({ length: 6 }).map((_, i) => (
          <KpiTile key={i} label="" value="" loading />
        ))}
      </div>
      <div className="grid grid-cols-1 gap-4 lg:grid-cols-12">
        <div className="lg:col-span-8">
          <TableSkeleton rows={5} columns={3} />
        </div>
        <div className="space-y-4 lg:col-span-4">
          <TableSkeleton rows={4} columns={3} />
          <TableSkeleton rows={4} columns={2} showHeader={false} />
          <TableSkeleton rows={4} columns={2} showHeader={false} />
        </div>
      </div>
    </div>
  );
}

function DashboardContent({ data: d }: { data: AdminDashboardData }) {
  const agreementTone =
    d.quality.agreementRate >= 80 ? 'success' : d.quality.agreementRate >= 65 ? 'warning' : 'danger';

  return (
    <>
      {/* KPI strip — six pulse-style tiles. */}
      <KpiStrip aria-label="Operations key metrics">
        <KpiTile
          label="Backlog"
          value={d.reviewOps.backlog.toLocaleString()}
          tone={d.reviewOps.backlog > 0 ? 'warning' : 'default'}
          icon={<Inbox className="h-4 w-4" />}
        />
        <KpiTile
          label="Overdue"
          value={d.reviewOps.overdue.toLocaleString()}
          tone={d.reviewOps.overdue > 0 ? 'danger' : 'success'}
          icon={<AlertTriangle className="h-4 w-4" />}
        />
        <KpiTile
          label="Active Subscribers"
          value={d.billingRisk.activeSubscribers.toLocaleString()}
          tone="info"
          icon={<Users className="h-4 w-4" />}
        />
        <KpiTile
          label="Agreement Rate"
          value={`${d.quality.agreementRate}%`}
          tone={agreementTone}
          icon={<Shield className="h-4 w-4" />}
        />
        <KpiTile
          label="Risk Cases"
          value={d.quality.riskCases.toLocaleString()}
          tone={d.quality.riskCases > 0 ? 'danger' : 'success'}
          icon={<TrendingUp className="h-4 w-4" />}
        />
        <KpiTile
          label="Live Flags"
          value={d.flags.liveExperiments.toLocaleString()}
          tone={d.flags.liveExperiments > 0 ? 'primary' : 'default'}
          icon={<Flag className="h-4 w-4" />}
        />
      </KpiStrip>

      {/* Main bento — large left (review ops) + right rail (3 cards). */}
      <BentoGrid>
        <BentoCell span={{ base: 12, lg: 8 }}>
          <Card>
            <CardHeader>
              <CardTitle>Review Operations</CardTitle>
              <CardAction>
                <Button
                  asChild
                  variant="ghost"
                  size="sm"
                  endIcon={<ArrowRight className="h-3.5 w-3.5" />}
                  aria-label="View all review operations"
                >
                  <a href="/admin/review-ops">View all</a>
                </Button>
              </CardAction>
            </CardHeader>
            <CardContent>
              <DataTable
                aria-label="Review operations workstreams"
                columns={reviewOpsColumns}
                data={reviewOpsRows(d)}
                keyExtractor={(row) => row.id}
              />
            </CardContent>
          </Card>
        </BentoCell>

        <BentoCell span={{ base: 12, lg: 4 }}>
          <div className="flex flex-col gap-4">
            <Card>
              <CardHeader>
                <CardTitle>Billing & Subscriptions</CardTitle>
                <CardAction>
                  <Button
                    asChild
                    variant="ghost"
                    size="sm"
                    endIcon={<ArrowRight className="h-3.5 w-3.5" />}
                    aria-label="Open billing operations"
                  >
                    <a href="/admin/billing">Open</a>
                  </Button>
                </CardAction>
              </CardHeader>
              <CardContent>
                <DataTable
                  aria-label="Billing risk and subscriptions"
                  columns={billingColumns}
                  data={billingRows(d)}
                  keyExtractor={(row) => row.id}
                />
              </CardContent>
            </Card>

            <Card>
              <CardHeader>
                <CardTitle>Content Health</CardTitle>
                <CardAction>
                  <Button
                    asChild
                    variant="ghost"
                    size="sm"
                    endIcon={<ArrowRight className="h-3.5 w-3.5" />}
                    aria-label="Open content hub"
                  >
                    <a href="/admin/content">Open</a>
                  </Button>
                </CardAction>
              </CardHeader>
              <CardContent>
                <div className="grid grid-cols-2 gap-3">
                  <KpiTile
                    label="Published"
                    value={d.contentHealth.published.toLocaleString()}
                    tone="success"
                    size="sm"
                  />
                  <KpiTile
                    label="Drafts"
                    value={d.contentHealth.drafts.toLocaleString()}
                    tone={d.contentHealth.drafts > 0 ? 'info' : 'default'}
                    size="sm"
                  />
                  <KpiTile
                    label="Archived"
                    value={d.contentHealth.archived.toLocaleString()}
                    tone="default"
                    size="sm"
                  />
                  <KpiTile
                    label="Stale Drafts"
                    value={d.contentHealth.staleDrafts.toLocaleString()}
                    tone={d.contentHealth.staleDrafts > 0 ? 'warning' : 'default'}
                    size="sm"
                  />
                </div>
              </CardContent>
            </Card>

            <Card>
              <CardHeader>
                <CardTitle>Quality</CardTitle>
                <CardAction>
                  <Button
                    asChild
                    variant="ghost"
                    size="sm"
                    endIcon={<ArrowRight className="h-3.5 w-3.5" />}
                    aria-label="Open quality analytics"
                  >
                    <a href="/admin/analytics/quality">Open</a>
                  </Button>
                </CardAction>
              </CardHeader>
              <CardContent>
                <ul
                  className="divide-y divide-admin-border"
                  aria-label="Quality metrics"
                >
                  {qualityRows(d).map((row) => (
                    <li
                      key={row.label}
                      className="flex items-center justify-between py-2.5 first:pt-0 last:pb-0"
                    >
                      <span className="text-sm text-admin-fg-default">{row.label}</span>
                      <Badge variant={row.tone} intensity="tinted" size="sm">
                        {row.value}
                      </Badge>
                    </li>
                  ))}
                </ul>
              </CardContent>
            </Card>
          </div>
        </BentoCell>
      </BentoGrid>
    </>
  );
}
