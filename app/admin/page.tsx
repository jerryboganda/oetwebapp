'use client';

import { useEffect, useState } from 'react';
import Link from 'next/link';
import { AlertTriangle, ArrowRight, BarChart3, CreditCard, FileText, Flag, Inbox, Sparkles } from 'lucide-react';
import { AdminRouteFreshnessBadge, AdminRouteHero, AdminRoutePanel, AdminRouteSummaryCard, AdminRouteWorkspace } from '@/components/domain/admin-route-surface';
import { AsyncStateWrapper } from '@/components/state/async-state-wrapper';
import { useAdminAuth } from '@/lib/hooks/use-admin-auth';
import { getAdminDashboardData } from '@/lib/admin';
import type { AdminDashboardData } from '@/lib/types/admin';

type PageStatus = 'loading' | 'success' | 'error';

export default function AdminDashboardPage() {
  const { isAuthenticated, role } = useAdminAuth();
  const [pageStatus, setPageStatus] = useState<PageStatus>('loading');
  const [dashboard, setDashboard] = useState<AdminDashboardData | null>(null);
  const quickActionLinkClassName = 'inline-flex w-full items-center justify-center rounded-lg px-5 py-2 text-sm font-medium transition-[background-color,border-color,color,box-shadow,transform,opacity] duration-200 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary focus-visible:ring-offset-2';

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
          <>
            <AdminRouteHero
              eyebrow="Operational Control"
              icon={Sparkles}
              accent="navy"
              title="Keep platform health, review risk, and rollout signals in one place"
              description="Use the admin console with the same visual hierarchy as the learner dashboard: start from the highest-signal summaries, then move directly into the workstream that needs attention."
              highlights={[
                { icon: FileText, label: 'Published content', value: String(dashboard.contentHealth.published) },
                { icon: Inbox, label: 'Backlog at risk', value: String(dashboard.reviewOps.overdue) },
                { icon: BarChart3, label: 'Agreement rate', value: `${dashboard.quality.agreementRate}%` },
              ]}
              aside={(
                <div className="space-y-4 rounded-2xl border border-gray-200 bg-background-light p-4 shadow-sm">
                  <div>
                    <p className="text-xs font-semibold uppercase tracking-[0.16em] text-muted">Quick Actions</p>
                    <p className="mt-1 text-sm text-muted">Move straight into the admin areas that typically need action first.</p>
                  </div>
                  <div className="space-y-2">
                    <Link href="/admin/review-ops" className={`${quickActionLinkClassName} bg-primary text-white shadow-sm hover:bg-primary/90`}>
                      Open Review Ops
                    </Link>
                    <Link href="/admin/content" className={`${quickActionLinkClassName} border border-gray-300 text-navy hover:bg-gray-50`}>
                      Open Content Library
                    </Link>
                  </div>
                  <div className="space-y-1 text-xs text-muted">
                    <AdminRouteFreshnessBadge value={dashboard.generatedAt} />
                    <p>Quality window {dashboard.freshness.qualityWindow}</p>
                  </div>
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
                actions={<Link href="/admin/content" className="text-sm font-medium text-blue-600 hover:underline">Open content library</Link>}
              >
                <div className="grid gap-4 sm:grid-cols-3">
                  <div>
                    <p className="text-sm text-slate-500">Drafts</p>
                    <p className="text-xl font-semibold text-slate-900">{dashboard.contentHealth.drafts}</p>
                  </div>
                  <div>
                    <p className="text-sm text-slate-500">Archived</p>
                    <p className="text-xl font-semibold text-slate-900">{dashboard.contentHealth.archived}</p>
                  </div>
                  <div>
                    <p className="text-sm text-slate-500">Stale Drafts</p>
                    <p className="text-xl font-semibold text-slate-900">{dashboard.contentHealth.staleDrafts}</p>
                  </div>
                </div>
              </AdminRoutePanel>

              <AdminRoutePanel
                title="Review Risk"
                description="Keep the productive-skill review pipeline honest and visible."
                actions={<Link href="/admin/review-ops" className="text-sm font-medium text-blue-600 hover:underline">Open review ops</Link>}
              >
                <div className="grid gap-4 sm:grid-cols-3">
                  <div>
                    <p className="text-sm text-slate-500">In Progress</p>
                    <p className="text-xl font-semibold text-slate-900">{dashboard.reviewOps.inProgress}</p>
                  </div>
                  <div>
                    <p className="text-sm text-slate-500">Failed Reviews</p>
                    <p className="text-xl font-semibold text-slate-900">{dashboard.reviewOps.failedReviews}</p>
                  </div>
                  <div>
                    <p className="text-sm text-slate-500">Failed Jobs</p>
                    <p className="text-xl font-semibold text-slate-900">{dashboard.reviewOps.failedJobs}</p>
                  </div>
                </div>
              </AdminRoutePanel>
            </div>

            <div className="grid gap-6 lg:grid-cols-3">
              <AdminRoutePanel title="Billing" description="Subscription exposure and legacy plan drag.">
                <p className="text-sm text-slate-500">Legacy plans</p>
                <p className="text-2xl font-semibold text-slate-900">{dashboard.billingRisk.legacyPlans}</p>
                <p className="mt-3 text-sm text-slate-500">Active subscribers: {dashboard.billingRisk.activeSubscribers.toLocaleString()}</p>
              </AdminRoutePanel>

              <AdminRoutePanel title="Feature Flags" description="Rollout footprint and recent changes.">
                <p className="text-sm text-slate-500">Enabled / total</p>
                <p className="text-2xl font-semibold text-slate-900">
                  {dashboard.flags.enabled} / {dashboard.flags.total}
                </p>
                <p className="mt-3 text-sm text-slate-500">Live experiments: {dashboard.flags.liveExperiments}</p>
                <p className="text-sm text-slate-500">Changed in 7 days: {dashboard.flags.recentChanges}</p>
              </AdminRoutePanel>

              <AdminRoutePanel title="Quality Risk" description="Signals that need closer QA review.">
                <div className="flex items-start gap-3">
                  <AlertTriangle className="mt-1 h-5 w-5 text-amber-500" />
                  <div>
                    <p className="text-2xl font-semibold text-slate-900">{dashboard.quality.riskCases}</p>
                    <p className="text-sm text-slate-500">Combined failed jobs and failed review cases in the current operational view.</p>
                  </div>
                </div>
              </AdminRoutePanel>
            </div>

            <AdminRoutePanel title="Operational Shortcuts" description="Jump straight into the admin workstreams that need action.">
              <div className="grid gap-3 md:grid-cols-2 xl:grid-cols-4">
                {[
                  { href: '/admin/content', label: 'Content Library' },
                  { href: '/admin/review-ops', label: 'Review Ops' },
                  { href: '/admin/billing', label: 'Billing Ops' },
                  { href: '/admin/analytics/quality', label: 'Quality Analytics' },
                ].map((link) => (
                  <Link
                    key={link.href}
                    href={link.href}
                    className="inline-flex items-center justify-between gap-2 rounded-lg border border-gray-300 px-5 py-2 text-sm font-medium text-navy transition-all duration-200 hover:bg-gray-50"
                  >
                    {link.label}
                    <ArrowRight className="h-4 w-4" />
                  </Link>
                ))}
              </div>
            </AdminRoutePanel>
          </>
        ) : null}
      </AsyncStateWrapper>
    </AdminRouteWorkspace>
  );
}
