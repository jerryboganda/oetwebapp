'use client';

import { useCallback, useEffect, useMemo, useState } from 'react';
import { BookOpenCheck, Layers, Repeat, ShieldCheck, Users } from 'lucide-react';

import { AdminOperationsLayout, KpiStrip } from '@/components/admin/layout/admin-operations-layout';
import { AdminTableLayout } from '@/components/admin/layout/admin-table-layout';
import { Badge } from '@/components/admin/ui/badge';
import { Card, CardContent, CardHeader, CardTitle } from '@/components/admin/ui/card';
import { EmptyState } from '@/components/admin/ui/empty-state';
import { KpiTile } from '@/components/admin/ui/kpi-tile';
import {
  NotInstrumentedPanel,
  type NotInstrumentedSignal,
} from '@/components/domain/admin/companion/not-instrumented-panel';
import { CompanionReportEmptyState } from '@/components/domain/admin/companion/companion-report-empty-state';
import { AsyncStateWrapper } from '@/components/state/async-state-wrapper';
import { DataTable, type Column } from '@/components/ui/data-table';
import { apiClient } from '@/lib/api';
import { useAdminAuth } from '@/lib/hooks/use-admin-auth';

type PageStatus = 'loading' | 'success' | 'empty' | 'error';

interface TeachingGapSource {
  sourceKind: string;
  rows: number;
}

interface TeachingGapPattern {
  patternKey: string;
  category: string;
  subtest: string;
  pattern: string;
  distinctLearners: number;
  totalEvidence: number;
  averageMasteryScore: number;
  minMasteryScore: number;
  maxMasteryScore: number;
  unaddressedLearnerRows: number;
  totalReviews: number;
  firstSeenAt: string;
  lastSeenAt: string;
  learnersSeenRecently: number;
  sources: TeachingGapSource[];
}

interface TeachingGapCategory {
  category: string;
  distinctPatterns: number;
  distinctLearners: number;
  totalEvidence: number;
  averageMasteryScore: number;
  patternsWithUnaddressedRows: number;
  lastSeenAt: string;
}

interface TeachingGapSubtest {
  subtest: string;
  distinctPatterns: number;
  distinctLearners: number;
  totalEvidence: number;
  averageMasteryScore: number;
}

interface TeachingGapReport {
  generatedAt: string;
  staleDays: number;
  filters: { subtest: string | null; sourceKind: string | null };
  totalRows: number;
  distinctLearners: number;
  distinctPatterns: number;
  patterns: TeachingGapPattern[];
  byCategory: TeachingGapCategory[];
  bySubtest: TeachingGapSubtest[];
  masteryDistribution: { bucket: string; count: number }[];
  evidenceSources: { sourceKind: string; rows: number }[];
  diagnostics: {
    noRowsMatchFilters: boolean;
    unknownSourceKindFilter: string | null;
    knownSourceKinds: string[];
  };
  notInstrumented: NotInstrumentedSignal[];
}

interface DimensionsReport {
  subtests: { subtest: string; rows: number; distinctLearners: number }[];
  categories: { category: string; rows: number; distinctLearners: number }[];
  sourceKinds: { sourceKind: string | null; rows: number }[];
}

const STALE_OPTIONS: { label: string; days: number }[] = [
  { label: 'Still seen in 7d', days: 7 },
  { label: 'Still seen in 30d', days: 30 },
  { label: 'Still seen in 90d', days: 90 },
];

/** 0..100 mastery, matching ErrorDnaEntry.MasteryScore. */
function masteryTone(score: number): 'danger' | 'warning' | 'success' {
  if (score < 30) return 'danger';
  if (score < 70) return 'warning';
  return 'success';
}

function fmtInt(value: number): string {
  return new Intl.NumberFormat('en-GB').format(value);
}

function fmtDate(value: string | null | undefined): string {
  if (!value) return '—';
  const parsed = new Date(value);
  return Number.isNaN(parsed.getTime()) ? '—' : parsed.toLocaleDateString();
}

/**
 * F-129 · Teaching-gap dashboard (SAMI handover §13.2).
 *
 * What learners repeatedly get wrong, aggregated from ErrorDnaEntry — the only
 * record in the platform that is created from observed evidence (a published
 * writing finding, a missed listening word, a wrong reading answer) rather than
 * from a guess about what a learner probably struggles with.
 *
 * The read is aggregate by construction: no user id and no per-learner row
 * reaches this page. That is what lets a tutor work from it without opening any
 * individual learner's record.
 *
 * The one trap this page calls out explicitly: today only writing feeds Error
 * DNA, so an empty listening/reading/speaking row is an absence of
 * instrumentation, not evidence that learners make no mistakes there. The by-subtest
 * table shows which subtests actually have rows, and the footer states the rest.
 */
export default function CompanionTeachingGapsPage() {
  const { isAuthenticated, role } = useAdminAuth();
  const [status, setStatus] = useState<PageStatus>('loading');
  const [error, setError] = useState<string | null>(null);
  const [staleDays, setStaleDays] = useState(30);
  const [subtest, setSubtest] = useState<string>('');
  const [sourceKind, setSourceKind] = useState<string>('');
  const [report, setReport] = useState<TeachingGapReport | null>(null);
  const [dimensions, setDimensions] = useState<DimensionsReport | null>(null);

  const query = useMemo(() => {
    const params = new URLSearchParams({ staleDays: String(staleDays) });
    if (subtest) params.set('subtest', subtest);
    if (sourceKind) params.set('sourceKind', sourceKind);
    return params.toString();
  }, [staleDays, subtest, sourceKind]);

  const load = useCallback(async () => {
    setStatus('loading');
    setError(null);
    try {
      const [reportData, dimensionData] = await Promise.all([
        apiClient.get<TeachingGapReport>(`/v1/admin/companion/teaching-gaps/recurring?${query}`),
        apiClient.get<DimensionsReport>('/v1/admin/companion/teaching-gaps/dimensions'),
      ]);
      setReport(reportData);
      setDimensions(dimensionData);
      setStatus(reportData.totalRows === 0 ? 'empty' : 'success');
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Could not read the teaching-gap report.');
      setStatus('error');
    }
  }, [query]);

  useEffect(() => {
    if (isAuthenticated && role === 'admin') {
      // eslint-disable-next-line react-hooks/set-state-in-effect
      void load();
    }
  }, [isAuthenticated, role, load]);

  const breadcrumbs = [
    { label: 'Admin', href: '/admin' },
    { label: 'Learning Companion', href: '/admin/companion/access' },
    { label: 'Teaching Gaps' },
  ];

  if (!isAuthenticated || role !== 'admin') {
    return (
      <AdminTableLayout title="Companion Teaching Gaps" eyebrow="Learning Companion" breadcrumbs={breadcrumbs}>
        <EmptyState
          title="Admin access required"
          description="Sign in with an admin account to read the teaching-gap report."
          illustration={<ShieldCheck className="h-8 w-8" />}
        />
      </AdminTableLayout>
    );
  }

  const patternColumns: Column<TeachingGapPattern>[] = [
    {
      key: 'pattern',
      header: 'Recurring error pattern',
      render: (row) => (
        <div className="min-w-0">
          <p className="text-sm text-admin-fg-strong">{row.pattern}</p>
          <p className="mt-0.5 flex flex-wrap items-center gap-1">
            <span className="rounded-admin border border-admin-border px-1.5 py-0.5 text-xs text-admin-fg-muted">
              {row.category}
            </span>
            <span className="rounded-admin border border-admin-border px-1.5 py-0.5 text-xs text-admin-fg-muted">
              {row.subtest}
            </span>
            {row.sources.map((source) => (
              <span key={`${row.patternKey}-${source.sourceKind}`} className="text-xs text-admin-fg-muted">
                via {source.sourceKind} ({fmtInt(source.rows)})
              </span>
            ))}
          </p>
        </div>
      ),
      className: 'max-w-2xl',
    },
    {
      key: 'distinctLearners',
      header: 'Learners',
      render: (row) => (
        <Badge variant={row.distinctLearners >= 5 ? 'danger' : 'default'} intensity="tinted" size="sm">
          {fmtInt(row.distinctLearners)}
        </Badge>
      ),
    },
    {
      key: 'totalEvidence',
      header: 'Evidence',
      render: (row) => <span className="tabular-nums text-admin-fg-strong">{fmtInt(row.totalEvidence)}</span>,
    },
    {
      key: 'averageMasteryScore',
      header: 'Avg mastery',
      render: (row) => (
        <Badge variant={masteryTone(row.averageMasteryScore)} intensity="tinted" size="sm">
          {row.averageMasteryScore}/100
        </Badge>
      ),
    },
    {
      key: 'unaddressedLearnerRows',
      header: 'Unaddressed',
      render: (row) =>
        row.unaddressedLearnerRows === 0 ? (
          <span className="tabular-nums text-admin-fg-muted">0</span>
        ) : (
          <span className="tabular-nums text-admin-fg-strong" title="Learner rows with mastery below 30">
            {fmtInt(row.unaddressedLearnerRows)}
          </span>
        ),
    },
    {
      key: 'learnersSeenRecently',
      header: `Seen in ${staleDays}d`,
      render: (row) => (
        <span className="tabular-nums text-admin-fg-muted" title="Learner rows with new evidence inside the recency window">
          {fmtInt(row.learnersSeenRecently)}
        </span>
      ),
    },
    {
      key: 'lastSeenAt',
      header: 'Last seen',
      render: (row) => <span className="text-xs text-admin-fg-muted">{fmtDate(row.lastSeenAt)}</span>,
    },
  ];

  const categoryColumns: Column<TeachingGapCategory>[] = [
    {
      key: 'category',
      header: 'Category',
      render: (row) => <span className="text-sm text-admin-fg-strong">{row.category}</span>,
    },
    {
      key: 'distinctPatterns',
      header: 'Patterns',
      render: (row) => <span className="tabular-nums text-admin-fg-strong">{fmtInt(row.distinctPatterns)}</span>,
    },
    {
      key: 'distinctLearners',
      header: 'Learners',
      render: (row) => <span className="tabular-nums text-admin-fg-muted">{fmtInt(row.distinctLearners)}</span>,
    },
    {
      key: 'totalEvidence',
      header: 'Evidence',
      render: (row) => <span className="tabular-nums text-admin-fg-strong">{fmtInt(row.totalEvidence)}</span>,
    },
    {
      key: 'averageMasteryScore',
      header: 'Avg mastery',
      render: (row) => (
        <Badge variant={masteryTone(row.averageMasteryScore)} intensity="tinted" size="sm">
          {row.averageMasteryScore}/100
        </Badge>
      ),
    },
    {
      key: 'patternsWithUnaddressedRows',
      header: 'Patterns with unaddressed rows',
      render: (row) => <span className="tabular-nums text-admin-fg-muted">{fmtInt(row.patternsWithUnaddressedRows)}</span>,
    },
    {
      key: 'lastSeenAt',
      header: 'Last seen',
      render: (row) => <span className="text-xs text-admin-fg-muted">{fmtDate(row.lastSeenAt)}</span>,
    },
  ];

  const subtestColumns: Column<TeachingGapSubtest>[] = [
    {
      key: 'subtest',
      header: 'Subtest',
      render: (row) => <span className="text-sm text-admin-fg-strong">{row.subtest}</span>,
    },
    {
      key: 'distinctPatterns',
      header: 'Patterns',
      render: (row) => <span className="tabular-nums text-admin-fg-strong">{fmtInt(row.distinctPatterns)}</span>,
    },
    {
      key: 'distinctLearners',
      header: 'Learners',
      render: (row) => <span className="tabular-nums text-admin-fg-muted">{fmtInt(row.distinctLearners)}</span>,
    },
    {
      key: 'totalEvidence',
      header: 'Evidence',
      render: (row) => <span className="tabular-nums text-admin-fg-strong">{fmtInt(row.totalEvidence)}</span>,
    },
    {
      key: 'averageMasteryScore',
      header: 'Avg mastery',
      render: (row) => (
        <Badge variant={masteryTone(row.averageMasteryScore)} intensity="tinted" size="sm">
          {row.averageMasteryScore}/100
        </Badge>
      ),
    },
  ];

  const totalEvidence = report?.patterns.reduce((sum, row) => sum + row.totalEvidence, 0) ?? 0;
  const unaddressedRows = report?.patterns.reduce((sum, row) => sum + row.unaddressedLearnerRows, 0) ?? 0;
  const widestPattern = report?.patterns.reduce<TeachingGapPattern | null>(
    (widest, row) => (widest === null || row.distinctLearners > widest.distinctLearners ? row : widest),
    null,
  );

  const subtestsWithRows = new Set((report?.bySubtest ?? []).map((row) => row.subtest));
  const subtestsWithoutRows = ['writing', 'speaking', 'reading', 'listening'].filter(
    (value) => !subtestsWithRows.has(value),
  );

  return (
    <AdminOperationsLayout
      title="Companion Teaching Gaps"
      description="What learners repeatedly get wrong, aggregated from evidenced Error DNA records — a surfaced writing finding, a missed listening word, a wrong reading answer. Every row is evidence, never a guess, and no learner is identifiable in this view."
      eyebrow="Learning Companion"
      breadcrumbs={breadcrumbs}
    >
      <AsyncStateWrapper
        status={status}
        onRetry={load}
        errorMessage={error ?? undefined}
        emptyContent={<CompanionReportEmptyState subject="evidenced recurring-error record" windowDays={staleDays} />}
      >
        <div className="flex flex-wrap items-end justify-between gap-3">
          <div className="flex flex-wrap items-end gap-3">
            <label className="block">
              <span className="block text-xs font-medium text-admin-fg-muted">Subtest</span>
              <select
                value={subtest}
                onChange={(event) => setSubtest(event.target.value)}
                className="mt-1 rounded-admin border border-admin-border bg-admin-bg-surface px-2.5 py-1.5 text-sm text-admin-fg-strong"
              >
                <option value="">All subtests</option>
                {(dimensions?.subtests ?? []).map((row) => (
                  <option key={row.subtest} value={row.subtest}>
                    {row.subtest} ({fmtInt(row.rows)})
                  </option>
                ))}
              </select>
            </label>
            <label className="block">
              <span className="block text-xs font-medium text-admin-fg-muted">Evidence source</span>
              <select
                value={sourceKind}
                onChange={(event) => setSourceKind(event.target.value)}
                className="mt-1 rounded-admin border border-admin-border bg-admin-bg-surface px-2.5 py-1.5 text-sm text-admin-fg-strong"
              >
                <option value="">All sources</option>
                {(dimensions?.sourceKinds ?? [])
                  .filter((row) => row.sourceKind !== null)
                  .map((row) => (
                    <option key={row.sourceKind} value={row.sourceKind ?? ''}>
                      {row.sourceKind} ({fmtInt(row.rows)})
                    </option>
                  ))}
              </select>
            </label>
            <label className="block">
              <span className="block text-xs font-medium text-admin-fg-muted">Recency</span>
              <select
                value={staleDays}
                onChange={(event) => setStaleDays(Number(event.target.value))}
                className="mt-1 rounded-admin border border-admin-border bg-admin-bg-surface px-2.5 py-1.5 text-sm text-admin-fg-strong"
              >
                {STALE_OPTIONS.map((option) => (
                  <option key={option.days} value={option.days}>
                    {option.label}
                  </option>
                ))}
              </select>
            </label>
          </div>
          <p className="text-xs text-admin-fg-muted">
            {report ? `Read at ${fmtDate(report.generatedAt)}` : ''}
          </p>
        </div>

        {report?.diagnostics.unknownSourceKindFilter && (
          <p className="mt-3 text-xs text-red-700 dark:text-red-300" role="alert">
            <span className="font-mono">{report.diagnostics.unknownSourceKindFilter}</span> is not a source kind this
            platform writes. Known values: {report.diagnostics.knownSourceKinds.join(', ')}.
          </p>
        )}

        <KpiStrip className="mt-4">
          <KpiTile
            label="Recurring patterns"
            value={fmtInt(report?.distinctPatterns ?? 0)}
            icon={<Repeat className="h-4 w-4" />}
            hint={`${fmtInt(report?.totalRows ?? 0)} learner-level rows`}
          />
          <KpiTile
            label="Learners with evidenced errors"
            value={fmtInt(report?.distinctLearners ?? 0)}
            icon={<Users className="h-4 w-4" />}
            hint="Distinct learners behind these rows"
          />
          <KpiTile
            label="Total evidence"
            value={fmtInt(totalEvidence)}
            icon={<BookOpenCheck className="h-4 w-4" />}
            hint="Times these patterns were observed"
          />
          <KpiTile
            label="Unaddressed rows"
            value={fmtInt(unaddressedRows)}
            tone={unaddressedRows > 0 ? 'warning' : 'success'}
            icon={<Layers className="h-4 w-4" />}
            hint="Mastery below 30/100"
          />
        </KpiStrip>

        {widestPattern && (
          <Card className="mt-5">
            <CardContent className="p-4 sm:p-5">
              <p className="text-xs font-semibold uppercase tracking-wide text-admin-fg-muted">
                Widest-reaching gap
              </p>
              <p className="mt-1 text-sm text-admin-fg-strong">{widestPattern.pattern}</p>
              <p className="mt-1 text-xs text-admin-fg-muted">
                {fmtInt(widestPattern.distinctLearners)} learners · {fmtInt(widestPattern.totalEvidence)} observations ·{' '}
                {widestPattern.category} / {widestPattern.subtest} · average mastery {widestPattern.averageMasteryScore}/100
              </p>
            </CardContent>
          </Card>
        )}

        <div className="mt-5 grid gap-4">
          <Card>
            <CardHeader>
              <CardTitle className="text-sm">Recurring error patterns</CardTitle>
            </CardHeader>
            <CardContent className="p-0 pt-0">
              <DataTable
                columns={patternColumns}
                data={report?.patterns ?? []}
                keyExtractor={(row) => `${row.patternKey}|${row.subtest}`}
                emptyMessage="No evidenced recurring-error row matches these filters."
                aria-label="Recurring evidenced error patterns"
              />
              <p className="px-4 pb-4 pt-3 text-xs leading-relaxed text-admin-fg-muted">
                A pattern is identified by the same SHA-256 key the recording path upserts on, so two categories that
                happen to share a pattern string are never merged. <strong className="font-semibold text-admin-fg-strong">Evidence</strong>{' '}
                is the number of times the pattern was observed; <strong className="font-semibold text-admin-fg-strong">Learners</strong>{' '}
                is how many distinct learners it was observed in, which is what separates one learner repeating a mistake
                from a teaching gap.
              </p>
            </CardContent>
          </Card>

          <div className="grid gap-4 lg:grid-cols-2">
            <Card>
              <CardHeader>
                <CardTitle className="text-sm">By category</CardTitle>
              </CardHeader>
              <CardContent className="p-0 pt-0">
                <DataTable
                  columns={categoryColumns}
                  data={report?.byCategory ?? []}
                  keyExtractor={(row) => row.category}
                  emptyMessage="No category has evidenced rows."
                  aria-label="Teaching gaps by category"
                />
              </CardContent>
            </Card>

            <Card>
              <CardHeader>
                <CardTitle className="text-sm">By subtest</CardTitle>
              </CardHeader>
              <CardContent className="p-0 pt-0">
                <DataTable
                  columns={subtestColumns}
                  data={report?.bySubtest ?? []}
                  keyExtractor={(row) => row.subtest}
                  emptyMessage="No subtest has evidenced rows."
                  aria-label="Teaching gaps by subtest"
                />
                {subtestsWithoutRows.length > 0 && (
                  <p className="px-4 pb-4 pt-3 text-xs leading-relaxed text-admin-fg-muted">
                    No row exists for {subtestsWithoutRows.join(', ')}. That is an instrumentation gap, not a clean bill of
                    health: only the writing grader feeds Error DNA today, so the other subtests have no evidenced data to
                    report.
                  </p>
                )}
              </CardContent>
            </Card>
          </div>

          <div className="grid gap-4 lg:grid-cols-2">
            <Card>
              <CardHeader>
                <CardTitle className="text-sm">Mastery distribution</CardTitle>
              </CardHeader>
              <CardContent className="pt-0">
                <ul className="space-y-2">
                  {(report?.masteryDistribution ?? []).map((row) => {
                    const total = (report?.masteryDistribution ?? []).reduce((sum, item) => sum + item.count, 0);
                    const pct = total === 0 ? 0 : Math.round((row.count * 100) / total);
                    return (
                      <li key={row.bucket}>
                        <div className="flex items-center justify-between text-xs">
                          <span className="text-admin-fg-muted">Mastery {row.bucket}</span>
                          <span className="tabular-nums text-admin-fg-strong">
                            {fmtInt(row.count)} <span className="text-admin-fg-muted">({pct}%)</span>
                          </span>
                        </div>
                        <div
                          className="mt-1 h-1.5 w-full overflow-hidden rounded-full bg-[var(--admin-bg-subtle)]"
                          role="presentation"
                        >
                          <div
                            className={
                              row.bucket === '0-29'
                                ? 'h-full rounded-full bg-[var(--admin-danger)]'
                                : row.bucket === '30-59'
                                  ? 'h-full rounded-full bg-[var(--admin-warning)]'
                                  : 'h-full rounded-full bg-[var(--admin-success)]'
                            }
                            style={{ width: `${pct}%` }}
                          />
                        </div>
                      </li>
                    );
                  })}
                </ul>
                <p className="mt-3 text-xs text-admin-fg-muted">
                  Mastery is 0–100 per learner row. Below 30 means unaddressed; each successful spaced review raises it and
                  new evidence of the same weakness lowers it by 10.
                </p>
              </CardContent>
            </Card>

            <Card>
              <CardHeader>
                <CardTitle className="text-sm">Where the evidence comes from</CardTitle>
              </CardHeader>
              <CardContent className="pt-0">
                <ul className="space-y-1.5">
                  {(report?.evidenceSources ?? []).map((row) => (
                    <li key={row.sourceKind} className="flex items-center justify-between text-xs">
                      <span className="font-mono text-admin-fg-muted">{row.sourceKind}</span>
                      <span className="tabular-nums text-admin-fg-strong">{fmtInt(row.rows)}</span>
                    </li>
                  ))}
                </ul>
                <p className="mt-3 text-xs text-admin-fg-muted">
                  This is the honest boundary of the report: it can only speak about the sources that actually feed it.
                </p>
              </CardContent>
            </Card>
          </div>

          {report && <NotInstrumentedPanel signals={report.notInstrumented} />}
        </div>
      </AsyncStateWrapper>
    </AdminOperationsLayout>
  );
}
