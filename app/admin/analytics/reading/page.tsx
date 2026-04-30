'use client';

import { useEffect, useMemo, useState } from 'react';
import { AlertTriangle, BarChart3, CheckCircle2, Download, FileText, Gauge, ListChecks, Target } from 'lucide-react';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Skeleton } from '@/components/ui/skeleton';
import { MotionSection } from '@/components/ui/motion-primitives';
import {
  AdminRouteFreshnessBadge,
  AdminRouteHero,
  AdminRoutePanel,
  AdminRouteSummaryCard,
  AdminRouteWorkspace,
} from '@/components/domain/admin-route-surface';
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

function toneVariant(tone: string): 'outline' | 'danger' | 'warning' | 'success' {
  if (tone === 'danger') return 'danger';
  if (tone === 'success') return 'success';
  if (tone === 'warning') return 'warning';
  return 'outline';
}

function AccuracyBar({ value }: { value: number | null }) {
  const width = value == null ? 0 : Math.max(0, Math.min(100, value));
  return (
    <div className="h-2 w-full overflow-hidden rounded-full bg-muted" aria-hidden="true">
      <div className="h-full rounded-full bg-primary" style={{ width: `${width}%` }} />
    </div>
  );
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
    <div className="grid gap-3 rounded-lg border border-border bg-background-light p-3 sm:grid-cols-[minmax(0,1fr)_9rem] sm:items-center">
      <div className="min-w-0 space-y-1">
        <div className="flex flex-wrap items-center gap-2">
          <p className="font-semibold text-navy">{label}</p>
          <Badge variant="outline" className="text-[10px]">{opportunities} signals</Badge>
        </div>
        <p className="text-sm text-muted">{meta}</p>
      </div>
      <div className="space-y-1.5">
        <div className="text-right text-sm font-semibold text-navy">{formatPercent(value)}</div>
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

  return (
    <AdminRouteWorkspace role="main" aria-label="Reading analytics">
      <AdminRouteHero
        eyebrow="Analytics"
        icon={BarChart3}
        accent="navy"
        title="Reading Analytics"
        description="Monitor paper readiness, score outcomes, weakest skills, and item-level quality signals from submitted Reading attempts."
        aside={(
          <div className="rounded-2xl border border-border bg-background-light p-4 shadow-sm">
            <div className="flex flex-wrap items-center gap-2">
              {windowOptions.map((option) => (
                <Button
                  key={option}
                  type="button"
                  size="sm"
                  variant={days === option ? 'primary' : 'outline'}
                  aria-pressed={days === option}
                  onClick={() => setDays(option)}
                >
                  {option}d
                </Button>
              ))}
              <Button
                type="button"
                size="sm"
                variant="outline"
                className="gap-2"
                disabled={!analytics || exportRows.length === 0}
                onClick={() => exportToCsv(exportRows, `reading-analytics-${days}d-${formatDateForExport(new Date())}.csv`)}
              >
                <Download className="h-4 w-4" />
                Export CSV
              </Button>
            </div>
            <div className="mt-3">
              <AdminRouteFreshnessBadge value={analytics?.generatedAt} />
            </div>
          </div>
        )}
      />

      {status === 'loading' ? (
        <div className="grid gap-4 md:grid-cols-2 xl:grid-cols-4">
          {Array.from({ length: 4 }).map((_, index) => <Skeleton key={index} className="h-32 rounded-xl" />)}
        </div>
      ) : null}

      {status === 'error' ? (
        <AdminRoutePanel title="Reading analytics unavailable">
          <p className="text-sm text-muted">The analytics service could not be loaded. Try again after the API is available.</p>
        </AdminRoutePanel>
      ) : null}

      {status === 'empty' ? (
        <AdminRoutePanel title="No Reading analytics yet">
          <p className="text-sm text-muted">Create and publish Reading papers, then learner submissions will populate this dashboard.</p>
        </AdminRoutePanel>
      ) : null}

      {status === 'success' && analytics ? (
        <div className="space-y-8">
          <MotionSection delayIndex={0}>
            <div className="grid gap-4 md:grid-cols-2 xl:grid-cols-4">
              <AdminRouteSummaryCard label="Exam-ready Papers" value={`${analytics.summary.examReadyPapers}/${analytics.summary.totalPapers}`} hint={`${analytics.summary.publishedPapers} published`} icon={<FileText className="h-5 w-5" />} tone={analytics.summary.examReadyPapers === analytics.summary.totalPapers ? 'success' : 'warning'} />
              <AdminRouteSummaryCard label="Submitted Attempts" value={analytics.summary.submittedAttempts} hint={`${analytics.summary.activeAttempts} active in window`} icon={<ListChecks className="h-5 w-5" />} />
              <AdminRouteSummaryCard label="Average Scaled" value={formatNumber(analytics.summary.averageScaledScore)} hint={`Raw ${formatNumber(analytics.summary.averageRawScore)}/42`} icon={<Gauge className="h-5 w-5" />} />
              <AdminRouteSummaryCard label="Pass Rate" value={formatPercent(analytics.summary.passRatePercent)} hint={`${formatPercent(analytics.summary.unansweredRatePercent)} unanswered rate`} icon={<Target className="h-5 w-5" />} tone={(analytics.summary.passRatePercent ?? 100) >= 70 ? 'success' : 'warning'} />
            </div>
          </MotionSection>

          <MotionSection delayIndex={1}>
            <div className="grid gap-6 xl:grid-cols-2">
              <AdminRoutePanel title="Part Performance" description="Accuracy treats unanswered submitted items as missed opportunities, which keeps the signal honest for exam-style attempts.">
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
              </AdminRoutePanel>

              <AdminRoutePanel title="Skill Breakdown" description="Lowest-accuracy tags rise to the top so content editors can target remediation and question QA.">
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
              </AdminRoutePanel>
            </div>
          </MotionSection>

          <MotionSection delayIndex={2}>
            <div className="grid gap-6 xl:grid-cols-[1.25fr_0.75fr]">
              <AdminRoutePanel title="Hardest Questions" description="Items are ranked by lowest accuracy, with answered evidence preferred over untouched items.">
                <div className="space-y-3">
                  {analytics.hardestQuestions.length > 0 ? analytics.hardestQuestions.slice(0, 8).map((question) => (
                    <div key={question.questionId} className="rounded-lg border border-border bg-background-light p-3">
                      <div className="flex flex-col gap-3 sm:flex-row sm:items-start sm:justify-between">
                        <div className="min-w-0 space-y-1">
                          <div className="flex flex-wrap items-center gap-2">
                            <p className="font-semibold text-navy">{question.label}</p>
                            <Badge variant="outline" className="text-[10px]">{question.skillTag}</Badge>
                            <Badge variant="outline" className="text-[10px]">{question.questionType}</Badge>
                          </div>
                          <p className="text-sm text-muted">{question.paperTitle}</p>
                          <p className="line-clamp-2 text-sm text-foreground">{question.stem}</p>
                        </div>
                        <div className="min-w-32 space-y-1 text-right">
                          <p className="text-sm font-semibold text-navy">{formatPercent(question.accuracyPercent)}</p>
                          <p className="text-xs text-muted">{question.correctCount}/{question.opportunities} correct</p>
                        </div>
                      </div>
                    </div>
                  )) : <p className="text-sm text-muted">No submitted question evidence in this window.</p>}
                </div>
              </AdminRoutePanel>

              <AdminRoutePanel title="Action Insights" description="Operational cues generated from the current Reading evidence window.">
                <div className="space-y-3">
                  {analytics.actionInsights.map((insight) => (
                    <div key={insight.id} className="rounded-lg border border-border bg-background-light p-3">
                      <div className="flex items-start justify-between gap-3">
                        <div className="space-y-1">
                          <p className="font-semibold text-navy">{insight.title}</p>
                          <p className="text-sm text-muted">{insight.description}</p>
                        </div>
                        <Badge variant={toneVariant(insight.tone)} className="text-[10px] capitalize">{insight.tone}</Badge>
                      </div>
                    </div>
                  ))}
                </div>
              </AdminRoutePanel>
            </div>
          </MotionSection>

          <MotionSection delayIndex={3}>
            <AdminRoutePanel title="Paper Quality" description="Per-paper readiness and outcome signals for authoring QA and content operations.">
              <div className="overflow-x-auto">
                <table className="min-w-full divide-y divide-border text-sm">
                  <thead>
                    <tr className="text-left text-xs font-semibold uppercase tracking-[0.12em] text-muted">
                      <th className="py-3 pr-4">Paper</th>
                      <th className="px-4 py-3">Shape</th>
                      <th className="px-4 py-3">Attempts</th>
                      <th className="px-4 py-3">Avg scaled</th>
                      <th className="px-4 py-3">Pass rate</th>
                      <th className="px-4 py-3">Avg time</th>
                    </tr>
                  </thead>
                  <tbody className="divide-y divide-border">
                    {analytics.papers.map((paper) => (
                      <tr key={paper.paperId}>
                        <td className="py-3 pr-4">
                          <p className="font-semibold text-navy">{paper.title}</p>
                          <p className="text-xs text-muted">{paper.status} · {paper.difficulty}</p>
                        </td>
                        <td className="px-4 py-3">
                          <Badge variant={paper.isExamReady ? 'success' : 'warning'} className="text-[10px]">
                            {paper.partACount}+{paper.partBCount}+{paper.partCCount}
                          </Badge>
                        </td>
                        <td className="px-4 py-3 text-navy">{paper.submittedCount}/{paper.attemptCount}</td>
                        <td className="px-4 py-3 text-navy">{formatNumber(paper.averageScaledScore)}</td>
                        <td className="px-4 py-3 text-navy">{formatPercent(paper.passRatePercent)}</td>
                        <td className="px-4 py-3 text-navy">{formatMinutes(paper.averageCompletionSeconds)}</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            </AdminRoutePanel>
          </MotionSection>

          {analytics.modeBreakdown.length > 0 ? (
            <MotionSection delayIndex={4}>
              <AdminRoutePanel title="Attempt Modes" description="Exam, learning, drill, mini-test, and error-bank activity in the selected window.">
                <div className="grid gap-3 md:grid-cols-2 xl:grid-cols-5">
                  {analytics.modeBreakdown.map((mode) => (
                    <div key={mode.mode} className="rounded-lg border border-border bg-background-light p-3">
                      <div className="flex items-center justify-between gap-2">
                        <p className="font-semibold text-navy">{mode.mode}</p>
                        {mode.passRatePercent != null && (mode.passRatePercent >= 70 ? <CheckCircle2 className="h-4 w-4 text-success" /> : <AlertTriangle className="h-4 w-4 text-warning" />)}
                      </div>
                      <p className="mt-2 text-sm text-muted">{mode.submittedCount}/{mode.attemptCount} submitted</p>
                      <p className="mt-1 text-sm font-semibold text-navy">{formatPercent(mode.passRatePercent)} pass</p>
                    </div>
                  ))}
                </div>
              </AdminRoutePanel>
            </MotionSection>
          ) : null}
        </div>
      ) : null}
    </AdminRouteWorkspace>
  );
}