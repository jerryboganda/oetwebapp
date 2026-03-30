'use client';

import { useEffect, useState } from 'react';
import Link from 'next/link';
import { AlertTriangle, ArrowRight, BarChart3, CreditCard, FileText, Flag, Inbox } from 'lucide-react';
import { AdminFreshnessBadge, AdminMetricCard, AdminPageHeader, AdminSectionPanel } from '@/components/domain/admin-surface';
import { AsyncStateWrapper } from '@/components/state/async-state-wrapper';
import { useAdminAuth } from '@/lib/hooks/use-admin-auth';
import { getAdminDashboardData } from '@/lib/admin';
import type { AdminDashboardData } from '@/lib/types/admin';

type PageStatus = 'loading' | 'success' | 'error';

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
    <div className="max-w-7xl space-y-6">
      <AdminPageHeader
        title="Admin Operations"
        description="Monitor content health, review throughput, billing risk, quality signals, and rollout changes from one operational landing page."
        meta={dashboard ? `Quality window ${dashboard.freshness.qualityWindow}` : undefined}
        actions={<AdminFreshnessBadge value={dashboard?.generatedAt} />}
      />

      <AsyncStateWrapper status={pageStatus} onRetry={() => window.location.reload()}>
        {dashboard ? (
          <>
            <div className="grid gap-4 md:grid-cols-2 xl:grid-cols-4">
              <AdminMetricCard label="Published Content" value={dashboard.contentHealth.published} hint={`${dashboard.contentHealth.drafts} drafts still in flight`} icon={<FileText className="h-5 w-5" />} />
              <AdminMetricCard label="Review Backlog" value={dashboard.reviewOps.backlog} hint={`${dashboard.reviewOps.overdue} already overdue`} icon={<Inbox className="h-5 w-5" />} tone={dashboard.reviewOps.overdue > 0 ? 'warning' : 'default'} />
              <AdminMetricCard label="Billing Risk" value={dashboard.billingRisk.failedInvoices} hint={`${dashboard.billingRisk.pendingInvoices} pending invoices`} icon={<CreditCard className="h-5 w-5" />} tone={dashboard.billingRisk.failedInvoices > 0 ? 'danger' : 'default'} />
              <AdminMetricCard label="Agreement Rate" value={`${dashboard.quality.agreementRate}%`} hint={`${dashboard.quality.evaluationCount} evaluations in window`} icon={<BarChart3 className="h-5 w-5" />} />
            </div>

            <div className="grid gap-6 lg:grid-cols-2">
              <AdminSectionPanel
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
              </AdminSectionPanel>

              <AdminSectionPanel
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
              </AdminSectionPanel>
            </div>

            <div className="grid gap-6 lg:grid-cols-3">
              <AdminSectionPanel title="Billing" description="Subscription exposure and legacy plan drag.">
                <p className="text-sm text-slate-500">Legacy plans</p>
                <p className="text-2xl font-semibold text-slate-900">{dashboard.billingRisk.legacyPlans}</p>
                <p className="mt-3 text-sm text-slate-500">Active subscribers: {dashboard.billingRisk.activeSubscribers.toLocaleString()}</p>
              </AdminSectionPanel>

              <AdminSectionPanel title="Feature Flags" description="Rollout footprint and recent changes.">
                <p className="text-sm text-slate-500">Enabled / total</p>
                <p className="text-2xl font-semibold text-slate-900">
                  {dashboard.flags.enabled} / {dashboard.flags.total}
                </p>
                <p className="mt-3 text-sm text-slate-500">Live experiments: {dashboard.flags.liveExperiments}</p>
                <p className="text-sm text-slate-500">Changed in 7 days: {dashboard.flags.recentChanges}</p>
              </AdminSectionPanel>

              <AdminSectionPanel title="Quality Risk" description="Signals that need closer QA review.">
                <div className="flex items-start gap-3">
                  <AlertTriangle className="mt-1 h-5 w-5 text-amber-500" />
                  <div>
                    <p className="text-2xl font-semibold text-slate-900">{dashboard.quality.riskCases}</p>
                    <p className="text-sm text-slate-500">Combined failed jobs and failed review cases in the current operational view.</p>
                  </div>
                </div>
              </AdminSectionPanel>
            </div>

            <AdminSectionPanel title="Operational Shortcuts" description="Jump straight into the admin workstreams that need action.">
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
            </AdminSectionPanel>
          </>
        ) : null}
      </AsyncStateWrapper>
    </div>
  );
}
