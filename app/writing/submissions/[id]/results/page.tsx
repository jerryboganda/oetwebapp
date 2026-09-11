'use client';

import { useCallback, useEffect, useState } from 'react';
import { useParams } from 'next/navigation';
import Link from 'next/link';
import { useTranslations } from 'next-intl';
import { Award, FileText, Flag, RefreshCw, Share2, Sparkles, UserRoundCheck } from 'lucide-react';
import { LearnerDashboardShell } from '@/components/layout/learner-dashboard-shell';
import { Button } from '@/components/ui/button';
import { Badge } from '@/components/ui/badge';
import { InlineAlert } from '@/components/ui/alert';
import { Card, CardContent } from '@/components/ui/card';
import { LearnerPageHero } from '@/components/domain/learner-surface';
import { ResultsScorePanel } from '@/components/domain/results/results-score-panel';
import { CriterionScoreRow } from '@/components/domain/results/criterion-score-row';
import { CriteriaRadar } from '@/components/domain/writing/CriteriaRadar';
import { CanonViolationCard } from '@/components/domain/writing/CanonViolationCard';
import {
  appealWritingSubmission,
  disputeWritingCanonViolation,
  getTutorReview,
  getWritingAnswerSheet,
  getWritingAssessmentV11,
  getWritingSubmission,
  getWritingSubmissionCaseNotes,
  getWritingSubmissionGrade,
  publishToShowcase,
} from '@/lib/writing/api';
import { parseHighlights } from '@/lib/writing/highlights';
import { TutorVoiceNotePlayer } from '@/components/domain/writing/TutorVoiceNotePlayer';
import { WritingStimulusViewer } from '@/components/domain/writing/WritingStimulusViewer';
import type {
  WritingCaseNotesDto,
  WritingCriteriaScoresDto,
  WritingCriterionCode,
  WritingGradeDto,
  WritingAssessmentV11ReportDto,
  WritingSubmissionDto,
  WritingTutorReviewDto,
} from '@/lib/writing/types';

const CRITERION_NAMES: Record<WritingCriterionCode, string> = {
  c1: 'C1 Purpose',
  c2: 'C2 Content',
  c3: 'C3 Conciseness & Clarity',
  c4: 'C4 Genre & Style',
  c5: 'C5 Organisation & Layout',
  c6: 'C6 Language Accuracy',
};

// OET writing criterion scales: C1 Purpose is out of 3, the rest out of 7.
// Targets mirror the CriteriaRadar overlay (band-6 style anchor).
const CRITERION_MAX: Record<WritingCriterionCode, number> = { c1: 3, c2: 7, c3: 7, c4: 7, c5: 7, c6: 7 };
const CRITERION_TARGET: Record<WritingCriterionCode, number> = { c1: 3, c2: 6, c3: 6, c4: 6, c5: 6, c6: 6 };

function gradeToScores(g: WritingGradeDto): WritingCriteriaScoresDto {
  return {
    c1: g.c1Purpose,
    c2: g.c2Content,
    c3: g.c3Conciseness,
    c4: g.c4Genre,
    c5: g.c5Organisation,
    c6: g.c6Language,
  };
}

function assessmentToScores(report: WritingAssessmentV11ReportDto): WritingCriteriaScoresDto {
  const scores: WritingCriteriaScoresDto = { c1: 0, c2: 0, c3: 0, c4: 0, c5: 0, c6: 0 };
  const map: Record<string, keyof WritingCriteriaScoresDto> = {
    purpose: 'c1',
    content: 'c2',
    conciseness_clarity: 'c3',
    genre_style: 'c4',
    organisation_layout: 'c5',
    language: 'c6',
  };
  for (const criterion of report.criteria) {
    const key = map[criterion.criterionCode];
    if (key) scores[key] = criterion.score;
  }
  return scores;
}

export default function WritingSubmissionResultsPage() {
  const t = useTranslations();
  const params = useParams<{ id: string }>();
  const submissionId = String(params?.id ?? '');

  const [submission, setSubmission] = useState<WritingSubmissionDto | null>(null);
  const [grade, setGrade] = useState<WritingGradeDto | null>(null);
  const [assessment, setAssessment] = useState<WritingAssessmentV11ReportDto | null>(null);
  const [tutorReview, setTutorReview] = useState<WritingTutorReviewDto | null>(null);
  const [answerSheetPath, setAnswerSheetPath] = useState<string | null>(null);
  const [caseNotes, setCaseNotes] = useState<WritingCaseNotesDto | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [actionStatus, setActionStatus] = useState<string | null>(null);

  useEffect(() => {
    if (!submissionId) return;
    void Promise.all([
      getWritingSubmission(submissionId),
      getWritingSubmissionGrade(submissionId).catch(() => null),
      getWritingAssessmentV11(submissionId).catch(() => null),
      // Tutor's review. Per-criterion comments render for mocks only; the
      // optional overall text note renders in BOTH modes when present.
      getTutorReview(submissionId).catch(() => null),
      // Answer-sheet PDF (post-submission only; null when none attached).
      getWritingAnswerSheet(submissionId).catch(() => ({ answerSheetPdfDownloadPath: null })),
      // Case Notes PDF + the learner's highlight snapshot (read-only review).
      getWritingSubmissionCaseNotes(submissionId).catch(() => null),
      ])
      .then(([sub, g, report, review, answerSheet, notes]) => {
        setSubmission(sub);
        setGrade(g);
        setAssessment(report);
        setTutorReview(review);
        setAnswerSheetPath(answerSheet?.answerSheetPdfDownloadPath ?? null);
        setCaseNotes(notes ?? null);
      })
      .catch((err) => setError(err instanceof Error ? err.message : t('writing.submissions.results.error.load')));
  }, [submissionId, t]);

  const onAppeal = useCallback(async () => {
    if (!submissionId) return;
    const reason = window.prompt(
      t('writing.submissions.results.actions.appealPrompt'),
      '',
    );
    if (!reason || reason.trim().length < 20) {
      setActionStatus(t('writing.submissions.results.actions.appealCancelled'));
      return;
    }
    setActionStatus(t('writing.submissions.results.actions.appealSubmitting'));
    try {
      await appealWritingSubmission(submissionId, { reason: reason.trim() });
      setActionStatus(t('writing.submissions.results.actions.appealSubmitted'));
    } catch (err) {
      setActionStatus(err instanceof Error ? err.message : t('writing.submissions.results.actions.appealError'));
    }
  }, [submissionId, t]);

  const onShowcase = useCallback(async () => {
    if (!submissionId) return;
    setActionStatus(t('writing.submissions.results.actions.showcasePublishing'));
    try {
      await publishToShowcase(submissionId);
      setActionStatus(t('writing.submissions.results.actions.showcasePublished'));
    } catch (err) {
      setActionStatus(err instanceof Error ? err.message : t('writing.submissions.results.actions.showcaseError'));
    }
  }, [submissionId, t]);

  const onDisputeViolation = useCallback(async (ruleId: string, violationId: string) => {
    const reason = window.prompt(t('writing.submissions.results.actions.disputePrompt'), '');
    if (!reason || reason.trim().length < 10) return;
    try {
      await disputeWritingCanonViolation(submissionId, { ruleId, violationId, reason: reason.trim() });
    } catch (err) {
      setActionStatus(err instanceof Error ? err.message : t('writing.submissions.results.actions.disputeError'));
    }
  }, [submissionId, t]);

  const assessmentVisible = assessment?.status === 'CandidateReady'
    && assessment.candidateReportVisible
    && assessment.candidateNumericScoreEnabled;
  const scores = grade ? gradeToScores(grade) : null;
  const assessmentScores = assessmentVisible && assessment ? assessmentToScores(assessment) : null;
  const isA = grade?.bandLabel?.startsWith('A');
  const offerRevision = grade?.revisionInvite?.shouldOffer ?? false;
  // Mock writing is human-marked with zero AI: show the tutor's WRITTEN feedback and
  // voice note, and suppress every AI-flavoured section. Normal writing keeps AI feedback.
  const isMock = submission?.mode === 'mock';

  return (
    <LearnerDashboardShell pageTitle={t('writing.submissions.results.pageTitle')}>
      <div className="space-y-6" aria-busy={!grade}>
        {grade ? (
          <ResultsScorePanel
            eyebrow={t('writing.submissions.results.eyebrow')}
            icon={Award}
            title={t('writing.submissions.results.estimatedBand', { band: grade.bandLabel })}
            subtitle={t('writing.submissions.results.description')}
            gaugeValue={(grade.estimatedBand / 7) * 100}
            gaugeCenter={<span className="text-2xl font-black text-navy dark:text-white">{grade.bandLabel}</span>}
            gaugeLabel={`${grade.rawTotal}/38`}
            gaugeColor={grade.estimatedBand >= 6 ? 'var(--color-success)' : grade.estimatedBand >= 4 ? 'var(--color-warning)' : 'var(--color-danger)'}
            stats={[
              { label: t('writing.submissions.results.highlights.raw'), value: `${grade.rawTotal}/38`, tone: 'info', icon: <Award /> },
              { label: t('writing.submissions.results.highlights.mode'), value: submission?.mode ?? '-', tone: 'default', icon: <FileText /> },
              // Confidence is an AI signal — hide it on mocks (human-marked, zero AI).
              ...(isMock ? [] : [{ label: t('writing.submissions.results.highlights.confidence'), value: grade.confidenceFlag, tone: 'default' as const, icon: <Sparkles /> }]),
            ]}
          />
        ) : assessmentVisible && assessment?.estimatedPracticeScore != null ? (
          <ResultsScorePanel
            eyebrow="Writing assessment v1.1"
            icon={Award}
            title={assessment.scoreLabel}
            subtitle="An AI-generated practice estimate, not an official OET result."
            gaugeValue={(assessment.estimatedPracticeScore / 500) * 100}
            gaugeCenter={<span className="text-2xl font-black text-navy dark:text-white">{assessment.estimatedPracticeScore}</span>}
            gaugeLabel={assessment.scoreRange ?? assessment.gradeBand ?? 'AI estimate'}
            gaugeColor={assessment.estimatedPracticeScore >= 350 ? 'var(--color-success)' : assessment.estimatedPracticeScore >= 300 ? 'var(--color-warning)' : 'var(--color-danger)'}
            stats={[
              { label: 'Score', value: `${assessment.estimatedPracticeScore}/500`, tone: 'info', icon: <Award /> },
              ...(assessment.gradeBand ? [{ label: 'Grade band', value: assessment.gradeBand, tone: 'info' as const, icon: <Award /> }] : []),
              { label: 'Confidence', value: assessment.confidenceLabel ?? 'restricted', tone: 'default', icon: <Sparkles /> },
              { label: 'Version', value: assessment.calibrationSetVersion, tone: 'default', icon: <FileText /> },
            ]}
          />
        ) : (
          <LearnerPageHero
            eyebrow={t('writing.submissions.results.eyebrow')}
            icon={Award}
            accent="amber"
            title={t('writing.submissions.results.awaiting')}
            description={t('writing.submissions.results.description')}
          />
        )}

        {error ? <InlineAlert variant="error">{error}</InlineAlert> : null}
        {actionStatus ? <InlineAlert variant="info">{actionStatus}</InlineAlert> : null}
        {assessment && !assessmentVisible ? (
          <InlineAlert variant="info">
            This submission is not yet candidate-visible under the v1.1 release gate.
            {assessment.blockingCodes.length ? ` Blocked by: ${assessment.blockingCodes.join(', ')}.` : ''}
          </InlineAlert>
        ) : null}

        {/* "Revise / Review the Letter" for THIS completed attempt is simply
            reopening this results page: it re-fetches the saved submission via
            GET only (no grading call, no credit deducted, no editable
            resubmission), so the exact original letter belongs in this same
            report alongside the score/criteria below (Writing Rule Enforcement
            Addendum Rev5, 10 Sep 2026, §13). */}
        {submission?.letterContent ? (
          <section aria-labelledby="your-letter-heading" className="rounded-2xl border border-border bg-surface p-5 shadow-sm">
            <div className="flex flex-wrap items-center justify-between gap-2">
              <h2 id="your-letter-heading" className="text-lg font-bold text-navy">Your submitted letter</h2>
              <Badge variant="muted" size="sm">Reviewing your saved submission — no credit used</Badge>
            </div>
            {/* The submitted letter text is learner-authored English content. */}
            <pre className="mt-3 whitespace-pre-wrap rounded-lg border border-border bg-background p-3 text-sm leading-relaxed font-sans" dir="ltr">
              {submission.letterContent}
            </pre>
          </section>
        ) : null}

        {scores ? (
          <section aria-labelledby="criteria-heading" className="grid gap-4 rounded-2xl border border-border bg-surface p-5 shadow-sm lg:grid-cols-2">
            <div>
              <h2 id="criteria-heading" className="text-lg font-bold text-navy">{t('writing.submissions.results.criteria.heading')}</h2>
              <CriteriaRadar scores={scores} targetScores={{ c1: 3, c2: 6, c3: 6, c4: 6, c5: 6, c6: 6 }} />
            </div>
            <details className="rounded-xl border border-border bg-background p-4" open>
              <summary className="cursor-pointer text-sm font-bold text-navy">{t('writing.submissions.results.criteria.perCriterion')}</summary>
              <ul className="mt-3 space-y-3">
                {isMock
                  // Mock: iterate the fixed criteria so the tutor's human scores + written
                  // comments always render, regardless of the AI feedback map. Zero AI.
                  ? (Object.keys(CRITERION_NAMES) as WritingCriterionCode[]).map((code) => (
                      <li key={code}>
                        <CriterionScoreRow
                          label={CRITERION_NAMES[code]}
                          score={scores![code]}
                          max={CRITERION_MAX[code]}
                          target={CRITERION_TARGET[code]}
                          feedback={tutorReview?.perCriterionComments?.[code] ?? null}
                        />
                      </li>
                    ))
                  : (Object.entries(grade!.perCriterion) as Array<[WritingCriterionCode, NonNullable<typeof grade>['perCriterion'][WritingCriterionCode]]>).map(([code, feedback]) => (
                      <li key={code}>
                        <CriterionScoreRow
                          label={CRITERION_NAMES[code]}
                          score={feedback.score}
                          max={CRITERION_MAX[code]}
                          target={CRITERION_TARGET[code]}
                          feedback={feedback.feedback}
                          exemplar={feedback.exemplarFix ? (
                            <>
                              <span className="font-bold">{t('writing.submissions.results.criteria.exemplarFix')}</span>{' '}
                              <span dir="ltr">{feedback.exemplarFix}</span>
                            </>
                          ) : null}
                        />
                      </li>
                    ))}
              </ul>
            </details>
          </section>
        ) : null}

        {assessmentVisible && assessmentScores && assessment ? (
          <section aria-labelledby="assessment-v11-heading" className="space-y-4 rounded-2xl border border-border bg-surface p-5 shadow-sm">
            <div>
              <h2 id="assessment-v11-heading" className="text-lg font-bold text-navy">Assessment v1.1 criteria and evidence</h2>
              <p className="mt-1 text-sm text-muted">Every finding is assigned to one primary criterion; secondary references are shown only as supporting context.</p>
            </div>
            <CriteriaRadar scores={assessmentScores} targetScores={{ c1: 3, c2: 6, c3: 6, c4: 6, c5: 6, c6: 6 }} />
            <div className="grid gap-3 md:grid-cols-2">
              {assessment.criteria.map((criterion) => (
                <article key={criterion.criterionCode} className="rounded-xl border border-border bg-background p-4">
                  <div className="flex items-center justify-between gap-2">
                    <h3 className="font-bold text-navy">{criterion.criterionCode}</h3>
                    <Badge variant="info" size="sm">{criterion.score}/{criterion.maximumScore}</Badge>
                  </div>
                  <p className="mt-2 text-sm text-navy">{criterion.strengthObservation}</p>
                  <p className="mt-1 text-sm text-muted">{criterion.limitationObservation}</p>
                  <p className="mt-2 text-sm text-primary">{criterion.improvementAction}</p>
                </article>
              ))}
            </div>
            {assessment.errors.length ? (
              <div>
                <h3 className="font-bold text-navy">Complete corrections</h3>
                <ul className="mt-2 space-y-2">
                  {assessment.errors.map((item) => (
                    <li key={item.id} className="rounded-xl border border-border bg-background p-3 text-sm">
                      <div className="flex flex-wrap items-center gap-2">
                        <Badge variant="warning" size="sm">{item.severity}</Badge>
                        <span className="font-semibold text-navy">{item.primaryCriterionCode}</span>
                        <span className="text-muted">{item.ruleSource ?? item.category}</span>
                      </div>
                      {item.candidateWording ? <p className="mt-1 text-navy" dir="ltr">“{item.candidateWording}”</p> : null}
                      {item.correction ? <p className="mt-1 text-primary" dir="ltr">{item.correction}</p> : null}
                      {item.whyItMatters ? <p className="mt-1 text-muted">{item.whyItMatters}</p> : null}
                    </li>
                  ))}
                </ul>
              </div>
            ) : null}
            {assessment.modelAnswer?.modelAnswerText ? (
              <article className="rounded-xl border border-primary/30 bg-primary/5 p-4">
                <h3 className="font-bold text-navy">Grounded model answer</h3>
                <p className="mt-2 whitespace-pre-line text-sm text-navy" dir="ltr">{assessment.modelAnswer.modelAnswerText}</p>
                {assessment.modelAnswer.whyThisWorks.length ? <p className="mt-2 text-sm text-muted">{assessment.modelAnswer.whyThisWorks.join(' ')}</p> : null}
              </article>
            ) : null}
          </section>
        ) : null}

        {/* Case Notes PDF with the learner's own highlights — read-only review of which
            portions they marked during the exam. Shown when a stimulus PDF exists. */}
        {caseNotes?.stimulusPdfDownloadPath ? (
          <section aria-labelledby="case-notes-heading" className="rounded-2xl border border-border bg-surface p-5 shadow-sm">
            <h2 id="case-notes-heading" className="flex items-center gap-1.5 text-lg font-bold text-navy">
              <FileText className="h-5 w-5 text-amber-600" aria-hidden="true" /> Your highlighted case notes
            </h2>
            <p className="mt-1 text-sm text-muted">The portions you highlighted during the exam.</p>
            <div className="mt-3 h-[75vh] overflow-hidden rounded-xl border border-border">
              <WritingStimulusViewer
                downloadPath={caseNotes.stimulusPdfDownloadPath}
                title="Case Notes"
                allowHighlight={false}
                highlights={parseHighlights(caseNotes.caseNoteHighlightsJson)}
              />
            </div>
          </section>
        ) : null}

        {/* Answer Sheet PDF — official answer to tally the letter against. Read-only
            (no highlighter, copy blocked). Only shown when the task has one. */}
        {answerSheetPath ? (
          <section aria-labelledby="answer-sheet-heading" className="rounded-2xl border border-border bg-surface p-5 shadow-sm">
            <h2 id="answer-sheet-heading" className="flex items-center gap-1.5 text-lg font-bold text-navy">
              <FileText className="h-5 w-5 text-primary" aria-hidden="true" /> Answer sheet
            </h2>
            <p className="mt-1 text-sm text-muted">Tally your letter against the official answer sheet.</p>
            <div className="mt-3 h-[75vh] overflow-hidden rounded-xl border border-border">
              <WritingStimulusViewer downloadPath={answerSheetPath} title="Answer Sheet" />
            </div>
          </section>
        ) : null}

        {/* Tutor text feedback — shown in BOTH modes when the tutor left an
            optional written note. For mocks it's the human-marked written
            channel; for normal writing it's the optional text note alongside
            the voice note + AI. */}
        {tutorReview?.freeTextFeedback ? (
          <section aria-labelledby="tutor-feedback-heading" className="rounded-2xl border border-border bg-surface p-5 shadow-sm">
            <h2 id="tutor-feedback-heading" className="flex items-center gap-1.5 text-lg font-bold text-navy">
              <UserRoundCheck className="h-5 w-5 text-primary" aria-hidden="true" /> Tutor feedback
            </h2>
            <p className="mt-2 whitespace-pre-line text-sm text-navy" dir="ltr">{tutorReview.freeTextFeedback}</p>
          </section>
        ) : null}

        {/* Tutor voice note — mock + normal, when the tutor recorded one. */}
        <TutorVoiceNotePlayer submissionId={submissionId} />

        {!isMock && grade?.topThreePriorities?.length ? (
          <section aria-labelledby="priorities-heading" className="rounded-2xl border border-border bg-surface p-5 shadow-sm">
            <h2 id="priorities-heading" className="text-lg font-bold text-navy">{t('writing.submissions.results.priorities.heading')}</h2>
            <ol className="mt-3 grid gap-2 md:grid-cols-3">
              {grade.topThreePriorities.map((priority, idx) => (
                <li key={idx} className="rounded-xl border border-border bg-background p-3">
                  <Badge variant="warning" size="sm">#{idx + 1}</Badge>
                  {/* Priorities are AI-generated English content. */}
                  <p className="mt-2 text-sm text-navy" dir="ltr">{priority}</p>
                </li>
              ))}
            </ol>
          </section>
        ) : null}

        {grade?.canonViolations?.length ? (
          <section aria-labelledby="canon-heading" className="rounded-2xl border border-border bg-surface p-5 shadow-sm">
            <h2 id="canon-heading" className="text-lg font-bold text-navy">{t('writing.submissions.results.canon.heading', { count: grade.canonViolations.length })}</h2>
            <div className="mt-3 grid gap-2 md:grid-cols-2">
              {grade.canonViolations.map((v) => (
                <CanonViolationCard key={v.id} violation={v} onDispute={(rid, vid) => onDisputeViolation(rid, vid)} />
              ))}
            </div>
          </section>
        ) : null}

        <section aria-labelledby="actions-heading" className="rounded-2xl border border-border bg-surface p-5 shadow-sm">
          <h2 id="actions-heading" className="text-lg font-bold text-navy">{t('writing.submissions.results.next.heading')}</h2>
          <p className="mt-1 text-sm text-muted">{t('writing.submissions.results.next.description')}</p>
          <div className="mt-3 flex flex-wrap gap-2">
            {/* "Practice this again" is a genuinely new attempt — links to the
                scenario's practice session so it runs the same entitlement
                gate as any other new attempt (Writing Rule Enforcement
                Addendum Rev5, 10 Sep 2026, §13). This submission's own
                letter/score/feedback stay reviewable, unchanged, above. */}
            {submission ? (
              <Button asChild>
                <Link href={`/writing/practice/session/${encodeURIComponent(submission.scenarioId)}`}>
                  <RefreshCw className="h-4 w-4" aria-hidden="true" /> {t('writing.submissions.results.actions.practiceAgain')}
                </Link>
              </Button>
            ) : null}
            <Button variant="outline" onClick={() => void onAppeal()}>
              <Flag className="h-4 w-4" aria-hidden="true" /> {t('writing.submissions.results.actions.appeal')}
            </Button>
            {/* Request tutor review removed from this AI result flow (Writing
                Rule Enforcement Addendum Rev5, 10 Sep 2026, §13) — tutor
                review is a separate product/workflow. Already-completed
                tutor feedback (fetched via getTutorReview above) still
                displays read-only where present. */}
            {isA ? (
              <Button variant="outline" onClick={() => void onShowcase()}>
                <Share2 className="h-4 w-4" aria-hidden="true" /> {t('writing.submissions.results.actions.showcase')}
              </Button>
            ) : null}
          </div>
          {offerRevision && grade?.revisionInvite?.reason ? (
            <Card padding="md" className="mt-4 border-amber-300/70 bg-amber-50/60">
              <CardContent>
                <p className="text-sm text-amber-900">
                  <span className="font-bold">{t('writing.submissions.results.next.whyRevise')}</span>{' '}
                  <span dir="ltr">{grade.revisionInvite.reason}</span>
                </p>
              </CardContent>
            </Card>
          ) : null}
        </section>
      </div>
    </LearnerDashboardShell>
  );
}
