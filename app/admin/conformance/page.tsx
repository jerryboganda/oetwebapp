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

const STATUS_META: Record<RuleEnforcementStatus, { label: string; className: string }> = {
  deterministic: { label: 'Deterministic', className: 'bg-emerald-50 text-emerald-700 ring-emerald-600/20' },
  'forbidden-pattern': { label: 'Forbidden-pattern', className: 'bg-teal-50 text-teal-700 ring-teal-600/20' },
  'ai-grounded': { label: 'AI-grounded', className: 'bg-blue-50 text-blue-700 ring-blue-600/20' },
  'human-review': { label: 'Human review', className: 'bg-violet-50 text-violet-700 ring-violet-600/20' },
  'not-enforced': { label: 'Not enforced', className: 'bg-red-50 text-red-700 ring-red-600/20' },
};

const SEVERITY_META: Record<string, string> = {
  critical: 'bg-red-50 text-red-700 ring-red-600/20',
  major: 'bg-amber-50 text-amber-700 ring-amber-600/20',
  minor: 'bg-slate-50 text-slate-600 ring-slate-500/20',
  info: 'bg-slate-50 text-slate-500 ring-slate-400/20',
};

function Pill({ label, className }: { label: string; className: string }) {
  return (
    <span className={`inline-flex items-center rounded-full px-2 py-0.5 text-xs font-medium ring-1 ring-inset ${className}`}>
      {label}
    </span>
  );
}

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

  return (
    <section className="mx-auto w-full max-w-6xl px-4 py-8">
      <header className="mb-6">
        <h1 className="text-2xl font-semibold text-navy">Rulebook Conformance</h1>
        <p className="mt-1 text-sm text-muted">
          How every rule in the four OET exam rulebooks is enforced. Read-only — the same classifier the
          <code className="mx-1 rounded bg-slate-100 px-1 py-0.5 text-xs">rulebook-conformance</code> CI gate uses.
        </p>
      </header>

      {totals.unenforced === 0 ? (
        <div className="mb-6 flex items-start gap-3 rounded-2xl border border-emerald-200 bg-emerald-50 p-4">
          <ShieldCheck className="mt-0.5 h-5 w-5 shrink-0 text-emerald-600" aria-hidden="true" />
          <p className="text-sm text-emerald-800">
            <span className="font-semibold">All {totals.total} rules have an asserted enforcement status.</span>{' '}
            Zero critical/major rules are silently unenforced across the four OET rulebooks.
          </p>
        </div>
      ) : (
        <div className="mb-6 flex items-start gap-3 rounded-2xl border border-red-200 bg-red-50 p-4">
          <AlertTriangle className="mt-0.5 h-5 w-5 shrink-0 text-red-600" aria-hidden="true" />
          <p className="text-sm text-red-800">
            <span className="font-semibold">{totals.unenforced} critical/major rule(s) are NOT enforced.</span>{' '}
            These are surfaced as warnings — they do not block publishing.
          </p>
        </div>
      )}

      <div className="mb-6 grid grid-cols-2 gap-3 sm:grid-cols-5">
        {[
          { label: 'Total rules', value: totals.total },
          { label: 'Deterministic', value: totals.deterministic },
          { label: 'AI-grounded', value: totals.aiGrounded },
          { label: 'Human review', value: totals.humanReview },
          { label: 'Not enforced', value: totals.unenforced },
        ].map((kpi) => (
          <div key={kpi.label} className="rounded-2xl border border-border bg-surface p-4">
            <div className="text-2xl font-semibold text-navy">{kpi.value}</div>
            <div className="mt-1 text-xs font-medium text-muted">{kpi.label}</div>
          </div>
        ))}
      </div>

      <div className="mb-4 flex flex-wrap gap-2" role="tablist" aria-label="Rulebook modules">
        {report.map((r) => (
          <button
            key={r.kind}
            type="button"
            role="tab"
            aria-selected={r.kind === active?.kind}
            onClick={() => setActiveKind(r.kind)}
            className={`rounded-full px-3 py-1.5 text-sm font-medium transition ${
              r.kind === active?.kind
                ? 'bg-primary text-white'
                : 'bg-slate-100 text-slate-600 hover:bg-slate-200'
            }`}
          >
            {r.label}
            <span className="ml-1.5 text-xs opacity-75">{r.summary.total}</span>
          </button>
        ))}
      </div>

      {active ? (
        <div className="overflow-hidden rounded-2xl border border-border bg-surface">
          <table className="w-full text-left text-sm">
            <thead className="border-b border-border bg-slate-50 text-xs uppercase tracking-wide text-muted">
              <tr>
                <th className="px-4 py-2 font-semibold">Rule</th>
                <th className="px-4 py-2 font-semibold">Section</th>
                <th className="px-4 py-2 font-semibold">Severity</th>
                <th className="px-4 py-2 font-semibold">Title</th>
                <th className="px-4 py-2 font-semibold">Enforcement</th>
              </tr>
            </thead>
            <tbody>
              {active.rows.map((row) => (
                <tr key={row.ruleId} className="border-b border-border/60 last:border-0">
                  <td className="whitespace-nowrap px-4 py-2 font-mono text-xs text-navy">{row.ruleId}</td>
                  <td className="px-4 py-2 text-muted">{row.section}</td>
                  <td className="px-4 py-2">
                    <Pill label={row.severity} className={SEVERITY_META[row.severity] ?? SEVERITY_META.info} />
                  </td>
                  <td className="px-4 py-2 text-navy">{row.title}</td>
                  <td className="px-4 py-2">
                    <Pill label={STATUS_META[row.status].label} className={STATUS_META[row.status].className} />
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      ) : null}
    </section>
  );
}
