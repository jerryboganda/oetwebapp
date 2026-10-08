'use client';

import { useCallback, useEffect, useState } from 'react';
import {
  AlertTriangle,
  Ban,
  Bot,
  Clock,
  FileQuestion,
  Gauge,
  Quote,
  ShieldCheck,
} from 'lucide-react';

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

interface FeatureCallRow {
  featureCode: string;
  totalCalls: number;
  failures: number;
  failureRatePct: number;
}

interface ErrorClassCode {
  errorCode: string;
  outcome: string;
  count: number;
  lastSeenAt: string;
}

interface ErrorClassRow {
  class: string;
  count: number;
  codes: ErrorClassCode[];
}

interface CitationCoverage {
  companionAnswers: number;
  citedAnswers: number;
  uncitedAnswers: number;
  uncitedPct: number;
  retrievalEnabled: boolean;
  messagesByThreadRole: { role: string; count: number }[];
}

interface QualitySummary {
  generatedAt: string;
  windowDays: number;
  flags: { companionEnabled: boolean; retrievalEnabled: boolean; actionsEnabled: boolean };
  calls: FeatureCallRow[];
  totals: {
    calls: number;
    failures: number;
    failureRatePct: number;
    failuresWithErrorCode: number;
  };
  errorClasses: ErrorClassRow[];
  citationCoverage: CitationCoverage;
  handoffs: {
    unresolvedTotal: number;
    unresolved: { route: string; count: number; oldestCreatedAt: string }[];
    inWindow: {
      status: string;
      route: string;
      count: number;
      distinctLearners: number;
      oldestAt: string;
    }[];
  };
  notInstrumented: NotInstrumentedSignal[];
}

interface CitedSourceRow {
  sourceKey: string;
  sourceTitle: string | null;
  citations: number;
}

interface NeverCitedSourceRow {
  sourceKey: string;
  title: string;
  professionId: string | null;
  subtestCode: string | null;
  isProprietary: boolean;
}

interface CitationReport {
  generatedAt: string;
  windowDays: number;
  citedSourceKeys: number;
  unparsableCitationRows: number;
  topCited: CitedSourceRow[];
  neverCitedCount: number;
  neverCited: NeverCitedSourceRow[];
  note: string;
}

const ERROR_CLASS_LABELS: Record<string, string> = {
  refusal: 'Refused before the provider was called',
  provider_failure: 'Provider failed',
  timeout_or_cancel: 'Timed out or cancelled',
  platform_error: 'Platform error',
  unclassified_no_code: 'Failed with no error code',
  other: 'Unclassified',
  success: 'Success',
};

const ERROR_CLASS_TONE: Record<string, 'danger' | 'warning' | 'default' | 'info'> = {
  refusal: 'warning',
  provider_failure: 'danger',
  timeout_or_cancel: 'info',
  platform_error: 'danger',
  other: 'default',
  unclassified_no_code: 'default',
};

const WINDOW_OPTIONS: { label: string; days: number }[] = [
  { label: '7 days', days: 7 },
  { label: '30 days', days: 30 },
  { label: '90 days', days: 90 },
];

/** Declared companion feature codes, so a code with no rows at all is visible. */
const DECLARED_COMPANION_FEATURE_CODES = [
  'ai_assistant.learner',
  'companion.chat.v1',
  'companion.retrieval.v1',
  'companion.action.v1',
];

function fmtInt(value: number): string {
  return new Intl.NumberFormat('en-GB').format(value);
}

function fmtPct(value: number): string {
  return `${value}%`;
}

function fmtTimestamp(value: string | null | undefined): string {
  if (!value) return '—';
  const parsed = new Date(value);
  return Number.isNaN(parsed.getTime()) ? '—' : parsed.toLocaleString();
}

function fmtAge(value: string | null | undefined): string {
  if (!value) return '—';
  const parsed = new Date(value);
  if (Number.isNaN(parsed.getTime())) return '—';
  const days = (Date.now() - parsed.getTime()) / 86_400_000;
  if (days < 1) return `${Math.max(1, Math.round(days * 24))}h`;
  return `${Math.round(days)}d`;
}

/**
 * F-127 · AI quality dashboard (SAMI handover §13.2).
 *
 * Three measured surfaces and one deliberate absence:
 *   • failures and refusals per companion feature code, from AiUsageRecords
 *     (one row per physical provider call);
 *   • citation coverage on companion answers, from AiAssistantMessages.CitationsJson;
 *   • the unresolved learner handoff backlog, from CompanionHandoffs.
 *
 * What is NOT here: answer correctness, a hallucination queue and a confidence
 * score. None of them has a data source, so the page states that explicitly at
 * the bottom rather than showing a zero that would look like a clean bill of
 * health. See NotInstrumentedPanel.
 */
export default function CompanionQualityPage() {
  const { isAuthenticated, role } = useAdminAuth();
  const [status, setStatus] = useState<PageStatus>('loading');
  const [error, setError] = useState<string | null>(null);
  const [windowDays, setWindowDays] = useState(30);
  const [summary, setSummary] = useState<QualitySummary | null>(null);
  const [citations, setCitations] = useState<CitationReport | null>(null);

  const load = useCallback(async () => {
    setStatus('loading');
    setError(null);
    try {
      const [quality, citationReport] = await Promise.all([
        apiClient.get<QualitySummary>(`/v1/admin/companion/quality/summary?days=${windowDays}`),
        apiClient.get<CitationReport>(`/v1/admin/companion/quality/citations?days=${windowDays}`),
      ]);
      setSummary(quality);
      setCitations(citationReport);
      const nothingMeasured =
        quality.totals.calls === 0 &&
        quality.citationCoverage.companionAnswers === 0 &&
        quality.handoffs.unresolvedTotal === 0;
      setStatus(nothingMeasured ? 'empty' : 'success');
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Could not read the companion quality report.');
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
    { label: 'AI Quality' },
  ];

  if (!isAuthenticated || role !== 'admin') {
    return (
      <AdminTableLayout title="Companion AI Quality" eyebrow="Learning Companion" breadcrumbs={breadcrumbs}>
        <EmptyState
          title="Admin access required"
          description="Sign in with an admin account to read the companion quality report."
          illustration={<ShieldCheck className="h-8 w-8" />}
        />
      </AdminTableLayout>
    );
  }

  const featureColumns: Column<FeatureCallRow>[] = [
    {
      key: 'featureCode',
      header: 'Feature code',
      render: (row) => <span className="font-mono text-xs text-admin-fg-strong">{row.featureCode}</span>,
    },
    {
      key: 'totalCalls',
      header: 'Calls',
      render: (row) => <span className="tabular-nums text-admin-fg-strong">{fmtInt(row.totalCalls)}</span>,
    },
    {
      key: 'failures',
      header: 'Failures',
      render: (row) =>
        row.failures === 0 ? (
          <span className="tabular-nums text-admin-fg-muted">0</span>
        ) : (
          <Badge variant={row.failureRatePct >= 20 ? 'danger' : 'warning'} intensity="tinted" size="sm">
            {fmtInt(row.failures)}
          </Badge>
        ),
    },
    {
      key: 'failureRatePct',
      header: 'Failure rate',
      render: (row) => <span className="tabular-nums text-admin-fg-muted">{fmtPct(row.failureRatePct)}</span>,
    },
  ];

  const errorColumns: Column<ErrorClassRow>[] = [
    {
      key: 'class',
      header: 'Class',
      render: (row) => (
        <Badge variant={ERROR_CLASS_TONE[row.class] ?? 'default'} intensity="tinted" size="sm">
          {ERROR_CLASS_LABELS[row.class] ?? row.class}
        </Badge>
      ),
    },
    {
      key: 'count',
      header: 'Calls',
      render: (row) => <span className="tabular-nums text-admin-fg-strong">{fmtInt(row.count)}</span>,
    },
    {
      key: 'codes',
      header: 'Error codes',
      render: (row) => (
        <div className="flex flex-wrap gap-1">
          {row.codes.map((code) => (
            <span
              key={`${row.class}-${code.errorCode}-${code.outcome}`}
              className="rounded-admin border border-admin-border bg-admin-bg-surface px-1.5 py-0.5 font-mono text-xs text-admin-fg-muted"
              title={`${code.outcome} · last seen ${fmtTimestamp(code.lastSeenAt)}`}
            >
              {code.errorCode}
              <span className="ml-1 tabular-nums text-admin-fg-strong">{fmtInt(code.count)}</span>
            </span>
          ))}
        </div>
      ),
      className: 'max-w-md',
    },
  ];

  const neverCitedColumns: Column<NeverCitedSourceRow>[] = [
    {
      key: 'title',
      header: 'Source',
      render: (row) => (
        <div className="min-w-0">
          <p className="truncate text-sm text-admin-fg-strong" title={row.title}>
            {row.title}
          </p>
          <p className="truncate font-mono text-xs text-admin-fg-muted" title={row.sourceKey}>
            {row.sourceKey}
          </p>
        </div>
      ),
    },
    {
      key: 'scope',
      header: 'Scope',
      render: (row) => (
        <span className="text-xs text-admin-fg-muted">
          {row.professionId ?? 'all professions'} · {row.subtestCode ?? 'all subtests'}
        </span>
      ),
    },
    {
      key: 'isProprietary',
      header: 'Type',
      render: (row) => (
        <Badge variant={row.isProprietary ? 'info' : 'default'} intensity="tinted" size="sm">
          {row.isProprietary ? 'Proprietary' : 'General'}
        </Badge>
      ),
    },
  ];

  const coverage = summary?.citationCoverage;
  const missingFeatureCodes = summary
    ? DECLARED_COMPANION_FEATURE_CODES.filter((code) => !summary.calls.some((row) => row.featureCode === code))
    : [];

  return (
    <AdminOperationsLayout
      title="Companion AI Quality"
      description="Refusals, provider failures and citation coverage for every companion call, read from the AI usage ledger. Nothing on this page is estimated: where a signal has no data source it is listed as not instrumented instead of shown as zero."
      eyebrow="Learning Companion"
      breadcrumbs={breadcrumbs}
    >
      <AsyncStateWrapper
        status={status}
        onRetry={load}
        errorMessage={error ?? undefined}
        emptyContent={<CompanionReportEmptyState subject="companion AI activity" windowDays={windowDays} />}
      >
        <div className="flex flex-wrap items-center justify-between gap-2">
          <p className="text-xs text-admin-fg-muted">
            Window: last {windowDays} days
            {summary ? ` · read at ${fmtTimestamp(summary.generatedAt)}` : ''}
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
                }              >
                {option.label}
              </button>
            ))}
          </div>
        </div>

        <KpiStrip className="mt-4">
          <KpiTile
            label="Companion calls"
            value={fmtInt(summary?.totals.calls ?? 0)}
            icon={<Gauge className="h-4 w-4" />}
            hint={`Last ${windowDays} days`}
          />
          <KpiTile
            label="Failed calls"
            value={fmtInt(summary?.totals.failures ?? 0)}
            tone={(summary?.totals.failures ?? 0) > 0 ? 'warning' : 'default'}
            icon={<AlertTriangle className="h-4 w-4" />}
            hint={`${fmtPct(summary?.totals.failureRatePct ?? 0)} of companion calls`}
          />
          <KpiTile
            label="Answers with no citation"
            value={fmtInt(coverage?.uncitedAnswers ?? 0)}
            tone={(coverage?.uncitedAnswers ?? 0) > 0 ? 'warning' : 'default'}
            icon={<Quote className="h-4 w-4" />}
            hint={`${fmtPct(coverage?.uncitedPct ?? 0)} of ${fmtInt(coverage?.companionAnswers ?? 0)} answers`}
          />
          <KpiTile
            label="Unresolved handoffs"
            value={fmtInt(summary?.handoffs.unresolvedTotal ?? 0)}
            tone={(summary?.handoffs.unresolvedTotal ?? 0) > 0 ? 'danger' : 'default'}
            icon={<FileQuestion className="h-4 w-4" />}
            hint="Learner escalations with no resolution, any age"
          />
        </KpiStrip>

        {summary && (
          <div className="mt-5 grid gap-4">
            <Card>
              <CardHeader>
                <CardTitle className="text-sm">
                  <Bot className="mr-2 inline-block h-4 w-4 text-admin-fg-muted" aria-hidden="true" />
                  Companion feature flags at read time
                </CardTitle>
              </CardHeader>
              <CardContent className="pt-0">
                <div className="flex flex-wrap items-center gap-2">
                  <Badge variant={summary.flags.companionEnabled ? 'success' : 'danger'} intensity="tinted" size="sm">
                    ai_learning_companion {summary.flags.companionEnabled ? 'on' : 'off'}
                  </Badge>
                  <Badge
                    variant={summary.flags.retrievalEnabled ? 'success' : 'warning'}
                    intensity="tinted"
                    size="sm"
                  >
                    companion_retrieval {summary.flags.retrievalEnabled ? 'on' : 'off'}
                  </Badge>
                  <Badge variant={summary.flags.actionsEnabled ? 'success' : 'default'} intensity="tinted" size="sm">
                    companion_actions {summary.flags.actionsEnabled ? 'on' : 'off'}
                  </Badge>
                </div>
                <p className="mt-2 text-xs text-admin-fg-muted">
                  With <span className="font-mono">companion_retrieval</span> off every answer is ungrounded and cites
                  nothing by design, so an uncited-answer count taken while it is off measures the flag, not the corpus.
                </p>
              </CardContent>
            </Card>

            <Card>
              <CardHeader>
                <CardTitle className="text-sm">Calls and failures per feature code</CardTitle>
              </CardHeader>
              <CardContent className="p-0 pt-0">
                <DataTable
                  columns={featureColumns}
                  data={summary.calls}
                  keyExtractor={(row) => row.featureCode}
                  emptyMessage={`No companion AI call was recorded in the last ${windowDays} days.`}
                  aria-label="Companion calls per feature code"
                />
              </CardContent>
            </Card>

            {missingFeatureCodes.length > 0 && (
              <Card className="border-amber-300 dark:border-amber-900/60">
                <CardContent className="p-4 sm:p-5">
                  <p className="text-sm font-semibold text-admin-fg-strong">
                    <Ban
                      className="mr-2 inline-block h-4 w-4 text-amber-700 dark:text-amber-300"
                      aria-hidden="true"
                    />
                    No usage rows exist for these declared companion feature codes in this window
                  </p>
                  <div className="mt-2 flex flex-wrap gap-1">
                    {missingFeatureCodes.map((code) => (
                      <span
                        key={code}
                        className="rounded-admin border border-admin-border px-1.5 py-0.5 font-mono text-xs text-admin-fg-muted"
                      >
                        {code}
                      </span>
                    ))}
                  </div>
                  <p className="mt-2 text-xs text-admin-fg-muted">
                    A declared code with no rows means no call site writes it yet, which is different from a code that was
                    called and failed. The learner companion turn is recorded under{' '}
                    <span className="font-mono">ai_assistant.learner</span> — that is the feature code the assistant
                    orchestrator resolves for the learner role.
                  </p>
                </CardContent>
              </Card>
            )}

            <Card>
              <CardHeader>
                <CardTitle className="text-sm">Failures by class</CardTitle>
              </CardHeader>
              <CardContent className="p-0 pt-0">
                <DataTable
                  columns={errorColumns}
                  data={summary.errorClasses}
                  keyExtractor={(row) => row.class}
                  emptyMessage="No failed companion call was recorded in this window."
                  aria-label="Companion failures by error class"
                />
              </CardContent>
            </Card>

            <div className="grid gap-4 lg:grid-cols-2">
              <Card>
                <CardHeader>
                  <CardTitle className="text-sm">Citation coverage on companion answers</CardTitle>
                </CardHeader>
                <CardContent className="pt-0">
                  {coverage ? (
                    <dl className="grid grid-cols-1 gap-x-6 gap-y-2 text-sm sm:grid-cols-2">
                      <div className="flex justify-between gap-2">
                        <dt className="text-admin-fg-muted">Companion answers</dt>
                        <dd className="tabular-nums text-admin-fg-strong">{fmtInt(coverage.companionAnswers)}</dd>
                      </div>
                      <div className="flex justify-between gap-2">
                        <dt className="text-admin-fg-muted">With citations</dt>
                        <dd className="tabular-nums text-admin-fg-strong">{fmtInt(coverage.citedAnswers)}</dd>
                      </div>
                      <div className="flex justify-between gap-2">
                        <dt className="text-admin-fg-muted">With no citation</dt>
                        <dd className="tabular-nums text-admin-fg-strong">{fmtInt(coverage.uncitedAnswers)}</dd>
                      </div>
                      <div className="flex justify-between gap-2">
                        <dt className="text-admin-fg-muted">Uncited share</dt>
                        <dd className="tabular-nums text-admin-fg-strong">{fmtPct(coverage.uncitedPct)}</dd>
                      </div>
                    </dl>
                  ) : (
                    <p className="text-xs text-admin-fg-muted">No coverage data in this window.</p>
                  )}
                  {coverage && coverage.messagesByThreadRole.length > 0 && (
                    <div className="mt-3 border-t border-admin-border pt-3">
                      <p className="text-xs font-medium text-admin-fg-muted">
                        Assistant messages in this window, by thread role (the companion rows above are the learner share
                        only)
                      </p>
                      <ul className="mt-1 space-y-0.5">
                        {coverage.messagesByThreadRole.map((row) => (
                          <li key={row.role} className="flex justify-between text-xs">
                            <span className="text-admin-fg-muted">{row.role}</span>
                            <span className="tabular-nums text-admin-fg-strong">{fmtInt(row.count)}</span>
                          </li>
                        ))}
                      </ul>
                    </div>
                  )}
                  <p className="mt-3 text-xs leading-relaxed text-admin-fg-muted">
                    &ldquo;No citation&rdquo; means the stored answer carried no citation entry. It can mean retrieval found
                    nothing, or that the retrieval flag was off, or that prompt composition failed before retrieval ran —
                    this number alone cannot tell those apart.
                  </p>
                </CardContent>
              </Card>

              <Card>
                <CardHeader>
                  <CardTitle className="text-sm">Most-cited sources</CardTitle>
                </CardHeader>
                <CardContent className="pt-0">
                  {citations && citations.topCited.length > 0 ? (
                    <>
                      <ul className="space-y-1.5">
                        {citations.topCited.slice(0, 10).map((row) => (
                          <li key={row.sourceKey} className="flex items-start justify-between gap-3">
                            <span className="min-w-0">
                              <span
                                className="block truncate text-xs text-admin-fg-strong"
                                title={row.sourceTitle ?? row.sourceKey}
                              >
                                {row.sourceTitle ?? row.sourceKey}
                              </span>
                              <span className="block truncate font-mono text-xs text-admin-fg-muted">{row.sourceKey}</span>
                            </span>
                            <span className="tabular-nums text-xs text-admin-fg-strong">{fmtInt(row.citations)}</span>
                          </li>
                        ))}
                      </ul>
                      <p className="mt-3 border-t border-admin-border pt-3 text-xs text-admin-fg-muted">
                        {fmtInt(citations.citedSourceKeys)} distinct source keys were cited;{' '}
                        {fmtInt(citations.neverCitedCount)} retrievable approved sources were never cited in this window.
                        {citations.unparsableCitationRows > 0
                          ? ` ${fmtInt(citations.unparsableCitationRows)} stored citation row(s) could not be parsed and were skipped.`
                          : ''}
                      </p>
                    </>
                  ) : (
                    <p className="text-xs text-admin-fg-muted">
                      No companion answer in this window stored a citation, so there is nothing to rank. Check the
                      retrieval flag above before reading this as a content problem.
                    </p>
                  )}
                </CardContent>
              </Card>
            </div>

            <Card>
              <CardHeader>
                <CardTitle className="text-sm">
                  <Clock className="mr-2 inline-block h-4 w-4 text-admin-fg-muted" aria-hidden="true" />
                  Unresolved learner handoffs
                </CardTitle>
              </CardHeader>
              <CardContent className="pt-0">
                {summary.handoffs.unresolved.length === 0 ? (
                  <p className="text-xs text-admin-fg-muted">
                    No unresolved handoff exists. Handoffs are learner-raised escalations; the full queue with the
                    learner&apos;s own wording is on the content-gap dashboard.
                  </p>
                ) : (
                  <div className="grid gap-3 sm:grid-cols-2">
                    {summary.handoffs.unresolved.map((row) => (
                      <div key={row.route} className="rounded-admin border border-admin-border bg-admin-bg-surface p-3">
                        <div className="flex items-center justify-between gap-2">
                          <span className="text-sm font-semibold text-admin-fg-strong">
                            {row.route === 'support' ? 'Support queue' : 'Tutor queue'}
                          </span>
                          <Badge variant="danger" intensity="tinted" size="sm">
                            {fmtInt(row.count)}
                          </Badge>
                        </div>
                        <p className="mt-1 text-xs text-admin-fg-muted">
                          Oldest: {fmtTimestamp(row.oldestCreatedAt)} ({fmtAge(row.oldestCreatedAt)} old)
                        </p>
                      </div>
                    ))}
                  </div>
                )}
              </CardContent>
            </Card>

            {citations && citations.neverCited.length > 0 && (
              <Card>
                <CardHeader>
                  <CardTitle className="text-sm">
                    Approved sources that answered nothing ({fmtInt(citations.neverCitedCount)})
                  </CardTitle>
                </CardHeader>
                <CardContent className="p-0 pt-0">
                  <DataTable
                    columns={neverCitedColumns}
                    data={citations.neverCited}
                    keyExtractor={(row) => row.sourceKey}
                    emptyMessage="Every retrievable approved source was cited at least once."
                    aria-label="Retrievable sources never cited"
                  />
                  <p className="px-4 pb-4 pt-3 text-xs text-admin-fg-muted">{citations.note}</p>
                </CardContent>
              </Card>
            )}

            <NotInstrumentedPanel signals={summary.notInstrumented} />
          </div>
        )}
      </AsyncStateWrapper>
    </AdminOperationsLayout>
  );
}
