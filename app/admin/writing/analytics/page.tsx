'use client';

import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import Link from 'next/link';
import {
  AlertTriangle,
  BarChart3,
  CheckCircle2,
  Clock3,
  Gauge,
  RefreshCw,
  ShieldAlert,
  TrendingUp,
  Users2,
} from 'lucide-react';
import {
  AdminOperationsLayout,
  BentoCell,
  BentoGrid,
  KpiStrip,
} from '@/components/admin/layout/admin-operations-layout';
import { Badge } from '@/components/admin/ui/badge';
import { Button } from '@/components/admin/ui/button';
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/admin/ui/card';
import { KpiTile } from '@/components/admin/ui/kpi-tile';
import { Skeleton } from '@/components/admin/ui/skeleton';
import {
  getWritingAdminAnalytics,
  getWritingMarkingQuality,
  type WritingAnalyticsQuery,
} from '@/lib/writing/exam-api';
import {
  WRITING_PROFESSION_LABELS,
  WRITING_PROFESSIONS,
  type WritingAdminAnalyticsDto,
  type WritingCriterionCode,
  type WritingLetterType,
  type WritingMarkingQualityDto,
  type WritingProfession,
} from '@/lib/writing/types';

const BREADCRUMBS = [
  { label: 'Admin', href: '/admin' },
  { label: 'Writing', href: '/admin/writing' },
  { label: 'Analytics' },
];

const WINDOW_OPTIONS = [7, 30, 90] as const;

const LETTER_TYPE_LABELS: Record<WritingLetterType, string> = {
  'LT-RR': 'Referral',
  'LT-UR': 'Urgent referral',
  'LT-DG': 'Discharge',
  'LT-TR': 'Transfer',
  'LT-NM': 'New management',
  'LT-RP': 'Response',
};

const LETTER_TYPE_OPTIONS: Array<{ value: WritingLetterType | ''; label: string }> = [
  { value: '', label: 'All letter types' },
  ...Object.entries(LETTER_TYPE_LABELS).map(([value, label]) => ({
    value: value as WritingLetterType,
    label,
  })),
];

const PROFESSION_OPTIONS: Array<{ value: WritingProfession | ''; label: string }> = [
  { value: '', label: 'All professions' },
  ...WRITING_PROFESSIONS.map((profession) => ({
    value: profession,
    label: WRITING_PROFESSION_LABELS[profession],
  })),
];

const CRITERION_LABELS: Record<WritingCriterionCode, string> = {
  c1: 'Purpose',
  c2: 'Content',
  c3: 'Conciseness',
  c4: 'Genre and style',
  c5: 'Organisation and layout',
  c6: 'Language',
};

type LoadStatus = 'loading' | 'success' | 'empty' | 'error';

function formatNumber(value: number | null | undefined, decimals = 1) {
  if (value == null || Number.isNaN(value)) return '--';
  return Number.isInteger(value)
    ? value.toLocaleString()
    : value.toLocaleString(undefined, { maximumFractionDigits: decimals, minimumFractionDigits: decimals });
}

function formatPercent(value: number | null | undefined, decimals = 1) {
  if (value == null || Number.isNaN(value)) return '--';
  return `${formatNumber(value, decimals)}%`;
}

function formatBand(value: number | null | undefined) {
  if (value == null || Number.isNaN(value)) return '--';
  return formatNumber(value, 0);
}

function formatMinutes(seconds: number | null | undefined) {
  if (seconds == null || Number.isNaN(seconds)) return '--';
  return `${Math.round(seconds / 60)}m`;
}

function rowToneByBand(value: number) {
  if (value >= 350) return 'success';
  if (value >= 320) return 'warning';
  return 'danger';
}

export default function AdminWritingAnalyticsOverviewPage() {
  const requestSeqRef = useRef(0);
  const [status, setStatus] = useState<LoadStatus>('loading');
  const [days, setDays] = useState<number>(30);
  const [profession, setProfession] = useState<WritingProfession | ''>('');
  const [letterType, setLetterType] = useState<WritingLetterType | ''>('');
  const [overview, setOverview] = useState<WritingAdminAnalyticsDto | null>(null);
  const [quality, setQuality] = useState<WritingMarkingQualityDto | null>(null);
  const [error, setError] = useState<string | null>(null);

  const query = useMemo<WritingAnalyticsQuery>(() => {
    const toDate = new Date();
    const fromDate = new Date(toDate);
    fromDate.setDate(fromDate.getDate() - days);
    return {
      fromDate: fromDate.toISOString(),
      toDate: toDate.toISOString(),
      profession: profession || undefined,
      letterType: letterType || undefined,
    };
  }, [days, profession, letterType]);

  const criteriaRows = useMemo(() => {
    if (!overview) return [];
    return (Object.entries(CRITERION_LABELS) as Array<[WritingCriterionCode, string]>).map(
      ([criterion, label]) => ({
        criterion,
        label,
        value: overview.averageCriteria[criterion],
      }),
    );
  }, [overview]);

  const load = useCallback(async () => {
    const requestSeq = requestSeqRef.current + 1;
    requestSeqRef.current = requestSeq;
    setStatus('loading');
    setError(null);
    setOverview(null);
    setQuality(null);

    try {
      const [nextOverview, nextQuality] = await Promise.all([
        getWritingAdminAnalytics(query),
        getWritingMarkingQuality(query),
      ]);

      if (requestSeq !== requestSeqRef.current) return;

      setOverview(nextOverview);
      setQuality(nextQuality);

      const hasData =
        nextOverview.totals.tasks > 0 ||
        nextOverview.totals.submissions > 0 ||
        nextOverview.totals.reviewed > 0;
      setStatus(hasData ? 'success' : 'empty');
    } catch (loadError) {
      if (requestSeq !== requestSeqRef.current) return;
      setStatus('error');
      setError(loadError instanceof Error ? loadError.message : 'Failed to load writing analytics.');
    }
  }, [query]);

  useEffect(() => {
    void load();
  }, [load]);

  return (
    <AdminOperationsLayout
      title="Writing analytics overview"
      description="Operational dashboard for writing outcomes, marking quality, and content-checklist risk signals."
      eyebrow="Writing analytics"
      breadcrumbs={BREADCRUMBS}
      icon={<BarChart3 className="h-5 w-5" />}
      actions={
        <div className="flex flex-wrap items-center gap-2">
          <Button asChild type="button" variant="outline" size="sm">
            <Link href="/admin/writing/analytics/rule-violations">Rule violations</Link>
          </Button>
          {WINDOW_OPTIONS.map((windowDays) => (
            <Button
              key={windowDays}
              type="button"
              size="sm"
              variant={days === windowDays ? 'primary' : 'secondary'}
              aria-pressed={days === windowDays}
              onClick={() => setDays(windowDays)}
            >
              {windowDays}d
            </Button>
          ))}
          <Button type="button" variant="secondary" size="sm" onClick={() => void load()}>
            <RefreshCw className="h-4 w-4" />
            Refresh
          </Button>
        </div>
      }
      kpis={
        status === 'success' && overview ? (
          <KpiStrip>
            <KpiTile
              label="Submissions"
              value={overview.totals.submissions}
              icon={<BarChart3 className="h-4 w-4" />}
              tone="primary"
            />
            <KpiTile
              label="Reviewed"
              value={overview.totals.reviewed}
              icon={<CheckCircle2 className="h-4 w-4" />}
              tone="success"
            />
            <KpiTile
              label="Learners"
              value={overview.totals.learners}
              icon={<Users2 className="h-4 w-4" />}
            />
            <KpiTile
              label="Avg writing time"
              value={formatMinutes(overview.writingPhaseSeconds.average)}
              icon={<Clock3 className="h-4 w-4" />}
              tone="info"
            />
            <KpiTile
              label="Abandonment"
              value={formatPercent(overview.abandonmentRatePercent)}
              icon={<AlertTriangle className="h-4 w-4" />}
              tone={overview.abandonmentRatePercent > 12 ? 'danger' : 'warning'}
            />
            <KpiTile
              label="AI vs tutor delta"
              value={formatNumber(quality?.aiVsTutorVariance.meanAbsoluteDelta, 2)}
              icon={<Gauge className="h-4 w-4" />}
              tone="default"
            />
          </KpiStrip>
        ) : undefined
      }
      primaryGrid={
        <div className="space-y-6">
          <Card>
            <CardContent className="p-4">
              <div className="flex flex-wrap items-end gap-3">
                <div>
                  <label
                    htmlFor="profession-filter"
                    className="block text-xs font-medium text-admin-fg-muted"
                  >
                    Profession filter
                  </label>
                  <select
                    id="profession-filter"
                    value={profession}
                    onChange={(event) => setProfession(event.target.value as WritingProfession | '')}
                    className="mt-1 rounded-admin border border-admin-border bg-admin-bg-surface px-3 py-2 text-sm"
                  >
                    {PROFESSION_OPTIONS.map((option) => (
                      <option key={option.value || 'all'} value={option.value}>
                        {option.label}
                      </option>
                    ))}
                  </select>
                </div>
                <div>
                  <label
                    htmlFor="letter-type-filter"
                    className="block text-xs font-medium text-admin-fg-muted"
                  >
                    Letter type filter
                  </label>
                  <select
                    id="letter-type-filter"
                    value={letterType}
                    onChange={(event) => setLetterType(event.target.value as WritingLetterType | '')}
                    className="mt-1 rounded-admin border border-admin-border bg-admin-bg-surface px-3 py-2 text-sm"
                  >
                    {LETTER_TYPE_OPTIONS.map((option) => (
                      <option key={option.value || 'all'} value={option.value}>
                        {option.label}
                      </option>
                    ))}
                  </select>
                </div>
                <div className="ml-auto">
                  <Button type="button" variant="outline" size="sm" onClick={() => void load()}>
                    Re-run analytics
                  </Button>
                </div>
              </div>
            </CardContent>
          </Card>

          {status === 'error' ? (
            <Card surface="tinted-danger" role="alert">
              <CardContent className="p-4">
                <div className="flex items-start gap-2">
                  <AlertTriangle className="mt-0.5 h-4 w-4 text-admin-danger" />
                  <div>
                    <p className="text-sm font-semibold text-admin-danger">Analytics unavailable</p>
                    <p className="mt-1 text-xs text-admin-fg-muted">{error ?? 'Please try again.'}</p>
                  </div>
                </div>
              </CardContent>
            </Card>
          ) : null}

          {status === 'loading' && !overview ? (
            <div className="grid grid-cols-1 gap-4 md:grid-cols-2 xl:grid-cols-3">
              {Array.from({ length: 6 }).map((_, index) => (
                <Skeleton key={index} className="h-32 rounded-admin" />
              ))}
            </div>
          ) : null}

          {status === 'empty' ? (
            <Card>
              <CardHeader>
                <CardTitle>No writing analytics data yet</CardTitle>
                <CardDescription>
                  Publish writing tasks and collect learner submissions to unlock cohort trends.
                </CardDescription>
              </CardHeader>
            </Card>
          ) : null}

          {status === 'success' && overview && quality ? (
            <BentoGrid>
              <BentoCell span={{ default: 12, xl: 4 }}>
                <Card>
                  <CardHeader>
                    <CardTitle>Rulebook integrity drill-down</CardTitle>
                    <CardDescription>
                      Open the dedicated violation dashboard to inspect severity and trend counts.
                    </CardDescription>
                  </CardHeader>
                  <CardContent>
                    <Button asChild variant="primary" size="sm" fullWidth>
                      <Link href="/admin/writing/analytics/rule-violations">
                        <ShieldAlert className="h-4 w-4" />
                        Open rule violation dashboard
                      </Link>
                    </Button>
                  </CardContent>
                </Card>
              </BentoCell>

              <BentoCell span={{ default: 12, xl: 8 }}>
                <Card>
                  <CardHeader>
                    <CardTitle>Marking quality</CardTitle>
                    <CardDescription>
                      Cohort-level reliability indicators for AI and tutor alignment.
                    </CardDescription>
                  </CardHeader>
                  <CardContent>
                    <div className="grid gap-3 sm:grid-cols-3">
                      <div className="rounded-admin border border-admin-border bg-admin-bg-surface p-3">
                        <p className="text-xs uppercase tracking-wide text-admin-fg-muted">AI vs tutor mean delta</p>
                        <p className="mt-1 text-xl font-semibold text-admin-fg-strong">
                          {formatNumber(quality.aiVsTutorVariance.meanAbsoluteDelta, 2)}
                        </p>
                        <p className="text-xs text-admin-fg-muted">{quality.aiVsTutorVariance.samples} scored samples</p>
                      </div>
                      <div className="rounded-admin border border-admin-border bg-admin-bg-surface p-3">
                        <p className="text-xs uppercase tracking-wide text-admin-fg-muted">Moderations triggered</p>
                        <p className="mt-1 text-xl font-semibold text-admin-fg-strong">{quality.moderationsTriggered}</p>
                      </div>
                      <div className="rounded-admin border border-admin-border bg-admin-bg-surface p-3">
                        <p className="text-xs uppercase tracking-wide text-admin-fg-muted">Review turnaround</p>
                        <p className="mt-1 text-xl font-semibold text-admin-fg-strong">
                          {formatNumber(quality.averageReviewTurnaroundHours, 1)}h
                        </p>
                      </div>
                    </div>
                  </CardContent>
                </Card>
              </BentoCell>

              <BentoCell span={{ default: 12, xl: 6 }}>
                <Card>
                  <CardHeader>
                    <CardTitle>Average band by profession</CardTitle>
                  </CardHeader>
                  <CardContent>
                    <div className="space-y-2">
                      {overview.averageBandByProfession.map((row) => (
                        <div
                          key={row.profession}
                          className="flex items-center justify-between rounded-admin border border-admin-border bg-admin-bg-surface p-2.5"
                        >
                          <div>
                            <p className="text-sm font-medium text-admin-fg-strong">
                              {WRITING_PROFESSION_LABELS[row.profession]}
                            </p>
                            <p className="text-xs text-admin-fg-muted">{row.attempts} attempts</p>
                          </div>
                          <Badge variant={rowToneByBand(row.averageBand)}>{formatBand(row.averageBand)}</Badge>
                        </div>
                      ))}
                    </div>
                  </CardContent>
                </Card>
              </BentoCell>

              <BentoCell span={{ default: 12, xl: 6 }}>
                <Card>
                  <CardHeader>
                    <CardTitle>Average band by letter type</CardTitle>
                  </CardHeader>
                  <CardContent>
                    <div className="space-y-2">
                      {overview.averageBandByLetterType.map((row) => (
                        <div
                          key={row.letterType}
                          className="flex items-center justify-between rounded-admin border border-admin-border bg-admin-bg-surface p-2.5"
                        >
                          <div>
                            <p className="text-sm font-medium text-admin-fg-strong">
                              {LETTER_TYPE_LABELS[row.letterType]}
                            </p>
                            <p className="text-xs text-admin-fg-muted">{row.attempts} attempts</p>
                          </div>
                          <Badge variant={rowToneByBand(row.averageBand)}>{formatBand(row.averageBand)}</Badge>
                        </div>
                      ))}
                    </div>
                  </CardContent>
                </Card>
              </BentoCell>

              <BentoCell span={{ default: 12, xl: 8 }}>
                <Card>
                  <CardHeader>
                    <CardTitle>Hardest tasks</CardTitle>
                    <CardDescription>Lowest cohort bands across active authored tasks.</CardDescription>
                  </CardHeader>
                  <CardContent>
                    <div className="overflow-x-auto">
                      <table className="w-full text-sm">
                        <thead>
                          <tr className="border-b border-admin-border text-left text-xs text-admin-fg-muted">
                            <th scope="col" className="py-2 pr-3">Task</th>
                            <th scope="col" className="py-2 pr-3 text-right">Attempts</th>
                            <th scope="col" className="py-2 pr-3 text-right">Avg band</th>
                          </tr>
                        </thead>
                        <tbody>
                          {overview.hardestTasks.map((row) => (
                            <tr key={row.taskId} className="border-b border-admin-border/50">
                              <td className="py-2 pr-3 text-admin-fg-strong">{row.title}</td>
                              <td className="py-2 pr-3 text-right text-admin-fg-muted">{row.attempts}</td>
                              <td className="py-2 pr-3 text-right font-medium text-admin-fg-strong">
                                {formatBand(row.averageBand)}
                              </td>
                            </tr>
                          ))}
                        </tbody>
                      </table>
                    </div>
                  </CardContent>
                </Card>
              </BentoCell>

              <BentoCell span={{ default: 12, xl: 4 }}>
                <Card>
                  <CardHeader>
                    <CardTitle>Criteria disagreement</CardTitle>
                    <CardDescription>Mean absolute deltas between first marks and final decisions.</CardDescription>
                  </CardHeader>
                  <CardContent className="space-y-2">
                    {quality.criteriaDisagreement.map((row) => (
                      <div
                        key={row.criterion}
                        className="flex items-center justify-between rounded-admin border border-admin-border bg-admin-bg-surface px-3 py-2"
                      >
                        <span className="text-sm text-admin-fg-strong">{CRITERION_LABELS[row.criterion]}</span>
                        <Badge variant="warning">{formatNumber(row.meanAbsoluteDelta, 2)}</Badge>
                      </div>
                    ))}
                  </CardContent>
                </Card>
              </BentoCell>

              <BentoCell span={{ default: 12, xl: 6 }}>
                <Card>
                  <CardHeader>
                    <CardTitle>Criteria averages</CardTitle>
                    <CardDescription>Current cohort-level raw means by OET criterion.</CardDescription>
                  </CardHeader>
                  <CardContent>
                    <div className="grid gap-2 sm:grid-cols-2">
                      {criteriaRows.map((row) => (
                        <div
                          key={row.criterion}
                          className="rounded-admin border border-admin-border bg-admin-bg-surface px-3 py-2"
                        >
                          <p className="text-xs uppercase tracking-wide text-admin-fg-muted">{row.label}</p>
                          <p className="mt-1 text-lg font-semibold text-admin-fg-strong">
                            {formatNumber(row.value, 2)}
                          </p>
                        </div>
                      ))}
                    </div>
                  </CardContent>
                </Card>
              </BentoCell>

              <BentoCell span={{ default: 12, xl: 6 }}>
                <Card>
                  <CardHeader>
                    <CardTitle>Language hotspots</CardTitle>
                  </CardHeader>
                  <CardContent className="space-y-2">
                    {overview.commonLanguageErrors.slice(0, 8).map((row) => (
                      <div
                        key={row.ruleId}
                        className="rounded-admin border border-admin-border bg-admin-bg-surface px-3 py-2"
                      >
                        <div className="flex items-center justify-between gap-2">
                          <p className="text-sm font-medium text-admin-fg-strong">{row.ruleText}</p>
                          <Badge variant="danger">{row.count}</Badge>
                        </div>
                        <p className="mt-1 text-xs text-admin-fg-muted">
                          {row.ruleId}
                          {row.criterion ? ` · ${CRITERION_LABELS[row.criterion]}` : ''}
                        </p>
                      </div>
                    ))}
                  </CardContent>
                </Card>
              </BentoCell>

              <BentoCell span={{ default: 12 }}>
                <Card>
                  <CardHeader>
                    <CardTitle>Content checklist hotspots</CardTitle>
                    <CardDescription>
                      Repeatedly omitted content and recurrent irrelevant details.
                    </CardDescription>
                  </CardHeader>
                  <CardContent>
                    <div className="grid gap-3 md:grid-cols-2">
                      <div className="space-y-2">
                        <p className="text-xs font-semibold uppercase tracking-wide text-admin-fg-muted">
                          Most missing
                        </p>
                        {overview.commonMissingContent.slice(0, 6).map((item) => (
                          <div
                            key={`missing-${item.itemText}`}
                            className="flex items-center justify-between rounded-admin border border-admin-border bg-admin-bg-surface px-3 py-2"
                          >
                            <span className="text-sm text-admin-fg-strong">{item.itemText}</span>
                            <Badge variant="warning">{item.count}</Badge>
                          </div>
                        ))}
                      </div>
                      <div className="space-y-2">
                        <p className="text-xs font-semibold uppercase tracking-wide text-admin-fg-muted">
                          Most irrelevant
                        </p>
                        {overview.commonIrrelevantContent.slice(0, 6).map((item) => (
                          <div
                            key={`irrelevant-${item.itemText}`}
                            className="flex items-center justify-between rounded-admin border border-admin-border bg-admin-bg-surface px-3 py-2"
                          >
                            <span className="text-sm text-admin-fg-strong">{item.itemText}</span>
                            <Badge variant="info">{item.count}</Badge>
                          </div>
                        ))}
                      </div>
                    </div>
                  </CardContent>
                </Card>
              </BentoCell>
            </BentoGrid>
          ) : null}
        </div>
      }
      secondaryGrid={
        status === 'success' && overview ? (
          <Card>
            <CardHeader>
              <CardTitle>Word-count distribution</CardTitle>
              <CardDescription>Submitted drafts grouped by word-count bucket.</CardDescription>
            </CardHeader>
            <CardContent>
              <div className="space-y-2">
                {overview.wordCountDistribution.map((bucket) => {
                  const total = overview.wordCountDistribution.reduce((sum, row) => sum + row.count, 0);
                  const width = total > 0 ? Math.max(6, (bucket.count / total) * 100) : 0;
                  return (
                    <div key={bucket.bucketLabel}>
                      <div className="mb-1 flex items-center justify-between text-xs text-admin-fg-muted">
                        <span>{bucket.bucketLabel}</span>
                        <span>{bucket.count}</span>
                      </div>
                      <div className="h-2 w-full overflow-hidden rounded-full bg-admin-bg-subtle">
                        <div
                          className="h-full rounded-full bg-[var(--admin-primary)]"
                          style={{ width: `${width}%` }}
                          aria-hidden="true"
                        />
                      </div>
                    </div>
                  );
                })}
              </div>
              <div className="mt-4 grid gap-2 sm:grid-cols-2">
                <div className="rounded-admin border border-admin-border bg-admin-bg-surface px-3 py-2">
                  <p className="text-xs uppercase tracking-wide text-admin-fg-muted">Median writing time</p>
                  <p className="mt-1 text-sm font-semibold text-admin-fg-strong">
                    {formatMinutes(overview.writingPhaseSeconds.median)}
                  </p>
                </div>
                <div className="rounded-admin border border-admin-border bg-admin-bg-surface px-3 py-2">
                  <p className="text-xs uppercase tracking-wide text-admin-fg-muted">Resubmission uplift</p>
                  <p className="mt-1 text-sm font-semibold text-admin-fg-strong">
                    <TrendingUp className="mr-1 inline h-3.5 w-3.5" />
                    {formatNumber(overview.resubmissionImprovementAverage, 1)} points
                  </p>
                </div>
              </div>
            </CardContent>
          </Card>
        ) : undefined
      }
    />
  );
}