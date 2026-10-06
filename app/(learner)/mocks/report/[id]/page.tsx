'use client';

import React, { Suspense, useEffect, useState } from 'react';
import { useParams } from 'next/navigation';
import { MotionItem, MotionSection } from '@/components/ui/motion-primitives';
import {
  ArrowRight,
  TrendingUp,
  TrendingDown,
  Minus,
  RefreshCw,
  FileText,
  Headphones,
  PenTool,
  Mic,
  ShieldCheck,
  CalendarCheck,
  Download,
} from 'lucide-react';
import Link from 'next/link';
import { LearnerSurfaceSectionHeader, OetStatementOfResultsCard } from '@/components/domain';
import { WeaknessNarrative } from '@/components/domain/mock-weakness-narrative';
import { ReadinessDeltaBanner } from '@/components/domain/readiness-delta-banner';
import { TimeAnalyticsBreakdown } from '@/components/domain/mock-time-analytics';
import { MockVocabularyReview } from '@/components/domain/vocabulary';
import { Skeleton } from '@/components/ui/skeleton';
import { InlineAlert } from '@/components/ui/alert';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Card } from '@/components/ui/card';
import { CardLink } from '@/components/ui/card-link';
import { cn } from '@/lib/utils';
import { ResultsScorePanel } from '@/components/domain/results/results-score-panel';
import { ResultGauge } from '@/components/domain/results/gauge';
import {
  fetchMockReport,
  fetchReadiness,
  reportMockLeak,
  fetchRemediationPlan,
  generateRemediationPlan,
  completeRemediationTask,
  downloadMockWritingPdf,
  learnerGetActiveResultTemplate,
  fetchAuthorizedObjectUrl,
  type RemediationTask,
  type LearnerResultTemplateDto,
} from '@/lib/api';
import type { MockReport } from '@/lib/mock-data';
import type {
  MockReportPerQuestionTimingV1,
  MockReportWeaknessNarrativeV1,
} from '@/lib/mocks/report-payload';
import { analytics } from '@/lib/analytics';
import { oetGradeFromScaled } from '@/lib/scoring';
import { isMockReportStatementOfResultsReady, mockReportToStatementOfResults } from '@/lib/adapters/oet-sor-adapter';
import { buildMockRemediationPlan, getMockReadinessDecision } from '@/lib/mocks/workflow';

/** Sub-test identity (DESIGN.md §2 skill tokens); grade status lives on the score, never here. */
const SUBTEST_META: Record<string, { icon: React.ElementType; color: string; bg: string }> = {
  listening: { icon: Headphones, color: 'text-skill-listening', bg: 'bg-skill-listening/10' },
  reading:   { icon: FileText,   color: 'text-skill-reading',   bg: 'bg-skill-reading/10' },
  writing:   { icon: PenTool,    color: 'text-skill-writing',   bg: 'bg-skill-writing/10' },
  speaking:  { icon: Mic,        color: 'text-skill-speaking',  bg: 'bg-skill-speaking/10' },
};

function ReportSkeleton() {
  return (
    <div className="space-y-6" role="status" aria-busy="true" aria-label="Loading mock report">
      {[1, 2, 3].map((i) => (
        <Skeleton key={i} className="h-32 rounded-2xl" />
      ))}
    </div>
  );
}

/** Colour a mock sub-test from persisted grade evidence. Reading/Listening
 * must never derive a grade from a numeric score in the browser. */
function scoreColor(score: string, persistedGrade?: string | null, governedScore = false) {
  const trimmed = score.trim();
  if (trimmed.length === 0) return 'text-muted';
  if (persistedGrade?.trim()) {
    const grade = persistedGrade.trim().toUpperCase().replace(/^GRADE\s*/, '').split(/\s|,/)[0] ?? '';
    if (grade === 'A' || grade === 'B') return 'text-success-strong';
    if (grade === 'C+' || grade === 'C') return 'text-warning-strong';
    if (grade === 'D' || grade === 'E') return 'text-danger-strong';
    return 'text-muted';
  }
  if (governedScore) return 'text-muted';
  const numeric = Number(trimmed);
  const grade = Number.isFinite(numeric)
    ? oetGradeFromScaled(numeric)
    : (trimmed.toUpperCase().replace(/^GRADE\s*/, '').split(/\s|,/)[0] ?? '');
  if (grade === 'A' || grade === 'B') return 'text-success-strong';
  if (grade === 'C+' || grade === 'C') return 'text-warning-strong';
  if (grade === 'D' || grade === 'E') return 'text-danger-strong';
  return 'text-muted';
}

/** Map a sub-test score to a stat-strip tone (mirrors {@link scoreColor}). */
function scoreTone(score: string, persistedGrade?: string | null, governedScore = false): 'success' | 'warning' | 'danger' | 'default' {
  const color = scoreColor(score, persistedGrade, governedScore);
  if (color === 'text-success-strong') return 'success';
  if (color === 'text-warning-strong') return 'warning';
  if (color === 'text-danger-strong') return 'danger';
  return 'default';
}

/** CSS colour for a sub-test gauge arc, derived from the same grade bands. */
function scoreGaugeColor(score: string, persistedGrade?: string | null, governedScore = false): string {
  const color = scoreColor(score, persistedGrade, governedScore);
  if (color === 'text-success-strong') return 'var(--color-success)';
  if (color === 'text-warning-strong') return 'var(--color-warning)';
  if (color === 'text-danger-strong') return 'var(--color-danger)';
  return 'var(--color-primary)';
}

function MockReportContent() {
  const params = useParams();
  const id = Array.isArray(params?.id) ? params?.id[0] : params?.id ?? '';
  const [report, setReport] = useState<MockReport | null>(null);
  const [error, setError] = useState('');
  const [leakState, setLeakState] = useState<'idle' | 'sending' | 'sent'>('idle');
  const [serverTasks, setServerTasks] = useState<RemediationTask[] | null>(null);
  const [busyTaskId, setBusyTaskId] = useState<string | null>(null);
  const [pdfState, setPdfState] = useState<'idle' | 'downloading' | 'error'>('idle');
  const [pdfError, setPdfError] = useState<string | null>(null);
  const [resultTemplate, setResultTemplate] = useState<LearnerResultTemplateDto | null>(null);
  const [resultTemplateUrl, setResultTemplateUrl] = useState<string | null>(null);
  const [readinessBefore, setReadinessBefore] = useState<number | null>(null);
  const [readinessAfter, setReadinessAfter] = useState<number | null>(null);
  const [readinessRisk, setReadinessRisk] = useState('Unknown');
  const [readinessLoading, setReadinessLoading] = useState(true);

  useEffect(() => {
    analytics.track('evaluation_viewed', { type: 'mock_report', id });
    fetchMockReport(id)
      .then(setReport)
      .catch(() => setError('Could not load report.'));
  }, [id]);

  useEffect(() => {
    let cancelled = false;
    let timer: ReturnType<typeof setTimeout> | undefined;

    const applySnapshot = (snapshot: { overallReadiness?: number; overallRisk?: string }, isFollowUp: boolean) => {
      const score = typeof snapshot.overallReadiness === 'number' ? snapshot.overallReadiness : null;
      const risk = snapshot.overallRisk?.trim() || 'Unknown';
      if (cancelled) return;
      if (!isFollowUp) {
        setReadinessBefore(score);
        setReadinessAfter(null);
      } else {
        setReadinessAfter(score);
      }
      setReadinessRisk(risk);
      setReadinessLoading(isFollowUp ? false : true);
    };

    const load = async (isFollowUp: boolean) => {
      try {
        const snapshot = await fetchReadiness();
        applySnapshot(snapshot, isFollowUp);
      } catch {
        if (!cancelled) setReadinessLoading(false);
      }
    };

    void load(false);
    timer = setTimeout(() => { void load(true); }, 4000);

    return () => {
      cancelled = true;
      if (timer) clearTimeout(timer);
    };
  }, [id]);

  useEffect(() => {
    let cancelled = false;
    learnerGetActiveResultTemplate()
      .then((template) => {
        if (!cancelled) setResultTemplate(template);
      })
      .catch(() => {
        if (!cancelled) setResultTemplate(null);
      });
    return () => {
      cancelled = true;
    };
  }, []);

  useEffect(() => {
    const mediaId = resultTemplate?.media?.id;
    if (!mediaId) {
      setResultTemplateUrl(null);
      return;
    }

    let objectUrl: string | null = null;
    let cancelled = false;
    fetchAuthorizedObjectUrl(`/v1/media/${encodeURIComponent(mediaId)}/content`)
      .then((nextUrl) => {
        objectUrl = nextUrl;
        if (!cancelled) setResultTemplateUrl(nextUrl);
      })
      .catch(() => {
        if (!cancelled) setResultTemplateUrl(null);
      });

    return () => {
      cancelled = true;
      if (objectUrl) URL.revokeObjectURL(objectUrl);
    };
  }, [resultTemplate?.media?.id]);

  useEffect(() => {
    if (!id) return;
    let cancelled = false;
    (async () => {
      try {
        const existing = await fetchRemediationPlan();
        const matched = existing.items.filter((t) => t.mockReportId === id);
        if (matched.length > 0) {
          if (!cancelled) setServerTasks(matched);
          return;
        }
        const generated = await generateRemediationPlan(id);
        if (!cancelled) setServerTasks(generated.items);
      } catch {
        // server plan optional — fallback to derived plan
      }
    })();
    return () => {
      cancelled = true;
    };
  }, [id]);

  const handleCompleteTask = async (taskId: string) => {
    setBusyTaskId(taskId);
    try {
      const updated = await completeRemediationTask(taskId);
      setServerTasks((prev) => (prev ? prev.map((t) => (t.id === updated.id ? updated : t)) : prev));
    } catch {
      // swallow — UI re-enables
    } finally {
      setBusyTaskId(null);
    }
  };

  if (error) {
    return <InlineAlert variant="error">{error}</InlineAlert>;
  }

  if (!report) {
    return <ReportSkeleton />;
  }

  // V1 payload fields are populated incrementally (see lib/mocks/report-payload.ts);
  // narrow once here so downstream JSX can read optional V1-only fields safely.
  const reportV1 = report as MockReport & {
    weaknessNarrative?: MockReportWeaknessNarrativeV1 | null;
    perQuestionTiming?: MockReportPerQuestionTimingV1[] | null;
  };
  const comp = report.priorComparison;
  const readiness = getMockReadinessDecision(report);
  const readinessTone: 'success' | 'warning' | 'danger' | 'info' | 'muted' =
    readiness.variant === 'success' ? 'success'
      : readiness.variant === 'warning' ? 'warning'
        : readiness.variant === 'danger' ? 'danger'
          : readiness.variant === 'info' ? 'info'
            : 'muted';
  const overallNumeric = Number(String(report.overallScore).replace(/[^0-9.]/g, ''));
  const overallScaled = Number.isFinite(overallNumeric) && overallNumeric > 0 ? overallNumeric : null;
  const overallGaugePct = overallScaled != null ? Math.min(100, (overallScaled / 500) * 100) : 0;
  const pendingTeacherReviews = report.reviewSummary
    ? report.reviewSummary.pending + report.reviewSummary.queued + report.reviewSummary.inReview
    : report.subTests.filter((test) => test.reviewState && test.reviewState !== 'completed').length;
  const remediationPlan = report.remediationPlan?.length ? report.remediationPlan : buildMockRemediationPlan(report);
  const statementReady = isMockReportStatementOfResultsReady(report);
  const handleLeakReport = async () => {
    setLeakState('sending');
    try {
      await reportMockLeak({
        mockAttemptId: report.mockAttemptId ?? null,
        reason: 'Learner reported possible leaked or rights-unclear mock content from the report page.',
      });
      setLeakState('sent');
    } catch {
      setLeakState('idle');
      setError('Could not send leak report. Please try again.');
    }
  };

  const handleDownloadWritingPdf = async () => {
    if (!report.mockAttemptId) return;
    setPdfState('downloading');
    setPdfError(null);
    try {
      analytics.track('writing_pdf_download_requested', { mockAttemptId: report.mockAttemptId });
      await downloadMockWritingPdf(report.mockAttemptId);
      analytics.track('writing_pdf_download_succeeded', { mockAttemptId: report.mockAttemptId });
      setPdfState('idle');
    } catch (err: unknown) {
      const message =
        err instanceof Error && err.message
          ? err.message
          : 'Could not download the PDF. Please try again later.';
      setPdfError(message);
      setPdfState('error');
      analytics.track('writing_pdf_download_failed', {
        mockAttemptId: report.mockAttemptId,
        message,
      });
    }
  };

  return (
    <>
      {/* The page's h1 block. It never animates in: it is the first thing read. */}
      <ResultsScorePanel
        eyebrow="Mock report"
        icon={ShieldCheck}
        title="Overall Performance"
        subtitle={report.summary}
        gaugeValue={overallGaugePct}
        gaugeCenter={<span className="text-2xl font-black tabular-nums text-navy">{report.overallGrade ?? report.overallScore}</span>}
        gaugeLabel={overallScaled != null ? `${overallScaled}/500` : undefined}
        gaugeColor={
          readinessTone === 'success' ? 'var(--color-success)'
            : readinessTone === 'warning' ? 'var(--color-warning)'
              : readinessTone === 'danger' ? 'var(--color-danger)'
                : 'var(--color-primary)'
        }
        grade={{ label: readiness.label, tone: readinessTone }}
        stats={report.subTests.map((test) => {
          const Icon = SUBTEST_META[test.id]?.icon ?? Headphones;
          const governedScore = test.id === 'reading' || test.id === 'listening';
          return { label: test.name, value: test.score, tone: scoreTone(String(test.score), test.grade, governedScore), icon: <Icon aria-hidden="true" /> };
        })}
        aside={(
          <div className="rounded-2xl border border-border bg-background-light p-4">
            <p className="eyebrow text-muted">Estimated academy report</p>
            <p className="text-sm leading-6 text-muted">{readiness.description}</p>
            <p className="mt-2 text-xs leading-5 text-muted">
              Do not treat mock results as a guaranteed pass. Use repeated green mock evidence and tutor feedback before booking the official OET.
            </p>
          </div>
        )}
      />

      {/* Readiness delta banner — surfaces the change this mock made
          to the learner's overall readiness and links into the
          full readiness centre for the "see why" follow-up. */}
      <ReadinessDeltaBanner
        before={readinessBefore}
        after={readinessAfter}
        risk={readinessRisk}
        loading={readinessLoading}
      />

      {/* OET Statement of Results — pixel-faithful CBLA format.
          Mission-critical: this is the single place the "official" OET
          result card is rendered. See docs/OET-RESULT-CARD-SPEC.md. */}
      <MotionSection>
        {statementReady ? (
          <OetStatementOfResultsCard data={mockReportToStatementOfResults({
            report,
            profession: report.profession ?? undefined,
            country: report.targetCountry ?? undefined,
          })} />
        ) : (
          <InlineAlert variant="warning" title="Practice Statement of Results pending">
            This report has incomplete or teacher-marked evidence still pending. The OET-style practice result card appears only after all four sub-tests have final numeric scores.
          </InlineAlert>
        )}
      </MotionSection>

      {resultTemplateUrl ? (
        <MotionSection delayIndex={1}>
          <Card>
            <LearnerSurfaceSectionHeader
              title="Result-table reference"
              description={resultTemplate?.title}
              action={<Badge variant="outline" className="self-start sm:self-auto">OET style</Badge>}
              className="mb-3"
            />
            {/* eslint-disable-next-line @next/next/no-img-element */}
            <img
              src={resultTemplateUrl}
              alt={resultTemplate?.title ?? 'OET result table reference'}
              className="max-h-[520px] w-full rounded-xl border border-border object-contain"
            />
          </Card>
        </MotionSection>
      ) : null}

      {pendingTeacherReviews > 0 ? (
        <MotionSection delayIndex={1}>
          <InlineAlert variant="warning" title="Teacher-marked sections still affect the final readiness report">
            Listening and Reading evidence may be available immediately, but Writing/Speaking readiness should remain provisional until tutor feedback is returned.
          </InlineAlert>
        </MotionSection>
      ) : null}

      <MotionSection delayIndex={2} className="grid grid-cols-1 gap-4 lg:grid-cols-3">
        <Card className="lg:col-span-2">
          <LearnerSurfaceSectionHeader icon={ShieldCheck} title="V2 readiness and integrity" className="mb-4" />
          <div className="grid grid-cols-1 gap-3 sm:grid-cols-2">
            {(report.perModuleReadiness?.length ? report.perModuleReadiness : []).map((item) => (
              <div key={item.subtest} className="min-w-0 rounded-xl border border-border bg-background-light p-4">
                <div className="flex flex-wrap items-center justify-between gap-2">
                  <p className="text-sm font-bold text-navy">{item.subtest}</p>
                  <Badge variant={item.rag === 'red' ? 'danger' : item.rag === 'amber' ? 'warning' : item.rag === 'pending' ? 'muted' : 'success'} size="sm">
                    {item.rag.replace(/-/g, ' ')}
                  </Badge>
                </div>
                <p className="mt-2 text-xs leading-5 text-muted">{item.message}</p>
              </div>
            ))}
            {report.proctoringSummary ? (
              <div className="min-w-0 rounded-xl border border-border bg-background-light p-4">
                <p className="text-sm font-bold text-navy">Proctoring summary</p>
                <p className="mt-2 text-xs leading-5 text-muted">{report.proctoringSummary.message}</p>
                <p className="mt-2 eyebrow tabular-nums text-muted">
                  {report.proctoringSummary.totalEvents} events / {report.proctoringSummary.warningEvents} warnings
                </p>
              </div>
            ) : null}
          </div>
        </Card>
        <Card>
          <LearnerSurfaceSectionHeader icon={CalendarCheck} title="Booking advice" className="mb-3" />
          <p className="text-sm leading-6 text-muted">{report.bookingAdvice?.message ?? readiness.description}</p>
          {report.retakeAdvice ? (
            <p className="mt-3 text-xs leading-5 text-muted">{report.retakeAdvice.message}</p>
          ) : null}
          <Button className="mt-4" fullWidth variant="secondary" onClick={handleLeakReport} loading={leakState === 'sending'} disabled={leakState === 'sent' || !report.mockAttemptId}>
            {leakState === 'sent' ? 'Leak report sent' : 'Report leaked content'}
          </Button>
        </Card>
      </MotionSection>

      {comp && comp.exists && (
        <MotionSection delayIndex={2}>
          <Card className="flex items-start gap-4">
            <div
              aria-hidden="true"
              className={`flex h-10 w-10 shrink-0 items-center justify-center rounded-full ${
                comp.overallTrend === 'up'   ? 'bg-success/10 text-success-strong' :
                comp.overallTrend === 'down' ? 'bg-danger/10 text-danger-strong' :
                                               'bg-background-light text-muted'
              }`}
            >
              {comp.overallTrend === 'up'   && <TrendingUp className="h-5 w-5" />}
              {comp.overallTrend === 'down' && <TrendingDown className="h-5 w-5" />}
              {comp.overallTrend === 'flat' && <Minus className="h-5 w-5" />}
            </div>
            <div className="min-w-0">
              <h2 className="mb-1 text-base font-bold text-navy">Compared to {comp.priorMockName}</h2>
              <p className="text-sm leading-relaxed text-muted">{comp.details}</p>
            </div>
          </Card>
        </MotionSection>
      )}

      <MotionSection delayIndex={3}>
        <LearnerSurfaceSectionHeader title="Sub-test Breakdown" className="mb-4" />
        <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
          {report.subTests.map((test, index) => {
            const meta = SUBTEST_META[test.id] ?? SUBTEST_META.listening;
            const Icon = meta.icon;
            const isWriting = test.id === 'writing';
            const governedScore = test.id === 'reading' || test.id === 'listening';
            const canDownload = isWriting && Boolean(report.mockAttemptId);
            return (
              <MotionItem key={test.id} delayIndex={Math.min(index, 5)}>
                <Card className="h-full">
                  <div className="flex items-center justify-between gap-3">
                    <div className="flex min-w-0 items-center gap-4">
                      <div className={`flex h-12 w-12 shrink-0 items-center justify-center rounded-2xl ${meta.bg}`}>
                        <Icon className={`h-6 w-6 ${meta.color}`} aria-hidden="true" />
                      </div>
                      <div className="min-w-0">
                        <h3 className="text-base font-bold text-navy">{test.name}</h3>
                        <p className="text-xs tabular-nums text-muted">{isWriting ? 'Criteria score' : 'Raw'}: {test.rawScore}</p>
                        {test.reviewState ? (
                          <p className="mt-1 tile-label text-warning-strong">
                            Review {test.reviewState.replace(/_/g, ' ')}
                          </p>
                        ) : null}
                      </div>
                    </div>
                    <ResultGauge
                      value={typeof test.scaledScore === 'number' ? (test.scaledScore / 500) * 100 : 0}
                      size={56}
                      stroke={6}
                      color={scoreGaugeColor(String(test.score), test.grade, governedScore)}
                    >
                      <span className={`text-sm font-bold tabular-nums ${scoreColor(String(test.score), test.grade, governedScore)}`}>{test.grade ?? test.score}</span>
                    </ResultGauge>
                  </div>
                  {canDownload ? (
                    <div className="mt-4 border-t border-border pt-3">
                      <Button
                        variant="secondary"
                        size="sm"
                        fullWidth
                        onClick={handleDownloadWritingPdf}
                        loading={pdfState === 'downloading'}
                        disabled={pdfState === 'downloading'}
                      >
                        <Download className="h-4 w-4" aria-hidden="true" />
                        Download practice PDF
                      </Button>
                      <p className="mt-2 text-2xs leading-4 text-muted">
                        Watermarked “Practice Copy”. For your own study only, not for resale or redistribution.
                      </p>
                      {pdfState === 'error' && pdfError ? (
                        <p className="mt-2 text-2xs text-danger-strong" role="alert">
                          {pdfError}
                        </p>
                      ) : null}
                    </div>
                  ) : null}
                </Card>
              </MotionItem>
            );
          })}
        </div>
      </MotionSection>

      {/* Weakest Criterion — V1 payload renders WeaknessNarrative with
          aggregated headline/body/tags; pre-V1 payloads fall back to the
          legacy weakestCriterion card via the component's `fallback` prop. */}
      <MotionSection delayIndex={4}>
        <LearnerSurfaceSectionHeader title="Area for Improvement" className="mb-4" />
        <WeaknessNarrative
          headline={reportV1.weaknessNarrative?.headline}
          body={reportV1.weaknessNarrative?.body}
          tags={reportV1.weaknessNarrative?.tags}
          fallback={report.weakestCriterion ? {
            subtest: report.weakestCriterion.subtest,
            criterion: report.weakestCriterion.criterion,
            description: report.weakestCriterion.description,
          } : undefined}
        />
      </MotionSection>

      {/* Time analytics — per-section utilisation and (when populated)
          longest-3-questions panel. Reads timing from the legacy MockReport
          field plus the optional V1 perQuestionTiming array. */}
      <MotionSection delayIndex={4}>
        <LearnerSurfaceSectionHeader title="Time analytics" className="mb-4" />
        <TimeAnalyticsBreakdown
          timingAnalysis={report.timingAnalysis ?? []}
          perQuestionTiming={reportV1.perQuestionTiming ?? null}
        />
      </MotionSection>

      {/* Words to Review — surfaces OET vocabulary tied to the weakest criterion */}
      {report.weakestCriterion && (
        <MockVocabularyReview
          mockId={report.id}
          weakSubtest={report.weakestCriterion.subtest}
          weakCriterion={report.weakestCriterion.criterion}
          weakDescription={report.weakestCriterion.description}
        />
      )}

      {/* Remediation Plan — spec requirement: every mock report ends with a concrete next 7-day plan.
          When server-side W5 RemediationTask plan exists we render it (with completion controls);
          otherwise we fall back to the deterministic client-derived plan. */}
      <MotionSection delayIndex={5}>
        <LearnerSurfaceSectionHeader title="Your next 7-day plan" className="mb-4" />
        {serverTasks && serverTasks.length > 0 ? (
          <div className="grid grid-cols-1 gap-4 sm:grid-cols-2 lg:grid-cols-5">
            {serverTasks
              .slice()
              .sort((a, b) => a.dayIndex - b.dayIndex)
              .map((task, index) => {
                const completed = task.status === 'completed';
                return (
                  <MotionItem key={task.id} delayIndex={Math.min(index, 5)}>
                    <Card padding="sm" className={cn('flex h-full flex-col', completed && 'border-success/40 opacity-80')}>
                      <span className="tile-label text-primary">Day {task.dayIndex}</span>
                      <h3 className="mt-2 text-sm font-bold text-navy">{task.title}</h3>
                      <p className="mt-2 text-xs leading-5 text-muted">{task.description}</p>
                      <div className="mt-auto flex flex-wrap items-center justify-between gap-2 pt-3">
                        {task.routeHref ? (
                          <Button asChild variant="outline" size="sm">
                            <Link href={task.routeHref}>
                              Start <ArrowRight className="h-3.5 w-3.5 rtl:rotate-180" aria-hidden="true" />
                            </Link>
                          </Button>
                        ) : <span />}
                        {completed ? (
                          <Badge variant="success" size="sm">Done</Badge>
                        ) : (
                          <Button
                            size="sm"
                            variant="secondary"
                            onClick={() => handleCompleteTask(task.id)}
                            loading={busyTaskId === task.id}
                            disabled={busyTaskId === task.id}
                          >
                            Mark done
                          </Button>
                        )}
                      </div>
                    </Card>
                  </MotionItem>
                );
              })}
          </div>
        ) : (
          <div className="grid grid-cols-1 gap-4 sm:grid-cols-2 lg:grid-cols-5">
            {remediationPlan.map((item, index) => (
              <MotionItem key={`${item.day || 'day'}-${item.title || 'item'}-${index}`} delayIndex={Math.min(index, 5)}>
                <CardLink href={item.route} padding="sm" className="group h-full">
                  <span className="tile-label text-primary">{item.day}</span>
                  <h3 className="mt-2 text-sm font-bold text-navy transition-colors group-hover:text-primary">{item.title}</h3>
                  <p className="mt-2 text-xs leading-5 text-muted">{item.description}</p>
                </CardLink>
              </MotionItem>
            ))}
          </div>
        )}
      </MotionSection>

      <MotionSection delayIndex={5}>
        <Card padding="lg" className="flex flex-col gap-4 sm:flex-row sm:items-center sm:justify-between">
          <div className="flex min-w-0 items-start gap-4">
            <div className="flex h-11 w-11 shrink-0 items-center justify-center rounded-2xl bg-primary/10 text-primary">
              <RefreshCw className="h-5 w-5" aria-hidden="true" />
            </div>
            <div className="min-w-0">
              <h2 className="text-lg font-bold text-navy">Update Your Study Plan</h2>
              {report.weakestCriterion ? (
                <p className="mt-1 max-w-prose text-sm text-muted">
                  Based on this report, we recommend focusing on <strong className="text-navy">{report.weakestCriterion.criterion}</strong> in {report.weakestCriterion.subtest}.
                </p>
              ) : (
                <p className="mt-1 max-w-prose text-sm text-muted">
                  Based on this report, review your detailed sub-test breakdown to update your focus areas.
                </p>
              )}
            </div>
          </div>
          <Button asChild className="shrink-0">
            <Link href="/study-plan">Update Study Plan</Link>
          </Button>
        </Card>
      </MotionSection>
    </>
  );
}

export default function MockReport() {
  return (
    <Suspense fallback={<ReportSkeleton />}>
      <MockReportContent />
    </Suspense>
  );
}
