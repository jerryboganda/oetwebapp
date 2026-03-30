'use client';

import { useEffect, useMemo, useState } from 'react';
import { useParams, useRouter } from 'next/navigation';
import { ArrowLeft, CheckCircle2, FileText, GraduationCap, MessageSquare } from 'lucide-react';
import { AsyncStateWrapper } from '@/components/state/async-state-wrapper';
import { Button } from '@/components/ui/button';
import { Card, CardContent } from '@/components/ui/card';
import { Select, Textarea } from '@/components/ui/form-controls';
import { InlineAlert, Toast } from '@/components/ui/alert';
import { ExpertFreshnessBadge, ExpertPageHeader, ExpertSectionPanel } from '@/components/domain/expert-surface';
import { fetchCalibrationCaseDetail, isApiError, submitCalibrationCase } from '@/lib/api';
import type { CalibrationCaseDetail } from '@/lib/types/expert';
import { analytics } from '@/lib/analytics';

type AsyncStatus = 'loading' | 'error' | 'success';

function toCriterionLabel(value: string) {
  return value.replace(/([A-Z])/g, ' $1').replace(/_/g, ' ').replace(/\b\w/g, (match) => match.toUpperCase()).trim();
}

export default function CalibrationCaseWorkspacePage() {
  const params = useParams();
  const caseId = params?.caseId as string | undefined;
  const router = useRouter();
  const [detail, setDetail] = useState<CalibrationCaseDetail | null>(null);
  const [status, setStatus] = useState<AsyncStatus>('loading');
  const [errorMessage, setErrorMessage] = useState<string | null>(null);
  const [scores, setScores] = useState<Record<string, string>>({});
  const [notes, setNotes] = useState('');
  const [isSubmitting, setIsSubmitting] = useState(false);
  const [toast, setToast] = useState<{ variant: 'success' | 'error'; message: string } | null>(null);

  useEffect(() => {
    if (!caseId) return;
    let cancelled = false;
    (async () => {
      try {
        setStatus('loading');
        setErrorMessage(null);
        const response = await fetchCalibrationCaseDetail(caseId);
        if (cancelled) return;
        setDetail(response);
        setScores(Object.fromEntries((response.existingSubmission?.submittedScores ? Object.entries(response.existingSubmission.submittedScores) : response.benchmarkRubric.map((entry) => [entry.criterion, ''])).map(([key, value]) => [key, String(value)])));
        setNotes(response.existingSubmission?.notes ?? '');
        setStatus('success');
        analytics.track('expert_calibration_case_viewed', { caseId });
      } catch (error) {
        if (!cancelled) {
          setErrorMessage(isApiError(error) ? error.userMessage : 'Unable to load this calibration case right now.');
          setStatus('error');
        }
      }
    })();

    return () => {
      cancelled = true;
    };
  }, [caseId]);

  const isReadOnly = Boolean(detail?.existingSubmission);
  const maxScore = detail?.subTest === 'writing' ? 7 : 6;
  const scoreOptions = useMemo(
    () => [{ value: '', label: 'Select score' }, ...Array.from({ length: maxScore + 1 }, (_, index) => ({ value: String(maxScore - index), label: String(maxScore - index) }))],
    [maxScore],
  );

  const handleSubmit = async () => {
    if (!detail) return;
    const normalized = Object.fromEntries(Object.entries(scores).filter(([, value]) => value !== '').map(([criterion, value]) => [criterion, Number(value)]));
    if (Object.keys(normalized).length !== detail.benchmarkRubric.length) {
      setToast({ variant: 'error', message: 'Score every benchmark criterion before submitting the calibration case.' });
      return;
    }

    setIsSubmitting(true);
    try {
      await submitCalibrationCase(detail.id, { scores: normalized, notes: notes.trim() || undefined });
      setToast({ variant: 'success', message: 'Calibration case submitted successfully.' });
      analytics.track('expert_calibration_case_submitted', { caseId: detail.id });
      const refreshed = await fetchCalibrationCaseDetail(detail.id);
      setDetail(refreshed);
      setScores(Object.fromEntries(Object.entries(refreshed.existingSubmission?.submittedScores ?? normalized).map(([key, value]) => [key, String(value)])));
      setNotes(refreshed.existingSubmission?.notes ?? notes);
    } catch (error) {
      setToast({ variant: 'error', message: isApiError(error) ? error.userMessage : 'Unable to submit this calibration case.' });
    } finally {
      setIsSubmitting(false);
    }
  };

  return (
    <div className="mx-auto flex max-w-7xl flex-col gap-6 p-4 md:p-8" role="main" aria-label="Calibration case workspace">
      {toast ? <Toast variant={toast.variant} message={toast.message} onClose={() => setToast(null)} /> : null}

      <Button variant="ghost" className="w-fit pl-0 text-slate-500" onClick={() => router.push('/expert/calibration')}>
        <ArrowLeft className="mr-2 h-4 w-4" /> Back to calibration
      </Button>

      <AsyncStateWrapper
        status={status}
        onRetry={() => router.refresh()}
        errorMessage={errorMessage ?? undefined}
      >
        {detail && (
          <>
            <ExpertPageHeader
              meta="Benchmark Workspace"
              title={detail.title}
              description="Inspect exemplar artifacts, compare against benchmark rubric logic, and submit criterion-level scoring in the same workspace."
              actions={<ExpertFreshnessBadge value={detail.existingSubmission?.submittedAt ?? detail.createdAt} />}
            />

            {isReadOnly ? (
              <InlineAlert variant="success" title="Calibration already submitted">
                Your benchmark submission is locked in for this case. You can still inspect the rubric rationale, your prior scores, and the benchmark evidence below.
              </InlineAlert>
            ) : (
              <InlineAlert variant="info" title="Advisory benchmark workflow">
                Score each criterion independently, then compare your reasoning with the benchmark rationale. This workspace is intended to sharpen rubric consistency, not replace live review judgment.
              </InlineAlert>
            )}

            <div className="grid grid-cols-1 gap-6 xl:grid-cols-[1.1fr_0.9fr]">
              <ExpertSectionPanel
                title="Case Artifacts"
                description={`${detail.subTest} benchmark • ${detail.profession.replace(/_/g, ' ')} • ${detail.benchmarkLabel}`}
              >
                <div className="space-y-4">
                  {detail.artifacts.map((artifact) => (
                    <Card key={`${artifact.kind}-${artifact.title}`} className="border-slate-200 shadow-none">
                      <CardContent className="space-y-2 p-4">
                        <p className="text-xs font-semibold uppercase tracking-[0.12em] text-slate-400">{artifact.title}</p>
                        <p className="whitespace-pre-wrap text-sm leading-6 text-slate-700">{artifact.content}</p>
                      </CardContent>
                    </Card>
                  ))}
                </div>
              </ExpertSectionPanel>

              <ExpertSectionPanel
                title="Benchmark Rubric"
                description="Reference benchmark scores and rationale for each criterion."
                actions={<span className="text-xs text-slate-400">Overall benchmark: {detail.benchmarkScore}</span>}
              >
                <div className="space-y-3">
                  {detail.benchmarkRubric.map((entry) => (
                    <div key={entry.criterion} className="rounded-2xl border border-slate-200 bg-white p-4">
                      <div className="flex items-center justify-between gap-3">
                        <div>
                          <p className="text-sm font-semibold text-slate-900">{toCriterionLabel(entry.criterion)}</p>
                          <p className="mt-1 text-sm text-slate-500">{entry.rationale}</p>
                        </div>
                        <span className="rounded-full bg-slate-100 px-2.5 py-1 text-xs font-medium text-slate-700">
                          Benchmark {entry.benchmarkScore}
                        </span>
                      </div>
                    </div>
                  ))}
                </div>

                <div className="rounded-2xl border border-slate-200 bg-slate-50 p-4">
                  <div className="flex items-center gap-2">
                    <FileText className="h-4 w-4 text-slate-400" />
                    <h3 className="text-sm font-semibold text-slate-900">Reference Notes</h3>
                  </div>
                  <ul className="mt-3 space-y-2">
                    {detail.referenceNotes.map((note) => (
                      <li key={note} className="text-sm text-slate-600">• {note}</li>
                    ))}
                  </ul>
                </div>
              </ExpertSectionPanel>
            </div>

            <div className="grid grid-cols-1 gap-6 xl:grid-cols-[0.95fr_1.05fr]">
              <ExpertSectionPanel
                title="Your Calibration Submission"
                description={isReadOnly ? 'Submitted scores and notes from your completed benchmark attempt.' : 'Enter criterion-level scores using the benchmark rubric as your comparison anchor.'}
                actions={<GraduationCap className="h-5 w-5 text-slate-400" />}
              >
                <div className="space-y-4">
                  {detail.benchmarkRubric.map((entry) => (
                    <div key={entry.criterion} className="grid grid-cols-1 gap-3 rounded-2xl border border-slate-200 bg-white p-4 md:grid-cols-[1fr_180px] md:items-start">
                      <div>
                        <p className="text-sm font-semibold text-slate-900">{toCriterionLabel(entry.criterion)}</p>
                        <p className="mt-1 text-xs text-slate-500">Benchmark {entry.benchmarkScore} • Submit the score you would give this criterion.</p>
                      </div>
                      <Select
                        value={scores[entry.criterion] ?? ''}
                        onChange={(event) => setScores((current) => ({ ...current, [entry.criterion]: event.target.value }))}
                        options={scoreOptions}
                        disabled={isReadOnly}
                        aria-label={`Calibration score for ${entry.criterion}`}
                      />
                    </div>
                  ))}

                  <Textarea
                    label="Calibration Notes"
                    value={notes}
                    onChange={(event) => setNotes(event.target.value)}
                    rows={5}
                    disabled={isReadOnly}
                    placeholder="Capture the reasoning behind any benchmark disagreement or borderline decision."
                  />

                  {isReadOnly ? null : (
                    <div className="flex justify-end">
                      <Button onClick={() => void handleSubmit()} disabled={isSubmitting}>
                        {isSubmitting ? 'Submitting...' : 'Submit Calibration'}
                      </Button>
                    </div>
                  )}
                </div>
              </ExpertSectionPanel>

              <ExpertSectionPanel
                title="Alignment Evidence"
                description="Use the benchmark workspace to inspect how your criterion judgments compare with the reference position."
                actions={detail.existingSubmission ? <CheckCircle2 className="h-5 w-5 text-emerald-500" /> : <MessageSquare className="h-5 w-5 text-slate-400" />}
              >
                {detail.existingSubmission ? (
                  <div className="space-y-4">
                    <div className="grid grid-cols-1 gap-4 sm:grid-cols-3">
                      <div className="rounded-2xl bg-slate-50 p-4">
                        <p className="text-xs font-medium uppercase tracking-[0.12em] text-slate-500">Reviewer Score</p>
                        <p className="mt-2 text-lg font-semibold text-slate-900">{detail.existingSubmission.reviewerScore}</p>
                      </div>
                      <div className="rounded-2xl bg-slate-50 p-4">
                        <p className="text-xs font-medium uppercase tracking-[0.12em] text-slate-500">Alignment</p>
                        <p className="mt-2 text-lg font-semibold text-slate-900">{detail.existingSubmission.alignmentScore}%</p>
                      </div>
                      <div className="rounded-2xl bg-slate-50 p-4">
                        <p className="text-xs font-medium uppercase tracking-[0.12em] text-slate-500">Submitted</p>
                        <p className="mt-2 text-lg font-semibold text-slate-900">{new Date(detail.existingSubmission.submittedAt).toLocaleDateString()}</p>
                      </div>
                    </div>

                    <div className="rounded-2xl border border-slate-200 bg-white p-4">
                      <p className="text-sm font-semibold text-slate-900">Disagreement summary</p>
                      <p className="mt-2 text-sm text-slate-600">{detail.existingSubmission.disagreementSummary}</p>
                    </div>

                    <div className="space-y-3">
                      {detail.benchmarkRubric.map((entry) => {
                        const submitted = detail.existingSubmission?.submittedScores?.[entry.criterion];
                        const gap = submitted === undefined ? null : Math.abs(submitted - entry.benchmarkScore);
                        return (
                          <div key={entry.criterion} className="rounded-2xl border border-slate-200 bg-white p-4">
                            <div className="flex items-center justify-between gap-3">
                              <p className="text-sm font-semibold text-slate-900">{toCriterionLabel(entry.criterion)}</p>
                              <div className="flex items-center gap-2 text-xs">
                                <span className="rounded-full bg-slate-100 px-2 py-1 text-slate-600">Benchmark {entry.benchmarkScore}</span>
                                <span className="rounded-full bg-blue-50 px-2 py-1 text-blue-700">You {submitted ?? '-'}</span>
                                {gap !== null ? <span className="rounded-full bg-amber-50 px-2 py-1 text-amber-700">Gap {gap}</span> : null}
                              </div>
                            </div>
                          </div>
                        );
                      })}
                    </div>
                  </div>
                ) : (
                  <div className="rounded-2xl border border-dashed border-slate-200 bg-slate-50 p-5 text-sm text-slate-500">
                    Submit your criterion scores to unlock alignment evidence, benchmark delta summaries, and your recorded notes for this case.
                  </div>
                )}
              </ExpertSectionPanel>
            </div>
          </>
        )}
      </AsyncStateWrapper>
    </div>
  );
}
