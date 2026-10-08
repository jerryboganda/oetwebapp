'use client';

import { useCallback, useEffect, useState } from 'react';
import { AlertTriangle, FileQuestion, Layers, Quote, ShieldCheck } from 'lucide-react';

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

interface CorpusCoverageRow {
  professionId: string | null;
  subtestCode: string | null;
  totalSources: number;
  approved: number;
  retrievable: number;
  pendingApproval: number;
  draft: number;
  superseded: number;
  noRetrievableSource: boolean;
}

interface UncitedTopicRow {
  professionId: string;
  uncitedAnswers: number;
  distinctLearners: number;
  totalAnswers: number;
}

interface CoverageReport {
  generatedAt: string;
  windowDays: number;
  retrievalEnabled: boolean;
  corpusCoverage: CorpusCoverageRow[];
  scopesWithoutRetrievableSource: number;
  uncitedTopics: UncitedTopicRow[];
  notInstrumented: NotInstrumentedSignal[];
}

interface HandoffRow {
  id: string;
  createdAt: string;
  ageDays: number;
  route: string;
  status: string;
  issue: string;
}

interface HandoffReport {
  generatedAt: string;
  windowDays: number;
  unresolvedCount: number;
  byRoute: { route: string; count: number; oldestCreatedAt: string }[];
  rows: HandoffRow[];
  repeatedIssues: { issue: string; count: number }[];
}

const WINDOW_OPTIONS: { label: string; days: number }[] = [
  { label: '7 days', days: 7 },
  { label: '30 days', days: 30 },
  { label: '90 days', days: 90 },
];

function fmtInt(value: number): string {
  return new Intl.NumberFormat('en-GB').format(value);
}

function fmtTimestamp(value: string | null | undefined): string {
  if (!value) return '—';
  const parsed = new Date(value);
  return Number.isNaN(parsed.getTime()) ? '—' : parsed.toLocaleString();
}

function scopeLabel(row: { professionId: string | null; subtestCode: string | null }): string {
  return `${row.professionId ?? 'All professions'} · ${row.subtestCode ?? 'all subtests'}`;
}

/**
 * F-128 · Content-gap dashboard (SAMI handover §13.2).
 *
 * The page keeps two different kinds of fact apart on purpose:
 *
 *   1. CORPUS COVERAGE — which (profession, subtest) scopes hold no retrievable
 *      approved source. This is measured from CompanionSources, not inferred: with
 *      zero retrievable sources in a scope, retrieval there provably cannot return
 *      evidence.
 *   2. OBSERVED UNCITED ANSWERS — which professions actually produced companion
 *      answers that cited nothing. This is a symptom, not proof of a content gap:
 *      the same symptom appears when the retrieval flag is off. The flag state is
 *      shown next to it so the two are never confused.
 *
 *   3. WHAT LEARNERS ASKED — unresolved CompanionHandoffs, verbatim. This is the
 *      only topic list that exists; the platform has no question-topic classifier,
 *      and inventing topic buckets from chat text would be exactly the fabrication
 *      this dashboard must not do.
 */
export default function CompanionContentGapsPage() {
  const { isAuthenticated, role } = useAdminAuth();
  const [status, setStatus] = useState<PageStatus>('loading');
  const [error, setError] = useState<string | null>(null);
  const [windowDays, setWindowDays] = useState(30);
  const [coverage, setCoverage] = useState<CoverageReport | null>(null);
  const [handoffs, setHandoffs] = useState<HandoffReport | null>(null);

  const load = useCallback(async () => {
    setStatus('loading');
    setError(null);
    try {
      const [coverageReport, handoffReport] = await Promise.all([
        apiClient.get<CoverageReport>(`/v1/admin/companion/content-gaps/coverage?days=${windowDays}`),
        apiClient.get<HandoffReport>(`/v1/admin/companion/content-gaps/handoffs?days=${windowDays}`),
      ]);
      setCoverage(coverageReport);
      setHandoffs(handoffReport);
      const nothingMeasured =
        coverageReport.corpusCoverage.length === 0 &&
        coverageReport.uncitedTopics.length === 0 &&
        handoffReport.unresolvedCount === 0;
      setStatus(nothingMeasured ? 'empty' : 'success');
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Could not read the content-gap report.');
      setStatus('error');
    }
  }, [windowDays]);

  useEffect(() => {
    if (isAuthenticated && role === 'admin') {
      // eslint-disable-next-line react-hooks/set-state-in-effect
      void load();
    }
  }, [isAuthenticated, role, load]);

  const breadcrumbs = [
    { label: 'Admin', href: '/admin' },
    { label: 'Learning Companion', href: '/admin/companion/access' },
    { label: 'Content Gaps' },
  ];

  if (!isAuthenticated || role !== 'admin') {
    return (
      <AdminTableLayout title="Companion Content Gaps" eyebrow="Learning Companion" breadcrumbs={breadcrumbs}>
        <EmptyState
          title="Admin access required"
          description="Sign in with an admin account to read the content-gap report."
          illustration={<ShieldCheck className="h-8 w-8" />}
        />
      </AdminTableLayout>
    );
  }

  const coverageColumns: Column<CorpusCoverageRow>[] = [
    {
      key: 'scope',
      header: 'Scope',
      render: (row) => (
        <div className="flex items-center gap-2">
          {row.noRetrievableSource && (
            <AlertTriangle className="h-3.5 w-3.5 shrink-0 text-amber-700 dark:text-amber-300" aria-hidden="true" />
          )}
          <span className="text-sm text-admin-fg-strong">{scopeLabel(row)}</span>
        </div>
      ),
    },
    {
      key: 'retrievable',
      header: 'Retrievable',
      render: (row) =>
        row.retrievable === 0 ? (
          <Badge variant="danger" intensity="tinted" size="sm">
            None
          </Badge>
        ) : (
          <span className="tabular-nums text-admin-fg-strong">{fmtInt(row.retrievable)}</span>
        ),
    },
    {
      key: 'approved',
      header: 'Approved',
      render: (row) => <span className="tabular-nums text-admin-fg-muted">{fmtInt(row.approved)}</span>,
    },
    {
      key: 'pendingApproval',
      header: 'Awaiting approval',
      render: (row) =>
        row.pendingApproval === 0 ? (
          <span className="tabular-nums text-admin-fg-muted">0</span>
        ) : (
          <Badge variant="warning" intensity="tinted" size="sm">
            {fmtInt(row.pendingApproval)}
          </Badge>
        ),
    },
    {
      key: 'draft',
      header: 'Draft',
      render: (row) => <span className="tabular-nums text-admin-fg-muted">{fmtInt(row.draft)}</span>,
    },
    {
      key: 'superseded',
      header: 'Superseded',
      render: (row) => <span className="tabular-nums text-admin-fg-muted">{fmtInt(row.superseded)}</span>,
    },
    {
      key: 'totalSources',
      header: 'Total',
      render: (row) => <span className="tabular-nums text-admin-fg-muted">{fmtInt(row.totalSources)}</span>,
    },
  ];

  const uncitedColumns: Column<UncitedTopicRow>[] = [
    {
      key: 'professionId',
      header: 'Profession',
      render: (row) => (
        <span className="text-sm text-admin-fg-strong">
          {row.professionId === 'unknown' ? 'Not on file' : row.professionId}
        </span>
      ),
    },
    {
      key: 'uncitedAnswers',
      header: 'Answers with no citation',
      render: (row) => (
        <Badge variant={row.uncitedAnswers > 0 ? 'warning' : 'default'} intensity="tinted" size="sm">
          {fmtInt(row.uncitedAnswers)}
        </Badge>
      ),
    },
    {
      key: 'totalAnswers',
      header: 'Answers in total',
      render: (row) => <span className="tabular-nums text-admin-fg-muted">{fmtInt(row.totalAnswers)}</span>,
    },
    {
      key: 'distinctLearners',
      header: 'Learners',
      render: (row) => <span className="tabular-nums text-admin-fg-muted">{fmtInt(row.distinctLearners)}</span>,
    },
  ];

  const handoffColumns: Column<HandoffRow>[] = [
    {
      key: 'issue',
      header: 'What the learner asked',
      render: (row) => (
        <span className="block max-w-xl whitespace-pre-wrap text-sm text-admin-fg-strong">{row.issue}</span>
      ),
    },
    {
      key: 'route',
      header: 'Queue',
      render: (row) => (
        <Badge variant={row.route === 'support' ? 'info' : 'default'} intensity="tinted" size="sm">
          {row.route === 'support' ? 'Support' : 'Tutor'}
        </Badge>
      ),
    },
    {
      key: 'status',
      header: 'Status',
      render: (row) => (
        <Badge variant={row.status === 'claimed' ? 'warning' : 'danger'} intensity="tinted" size="sm">
          {row.status}
        </Badge>
      ),
    },
    {
      key: 'ageDays',
      header: 'Age',
      render: (row) => <span className="tabular-nums text-admin-fg-muted">{row.ageDays}d</span>,
    },
    {
      key: 'createdAt',
      header: 'Raised',
      render: (row) => <span className="text-xs text-admin-fg-muted">{fmtTimestamp(row.createdAt)}</span>,
    },
  ];

  const emptyScopeCount = coverage?.scopesWithoutRetrievableSource ?? 0;
  const totalUncited = coverage?.uncitedTopics.reduce((sum, row) => sum + row.uncitedAnswers, 0) ?? 0;

  return (
    <AdminOperationsLayout
      title="Companion Content Gaps"
      description="Which topics have no supporting source. Corpus coverage is measured from the knowledge store; uncited answers and learner-raised issues are the observed symptoms. The two are shown separately because an uncited answer is not proof that content is missing."
      eyebrow="Learning Companion"
      breadcrumbs={breadcrumbs}
    >
      <AsyncStateWrapper
        status={status}
        onRetry={load}
        errorMessage={error ?? undefined}
        emptyContent={<CompanionReportEmptyState subject="companion activity or knowledge source" windowDays={windowDays} />}
      >
        <div className="flex flex-wrap items-center justify-between gap-2">
          <p className="text-xs text-admin-fg-muted">
            Window: last {windowDays} days
            {coverage ? ` · read at ${fmtTimestamp(coverage.generatedAt)}` : ''}
          </p>
          <div className="flex items-center gap-1" role="group" aria-label="Reporting window">
            {WINDOW_OPTIONS.map((option) => (
              <button
                key={option.days}
                type="button"
                onClick={() => setWindowDays(option.days)}
                aria-pressed={windowDays === option.days}
                className={
                  windowDays === option.days
                    ? 'rounded-admin border border-admin-border bg-[var(--admin-bg-subtle)] px-2.5 py-1 text-xs font-semibold text-admin-fg-strong'
                    : 'rounded-admin border border-admin-border px-2.5 py-1 text-xs text-admin-fg-muted hover:text-admin-fg-default'
                }
              >
                {option.label}
              </button>
            ))}
          </div>
        </div>

        <KpiStrip className="mt-4">
          <KpiTile
            label="Scopes with no retrievable source"
            value={fmtInt(emptyScopeCount)}
            tone={emptyScopeCount > 0 ? 'danger' : 'success'}
            icon={<Layers className="h-4 w-4" />}
            hint="Profession × subtest with zero approved, in-window sources"
          />
          <KpiTile
            label="Answers with no citation"
            value={fmtInt(totalUncited)}
            tone={totalUncited > 0 ? 'warning' : 'default'}
            icon={<Quote className="h-4 w-4" />}
            hint={`Last ${windowDays} days, all professions`}
          />
          <KpiTile
            label="Unresolved learner issues"
            value={fmtInt(handoffs?.unresolvedCount ?? 0)}
            tone={(handoffs?.unresolvedCount ?? 0) > 0 ? 'danger' : 'default'}
            icon={<FileQuestion className="h-4 w-4" />}
            hint="Questions Sami could not settle"
          />
          <KpiTile
            label="Retrieval flag"
            value={coverage?.retrievalEnabled ? 'On' : 'Off'}
            tone={coverage?.retrievalEnabled ? 'success' : 'warning'}
            icon={<AlertTriangle className="h-4 w-4" />}
            hint={
              coverage?.retrievalEnabled
                ? 'Answers can be grounded'
                : 'Off — every answer is ungrounded by design'
            }
          />
        </KpiStrip>

        <div className="mt-5 grid gap-4">
          <Card>
            <CardHeader>
              <CardTitle className="text-sm">Corpus coverage by profession and subtest</CardTitle>
            </CardHeader>
            <CardContent className="p-0 pt-0">
              <DataTable
                columns={coverageColumns}
                data={coverage?.corpusCoverage ?? []}
                keyExtractor={(row) => `${row.professionId ?? 'all'}|${row.subtestCode ?? 'all'}`}
                emptyMessage="No companion knowledge source is registered, so no scope can be scored."
                aria-label="Companion corpus coverage by scope"
              />
              <p className="px-4 pb-4 pt-3 text-xs leading-relaxed text-admin-fg-muted">
                <strong className="font-semibold text-admin-fg-strong">Retrievable</strong> counts only sources that are
                Approved, not superseded and inside their effective window — the same filter retrieval applies before any
                search runs. A scope showing <em>None</em> is a measured content gap: nothing can be cited there. Draft and
                awaiting-approval counts are shown beside it so the remedy is visible.
                {emptyScopeCount === 0 && ' No scope is currently without a retrievable source.'}
              </p>
            </CardContent>
          </Card>

          <Card>
            <CardHeader>
              <CardTitle className="text-sm">Companion answers that cited nothing, by profession</CardTitle>
            </CardHeader>
            <CardContent className="p-0 pt-0">
              <DataTable
                columns={uncitedColumns}
                data={coverage?.uncitedTopics ?? []}
                keyExtractor={(row) => row.professionId}
                emptyMessage={`No companion answer was recorded in the last ${windowDays} days.`}
                aria-label="Uncited companion answers by profession"
              />
              <p className="px-4 pb-4 pt-3 text-xs leading-relaxed text-admin-fg-muted">
                Profession is the learner&apos;s <em>current</em> active profession, not the value at answer time — that is
                not recorded, so read this as an attribution rather than a historical fact. An answer can cite nothing
                because retrieval found nothing, because the retrieval flag is off, or because prompt composition failed
                before retrieval ran; this table cannot separate those. Compare it with the coverage table above before
                calling anything a content gap.
              </p>
            </CardContent>
          </Card>

          <Card>
            <CardHeader>
              <CardTitle className="text-sm">
                Questions Sami could not settle ({fmtInt(handoffs?.unresolvedCount ?? 0)})
              </CardTitle>
            </CardHeader>
            <CardContent className="p-0 pt-0">
              <DataTable
                columns={handoffColumns}
                data={handoffs?.rows ?? []}
                keyExtractor={(row) => row.id}
                emptyMessage={`No unresolved learner handoff was raised in the last ${windowDays} days.`}
                aria-label="Unresolved companion handoffs"
                mobileCardRender={(row) => (
                  <div className="space-y-1">
                    <p className="text-sm text-admin-fg-strong">{row.issue}</p>
                    <p className="text-xs text-admin-fg-muted">
                      {row.route} · {row.status} · {row.ageDays}d old
                    </p>
                  </div>
                )}
              />
              <p className="px-4 pb-4 pt-3 text-xs leading-relaxed text-admin-fg-muted">
                These are the learner&apos;s own words, filed expressly for a human to read — the closest thing to a real
                topic list the platform has. Chat message content is deliberately not mined for this report.
              </p>
              {handoffs && handoffs.repeatedIssues.length > 0 && (
                <div className="border-t border-admin-border px-4 py-3">
                  <p className="text-xs font-medium text-admin-fg-muted">
                    The same issue raised more than once (strongest signal of a real gap)
                  </p>
                  <ul className="mt-1.5 space-y-1">
                    {handoffs.repeatedIssues.map((row) => (
                      <li key={row.issue} className="flex items-start justify-between gap-3 text-xs">
                        <span className="min-w-0 whitespace-pre-wrap text-admin-fg-strong">{row.issue}</span>
                        <Badge variant="warning" intensity="tinted" size="sm">
                          {fmtInt(row.count)}
                        </Badge>
                      </li>
                    ))}
                  </ul>
                </div>
              )}
            </CardContent>
          </Card>

          {coverage && <NotInstrumentedPanel signals={coverage.notInstrumented} />}
        </div>
      </AsyncStateWrapper>
    </AdminOperationsLayout>
  );
}
