'use client';

import { useEffect, useState } from 'react';
import Link from 'next/link';
import {
  Activity,
  AlertTriangle,
  BarChart3,
  Clock4,
  CreditCard,
  FileText,
  Flag,
  Inbox,
  Shield,
  Sparkles,
  TrendingUp,
  Users,
  Zap,
} from 'lucide-react';
import { AdminRouteWorkspace } from '@/components/domain/admin-route-surface';
import { AsyncStateWrapper } from '@/components/state/async-state-wrapper';
import { useAdminAuth } from '@/lib/hooks/use-admin-auth';
import { getAdminDashboardData } from '@/lib/admin';
import type { AdminDashboardData } from '@/lib/types/admin';
import { cn } from '@/lib/utils';
import { StatusBadge, PulseTile, Panel, DenseTable, MetricGrid2x2, type MetricTone } from '@/components/admin/ui';

/* ── Main page ────────────────────────────────────────────────────────── */
export default function AdminDashboardPage() {
  const { isAuthenticated, role } = useAdminAuth();
  const [status, setStatus] = useState<'loading' | 'success' | 'error'>('loading');
  const [d, setD] = useState<AdminDashboardData | null>(null);

  useEffect(() => {
    let cancelled = false;
    getAdminDashboardData()
      .then((data) => { if (!cancelled) { setD(data); setStatus('success'); } })
      .catch(() => { if (!cancelled) setStatus('error'); });
    return () => { cancelled = true; };
  }, []);

  if (!isAuthenticated || role !== 'admin') return null;

  return (
    <AdminRouteWorkspace role="main" aria-label="Admin operations">
      <AsyncStateWrapper status={status} onRetry={() => window.location.reload()}>
        {d ? (
          <div className="flex flex-col gap-3">

            {/* ── Command strip ─────────────────────────────────────── */}
            <div className="flex items-center justify-between gap-3 rounded-xl border border-zinc-800 bg-zinc-950 px-5 py-3.5 shadow-sm">
              <div className="flex items-center gap-3 min-w-0">
                <div className="rounded-lg bg-violet-500/20 p-2 shrink-0">
                  <Sparkles className="h-4 w-4 text-violet-400" />
                </div>
                <div className="min-w-0">
                  <p className="text-[11px] font-extrabold uppercase tracking-[0.2em] text-violet-400 leading-none">Operations Center</p>
                  <p className="text-[11px] text-zinc-400 leading-none mt-1 truncate">Platform health · review risk · rollout signals</p>
                </div>
              </div>
              <div className="flex items-center gap-2.5 shrink-0 text-[10px] font-bold uppercase tracking-widest">
                <span className="text-zinc-400">Q·{d.freshness.qualityWindow}</span>
                <span className="text-zinc-700">|</span>
                <span className="text-zinc-400">{new Date(d.generatedAt).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' })}</span>
                <span className="text-zinc-700">|</span>
                <span className="flex items-center gap-1.5 text-emerald-400">
                  <span className="h-2 w-2 rounded-full bg-emerald-400 animate-pulse inline-block" />
                  Live
                </span>
              </div>
            </div>

            {/* ── KPI pulse bar ─────────────────────────────────────── */}
            <div className="grid grid-cols-2 sm:grid-cols-3 lg:grid-cols-6 gap-3">
              <PulseTile label="Backlog"     value={d.reviewOps.backlog}                            tone={d.reviewOps.backlog > 0 ? 'warning' : 'default'} icon={Inbox} />
              <PulseTile label="Overdue"     value={d.reviewOps.overdue}                            tone={d.reviewOps.overdue > 0 ? 'danger' : 'success'} icon={AlertTriangle} />
              <PulseTile label="Active Subs" value={d.billingRisk.activeSubscribers.toLocaleString()} tone="info"    icon={Users} />
              <PulseTile label="Agreement"  value={`${d.quality.agreementRate}%`}                  tone={d.quality.agreementRate >= 80 ? 'success' : d.quality.agreementRate >= 65 ? 'warning' : 'danger'} icon={Shield} />
              <PulseTile label="Risk Cases"  value={d.quality.riskCases}                            tone={d.quality.riskCases > 0 ? 'danger' : 'success'} icon={TrendingUp} />
              <PulseTile label="Live Flags"  value={d.flags.liveExperiments}                        tone={d.flags.liveExperiments > 0 ? 'purple' : 'default'} icon={Flag} />
            </div>

            {/* ── Main grid: left (tables) + right rail ─────────────── */}
            <div className="grid grid-cols-1 lg:grid-cols-[1fr_380px] gap-3">

              {/* Left — two dense tables stacked */}
              <div className="flex flex-col gap-3">

                {/* Review Operations */}
                <Panel title="Review Operations" icon={Inbox} href="/admin/review-ops">
                  <DenseTable
                    cols={['Workstream', 'Status', 'Vol']}
                    rows={[
                      [
                        <span key="bl" className="font-semibold text-zinc-800 dark:text-zinc-200">Backlog</span>,
                        <StatusBadge key="bls" tone={d.reviewOps.backlog > 0 ? 'warning' : 'default'} label={d.reviewOps.backlog > 0 ? 'Action Req' : 'Normal'} />,
                        <span key="blv" className={cn('font-mono font-bold tabular-nums', d.reviewOps.backlog > 0 ? 'text-amber-600 dark:text-amber-400' : 'text-zinc-500 dark:text-zinc-400')}>{d.reviewOps.backlog}</span>,
                      ],
                      [
                        <span key="ov" className="font-semibold text-zinc-800 dark:text-zinc-200">Overdue</span>,
                        <StatusBadge key="ovs" tone={d.reviewOps.overdue > 0 ? 'danger' : 'success'} label={d.reviewOps.overdue > 0 ? 'Critical' : 'Clear'} />,
                        <span key="ovv" className={cn('font-mono font-bold tabular-nums', d.reviewOps.overdue > 0 ? 'text-rose-600 dark:text-rose-400' : 'text-emerald-600 dark:text-emerald-400')}>{d.reviewOps.overdue}</span>,
                      ],
                      [
                        <span key="ip" className="font-semibold text-zinc-800 dark:text-zinc-200">In Progress</span>,
                        <StatusBadge key="ips" tone="info" label="Active" />,
                        <span key="ipv" className="font-mono text-zinc-500 dark:text-zinc-400 tabular-nums">{d.reviewOps.inProgress}</span>,
                      ],
                      [
                        <span key="fr" className="font-semibold text-zinc-800 dark:text-zinc-200">Failed Reviews</span>,
                        <StatusBadge key="frs" tone={d.reviewOps.failedReviews > 0 ? 'danger' : 'success'} label={d.reviewOps.failedReviews > 0 ? 'Failing' : 'Clear'} />,
                        <span key="frv" className={cn('font-mono font-bold tabular-nums', d.reviewOps.failedReviews > 0 ? 'text-rose-600 dark:text-rose-400' : 'text-emerald-600 dark:text-emerald-400')}>{d.reviewOps.failedReviews}</span>,
                      ],
                      [
                        <span key="fj" className="font-semibold text-zinc-800 dark:text-zinc-200">Failed Jobs</span>,
                        <StatusBadge key="fjs" tone={d.reviewOps.failedJobs > 0 ? 'danger' : 'success'} label={d.reviewOps.failedJobs > 0 ? 'Failing' : 'Healthy'} />,
                        <span key="fjv" className={cn('font-mono font-bold tabular-nums', d.reviewOps.failedJobs > 0 ? 'text-rose-600 dark:text-rose-400' : 'text-emerald-600 dark:text-emerald-400')}>{d.reviewOps.failedJobs}</span>,
                      ],
                    ]}
                  />
                </Panel>

                {/* Billing Risk & Subscriptions */}
                <Panel title="Billing Risk & Subscriptions" icon={CreditCard} href="/admin/billing">
                  <DenseTable
                    cols={['Category', 'Status', 'Count']}
                    rows={[
                      [
                        <span key="as" className="font-semibold text-zinc-800 dark:text-zinc-200">Active Subscribers</span>,
                        <StatusBadge key="ass" tone="success" label="Stable" />,
                        <span key="asv" className="font-mono font-bold text-emerald-600 dark:text-emerald-400 tabular-nums">{d.billingRisk.activeSubscribers.toLocaleString()}</span>,
                      ],
                      [
                        <span key="pi" className="font-semibold text-zinc-800 dark:text-zinc-200">Pending Invoices</span>,
                        <StatusBadge key="pis" tone={d.billingRisk.pendingInvoices > 0 ? 'warning' : 'success'} label={d.billingRisk.pendingInvoices > 0 ? 'Pending' : 'Clear'} />,
                        <span key="piv" className={cn('font-mono font-bold tabular-nums', d.billingRisk.pendingInvoices > 0 ? 'text-amber-600 dark:text-amber-400' : 'text-emerald-600 dark:text-emerald-400')}>{d.billingRisk.pendingInvoices}</span>,
                      ],
                      [
                        <span key="fi" className="font-semibold text-zinc-800 dark:text-zinc-200">Failed Invoices</span>,
                        <StatusBadge key="fis" tone={d.billingRisk.failedInvoices > 0 ? 'danger' : 'success'} label={d.billingRisk.failedInvoices > 0 ? 'At Risk' : 'Clear'} />,
                        <span key="fiv" className={cn('font-mono font-bold tabular-nums', d.billingRisk.failedInvoices > 0 ? 'text-rose-600 dark:text-rose-400' : 'text-emerald-600 dark:text-emerald-400')}>{d.billingRisk.failedInvoices}</span>,
                      ],
                      [
                        <span key="lp" className="font-semibold text-zinc-800 dark:text-zinc-200">Legacy Plans</span>,
                        <StatusBadge key="lps" tone="warning" label="Deprecating" />,
                        <span key="lpv" className="font-mono text-amber-600 dark:text-amber-400 tabular-nums">{d.billingRisk.legacyPlans}</span>,
                      ],
                    ]}
                  />
                </Panel>

              </div>

              {/* Right rail — four compact panels */}
              <div className="flex flex-col gap-3">

                {/* Content Health + Quality side by side */}
                <div className="grid grid-cols-2 gap-3">
                  <Panel title="Content" icon={FileText} href="/admin/content">
                    <MetricGrid2x2
                      items={[
                        { label: 'Published',    value: d.contentHealth.published,    tone: 'success' },
                        { label: 'Drafts',       value: d.contentHealth.drafts,       tone: d.contentHealth.drafts > 0 ? 'info' : 'default' },
                        { label: 'Archived',     value: d.contentHealth.archived,     tone: 'default' },
                        { label: 'Stale Drafts', value: d.contentHealth.staleDrafts,  tone: d.contentHealth.staleDrafts > 0 ? 'warning' : 'default' },
                      ]}
                    />
                  </Panel>

                  <Panel title="Quality" icon={Shield} href="/admin/analytics/quality">
                    <MetricGrid2x2
                      items={[
                        { label: 'Agreement',  value: `${d.quality.agreementRate}%`,  tone: d.quality.agreementRate >= 80 ? 'success' : d.quality.agreementRate >= 65 ? 'warning' : 'danger' },
                        { label: 'Evals',      value: d.quality.evaluationCount,      tone: 'info' },
                        { label: 'Avg Review', value: `${d.quality.avgReviewHours}h`, tone: 'default' },
                        { label: 'Risk Cases', value: d.quality.riskCases,            tone: d.quality.riskCases > 0 ? 'danger' : 'success' },
                      ]}
                    />
                  </Panel>
                </div>

                {/* Feature Flags */}
                <Panel title="Feature Flags" icon={Flag} href="/admin/flags">
                  <MetricGrid2x2
                    items={[
                      { label: 'Total',       value: d.flags.total,            tone: 'default' },
                      { label: 'Enabled',     value: d.flags.enabled,          tone: 'success' },
                      { label: 'Live Exps',   value: d.flags.liveExperiments,  tone: d.flags.liveExperiments > 0 ? 'purple' : 'default' },
                      { label: 'Changes 24h', value: d.flags.recentChanges,    tone: d.flags.recentChanges > 0 ? 'warning' : 'default' },
                    ]}
                  />
                </Panel>

                {/* Quick Actions */}
                <Panel title="Quick Actions" icon={Zap}>
                  <div className="grid grid-cols-3">
                    {([
                      { href: '/admin/content',               label: 'Content Hub', icon: FileText,  color: 'text-blue-500',   bg: 'hover:bg-blue-50   dark:hover:bg-blue-950/30' },
                      { href: '/admin/review-ops',            label: 'Review Ops',  icon: Inbox,     color: 'text-violet-500', bg: 'hover:bg-violet-50 dark:hover:bg-violet-950/30' },
                      { href: '/admin/freeze',                label: 'Freezes',     icon: Clock4,    color: 'text-cyan-500',   bg: 'hover:bg-cyan-50   dark:hover:bg-cyan-950/30' },
                      { href: '/admin/billing',               label: 'Billing',     icon: CreditCard, color: 'text-emerald-500', bg: 'hover:bg-emerald-50 dark:hover:bg-emerald-950/30' },
                      { href: '/admin/business-intelligence', label: 'BI',          icon: BarChart3, color: 'text-amber-500',  bg: 'hover:bg-amber-50  dark:hover:bg-amber-950/30' },
                      { href: '/admin/analytics/quality',     label: 'Quality',     icon: Shield,    color: 'text-rose-500',   bg: 'hover:bg-rose-50   dark:hover:bg-rose-950/30' },
                    ] as const).map((a, i) => (
                      <Link
                        key={a.href}
                        href={a.href}
                        className={cn(
                          'flex flex-col items-center justify-center gap-2 py-4 transition-colors group',
                          i % 3 !== 2 && 'border-r border-zinc-100 dark:border-zinc-800/50',
                          i < 3 && 'border-b border-zinc-100 dark:border-zinc-800/50',
                          a.bg,
                        )}
                      >
                        <a.icon className={cn('h-5 w-5 transition-transform group-hover:scale-110', a.color)} />
                        <span className="text-[10px] font-extrabold uppercase tracking-[0.15em] text-zinc-600 dark:text-zinc-400 group-hover:text-zinc-900 dark:group-hover:text-zinc-100 transition-colors leading-none">{a.label}</span>
                      </Link>
                    ))}
                  </div>
                </Panel>

              </div>
            </div>

          </div>
        ) : null}
      </AsyncStateWrapper>
    </AdminRouteWorkspace>
  );
}
