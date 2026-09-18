'use client';

import type { PlacementResultReport } from '@/lib/api/placement';

/**
 * Skill-first result report renderer shared by the placement journey's
 * inline result stage and the standalone results/history pages.
 */
export function ResultReportCard({
  title,
  report,
  primaryLabel,
  onPrimary,
}: {
  title: string;
  report: PlacementResultReport;
  primaryLabel?: string;
  onPrimary?: () => void;
}) {
  return (
    <div className="mx-auto max-w-2xl space-y-4">
      <div className="rounded-2xl border border-border bg-surface p-6">
        <h2 className="text-xl font-semibold text-navy">{title}</h2>
        <p className="mt-1 text-sm text-muted">
          Confidence: {report.confidence}
          {report.headline.kind === 'indicative_overall' && report.headline.band
            ? ` · indicative overall ${report.headline.band}`
            : report.headline.kind === 'uneven' && report.headline.range
              ? ` · uneven profile (${report.headline.range[0]}–${report.headline.range[1]})`
              : ''}
        </p>
        <div className="mt-4 grid gap-3 sm:grid-cols-2">
          {report.skills.map((skill) => (
            <div key={skill.skill} className="rounded-xl border border-border bg-background-light p-4">
              <p className="text-sm font-semibold text-navy">{SKILL_LABELS[skill.skill] ?? skill.skill}</p>
              <p className="text-lg font-semibold text-primary">{skill.band ?? '—'}</p>
              <p className="text-xs uppercase tracking-wide text-muted">{skill.status}</p>
              {skill.notes.slice(0, 2).map((note) => (
                <p key={note} className="mt-1 text-xs text-muted">{note}</p>
              ))}
            </div>
          ))}
        </div>
        {(report.confidenceReasons ?? report.confidence_reasons ?? []).length > 0 ? (
          <ul className="mt-4 space-y-1 text-xs text-muted">
            {(report.confidenceReasons ?? report.confidence_reasons ?? []).map((reason) => <li key={reason}>• {reason}</li>)}
          </ul>
        ) : null}
        <p className="mt-4 text-xs text-muted">{report.retestAdvice ?? report.retest_advice}</p>
        {report.readiness ? (
          <p className="mt-2 text-xs text-muted">{report.readiness.text} {report.readiness.disclaimer}</p>
        ) : null}
      </div>
      {primaryLabel && onPrimary ? (
        <div className="text-center">
          <button type="button" onClick={onPrimary} className="inline-flex items-center gap-2 rounded-xl bg-primary px-5 py-2.5 text-sm font-semibold text-white hover:opacity-90">
            {primaryLabel}
          </button>
        </div>
      ) : null}
    </div>
  );
}

const SKILL_LABELS: Record<string, string> = {
  LS: 'Language Systems',
  RD: 'Reading',
  LSN: 'Listening',
  SPK: 'Speaking',
  WRT: 'Writing',
};
