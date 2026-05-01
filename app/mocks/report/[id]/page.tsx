'use client';

import React, { Suspense, useEffect, useState } from 'react';
import { useParams } from 'next/navigation';
import { MotionSection } from '@/components/ui/motion-primitives';
import {
  TrendingUp,
  TrendingDown,
  Minus,
  AlertTriangle,
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
import { LearnerDashboardShell } from '@/components/layout';
import { OetStatementOfResultsCard } from '@/components/domain';
import { MockVocabularyReview } from '@/components/domain/vocabulary';
import { Skeleton } from '@/components/ui/skeleton';
import { InlineAlert } from '@/components/ui/alert';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import {
  fetchMockReport,
  reportMockLeak,
  fetchRemediationPlan,
  generateRemediationPlan,
  completeRemediationTask,
  downloadMockWritingPdf,
  type RemediationTask,
} from '@/lib/api';
import type { MockReport } from '@/lib/mock-data';
import { analytics } from '@/lib/analytics';
import { oetGradeFromScaled } from '@/lib/scoring';
import { mockReportToStatementOfResults } from '@/lib/adapters/oet-sor-adapter';
import { buildMockRemediationPlan, getMockReadinessDecision } from '@/lib/mocks/workflow';

const SUBTEST_META: Record<string, { icon: React.ElementType; color: string; bg: string }> = {
  listening: { icon: Headphones, color: 'text-primary', bg: 'bg-primary/10' },
  reading:   { icon: FileText,   color: 'text-info',   bg: 'bg-info/10' },
  writing:   { icon: PenTool,    color: 'text-rose-600',   bg: 'bg-rose-50' },
  speaking:  { icon: Mic,        color: 'text-primary', bg: 'bg-primary/10' },
};

/**
 * Colour a sub-test score cell based on its grade band. Always derives the
 * grade through the canonical scoring module so we can never drift from the
 * 350/300 thresholds or the 30/42 raw mapping. Accepts either a scaled
 * numeric string ("370", "350") or a letter-prefixed label ("A", "B").
 */
function scoreColor(score: string) {
  const trimmed = score.trim();
  if (trimmed.length === 0) return 'text-muted';
  const numeric = Number(trimmed);
  const grade = Number.isFinite(numeric)
    ? oetGradeFromScaled(numeric)
    : (trimmed.toUpperCase().replace(/^GRADE\s*/, '').split(/\s|,/)[0] ?? '');
  if (grade === 'A' || grade === 'B') return 'text-success';
  if (grade === 'C+' || grade === 'C') return 'text-warning';
  if (grade === 'D' || grade === 'E') return 'text-danger';
  return 'text-muted';
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

  useEffect(() => {
    analytics.track('evaluation_viewed', { type: 'mock_report', id });
    fetchMockReport(id)
      .then(setReport)
      .catch(() => setError('Could not load report.'));
  }, [id]);

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
    return (
      <LearnerDashboardShell pageTitle="Mock Report" backHref="/mocks">
        <div>
          <InlineAlert variant="error">{error}</InlineAlert>
        </div>
      </LearnerDashboardShell>
    );
  }

  if (!report) {
    return (
      <LearnerDashboardShell pageTitle="Mock Report" backHref="/mocks">
        <div className="space-y-6">
          {[1, 2, 3].map(i => (
            <Skeleton key={i} className="h-32 rounded-2xl" />
          ))}
        </div>
      </LearnerDashboardShell>
    );
  }

  const comp = report.priorComparison;
  const readiness = getMockReadinessDecision(report);
  const pendingTeacherReviews = report.reviewSummary
    ? report.reviewSummary.pending + report.reviewSummary.queued + report.reviewSummary.inReview
    : report.subTests.filter((test) => test.reviewState && test.reviewState !== 'completed').length;
  const remediationPlan = report.remediationPlan?.length ? report.remediationPlan : buildMockRemediationPlan(report);

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
    <LearnerDashboardShell
      pageTitle="Mock Report"
      subtitle={`${report.title} · ${report.date}`}
      backHref="/mocks"
    >
      <div className="space-y-8">

        {/* 0. OET Statement of Results — pixel-faithful CBLA format.
            Mission-critical: this is the single place the "official" OET
            result card is rendered. See docs/OET-RESULT-CARD-SPEC.md. */}
        <MotionSection delayIndex={0}>
          <OetStatementOfResultsCard data={mockReportToStatementOfResults({ report })} />
        </MotionSection>

        {/* 1. Overall Score */}
        <MotionSection
          delayIndex={1}
          className="bg-surface rounded-2xl border border-border p-8 sm:p-10 text-center shadow-sm flex flex-col items-center"
        >
          <div className="w-24 h-24 rounded-full bg-primary/10 flex items-center justify-center mb-6">
            <span className="text-5xl font-black text-primary">{report.overallScore}</span>
          </div>
          <h2 className="text-xl font-black text-navy mb-3">Overall Performance</h2>
          <p className="text-sm text-muted max-w-lg leading-relaxed">{report.summary}</p>
          <div className="mt-5 max-w-2xl rounded-2xl border border-border bg-background-light p-4 text-left">
            <div className="flex flex-wrap items-center gap-2">
              <Badge variant={readiness.variant} size="sm">{readiness.label}</Badge>
              <Badge variant="outline" size="sm">Estimated academy report</Badge>
            </div>
            <p className="mt-3 text-sm leading-6 text-muted">{readiness.description}</p>
            <p className="mt-2 text-xs leading-5 text-muted">
              Do not treat mock results as a guaranteed pass. Use repeated green mock evidence and tutor feedback before booking the official OET.
            </p>
          </div>
        </MotionSection>

        {pendingTeacherReviews > 0 ? (
          <MotionSection delayIndex={1}>
            <InlineAlert variant="warning" title="Teacher-marked sections still affect the final readiness report">
              Listening and Reading evidence may be available immediately, but Writing/Speaking readiness should remain provisional until tutor feedback is returned.
            </InlineAlert>
          </MotionSection>
        ) : null}

        <MotionSection delayIndex={2} className="grid gap-4 lg:grid-cols-3">
          <div className="rounded-2xl border border-border bg-surface p-5 shadow-sm lg:col-span-2">
            <div className="mb-4 flex items-center gap-2">
              <ShieldCheck className="h-5 w-5 text-primary" />
              <h2 className="text-sm font-black uppercase tracking-widest text-muted">V2 readiness and integrity</h2>
            </div>
            <div className="grid gap-3 sm:grid-cols-2">
              {(report.perModuleReadiness?.length ? report.perModuleReadiness : []).map((item) => (
                <div key={item.subtest} className="rounded-xl border border-border bg-background-light p-4">
                  <div className="flex items-center justify-between gap-2">
                    <p className="text-sm font-bold text-navy">{item.subtest}</p>
                    <Badge variant={item.rag === 'red' ? 'danger' : item.rag === 'amber' ? 'warning' : item.rag === 'pending' ? 'muted' : 'success'} size="sm">
                      {item.rag.replace(/-/g, ' ')}
                    </Badge>
                  </div>
                  <p className="mt-2 text-xs leading-5 text-muted">{item.message}</p>
                </div>
              ))}
              {report.proctoringSummary ? (
                <div className="rounded-xl border border-border bg-background-light p-4">
                  <p className="text-sm font-bold text-navy">Proctoring summary</p>
                  <p className="mt-2 text-xs leading-5 text-muted">{report.proctoringSummary.message}</p>
                  <p className="mt-2 text-[11px] font-black uppercase tracking-widest text-muted">
                    {report.proctoringSummary.totalEvents} events / {report.proctoringSummary.warningEvents} warnings
                  </p>
                </div>
              ) : null}
            </div>
          </div>
          <div className="rounded-2xl border border-border bg-surface p-5 shadow-sm">
            <div className="mb-3 flex items-center gap-2">
              <CalendarCheck className="h-5 w-5 text-success" />
              <h2 className="text-sm font-black uppercase tracking-widest text-muted">Booking advice</h2>
            </div>
            <p className="text-sm leading-6 text-muted">{report.bookingAdvice?.message ?? readiness.description}</p>
            {report.retakeAdvice ? (
              <p className="mt-3 text-xs leading-5 text-muted">{report.retakeAdvice.message}</p>
            ) : null}
            <Button className="mt-4 w-full" variant="secondary" onClick={handleLeakReport} loading={leakState === 'sending'} disabled={leakState === 'sent' || !report.mockAttemptId}>
              {leakState === 'sent' ? 'Leak report sent' : 'Report leaked content'}
            </Button>
          </div>
        </MotionSection>

        {/* 2. Prior Comparison */}
        {comp.exists && (
          <MotionSection
            delayIndex={1}
            className="bg-background-light rounded-2xl border border-border p-6"
          >
            <div className="flex items-start gap-4">
              <div className={`w-10 h-10 rounded-full flex items-center justify-center shrink-0 ${
                comp.overallTrend === 'up'   ? 'bg-success/10 text-success' :
                comp.overallTrend === 'down' ? 'bg-danger/10 text-danger' :
                                               'bg-border text-muted'
              }`}>
                {comp.overallTrend === 'up'   && <TrendingUp className="w-5 h-5" />}
                {comp.overallTrend === 'down' && <TrendingDown className="w-5 h-5" />}
                {comp.overallTrend === 'flat' && <Minus className="w-5 h-5" />}
              </div>
              <div>
                <h3 className="text-sm font-black text-navy uppercase tracking-widest mb-1">
                  Compared to {comp.priorMockName}
                </h3>
                <p className="text-sm text-muted leading-relaxed">{comp.details}</p>
              </div>
            </div>
          </MotionSection>
        )}

        {/* 3. Sub-test Breakdown */}
        <MotionSection
          delayIndex={2}
        >
          <h2 className="text-sm font-black text-muted uppercase tracking-widest mb-4">Sub-test Breakdown</h2>
          <div className="grid grid-cols-1 sm:grid-cols-2 gap-4">
            {report.subTests.map((test) => {
              const meta = SUBTEST_META[test.id] ?? SUBTEST_META.listening;
              const Icon = meta.icon;
              const isWriting = test.id === 'writing';
              const canDownload = isWriting && Boolean(report.mockAttemptId);
              return (
                <div key={test.id} className="bg-surface rounded-2xl border border-border p-5 shadow-sm">
                  <div className="flex items-center justify-between">
                    <div className="flex items-center gap-4">
                      <div className={`w-12 h-12 rounded-2xl ${meta.bg} flex items-center justify-center shrink-0`}>
                        <Icon className={`w-6 h-6 ${meta.color}`} />
                      </div>
                      <div>
                        <h3 className="text-base font-bold text-navy">{test.name}</h3>
                        <p className="text-xs text-muted">Raw: {test.rawScore}</p>
                        {test.reviewState ? (
                          <p className="mt-1 text-[10px] font-black uppercase tracking-widest text-warning">
                            Review {test.reviewState.replace(/_/g, ' ')}
                          </p>
                        ) : null}
                      </div>
                    </div>
                    <span className={`text-2xl font-black ${scoreColor(test.score)}`}>{test.score}</span>
                  </div>
                  {canDownload ? (
                    <div className="mt-4 border-t border-border pt-3">
                      <Button
                        variant="secondary"
                        size="sm"
                        className="w-full"
                        onClick={handleDownloadWritingPdf}
                        loading={pdfState === 'downloading'}
                        disabled={pdfState === 'downloading'}
                      >
                        <Download className="mr-2 h-4 w-4" />
                        Download practice PDF
                      </Button>
                      <p className="mt-2 text-[11px] leading-4 text-muted">
                        Watermarked “Practice Copy”. For your own study only — not for resale or redistribution.
                      </p>
                      {pdfState === 'error' && pdfError ? (
                        <p className="mt-2 text-[11px] text-danger" role="alert">
                          {pdfError}
                        </p>
                      ) : null}
                    </div>
                  ) : null}
                </div>
              );
            })}
          </div>
        </MotionSection>

        {/* 4. Weakest Criterion */}
        <MotionSection
          delayIndex={3}
        >
          <h2 className="text-sm font-black text-muted uppercase tracking-widest mb-4">Area for Improvement</h2>
          <div className="bg-danger/10 rounded-2xl border border-danger/30 p-6 sm:p-8">
            <div className="flex items-start gap-4">
              <div className="w-12 h-12 rounded-2xl bg-danger/10 flex items-center justify-center shrink-0">
                <AlertTriangle className="w-6 h-6 text-danger" />
              </div>
              <div>
                <div className="flex items-center gap-2 mb-1">
                  <span className="text-xs font-black text-danger uppercase tracking-widest">{report.weakestCriterion.subtest}</span>
                  <span className="text-danger/40">•</span>
                  <span className="text-xs font-black text-danger uppercase tracking-widest">Weakest Criterion</span>
                </div>
                <h3 className="text-lg font-black text-danger mb-2">{report.weakestCriterion.criterion}</h3>
                <p className="text-sm text-danger/80 leading-relaxed">{report.weakestCriterion.description}</p>
              </div>
            </div>
          </div>
        </MotionSection>

        {/* 5. Words to Review — surfaces OET vocabulary tied to the weakest criterion */}
        <MockVocabularyReview
          mockId={report.id}
          weakSubtest={report.weakestCriterion.subtest}
          weakCriterion={report.weakestCriterion.criterion}
          weakDescription={report.weakestCriterion.description}
        />

        {/* 6. Remediation Plan — spec requirement: every mock report ends with a concrete next 7-day plan.
            When server-side W5 RemediationTask plan exists we render it (with completion controls);
            otherwise we fall back to the deterministic client-derived plan. */}
        <MotionSection delayIndex={5}>
          <h2 className="text-sm font-black text-muted uppercase tracking-widest mb-4">Your next 7-day plan</h2>
          {serverTasks && serverTasks.length > 0 ? (
            <div className="grid gap-4 lg:grid-cols-5">
              {serverTasks
                .slice()
                .sort((a, b) => a.dayIndex - b.dayIndex)
                .map((task) => {
                  const completed = task.status === 'completed';
                  return (
                    <div
                      key={task.id}
                      className={`group flex flex-col rounded-2xl border bg-surface p-4 shadow-sm transition-all ${completed ? 'border-success/40 opacity-80' : 'border-border hover:border-primary/30 hover:shadow-md'}`}
                    >
                      <span className="text-[10px] font-black uppercase tracking-widest text-primary">Day {task.dayIndex}</span>
                      <h3 className="mt-2 text-sm font-black text-navy">{task.title}</h3>
                      <p className="mt-2 text-xs leading-5 text-muted">{task.description}</p>
                      <div className="mt-auto flex items-center justify-between gap-2 pt-3">
                        {task.routeHref ? (
                          <Link href={task.routeHref} className="text-[11px] font-black uppercase tracking-widest text-primary hover:underline">
                            Start
                          </Link>
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
                    </div>
                  );
                })}
            </div>
          ) : (
            <div className="grid gap-4 lg:grid-cols-5">
              {remediationPlan.map((item) => (
                <Link
                  key={`${item.day}-${item.title}`}
                  href={item.route}
                  className="group rounded-2xl border border-border bg-surface p-4 shadow-sm transition-all hover:border-primary/30 hover:shadow-md"
                >
                  <span className="text-[10px] font-black uppercase tracking-widest text-primary">{item.day}</span>
                  <h3 className="mt-2 text-sm font-black text-navy transition-colors group-hover:text-primary">{item.title}</h3>
                  <p className="mt-2 text-xs leading-5 text-muted">{item.description}</p>
                </Link>
              ))}
            </div>
          )}
        </MotionSection>

        {/* 7. Study Plan CTA */}
        <MotionSection
          delayIndex={6}
          className="pt-4"
        >
          <div className="bg-navy rounded-2xl p-8 text-center text-white relative overflow-hidden shadow-lg">
            <div className="absolute inset-0 bg-[radial-gradient(circle_at_top_right,_rgba(255,255,255,0.1),_transparent)]" />
            <div className="relative z-10">
              <div className="w-16 h-16 rounded-full bg-white/10 flex items-center justify-center mx-auto mb-4">
                <RefreshCw className="w-8 h-8 text-white" />
              </div>
              <h2 className="text-xl font-black mb-2">Update Your Study Plan</h2>
              <p className="text-sm text-white/70 max-w-md mx-auto mb-6">
                Based on this report, we recommend focusing on <strong>{report.weakestCriterion.criterion}</strong> in {report.weakestCriterion.subtest}.
              </p>
              <Link
                href="/study-plan"
                className="bg-white text-navy px-8 py-4 rounded-xl font-black hover:bg-background-light transition-colors inline-flex items-center justify-center gap-2"
              >
                Update Study Plan
              </Link>
            </div>
          </div>
        </MotionSection>

      </div>
    </LearnerDashboardShell>
  );
}

export default function MockReport() {
  return (
    <Suspense fallback={
      <LearnerDashboardShell pageTitle="Mock Report" backHref="/mocks">
        <div className="space-y-6">
          {[1, 2, 3].map(i => (
            <Skeleton key={i} className="h-32 rounded-2xl" />
          ))}
        </div>
      </LearnerDashboardShell>
    }>
      <MockReportContent />
    </Suspense>
  );
}
