'use client';

import { useEffect, useState } from 'react';
import Link from 'next/link';
import { useRouter } from 'next/navigation';
import {
  AlertTriangle,
  ArrowRight,
  BarChart3,
  CreditCard,
  FileText,
  Inbox,
  Sparkles,
} from 'lucide-react';
import { CardLink } from '@/components/ui';
import { Button } from '@/components/ui/button';
import {
  AdminRouteFreshnessBadge,
  AdminRouteHero,
  AdminRoutePanel,
  AdminRoutePanelFooter,
  AdminRouteStatRow,
  AdminRouteSummaryCard,
  AdminRouteWorkspace,
} from '@/components/domain/admin-route-surface';
import { AsyncStateWrapper } from '@/components/state/async-state-wrapper';
import { useAdminAuth } from '@/lib/hooks/use-admin-auth';
import { getAdminDashboardData } from '@/lib/admin';
import type { AdminDashboardData } from '@/lib/types/admin';

type PageStatus = 'loading' | 'success' | 'error';

const SHORTCUT_LINKS = [
  { href: '/admin/content', label: 'Content Library', hint: 'Papers, drafts, archive' },
  { href: '/admin/review-ops', label: 'Review Ops', hint: 'Backlog, assignments' },
  { href: '/admin/freeze', label: 'Freeze Center', hint: 'Read-only accounts' },
  { href: '/admin/billing', label: 'Billing Ops', hint: 'Invoices, subs' },
  { href: '/admin/business-intelligence', label: 'Business Intelligence', hint: 'Cohorts, revenue' },
  { href: '/admin/analytics/quality', label: 'Quality Analytics', hint: 'Agreement, calibration' },
  { href: '/admin/ai-usage', label: 'AI Usage & Budget', hint: 'Quotas, BYOK' },
  { href: '/admin/content-papers', label: 'Content Papers', hint: 'Structured papers' },
];

export default function AdminDashboardPage() {
  const router = useRouter();
  const { isAuthenticated, role } = useAdminAuth();
  const [pageStatus, setPageStatus] = useState<PageStatus>('loading');
  const [dashboard, setDashboard] = useState<AdminDashboardData | null>(null);

  useEffect(() => {
    let cancelled = false;
    (async () => {
      setPageStatus('loading');
      try {
        const summary = await getAdminDashboardData();
        if (!cancelled) {
          setDashboard(summary);
          setPageStatus('success');
        }
      } catch (error) {
        console.error(error);
        if (!cancelled) setPageStatus('error');
      }
    })();
    return () => {
      cancelled = true;
    };
  }, []);

  if (!isAuthenticated || role !== 'admin') return null;

  return (
    <AdminRouteWorkspace role="main" aria-label="Admin operations">
      <AsyncStateWrapper status={pageStatus} onRetry={() => window.location.reload()}>
        {dashboard ? (
          <>
            {/* Hero — highlights mirror learner dashboard chip rhythm. */}
            <AdminRouteHero
              eyebrow="Operational Control"
              icon={Sparkles}
              accent="navy"
              title="Platform health, review risk, and rollout in one place"
              description="Start from the highest-signal summaries, then move directly into the workstream that needs attention."
              highlights={[
                {
                  icon: Inbox,
                  label: 'Review backlog',
                  value: `${dashboard.reviewOps.backlog} items · ${dashboard.reviewOps.overdue} overdue`,
                },
                {
                  icon: CreditCard,
                  label: 'Billing risk',
                  value: `${dashboard.billingRisk.failedInvoices} failed · ${dashboard.billingRisk.pendingInvoices} pending`,
                },
                {
                  icon: BarChart3,
                  label: 'Agreement rate',
                  value: `${dashboard.quality.agreementRate}% (${dashboard.quality.evaluationCount})`,
                },
              ]}
              aside={
                <div className="space-y-4 rounded-2xl border border-border bg-background-light p-4 shadow-sm">
                  <div className="space-y-1">
                    <p className="text-[11px] font-semibold uppercase tracking-[0.18em] text-muted">
                      Quick Actions
                    </p>
                    <p className="text-sm text-muted">
                      Jump straight into the admin areas that typically need action first.
                    </p>
                  </div>
                  <div className="space-y-2">
                    <Button fullWidth onClick={() => router.push('/admin/review-ops')}>
                      Open Review Ops
                    </Button>
                    <Button fullWidth variant="outline" onClick={() => router.push('/admin/freeze')}>
                      Open Freeze Center
                    </Button>
                    <Button fullWidth variant="outline" onClick={() => router.push('/admin/business-intelligence')}>
                      Open BI Dashboard
                    </Button>
                  </div>
                  <div className="flex items-center justify-between gap-3 border-t border-border pt-3 text-xs text-muted">
                    <AdminRouteFreshnessBadge value={dashboard.generatedAt} />
                    <span>Window {dashboard.freshness.qualityWindow}</span>
                  </div>
                </div>
              }
            />

            {/* Top KPIs. */}
            <div className="grid gap-4 md:grid-cols-2 xl:grid-cols-4">
              <AdminRouteSummaryCard
                label="Published Content"
                value={dashboard.contentHealth.published}
                hint={`${dashboard.contentHealth.drafts} drafts still in flight`}
                icon={<FileText className="h-5 w-5" />}
              />
              <AdminRouteSummaryCard
                label="Review Backlog"
                value={dashboard.reviewOps.backlog}
                hint={`${dashboard.reviewOps.overdue} overdue`}
                icon={<Inbox className="h-5 w-5" />}
                tone={dashboard.reviewOps.overdue > 0 ? 'warning' : 'default'}
              />
              <AdminRouteSummaryCard
                label="Billing Risk"
                value={dashboard.billingRisk.failedInvoices}
                hint={`${dashboard.billingRisk.pendingInvoices} pending invoices`}
                icon={<CreditCard className="h-5 w-5" />}
                tone={dashboard.billingRisk.failedInvoices > 0 ? 'danger' : 'default'}
              />
              <AdminRouteSummaryCard
                label="Agreement Rate"
                value={`${dashboard.quality.agreementRate}%`}
                hint={`${dashboard.quality.evaluationCount} evaluations in window`}
                icon={<BarChart3 className="h-5 w-5" />}
                tone="info"
              />
            </div>

            {/* Content Health + Review Risk side-by-side. */}
            <div className="grid gap-6 lg:grid-cols-2">
              <AdminRoutePanel
                eyebrow="Content"
                title="Content Health"
                description="Surface stale drafts before they become invisible delivery debt."
                actions={
                  <Link
                    href="/admin/content"
                    className="inline-flex items-center gap-1 text-sm font-medium text-primary hover:text-primary-dark hover:underline"
                  >
                    Open content library
                    <ArrowRight className="h-3.5 w-3.5" aria-hidden />
                  </Link>
                }
              >
                <AdminRouteStatRow
                  items={[
                    { label: 'Drafts', value: dashboard.contentHealth.drafts },
                    { label: 'Archived', value: dashboard.contentHealth.archived },
                    {
                      label: 'Stale drafts',
                      value: dashboard.contentHealth.staleDrafts,
                      tone: dashboard.contentHealth.staleDrafts > 0 ? 'warning' : 'default',
                    },
                  ]}
                />
                <AdminRoutePanelFooter
                  updatedAt={dashboard.freshness.contentUpdatedAt}
                  source="Content pipeline"
                />
              </AdminRoutePanel>

              <AdminRoutePanel
                eyebrow="Review"
                title="Review Risk"
                description="Keep the productive-skill review pipeline honest and visible."
                actions={
                  <Link
                    href="/admin/review-ops"
                    className="inline-flex items-center gap-1 text-sm font-medium text-primary hover:text-primary-dark hover:underline"
                  >
                    Open review ops
                    <ArrowRight className="h-3.5 w-3.5" aria-hidden />
                  </Link>
                }
              >
                <AdminRouteStatRow
                  items={[
                    { label: 'In progress', value: dashboard.reviewOps.inProgress },
                    {
                      label: 'Failed reviews',
                      value: dashboard.reviewOps.failedReviews,
                      tone: dashboard.reviewOps.failedReviews > 0 ? 'danger' : 'default',
                    },
                    {
                      label: 'Failed jobs',
                      value: dashboard.reviewOps.failedJobs,
                      tone: dashboard.reviewOps.failedJobs > 0 ? 'danger' : 'default',
                    },
                  ]}
                />
                <AdminRoutePanelFooter
                  updatedAt={dashboard.freshness.reviewUpdatedAt}
                  source="Review events"
                />
              </AdminRoutePanel>
            </div>

            {/* Three-up support strip. */}
            <div className="grid gap-6 lg:grid-cols-3">
              <AdminRoutePanel eyebrow="Billing" title="Subscription exposure">
                <AdminRouteStatRow
                  items={[
                    { label: 'Legacy plans', value: dashboard.billingRisk.legacyPlans },
                    {
                      label: 'Active subs',
                      value: dashboard.billingRisk.activeSubscribers.toLocaleString(),
                    },
                  ]}
                />
              </AdminRoutePanel>

              <AdminRoutePanel eyebrow="Feature flags" title="Rollout footprint">
                <AdminRouteStatRow
                  items={[
                    { label: 'Enabled', value: `${dashboard.flags.enabled}/${dashboard.flags.total}` },
                    { label: 'Live experiments', value: dashboard.flags.liveExperiments },
                    { label: 'Changed (7d)', value: dashboard.flags.recentChanges },
                  ]}
                />
              </AdminRoutePanel>

              <AdminRoutePanel eyebrow="Quality" title="Risk signals">
                <div className="flex items-start gap-3">
                  <span className="inline-flex h-9 w-9 flex-shrink-0 items-center justify-center rounded-full bg-amber-50 text-amber-700">
                    <AlertTriangle className="h-5 w-5" aria-hidden />
                  </span>
                  <div className="space-y-1">
                    <p className="text-2xl font-bold text-navy leading-tight">
                      {dashboard.quality.riskCases}
                    </p>
                    <p className="text-sm text-muted leading-snug">
                      Combined failed jobs and failed review cases in the current operational view.
                    </p>
                  </div>
                </div>
              </AdminRoutePanel>
            </div>

            {/* Full shortcut grid. */}
            <AdminRoutePanel
              eyebrow="Shortcuts"
              title="Operational shortcuts"
              description="Jump straight into the admin workstreams that need action."
            >
              <div className="grid gap-3 md:grid-cols-2 xl:grid-cols-4">
                {SHORTCUT_LINKS.map((link) => (
                  <CardLink key={link.href} href={link.href} padding="sm">
                    <div className="flex items-center justify-between gap-3">
                      <div className="min-w-0">
                        <p className="text-sm font-semibold text-navy truncate">{link.label}</p>
                        <p className="mt-0.5 text-xs text-muted truncate">{link.hint}</p>
                      </div>
                      <ArrowRight className="h-4 w-4 shrink-0 text-muted" aria-hidden />
                    </div>
                  </CardLink>
                ))}
              </div>
            </AdminRoutePanel>
          </>
        ) : null}
      </AsyncStateWrapper>
    </AdminRouteWorkspace>
  );
}
