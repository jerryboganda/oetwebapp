'use client';

import { useEffect, useMemo, useState } from 'react';
import { useParams, useRouter } from 'next/navigation';
import { ArrowLeft, CheckCircle2, FileText, GraduationCap, MessageSquare, Sparkles } from 'lucide-react';
import { AsyncStateWrapper } from '@/components/state/async-state-wrapper';
import { InlineAlert, Toast } from '@/components/ui/alert';
import { Button } from '@/components/ui/button';
import { Card, CardContent } from '@/components/ui/card';
import { Select, Textarea } from '@/components/ui/form-controls';
import {
  ExpertRouteFreshnessBadge,
  ExpertRouteHero,
  ExpertRouteSectionHeader,
  ExpertRouteSummaryCard,
  ExpertRouteWorkspace,
} from '@/components/domain/expert-route-surface';
import { fetchCalibrationCaseDetail, isApiError, saveCalibrationDraft, submitCalibrationCase } from '@/lib/api';
import { analytics } from '@/lib/analytics';
import type { CalibrationCaseDetail, SubTest } from '@/lib/types/expert';

type AsyncStatus = 'loading' | 'error' | 'success';

const SPEAKING_CLINICAL_CRITERIA = new Set([
  'relationshipBuilding',
  'patientPerspective',
  'providingStructure',
  'informationGathering',
  'informationGiving',
]);

function toCriterionLabel(value: string) {
  return value.replace(/([A-Z])/g, ' $1').replace(/_/g, ' ').replace(/\b\w/g, (match) => match.toUpperCase()).trim();
}

function formatDisplayDate(value: string | null | undefined) {
  if (!value) return null;
  const parsed = new Date(value);
  if (Number.isNaN(parsed.getTime())) return null;
  return parsed.toLocaleString();
}

function maxScoreForCriterion(subTest: SubTest, criterion: string) {
  if (subTest === 'writing') {
    return criterion === 'purpose' ? 3 : 7;
  }

  return SPEAKING_CLINICAL_CRITERIA.has(criterion) ? 3 : 6;
}

function scoreOptionsFor(subTest: SubTest, criterion: string) {
  const maxScore = maxScoreForCriterion(subTest, criterion);
  return [{ value: '', label: 'Select score' }, ...Array.from({ length: maxScore + 1 }, (_, index) => {
    const score = maxScore - index;
    return { value: String(score), label: String(score) };
  })];
}

function scoresFromDetail(response: CalibrationCaseDetail) {
  const submittedScores = response.existingSubmission?.submittedScores ?? {};
  return Object.fromEntries(response.benchmarkRubric.map((entry) => [entry.criterion, submittedScores[entry.criterion] === undefined ? '' : String(submittedScores[entry.criterion])]));
}

export default function CalibrationCaseWorkspacePage() {
  const params = useParams();
  const rawCaseId = params?.caseId;
  const caseId = Array.isArray(rawCaseId) ? rawCaseId[0] ?? '' : rawCaseId ?? '';
  const router = useRouter();
  const [detail, setDetail] = useState<CalibrationCaseDetail | null>(null);
  const [status, setStatus] = useState<AsyncStatus>('loading');
  const [errorMessage, setErrorMessage] = useState<string | null>(null);
  const [scores, setScores] = useState<Record<string, string>>({});
  const [notes, setNotes] = useState('');
  const [isSubmitting, setIsSubmitting] = useState(false);
  const [isSavingDraft, setIsSavingDraft] = useState(false);
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
        setScores(scoresFromDetail(response));
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

  const isDraft = detail?.status === 'draft' || detail?.existingSubmission?.isDraft === true;
  const isReadOnly = detail?.status === 'completed';
  const submissionState = isReadOnly ? 'Submitted (Locked)' : isDraft ? 'Draft (Editable)' : 'Open';
  const freshnessValue = detail?.existingSubmission?.updatedAt ?? detail?.existingSubmission?.submittedAt ?? detail?.createdAt;
  const draftLastSavedAt = formatDisplayDate(detail?.existingSubmission?.updatedAt);
  const submissionHint = isReadOnly
    ? 'Final submission is locked and preserved for reference only.'
    : isDraft
      ? `Draft is editable and resumes from your last save${draftLastSavedAt ? ` (${draftLastSavedAt})` : ''}.`
      : 'No draft exists yet — use Save Draft to continue this benchmark later.';
  const alignmentDisplay = detail?.existingSubmission?.alignmentScore == null ? 'N/A' : `${detail.existingSubmission.alignmentScore.toFixed(1)}%`;

  const normalizedScores = useMemo(
    () => Object.fromEntries(Object.entries(scores).filter(([, value]) => value !== '').map(([criterion, value]) => [criterion, Number(value)])),
    [scores],
  );

  const syncFromDetail = (payload: CalibrationCaseDetail) => {
    setDetail(payload);
    setScores(scoresFromDetail(payload));
    setNotes(payload.existingSubmission?.notes ?? '');
  };

  const refreshCase = async () => {
    if (!detail) return;
    const refreshed = await fetchCalibrationCaseDetail(detail.id);
    syncFromDetail(refreshed);
  };

  const handleSaveDraft = async () => {
    if (!detail) return;

    setIsSavingDraft(true);
    try {
      await saveCalibrationDraft(detail.id, { scores: normalizedScores, notes: notes.trim() || undefined });
      setToast({ variant: 'success', message: 'Calibration draft saved.' });
      analytics.track('expert_calibration_draft_saved', { caseId: detail.id, completedCriteria: Object.keys(normalizedScores).length });
      await refreshCase();
    } catch (error) {
      if (isApiError(error) && error.code === 'calibration_already_submitted') {
        await refreshCase();
      }
      setToast({ variant: 'error', message: isApiError(error) ? error.userMessage : 'Unable to save this calibration draft.' });
    } finally {
      setIsSavingDraft(false);
    }
  };

  const handleSubmit = async () => {
    if (!detail) return;
    if (Object.keys(normalizedScores).length !== detail.benchmarkRubric.length) {
      setToast({ variant: 'error', message: 'Score every benchmark criterion before submitting the calibration case.' });
      return;
    }

    setIsSubmitting(true);
    try {
      await submitCalibrationCase(detail.id, { scores: normalizedScores, notes: notes.trim() || undefined });
      setToast({ variant: 'success', message: 'Calibration case submitted successfully.' });
      analytics.track('expert_calibration_case_submitted', { caseId: detail.id });
      await refreshCase();
    } catch (error) {
      if (isApiError(error) && error.code === 'calibration_already_submitted') {
        await refreshCase();
      }
      setToast({ variant: 'error', message: isApiError(error) ? error.userMessage : 'Unable to submit this calibration case.' });
    } finally {
      setIsSubmitting(false);
    }
  };

  return (
    <ExpertRouteWorkspace role="main" aria-label="Calibration case workspace">
      {toast ? <Toast variant={toast.variant} message={toast.message} onClose={() => setToast(null)} /> : null}

      <div className="space-y-4">
        <Button variant="ghost" className="w-fit pl-0 text-slate-500" onClick={() => router.push('/expert/calibration')}>
          <ArrowLeft className="mr-2 h-4 w-4" /> Back to calibration
        </Button>

        <AsyncStateWrapper
          status={status}
          onRetry={() => router.refresh()}
          errorMessage={errorMessage ?? undefined}
        >
          {detail ? (
            <div className="space-y-6">
              <ExpertRouteHero
                eyebrow="Benchmark Workspace"
                icon={Sparkles}
                accent="primary"
                title={detail.title}
                description="Inspect exemplar artifacts, compare against benchmark rubric logic, and submit criterion-level scoring in a learner-style workspace."
                highlights={[
                  { icon: FileText, label: 'Artifacts', value: String(detail.artifacts.length) },
                  { icon: GraduationCap, label: 'Rubric criteria', value: String(detail.benchmarkRubric.length) },
                  { icon: CheckCircle2, label: 'Submission state', value: submissionState },
                ]}
                aside={<ExpertRouteFreshnessBadge value={freshnessValue} />}
              />

              <div className="grid grid-cols-1 gap-4 md:grid-cols-3">
                <ExpertRouteSummaryCard
                  label="Artifacts"
                  value={detail.artifacts.length}
                  hint={`${detail.subTest} benchmark - ${detail.profession.replace(/_/g, ' ')} - ${detail.benchmarkLabel}`}
                  icon={FileText}
                />
                <ExpertRouteSummaryCard
                  label="Rubric Criteria"
                  value={detail.benchmarkRubric.length}
                  hint="Reference benchmark scores and rationale for each criterion."
                  accent="navy"
                  icon={GraduationCap}
                />
                <ExpertRouteSummaryCard
                  label="Submission"
                  value={submissionState}
                  hint={submissionHint}
                  accent={isReadOnly ? 'emerald' : isDraft ? 'blue' : 'amber'}
                  icon={CheckCircle2}
                />
              </div>

              {isReadOnly ? (
                <InlineAlert variant="success" title="Calibration already submitted">
                  Your benchmark submission is locked in for this case. You can still inspect the rubric rationale, your prior scores, and the benchmark evidence below.
                </InlineAlert>
              ) : isDraft ? (
                <InlineAlert variant="info" title="Draft saved">
                  Your saved scores and notes are prefilled. Continue editing, save again, or submit when every benchmark criterion is complete.
                </InlineAlert>
              ) : (
                <InlineAlert variant="info" title="Advisory benchmark workflow">
                  Score each criterion independently, then compare your reasoning with the benchmark rationale. This workspace is intended to sharpen rubric consistency, not replace live review judgment.
                </InlineAlert>
              )}

              <div className="grid grid-cols-1 gap-6 xl:grid-cols-[1.1fr_0.9fr]">
                <section className="space-y-4">
                  <ExpertRouteSectionHeader
                    eyebrow="Case Artifacts"
                    title="Benchmark evidence"
                    description={`${detail.subTest} benchmark - ${detail.profession.replace(/_/g, ' ')} - ${detail.benchmarkLabel}`}
                  />
                  <Card className="border-slate-200 shadow-sm">
                    <CardContent className="space-y-4 p-5">
                      {detail.artifacts.map((artifact) => (
                        <Card key={`${artifact.kind}-${artifact.title}`} className="border-slate-200 shadow-none">
                          <CardContent className="space-y-2 p-4">
                            <p className="text-xs font-semibold uppercase tracking-[0.12em] text-slate-400">{artifact.title}</p>
                            <p className="whitespace-pre-wrap text-sm leading-6 text-slate-700">{artifact.content}</p>
                          </CardContent>
                        </Card>
                      ))}
                    </CardContent>
                  </Card>
                </section>

                <section className="space-y-4">
                  <ExpertRouteSectionHeader
                    eyebrow="Benchmark Rubric"
                    title="Reference scoring"
                    description="Use the reference benchmark scores and rationale for each criterion."
                    action={<span className="text-xs text-slate-400">Overall benchmark: {detail.benchmarkScore}</span>}
                  />
                  <Card className="border-slate-200 shadow-sm">
                    <CardContent className="space-y-3 p-5">
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
                    </CardContent>
                  </Card>

                  <Card className="border-slate-200 shadow-sm">
                    <CardContent className="p-5">
                      <div className="flex items-center gap-2">
                        <FileText className="h-4 w-4 text-slate-400" />
                        <h3 className="text-sm font-semibold text-slate-900">Reference Notes</h3>
                      </div>
                      <ul className="mt-3 space-y-2">
                        {detail.referenceNotes.map((note) => (
                          <li key={note} className="text-sm text-slate-600">
                            - {note}
                          </li>
                        ))}
                      </ul>
                    </CardContent>
                  </Card>
                </section>
              </div>

              <div className="grid grid-cols-1 gap-6 xl:grid-cols-[0.95fr_1.05fr]">
                <section className="space-y-4">
                  <ExpertRouteSectionHeader
                    eyebrow="Submission"
                    title="Your calibration"
                    description={isReadOnly ? 'Submitted scores and notes from your completed benchmark attempt.' : 'Enter criterion-level scores using the benchmark rubric as your comparison anchor.'}
                    action={isReadOnly ? <CheckCircle2 className="h-5 w-5 text-emerald-500" /> : <GraduationCap className="h-5 w-5 text-slate-400" />}
                  />
                  <Card className="border-slate-200 shadow-sm">
                    <CardContent className="space-y-4 p-5">
                      {detail.benchmarkRubric.map((entry) => (
                        <div key={entry.criterion} className="grid grid-cols-1 gap-3 rounded-2xl border border-slate-200 bg-white p-4 md:grid-cols-[1fr_180px] md:items-start">
                          <div>
                            <p className="text-sm font-semibold text-slate-900">{toCriterionLabel(entry.criterion)}</p>
                            <p className="mt-1 text-xs text-slate-500">
                              Benchmark {entry.benchmarkScore} - Submit a 0-{maxScoreForCriterion(detail.subTest, entry.criterion)} score for this criterion.
                            </p>
                          </div>
                          <Select
                            value={scores[entry.criterion] ?? ''}
                            onChange={(event) => setScores((current) => ({ ...current, [entry.criterion]: event.target.value }))}
                            options={scoreOptionsFor(detail.subTest, entry.criterion)}
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
                        <div className="flex flex-col gap-3 sm:flex-row sm:justify-end">
                          <Button variant="primary" onClick={() => void handleSaveDraft()} disabled={isSubmitting || isSavingDraft}>
                            {isSavingDraft ? 'Saving...' : 'Save draft'}
                          </Button>
                          <Button variant="secondary" onClick={() => void handleSubmit()} disabled={isSubmitting || isSavingDraft}>
                            {isSubmitting ? 'Submitting...' : 'Submit Calibration'}
                          </Button>
                        </div>
                      )}
                    </CardContent>
                  </Card>
                </section>

                <section className="space-y-4">
                  <ExpertRouteSectionHeader
                    eyebrow="Alignment Evidence"
                    title="How you compare"
                    description="Use the benchmark workspace to inspect how your criterion judgments compare with the reference position."
                    action={isReadOnly ? <CheckCircle2 className="h-5 w-5 text-emerald-500" /> : <MessageSquare className="h-5 w-5 text-slate-400" />}
                  />
                  <Card className="border-slate-200 shadow-sm">
                    <CardContent className="space-y-4 p-5">
                      {isReadOnly && detail.existingSubmission ? (
                        <>
                          <div className="grid grid-cols-1 gap-4 sm:grid-cols-3">
                            <div className="rounded-2xl bg-slate-50 p-4">
                              <p className="text-xs font-medium uppercase tracking-[0.12em] text-slate-500">Reviewer Score</p>
                              <p className="mt-2 text-lg font-semibold text-slate-900">{detail.existingSubmission.reviewerScore}</p>
                            </div>
                            <div className="rounded-2xl bg-slate-50 p-4">
                              <p className="text-xs font-medium uppercase tracking-[0.12em] text-slate-500">Alignment</p>
                              <p className="mt-2 text-lg font-semibold text-slate-900">{alignmentDisplay}</p>
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
                        </>
                      ) : isDraft ? (
                        <div className="rounded-2xl border border-dashed border-blue-200 bg-blue-50 p-5 text-sm text-blue-700">
                          Draft saved. Submit the completed rubric to unlock alignment evidence, benchmark delta summaries, and your recorded final notes for this case.
                        </div>
                      ) : (
                        <div className="rounded-2xl border border-dashed border-slate-200 bg-slate-50 p-5 text-sm text-slate-500">
                          Submit your criterion scores to unlock alignment evidence, benchmark delta summaries, and your recorded notes for this case.
                        </div>
                      )}
                    </CardContent>
                  </Card>
                </section>
              </div>
            </div>
          ) : null}
        </AsyncStateWrapper>
      </div>
    </ExpertRouteWorkspace>
  );
}
