'use client';

import { useEffect, useMemo, useState } from 'react';
import { AlertTriangle, CheckCircle2, Download, FileText, Gauge, ListChecks, Target } from 'lucide-react';
import { Badge } from '@/components/admin/ui/badge';
import { Button } from '@/components/admin/ui/button';
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/admin/ui/card';
import { KpiTile } from '@/components/admin/ui/kpi-tile';
import { Skeleton } from '@/components/admin/ui/skeleton';
import { AdminOperationsLayout, KpiStrip, BentoGrid, BentoCell } from '@/components/admin/layout/admin-operations-layout';
import { exportToCsv, formatDateForExport } from '@/lib/csv-export';
import { useAdminAuth } from '@/lib/hooks/use-admin-auth';
import { getReadingAdminAnalytics, type ReadingAdminAnalyticsDto } from '@/lib/reading-authoring-api';

type PageStatus = 'loading' | 'success' | 'empty' | 'error';

const windowOptions = [7, 30, 90];

function formatPercent(value: number | null | undefined) {
  if (value == null) return '--';
  return `${Number.isInteger(value) ? value.toFixed(0) : value.toFixed(1)}%`;
}

function formatNumber(value: number | null | undefined) {
  if (value == null) return '--';
  return Number.isInteger(value) ? value.toLocaleString() : value.toFixed(1);
}

function formatMinutes(seconds: number | null | undefined) {
  if (seconds == null) return '--';
  return `${Math.round(seconds / 60)}m`;
}

function AccuracyBar({ value }: { value: number | null }) {
  const width = value == null ? 0 : Math.max(0, Math.min(100, value));
  return (
    <div className="h-2 w-full overflow-hidden rounded-full bg-admin-bg-subtle" aria-hidden="true">
      <div className="h-full rounded-full bg-[var(--admin-primary)]" style={{ width: `${width}%` }} />
    </div>
  );
}

function toneToVariant(tone: 'default' | 'success' | 'warning' | 'danger' | undefined): 'primary' | 'success' | 'warning' | 'danger' {
  if (tone === 'success') return 'success';
  if (tone === 'warning') return 'warning';
  if (tone === 'danger') return 'danger';
  return 'primary';
}

function MetricRow({
  label,
  meta,
  value,
  opportunities,
}: {
  label: string;
  meta: string;
  value: number | null;
  opportunities: number;
}) {
  return (
    <div className="grid gap-3 rounded-admin border border-admin-border bg-admin-bg-surface p-3 sm:grid-cols-[minmax(0,1fr)_9rem] sm:items-center">
      <div className="min-w-0 space-y-1">
        <div className="flex flex-wrap items-center gap-2">
          <p className="font-semibold text-admin-fg-strong">{label}</p>
          <Badge variant="default" intensity="tinted" size="sm">{opportunities} signals</Badge>
        </div>
        <p className="text-sm text-admin-fg-muted">{meta}</p>
      </div>
      <div className="space-y-1.5">
        <div className="text-right text-sm font-semibold text-admin-fg-strong">{formatPercent(value)}</div>
        <AccuracyBar value={value} />
      </div>
    </div>
  );
}

export default function ReadingAnalyticsPage() {
  const { isAuthenticated, role } = useAdminAuth();
  const [status, setStatus] = useState<PageStatus>('loading');
  const [days, setDays] = useState(30);
  const [analytics, setAnalytics] = useState<ReadingAdminAnalyticsDto | null>(null);

  useEffect(() => {
    if (!isAuthenticated || role !== 'admin') {
      return;
    }

    let cancelled = false;

    async function loadAnalytics() {
      setStatus('loading');
      try {
        const result = await getReadingAdminAnalytics(days);
        if (cancelled) return;
        setAnalytics(result);
        setStatus(result.summary.totalPapers > 0 ? 'success' : 'empty');
      } catch (error) {
        console.error(error);
        if (!cancelled) setStatus('error');
      }
    }

    loadAnalytics();
    return () => {
      cancelled = true;
    };
  }, [days, isAuthenticated, role]);

  const exportRows = useMemo(() => {
    if (!analytics) return [];
    return analytics.papers.map((paper) => ({
      title: paper.title,
      status: paper.status,
      difficulty: paper.difficulty,
      examReady: paper.isExamReady,
      questionCount: paper.questionCount,
      attemptCount: paper.attemptCount,
      submittedCount: paper.submittedCount,
      averageRawScore: paper.averageRawScore,
      averageScaledScore: paper.averageScaledScore,
      passRatePercent: paper.passRatePercent,
      averageCompletionSeconds: paper.averageCompletionSeconds,
    }));
  }, [analytics]);

  if (!isAuthenticated || role !== 'admin') return null;

  const breadcrumbs = [
    { label: 'Admin', href: '/admin' },
    { label: 'Analytics', href: '/admin' },
    { label: 'Reading' },
  ];

  return (
    <AdminOperationsLayout
      title="Reading Analytics"
      description="Monitor paper readiness, score outcomes, weakest skills, and item-level quality signals from submitted Reading attempts."
      eyebrow="Analytics"
      breadcrumbs={breadcrumbs}
      actions={
        <div className="flex flex-wrap items-center gap-2">
          {windowOptions.map((option) => (
            <Button
              key={option}
              type="button"
              size="sm"
              variant={days === option ? 'primary' : 'secondary'}
              aria-pressed={days === option}
              onClick={() => setDays(option)}
            >
              {option}d
            </Button>
          ))}
          <Button
            type="button"
            size="sm"
            variant="secondary"
            disabled={!analytics || exportRows.length === 0}
            onClick={() => exportToCsv(exportRows, `reading-analytics-${days}d-${formatDateForExport(new Date())}.csv`)}
          >
            <Download className="h-4 w-4" />
            Export CSV
          </Button>
        </div>
      }
      kpis={
        status === 'success' && analytics ? (
          <KpiStrip>
            <KpiTile label="Exam-ready Papers" value={`${analytics.summary.examReadyPapers}/${analytics.summary.totalPapers}`} icon={<FileText className="h-4 w-4" />} tone={analytics.summary.examReadyPapers === analytics.summary.totalPapers ? 'success' : 'warning'} />
            <KpiTile label="Submitted Attempts" value={analytics.summary.submittedAttempts} icon={<ListChecks className="h-4 w-4" />} tone="info" />
            <KpiTile label="Average Scaled" value={formatNumber(analytics.summary.averageScaledScore)} icon={<Gauge className="h-4 w-4" />} tone="primary" />
            <KpiTile label="Pass Rate" value={formatPercent(analytics.summary.passRatePercent)} icon={<Target className="h-4 w-4" />} tone={(analytics.summary.passRatePercent ?? 100) >= 70 ? 'success' : 'warning'} />
          </KpiStrip>
        ) : null
      }
      primaryGrid={
        <div className="space-y-8">
          {/* legacy body */}

      {status === 'loading' ? (
        <div className="grid gap-4 md:grid-cols-2 xl:grid-cols-4">
          {Array.from({ length: 4 }).map((_, index) => <Skeleton key={index} className="h-32 rounded-admin" />)}
        </div>
      ) : null}

      {status === 'error' ? (
        <Card>
          <CardHeader><CardTitle>Reading analytics unavailable</CardTitle></CardHeader>
          <CardContent>
            <p className="text-sm text-admin-fg-muted">The analytics service could not be loaded. Try again after the API is available.</p>
          </CardContent>
        </Card>
      ) : null}

      {status === 'empty' ? (
        <Card>
          <CardHeader><CardTitle>No Reading analytics yet</CardTitle></CardHeader>
          <CardContent>
            <p className="text-sm text-admin-fg-muted">Create and publish Reading papers, then learner submissions will populate this dashboard.</p>
          </CardContent>
        </Card>
      ) : null}

      {status === 'success' && analytics ? (
        <>
          <BentoGrid>
            <BentoCell span={{ default: 12, xl: 6 }}>
              <Card>
                <CardHeader>
                  <CardTitle>Part Performance</CardTitle>
                  <CardDescription>Accuracy treats unanswered submitted items as missed opportunities, which keeps the signal honest for exam-style attempts.</CardDescription>
                </CardHeader>
                <CardContent>
                  <div className="space-y-3">
                    {analytics.partBreakdown.map((part) => (
                      <MetricRow
                        key={part.partCode}
                        label={`Part ${part.partCode}`}
                        meta={`${part.questionCount} authored items, ${part.correctCount} correct, ${part.unansweredCount} unanswered`}
                        value={part.accuracyPercent}
                        opportunities={part.opportunities}
                      />
                    ))}
                  </div>
                </CardContent>
              </Card>
            </BentoCell>
            <BentoCell span={{ default: 12, xl: 6 }}>
              <Card>
                <CardHeader>
                  <CardTitle>Skill Breakdown</CardTitle>
                  <CardDescription>Lowest-accuracy tags rise to the top so content editors can target remediation and question QA.</CardDescription>
                </CardHeader>
                <CardContent>
                  <div className="space-y-3">
                    {analytics.skillBreakdown.slice(0, 6).map((skill) => (
                      <MetricRow
                        key={skill.label}
                        label={skill.label}
                        meta={`${skill.questionCount} items, ${skill.correctCount} correct, ${skill.unansweredCount} unanswered`}
                        value={skill.accuracyPercent}
                        opportunities={skill.opportunities}
                      />
                    ))}
                  </div>
                </CardContent>
              </Card>
            </BentoCell>
          </BentoGrid>

          <BentoGrid>
            <BentoCell span={{ default: 12, xl: 8 }}>
              <Card>
                <CardHeader>
                  <CardTitle>Hardest Questions</CardTitle>
                  <CardDescription>Items are ranked by lowest accuracy, with answered evidence preferred over untouched items.</CardDescription>
                </CardHeader>
                <CardContent>
                  <div className="space-y-3">
                    {analytics.hardestQuestions.length > 0 ? analytics.hardestQuestions.slice(0, 8).map((question) => (
                      <div key={question.questionId} className="rounded-admin border border-admin-border bg-admin-bg-surface p-3">
                        <div className="flex flex-col gap-3 sm:flex-row sm:items-start sm:justify-between">
                          <div className="min-w-0 space-y-1">
                            <div className="flex flex-wrap items-center gap-2">
                              <p className="font-semibold text-admin-fg-strong">{question.label}</p>
                              <Badge variant="default" intensity="tinted" size="sm">{question.skillTag}</Badge>
                              <Badge variant="default" intensity="tinted" size="sm">{question.questionType}</Badge>
                            </div>
                            <p className="text-sm text-admin-fg-muted">{question.paperTitle}</p>
                            <p className="line-clamp-2 text-sm text-admin-fg-default">{question.stem}</p>
                          </div>
                          <div className="min-w-32 space-y-1 text-right">
                            <p className="text-sm font-semibold text-admin-fg-strong">{formatPercent(question.accuracyPercent)}</p>
                            <p className="text-xs text-admin-fg-muted">{question.correctCount}/{question.opportunities} correct</p>
                          </div>
                        </div>
                      </div>
                    )) : <p className="text-sm text-admin-fg-muted">No submitted question evidence in this window.</p>}
                  </div>
                </CardContent>
              </Card>
            </BentoCell>
            <BentoCell span={{ default: 12, xl: 4 }}>
              <Card>
                <CardHeader>
                  <CardTitle>Action Insights</CardTitle>
                  <CardDescription>Operational cues generated from the current Reading evidence window.</CardDescription>
                </CardHeader>
                <CardContent>
                  <div className="space-y-3">
                    {analytics.actionInsights.map((insight) => (
                      <div key={insight.id} className="rounded-admin border border-admin-border bg-admin-bg-surface p-3">
                        <div className="flex items-start justify-between gap-3">
                          <div className="space-y-1">
                            <p className="font-semibold text-admin-fg-strong">{insight.title}</p>
                            <p className="text-sm text-admin-fg-muted">{insight.description}</p>
                          </div>
                          <Badge variant={toneToVariant(insight.tone as any)} intensity="tinted" size="sm" className="capitalize">{insight.tone}</Badge>
                        </div>
                      </div>
                    ))}
                  </div>
                </CardContent>
              </Card>
            </BentoCell>
          </BentoGrid>

          {analytics.distractorTraps.length > 0 ? (
            <Card>
              <CardHeader>
                <CardTitle>Distractor Traps</CardTitle>
                <CardDescription>Wrong options ranked by how often the cohort fell for them. Categories (Opposite, TooBroad, etc.) come from each option&apos;s authored distractor metadata.</CardDescription>
              </CardHeader>
              <CardContent>
                <div className="overflow-x-auto">
                  <table className="min-w-full divide-y divide-admin-border text-sm">
                    <thead>
                      <tr className="text-left text-xs font-semibold uppercase tracking-[0.12em] text-admin-fg-muted">
                        <th className="py-3 pr-4">Paper / question</th>
                        <th className="px-4 py-3">Part</th>
                        <th className="px-4 py-3">Option</th>
                        <th className="px-4 py-3">Category</th>
                        <th className="px-4 py-3">Selected</th>
                        <th className="px-4 py-3">Trap rate</th>
                      </tr>
                    </thead>
                    <tbody className="divide-y divide-admin-border" data-testid="distractor-traps-rows">
                      {analytics.distractorTraps.slice(0, 10).map((trap) => (
                        <tr key={`${trap.questionId}-${trap.category}-${trap.optionKey}`}>
                          <td className="py-3 pr-4 align-top">
                            <p className="font-semibold text-admin-fg-strong">{trap.paperTitle || 'Unknown paper'}</p>
                            <p className="line-clamp-2 text-xs text-admin-fg-muted">{trap.stem}</p>
                          </td>
                          <td className="px-4 py-3 align-top">
                            <Badge variant="default" intensity="tinted" size="sm">Part {trap.partCode}</Badge>
                          </td>
                          <td className="px-4 py-3 align-top font-mono text-xs text-admin-fg-strong">{trap.optionKey}</td>
                          <td className="px-4 py-3 align-top">
                            <Badge variant="warning" intensity="tinted" size="sm">{trap.category}</Badge>
                          </td>
                          <td className="px-4 py-3 align-top text-admin-fg-strong">
                            {trap.selectedCount}
                            {trap.opportunities > 0 ? <span className="text-xs text-admin-fg-muted"> / {trap.opportunities}</span> : null}
                          </td>
                          <td className="px-4 py-3 align-top text-admin-fg-strong">
                            <div className="flex items-center justify-end gap-2">
                              <span className="font-semibold">{formatPercent(trap.selectionRatePercent)}</span>
                              <div className="h-2 w-16 overflow-hidden rounded-full bg-admin-bg-subtle" aria-hidden="true">
                                <div
                                  className="h-full rounded-full bg-[var(--admin-warning)]"
                                  style={{ width: `${Math.min(100, Math.max(0, trap.selectionRatePercent ?? 0))}%` }}
                                />
                              </div>
                            </div>
                          </td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                </div>
              </CardContent>
            </Card>
          ) : null}

          <Card>
            <CardHeader>
              <CardTitle>Paper Quality</CardTitle>
              <CardDescription>Per-paper readiness and outcome signals for authoring QA and content operations.</CardDescription>
            </CardHeader>
            <CardContent>
              <div className="overflow-x-auto">
                <table className="min-w-full divide-y divide-admin-border text-sm">
                  <thead>
                    <tr className="text-left text-xs font-semibold uppercase tracking-[0.12em] text-admin-fg-muted">
                      <th className="py-3 pr-4">Paper</th>
                      <th className="px-4 py-3">Shape</th>
                      <th className="px-4 py-3">Attempts</th>
                      <th className="px-4 py-3">Avg scaled</th>
                      <th className="px-4 py-3">Pass rate</th>
                      <th className="px-4 py-3">Avg time</th>
                    </tr>
                  </thead>
                  <tbody className="divide-y divide-admin-border">
                    {analytics.papers.map((paper) => (
                      <tr key={paper.paperId}>
                        <td className="py-3 pr-4">
                          <p className="font-semibold text-admin-fg-strong">{paper.title}</p>
                          <p className="text-xs text-admin-fg-muted">{paper.status} · {paper.difficulty}</p>
                        </td>
                        <td className="px-4 py-3">
                          <Badge variant={paper.isExamReady ? 'success' : 'warning'} intensity="tinted" size="sm">
                            {paper.partACount}+{paper.partBCount}+{paper.partCCount}
                          </Badge>
                        </td>
                        <td className="px-4 py-3 text-admin-fg-strong">{paper.submittedCount}/{paper.attemptCount}</td>
                        <td className="px-4 py-3 text-admin-fg-strong">{formatNumber(paper.averageScaledScore)}</td>
                        <td className="px-4 py-3 text-admin-fg-strong">{formatPercent(paper.passRatePercent)}</td>
                        <td className="px-4 py-3 text-admin-fg-strong">{formatMinutes(paper.averageCompletionSeconds)}</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            </CardContent>
          </Card>

          {analytics.modeBreakdown.length > 0 ? (
            <Card>
              <CardHeader>
                <CardTitle>Attempt Modes</CardTitle>
                <CardDescription>Exam, learning, drill, mini-test, and error-bank activity in the selected window.</CardDescription>
              </CardHeader>
              <CardContent>
                <div className="grid gap-3 md:grid-cols-2 xl:grid-cols-5">
                  {analytics.modeBreakdown.map((mode) => (
                    <div key={mode.mode} className="rounded-admin border border-admin-border bg-admin-bg-surface p-3">
                      <div className="flex items-center justify-between gap-2">
                        <p className="font-semibold text-admin-fg-strong">{mode.mode}</p>
                        {mode.passRatePercent != null && (mode.passRatePercent >= 70 ? <CheckCircle2 className="h-4 w-4 text-[var(--admin-success)]" /> : <AlertTriangle className="h-4 w-4 text-[var(--admin-warning)]" />)}
                      </div>
                      <p className="mt-2 text-sm text-admin-fg-muted">{mode.submittedCount}/{mode.attemptCount} submitted</p>
                      <p className="mt-1 text-sm font-semibold text-admin-fg-strong">{formatPercent(mode.passRatePercent)} pass</p>
                    </div>
                  ))}
                </div>
              </CardContent>
            </Card>
          ) : null}
        </>
      ) : null}
        </div>
      }
    />
  );
}