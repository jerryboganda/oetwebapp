'use client';

import { useEffect, useState } from 'react';
import Link from 'next/link';
import {
  Activity,
  AlertTriangle,
  Archive,
  BarChart3,
  Briefcase,
  CalendarClock,
  CheckCircle2,
  Clock4,
  CreditCard,
  FileText,
  Flag,
  Inbox,
  PlayCircle,
  Shield,
  Sparkles,
  Users,
  XCircle,
  Zap,
} from 'lucide-react';
import type { ElementType } from 'react';
import {
  AdminRouteFreshnessBadge,
  AdminRouteHero,
  AdminRoutePanel,
  AdminRouteSummaryCard,
  AdminRouteWorkspace,
} from '@/components/domain/admin-route-surface';
import { AdminQuickAction } from '@/components/domain/admin-quick-action';
import { AsyncStateWrapper } from '@/components/state/async-state-wrapper';
import { useAdminAuth } from '@/lib/hooks/use-admin-auth';
import { getAdminDashboardData } from '@/lib/admin';
import type { AdminDashboardData } from '@/lib/types/admin';
import { cn } from '@/lib/utils';

type PageStatus = 'loading' | 'success' | 'error';

type MetricTone = 'default' | 'success' | 'warning' | 'danger';

const METRIC_TONE_STYLES: Record<MetricTone, { bg: string; icon: string; value: string }> = {
  default: { bg: 'bg-background-light', icon: 'text-primary', value: 'text-navy' },
  success: { bg: 'bg-success/10', icon: 'text-success', value: 'text-navy' },
  warning: { bg: 'bg-warning/10', icon: 'text-warning', value: 'text-navy' },
  danger: { bg: 'bg-danger/10', icon: 'text-danger', value: 'text-danger' },
};

function MetricTile({
  icon: Icon,
  label,
  value,
  tone = 'default',
}: {
  icon: ElementType;
  label: string;
  value: string | number;
  tone?: MetricTone;
}) {
  const styles = METRIC_TONE_STYLES[tone];
  return (
    <div className={cn('flex items-center gap-3 rounded-xl border border-border p-3', styles.bg)}>
      <div className={cn('flex h-9 w-9 shrink-0 items-center justify-center rounded-lg bg-surface', styles.icon)}>
        <Icon className="h-4 w-4" aria-hidden />
      </div>
      <div className="min-w-0">
        <p className="text-xs font-semibold uppercase tracking-[0.14em] text-muted">{label}</p>
        <p className={cn('text-lg font-semibold leading-tight', styles.value)}>{value}</p>
      </div>
    </div>
  );
}

export default function AdminDashboardPage() {
  const { isAuthenticated, role } = useAdminAuth();
  const [pageStatus, setPageStatus] = useState<PageStatus>('loading');
  const [dashboard, setDashboard] = useState<AdminDashboardData | null>(null);

  useEffect(() => {
    let cancelled = false;
    async function load() {
      setPageStatus('loading');
      try {
        const summary = await getAdminDashboardData();
        if (!cancelled) {
          setDashboard(summary);
          setPageStatus('success');
        }
      } catch (error) {
        console.error(error);
        if (!cancelled) {
          setPageStatus('error');
        }
      }
    }

    load();
    return () => {
      cancelled = true;
    };
  }, []);

  if (!isAuthenticated || role !== 'admin') return null;

  return (
    <AdminRouteWorkspace role="main" aria-label="Admin operations">
      <AsyncStateWrapper status={pageStatus} onRetry={() => window.location.reload()}>
        {dashboard ? (
          <div className="space-y-8">
            <AdminRouteHero
              eyebrow="Operational Control"
              icon={Sparkles}
              accent="primary"
              title="Keep platform health, review risk, and rollout signals in one place"
              description="Start from the highest-signal summaries, then move directly into the workstream that needs attention."
              highlights={[
                {
                  icon: Activity,
                  label: 'Quality window',
                  value: dashboard.freshness.qualityWindow,
                },
                {
                  icon: CalendarClock,
                  label: 'Last sync',
                  value: new Date(dashboard.generatedAt).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' }),
                },
                {
                  icon: Users,
                  label: 'Active subscribers',
                  value: dashboard.billingRisk.activeSubscribers.toLocaleString(),
                },
              ]}
              aside={(
                <div className="space-y-4 rounded-2xl border border-border bg-background-light p-4 shadow-sm">
                  <div>
                    <p className="text-xs font-semibold uppercase tracking-[0.16em] text-muted">Quick Actions</p>
                    <p className="mt-1 text-sm text-muted">Move straight into the admin areas that typically need action first.</p>
                  </div>
                  <div className="space-y-2">
                    <AdminQuickAction href="/admin/review-ops" label="Open Review Ops" variant="primary" />
                    <AdminQuickAction href="/admin/freeze" label="Open Freeze Center" />
                    <AdminQuickAction href="/admin/content" label="Open Content Library" />
                    <AdminQuickAction href="/admin/business-intelligence" label="Open BI Dashboard" />
                  </div>
                  <AdminRouteFreshnessBadge value={dashboard.generatedAt} />
                </div>
              )}
            />

            <div className="grid gap-4 md:grid-cols-2 xl:grid-cols-4">
              <AdminRouteSummaryCard label="Published Content" value={dashboard.contentHealth.published} hint={`${dashboard.contentHealth.drafts} drafts still in flight`} icon={<FileText className="h-5 w-5" />} />
              <AdminRouteSummaryCard label="Review Backlog" value={dashboard.reviewOps.backlog} hint={`${dashboard.reviewOps.overdue} already overdue`} icon={<Inbox className="h-5 w-5" />} tone={dashboard.reviewOps.overdue > 0 ? 'warning' : 'default'} />
              <AdminRouteSummaryCard label="Billing Risk" value={dashboard.billingRisk.failedInvoices} hint={`${dashboard.billingRisk.pendingInvoices} pending invoices`} icon={<CreditCard className="h-5 w-5" />} tone={dashboard.billingRisk.failedInvoices > 0 ? 'danger' : 'default'} />
              <AdminRouteSummaryCard label="Agreement Rate" value={`${dashboard.quality.agreementRate}%`} hint={`${dashboard.quality.evaluationCount} evaluations in window`} icon={<BarChart3 className="h-5 w-5" />} />
            </div>

            <div className="grid gap-6 lg:grid-cols-2">
              <AdminRoutePanel
                title="Content Health"
                description="Surface stale drafts before they become invisible delivery debt."
                actions={<Link href="/admin/content" className="text-sm font-medium text-primary hover:text-primary-dark hover:underline">Open content library</Link>}
              >
                <div className="grid gap-3 sm:grid-cols-3">
                  <MetricTile icon={FileText} label="Drafts" value={dashboard.contentHealth.drafts} tone={dashboard.contentHealth.drafts > 0 ? 'warning' : 'default'} />
                  <MetricTile icon={Archive} label="Archived" value={dashboard.contentHealth.archived} />
                  <MetricTile icon={Clock4} label="Stale Drafts" value={dashboard.contentHealth.staleDrafts} tone={dashboard.contentHealth.staleDrafts > 0 ? 'danger' : 'success'} />
                </div>
              </AdminRoutePanel>

              <AdminRoutePanel
                title="Review Risk"
                description="Keep the productive-skill review pipeline honest and visible."
                actions={<Link href="/admin/review-ops" className="text-sm font-medium text-primary hover:text-primary-dark hover:underline">Open review ops</Link>}
              >
                <div className="grid gap-3 sm:grid-cols-3">
                  <MetricTile icon={PlayCircle} label="In Progress" value={dashboard.reviewOps.inProgress} tone={dashboard.reviewOps.inProgress > 0 ? 'default' : 'success'} />
                  <MetricTile icon={XCircle} label="Failed Reviews" value={dashboard.reviewOps.failedReviews} tone={dashboard.reviewOps.failedReviews > 0 ? 'danger' : 'success'} />
                  <MetricTile icon={AlertTriangle} label="Failed Jobs" value={dashboard.reviewOps.failedJobs} tone={dashboard.reviewOps.failedJobs > 0 ? 'danger' : 'success'} />
                </div>
              </AdminRoutePanel>
            </div>

            <div className="grid gap-6 lg:grid-cols-3">
              <AdminRoutePanel title="Billing" description="Subscription exposure and legacy plan drag.">
                <div className="grid gap-3">
                  <MetricTile icon={Briefcase} label="Legacy Plans" value={dashboard.billingRisk.legacyPlans} tone={dashboard.billingRisk.legacyPlans > 0 ? 'warning' : 'success'} />
                  <MetricTile icon={Users} label="Active Subscribers" value={dashboard.billingRisk.activeSubscribers.toLocaleString()} tone="success" />
                </div>
              </AdminRoutePanel>

              <AdminRoutePanel title="Feature Flags" description="Rollout footprint and recent changes.">
                <div className="grid gap-3">
                  <MetricTile icon={Flag} label="Enabled / Total" value={`${dashboard.flags.enabled} / ${dashboard.flags.total}`} />
                  <div className="grid grid-cols-2 gap-3">
                    <MetricTile icon={Zap} label="Live experiments" value={dashboard.flags.liveExperiments} />
                    <MetricTile icon={CalendarClock} label="Changed (7d)" value={dashboard.flags.recentChanges} />
                  </div>
                </div>
              </AdminRoutePanel>

              <AdminRoutePanel title="Quality Risk" description="Signals that need closer QA review.">
                <div className="grid gap-3">
                  <MetricTile
                    icon={dashboard.quality.riskCases > 0 ? AlertTriangle : CheckCircle2}
                    label="Combined risk cases"
                    value={dashboard.quality.riskCases}
                    tone={dashboard.quality.riskCases > 0 ? 'danger' : 'success'}
                  />
                  <MetricTile icon={Shield} label="Agreement rate" value={`${dashboard.quality.agreementRate}%`} />
                </div>
                <p className="mt-3 text-xs text-muted">Failed jobs and failed review cases in the current operational view.</p>
              </AdminRoutePanel>
            </div>

            <AdminRoutePanel title="Operational Shortcuts" description="Jump straight into the admin workstreams that need action.">
              <div className="grid gap-3 md:grid-cols-2 xl:grid-cols-4">
                {[
                  { href: '/admin/content', label: 'Content Hub' },
                  { href: '/admin/review-ops', label: 'Review Ops' },
                  { href: '/admin/freeze', label: 'Subscription Freezes' },
                  { href: '/admin/billing', label: 'Billing Ops' },
                  { href: '/admin/business-intelligence', label: 'Business Intelligence' },
                  { href: '/admin/analytics/quality', label: 'Quality Analytics' },
                  { href: '/admin/analytics/reading', label: 'Reading Analytics' },
                ].map((link) => (
                  <AdminQuickAction key={link.href} href={link.href} label={link.label} />
                ))}
              </div>
            </AdminRoutePanel>
          </div>
        ) : null}
      </AsyncStateWrapper>
    </AdminRouteWorkspace>
  );
}
