'use client';

/**
 * Admin Rulebook Conformance dashboard (read-only).
 *
 * Shows, per OET exam rulebook, exactly how every rule is enforced
 * (deterministic detector / forbidden-pattern / ai-grounded / human-review /
 * not-enforced) using the same browser-safe classifier the CI gate uses
 * (`lib/rulebook/coverage.ts`). This is reporting only — per decision 2 there
 * are NO publish/validate/mutation controls here; structural problems surface
 * as non-blocking warnings, never a publish block.
 */

import { useMemo, useState } from 'react';
import { ShieldCheck, AlertTriangle } from 'lucide-react';
import {
  buildConformanceReport,
  type ConformanceKindReport,
  type RuleEnforcementStatus,
} from '@/lib/rulebook';
import { cn } from '@/lib/utils';
import { AdminPageShell } from '@/components/admin/layout/admin-page-shell';
import { PageHeader } from '@/components/admin/ui/page-header';
import { KpiTile } from '@/components/admin/ui/kpi-tile';
import { StatusBadge } from '@/components/admin/ui/status-badge';
import type { MetricTone } from '@/components/admin/ui/types';

const STATUS_META: Record<RuleEnforcementStatus, { label: string; tone: MetricTone }> = {
  deterministic: { label: 'Deterministic', tone: 'success' },
  'forbidden-pattern': { label: 'Forbidden-pattern', tone: 'info' },
  'ai-grounded': { label: 'AI-grounded', tone: 'purple' },
  'human-review': { label: 'Human review', tone: 'warning' },
  'not-enforced': { label: 'Not enforced', tone: 'danger' },
};

const SEVERITY_TONE: Record<string, MetricTone> = {
  critical: 'danger',
  major: 'warning',
  minor: 'default',
  info: 'default',
};

export default function AdminConformancePage() {
  const report = useMemo<ConformanceKindReport[]>(() => buildConformanceReport(), []);
  const [activeKind, setActiveKind] = useState<string>(report[0]?.kind ?? '');

  const totals = useMemo(() => {
    const acc = { total: 0, deterministic: 0, forbiddenPattern: 0, aiGrounded: 0, humanReview: 0, unenforced: 0 };
    for (const r of report) {
      acc.total += r.summary.total;
      acc.deterministic += r.summary.byStatus.deterministic;
      acc.forbiddenPattern += r.summary.byStatus['forbidden-pattern'];
      acc.aiGrounded += r.summary.byStatus['ai-grounded'];
      acc.humanReview += r.summary.byStatus['human-review'];
      acc.unenforced += r.summary.unenforcedCriticalMajor;
    }
    return acc;
  }, [report]);

  const active = report.find((r) => r.kind === activeKind) ?? report[0];
  const allEnforced = totals.unenforced === 0;

  return (
    <AdminPageShell>
      <PageHeader
        eyebrow="Quality"
        title="Rulebook Conformance"
        description="How every rule in the four OET exam rulebooks is enforced. Read-only — the same classifier the rulebook-conformance CI gate uses."
      />

      <div
        className={cn(
          'flex items-start gap-3 rounded-admin-lg border p-4',
          allEnforced
            ? 'border-[var(--admin-success-tint-strong)] bg-[var(--admin-success-tint)]'
            : 'border-[var(--admin-danger-tint-strong)] bg-[var(--admin-danger-tint)]',
        )}
      >
        {allEnforced ? (
          <ShieldCheck className="mt-0.5 h-5 w-5 shrink-0 text-admin-success" aria-hidden="true" />
        ) : (
          <AlertTriangle className="mt-0.5 h-5 w-5 shrink-0 text-admin-danger" aria-hidden="true" />
        )}
        <p className="text-sm text-admin-fg-strong">
          {allEnforced ? (
            <>
              <span className="font-semibold">All {totals.total} rules have an asserted enforcement status.</span>{' '}
              Zero critical/major rules are silently unenforced across the four OET rulebooks.
            </>
          ) : (
            <>
              <span className="font-semibold">{totals.unenforced} critical/major rule(s) are NOT enforced.</span>{' '}
              These are surfaced as warnings — they do not block publishing.
            </>
          )}
        </p>
      </div>

      <div className="grid grid-cols-2 gap-3 sm:grid-cols-5">
        <KpiTile size="sm" label="Total rules" value={totals.total} />
        <KpiTile size="sm" label="Deterministic" value={totals.deterministic} tone="success" />
        <KpiTile size="sm" label="AI-grounded" value={totals.aiGrounded} tone="primary" />
        <KpiTile size="sm" label="Human review" value={totals.humanReview} />
        <KpiTile size="sm" label="Not enforced" value={totals.unenforced} tone={allEnforced ? 'default' : 'danger'} />
      </div>

      <div className="flex flex-wrap gap-2" role="tablist" aria-label="Rulebook modules">
        {report.map((r) => {
          const selected = r.kind === active?.kind;
          return (
            <button
              key={r.kind}
              type="button"
              role="tab"
              aria-selected={selected}
              onClick={() => setActiveKind(r.kind)}
              className={cn(
                'rounded-full px-3 py-1.5 text-sm font-medium transition-colors focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[var(--admin-primary)] focus-visible:ring-offset-2 focus-visible:ring-offset-[var(--admin-bg-page)]',
                selected
                  ? 'bg-admin-primary text-admin-primary-fg'
                  : 'bg-admin-bg-subtle text-admin-fg-muted hover:bg-[var(--admin-state-active)] hover:text-admin-fg-default',
              )}
            >
              {r.label}
              <span className="ml-1.5 text-xs tabular-nums opacity-75">{r.summary.total}</span>
            </button>
          );
        })}
      </div>

      {active ? (
        <div className="overflow-x-auto rounded-admin-lg border border-admin-border bg-admin-bg-surface shadow-admin-sm">
          <table className="w-full min-w-[40rem] text-left text-sm">
            <thead className="border-b border-admin-border bg-admin-bg-subtle text-xs uppercase tracking-wide text-admin-fg-muted">
              <tr>
                <th scope="col" className="px-4 py-2 font-semibold">Rule</th>
                <th scope="col" className="px-4 py-2 font-semibold">Section</th>
                <th scope="col" className="px-4 py-2 font-semibold">Severity</th>
                <th scope="col" className="px-4 py-2 font-semibold">Title</th>
                <th scope="col" className="px-4 py-2 font-semibold">Enforcement</th>
              </tr>
            </thead>
            <tbody>
              {active.rows.map((row) => (
                <tr key={row.ruleId} className="border-b border-admin-border last:border-0 hover:bg-[var(--admin-state-hover)]">
                  <td className="whitespace-nowrap px-4 py-2 font-mono text-xs text-admin-fg-strong">{row.ruleId}</td>
                  <td className="px-4 py-2 text-admin-fg-muted">{row.section}</td>
                  <td className="px-4 py-2">
                    <StatusBadge intensity="tinted" tone={SEVERITY_TONE[row.severity] ?? 'default'} label={row.severity} />
                  </td>
                  <td className="px-4 py-2 text-admin-fg-strong">{row.title}</td>
                  <td className="px-4 py-2">
                    <StatusBadge intensity="tinted" tone={STATUS_META[row.status].tone} label={STATUS_META[row.status].label} />
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      ) : null}
    </AdminPageShell>
  );
}
