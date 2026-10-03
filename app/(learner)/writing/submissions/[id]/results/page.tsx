'use client';

import { useCallback, useEffect, useState, type ReactNode } from 'react';
import { useParams } from 'next/navigation';
import Link from 'next/link';
import { useTranslations } from 'next-intl';
import { Award, FileText, PenLine, RefreshCw, Share2, Sparkles } from 'lucide-react';
import { Button } from '@/components/ui/button';
import { Badge } from '@/components/ui/badge';
import { InlineAlert } from '@/components/ui/alert';
import { cardClassName } from '@/components/ui/card';
import { CountUp } from '@/components/ui/count-up';
import { MotionItem, MotionSection } from '@/components/ui/motion-primitives';
import { LearnerPageHero } from '@/components/domain/learner-surface';
import { LearnerSkeleton } from '@/components/domain/learner-skeletons';
import { ResultsScorePanel } from '@/components/domain/results/results-score-panel';
import { CriterionScoreRow } from '@/components/domain/results/criterion-score-row';
import { WritingPassCelebration, useWritingPassMark, writingGaugeColor } from '@/components/domain/writing/writing-pass-celebration';
import { CanonViolationCard } from '@/components/domain/writing/CanonViolationCard';
import {
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
import { listFreeSamples, type FreeSampleOption } from '@/lib/api/free-samples';
import {
  OET_SCALED_MAX,
  WRITING_RAW_MAX,
  writingRawTotalFromCriterionScores,
} from '@/lib/scoring';
import { TutorVoiceNotePlayer } from '@/components/domain/writing/TutorVoiceNotePlayer';
import { WritingStimulusViewer } from '@/components/domain/writing/WritingStimulusViewer';
import type {
  WritingCaseNotesDto,
  WritingCriteriaScoresDto,
  WritingCriterionCode,
  WritingGradeConfidenceFlag,
  WritingGradeDto,
  WritingAssessmentV11ErrorDto,
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
// Targets are the band-6 style anchor that tints each criterion row.
const CRITERION_MAX: Record<WritingCriterionCode, number> = { c1: 3, c2: 7, c3: 7, c4: 7, c5: 7, c6: 7 };
const CRITERION_TARGET: Record<WritingCriterionCode, number> = { c1: 3, c2: 6, c3: 6, c4: 6, c5: 6, c6: 6 };

// v1.1 assessment criterion codes → the c1..c6 keys used by the labels.
const V11_CRITERION_KEY: Record<string, WritingCriterionCode> = {
  purpose: 'c1',
  content: 'c2',
  conciseness_clarity: 'c3',
  genre_style: 'c4',
  organisation_layout: 'c5',
  language: 'c6',
};

/** Complete corrections show this many items until "View all corrections". */
const CORRECTIONS_PREVIEW = 5;

// The backend stores each priority as "<rule id>: <message>", and AI findings
// carry "AI.<criterion>" / "AI:<rule id>" ids (WritingAssessmentReportBuilder,
// BuildTopThreePrioritiesJson). A rule id has no spaces and contains a digit,
// "." , "_" or "-", so a plain lead-in such as "Purpose: …" is left alone.
const PRIORITY_RULE_LABEL = /^(?:AI(?:[.:][\w.-]*)?|[\w-]*[\d._-][\w.-]*):\s+/;

/** The candidate-facing priority text: the message without its internal rule label. */
function priorityText(priority: string): string {
  return priority.replace(PRIORITY_RULE_LABEL, '').trim();
}

/** Gauge fill on the value's own scale, clamped to 0–100%. */
const gaugePercent = (value: number, max: number) => Math.min(100, Math.max(0, (value / max) * 100));

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
  for (const criterion of report.criteria) {
    const key = V11_CRITERION_KEY[criterion.criterionCode];
    if (key) scores[key] = criterion.score;
  }
  return scores;
}

export default function WritingSubmissionResultsPage() {
  const t = useTranslations();
  const writingPassMark = useWritingPassMark();
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
  const [freeSample, setFreeSample] = useState<FreeSampleOption | null>(null);
  const [showAllCorrections, setShowAllCorrections] = useState(false);

  // Free Writing sample (retry addendum, 23 Sep 2026): the second free result
  // is a revise & resubmit of the same letter. A failed lookup just hides it.
  useEffect(() => {
    let cancelled = false;
    listFreeSamples('writing')
      .then((rows) => {
        if (!cancelled) setFreeSample(Array.isArray(rows) && rows.length > 0 ? rows[0] : null);
      })
      .catch(() => {
        if (!cancelled) setFreeSample(null);
      });
    return () => {
      cancelled = true;
    };
  }, []);

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
  const visibleReport = assessmentVisible ? assessment : null;
  // The persisted grade (a tutor override included) wins; the v1.1 report's
  // scores stand in only when no grade loaded.
  const scores = grade ? gradeToScores(grade) : visibleReport ? assessmentToScores(visibleReport) : null;
  const isA = grade?.bandLabel?.startsWith('A');
  const offerRevision = grade?.revisionInvite?.shouldOffer ?? false;
  // Mock writing is human-marked with zero AI: show the tutor's WRITTEN feedback and
  // voice note, and suppress every AI-flavoured section. Normal writing keeps AI feedback.
  const isMock = submission?.mode === 'mock';
  // The AI Estimated Practice Score (/500) + grade band is the headline result
  // whenever the v1.1 report is candidate-visible (Writing Rule Enforcement
  // Addendum Rev8 §12.4/§19.4); the raw criteria total stays as secondary
  // context. A mock keeps its tutor's human grade as the headline (zero AI).
  const practiceScore = assessmentVisible && !(isMock && grade)
    ? assessment?.estimatedPracticeScore ?? null
    : null;
  const freeSampleForThisLetter = freeSample && submission && freeSample.contentId === submission.scenarioId
    ? freeSample
    : null;
  const freeRevisionHref = freeSampleForThisLetter?.state === 'retry_available'
    ? `/writing/submissions/${encodeURIComponent(freeSampleForThisLetter.lastSubmissionId ?? submissionId)}/revise`
    : null;
  // Paid Revise & Resubmit: only on a graded, non-mock letter the grade invites
  // revising (revisionInvite.shouldOffer). A free-sample letter keeps its own
  // free retry above instead.
  const paidRevisionHref = submission?.status === 'graded' && !isMock && offerRevision && !freeSampleForThisLetter
    ? `/writing/submissions/${encodeURIComponent(submission.id)}/revise`
    : null;
  const practiceRawTotal = assessment
    ? writingRawTotalFromCriterionScores(
        Object.fromEntries(assessment.criteria.map((c) => [c.criterionCode, c.score] as const)),
      )
    : 0;

  // ② The v1.1 report's priorities, else the grade's — never on a mock (zero AI).
  const priorities = isMock
    ? []
    : (visibleReport?.topPriorities.length ? visibleReport.topPriorities : grade?.topThreePriorities ?? [])
        .map(priorityText)
        .filter(Boolean)
        .slice(0, 3);
  const modelAnswer = visibleReport?.modelAnswer ?? null;
  // ⑤ The server sends corrections severity-first (WritingAssessmentV11ResultService),
  // so the preview is the five most severe and every item stays one tap away.
  const corrections = visibleReport?.errors ?? [];
  const correctionsCollapsible = corrections.length > CORRECTIONS_PREVIEW;
  const shownCorrections = correctionsCollapsible && !showAllCorrections
    ? corrections.slice(0, CORRECTIONS_PREVIEW)
    : corrections;
  const canonViolations = grade?.canonViolations ?? [];
  // ④ Each criterion's findings in that same order, so [0] is its top-severity evidence.
  const findingsByCriterion: Partial<Record<WritingCriterionCode, WritingAssessmentV11ErrorDto[]>> = {};
  for (const finding of corrections) {
    const code = V11_CRITERION_KEY[finding.primaryCriterionCode];
    if (code) (findingsByCriterion[code] ??= []).push(finding);
  }

  // The stored flag is a grader band or a review state; the learner only ever
  // sees neutral copy. A value this page does not know hides the stat.
  const confidenceLabels: Record<WritingGradeConfidenceFlag, string> = {
    high: t('writing.submissions.results.confidence.high'),
    medium: t('writing.submissions.results.confidence.medium'),
    low: t('writing.submissions.results.confidence.low'),
    jev_review: t('writing.submissions.results.confidence.awaitingReview'),
    tutor_reviewed: t('writing.submissions.results.confidence.tutorReviewed'),
  };
  const confidenceLabel = grade ? confidenceLabels[grade.confidenceFlag] : null;

  const sectionCard = cardClassName({ padding: 'lg' });

  return (
    <>
      {/* Report order (launch handoff UI-3, 2 Oct 2026): score → top priorities →
          model answer → per-criterion feedback → complete corrections → reference
          material → what's next. Each block is a `result-section`; its
          `data-section` is read by the live QA harness, and a block with no real
          data is left out. */}
      <div data-testid="result-section" data-section="score">
        {practiceScore != null && assessment ? (
          <ResultsScorePanel
            eyebrow={t('writing.submissions.results.eyebrow')}
            icon={Award}
            title={assessment.scoreLabel}
            subtitle="An AI-generated practice estimate, not an official OET result."
            gaugeValue={gaugePercent(practiceScore, OET_SCALED_MAX)}
            gaugeCenter={
              <>
                <CountUp value={practiceScore} className="text-2xl font-black text-navy" />
                <WritingPassCelebration score={practiceScore} onceKey={`writing-result:${submissionId}`} />
              </>
            }
            gaugeLabel={assessment.scoreRange ?? assessment.gradeBand ?? 'AI estimate'}
            gaugeColor={writingGaugeColor(practiceScore, writingPassMark)}
            stats={[
              { label: 'Score', value: <span data-testid="ai-estimated-score"><CountUp value={practiceScore} suffix={`/${OET_SCALED_MAX}`} /></span>, tone: 'info', icon: <Award /> },
              ...(assessment.gradeBand ? [{ label: 'Grade band', value: <span data-testid="ai-grade-band">{assessment.gradeBand}</span>, tone: 'info' as const, icon: <Award /> }] : []),
              { label: t('writing.submissions.results.highlights.raw'), value: <CountUp value={practiceRawTotal} suffix={`/${WRITING_RAW_MAX}`} />, tone: 'default', icon: <FileText /> },
              ...(assessment.confidenceLabel ? [{ label: 'Confidence', value: assessment.confidenceLabel, tone: 'default' as const, icon: <Sparkles /> }] : []),
            ]}
          />
        ) : grade ? (
          <ResultsScorePanel
            eyebrow={t('writing.submissions.results.eyebrow')}
            icon={Award}
            title={t('writing.submissions.results.estimatedBand', { band: grade.bandLabel })}
            subtitle={t('writing.submissions.results.description')}
            // The ring fills on the raw /38 scale (estimatedBand is also stored
            // in raw-total units, never a 0–7 band); bandLabel is the letter.
            gaugeValue={gaugePercent(grade.rawTotal, WRITING_RAW_MAX)}
            gaugeCenter={<span className="text-2xl font-black text-navy">{grade.bandLabel}</span>}
            gaugeLabel={`${grade.rawTotal}/${WRITING_RAW_MAX}`}
            stats={[
              { label: t('writing.submissions.results.highlights.raw'), value: `${grade.rawTotal}/${WRITING_RAW_MAX}`, tone: 'info', icon: <Award /> },
              { label: t('writing.submissions.results.highlights.mode'), value: submission?.mode ?? '-', tone: 'default', icon: <FileText /> },
              // Confidence is an AI signal — hide it on mocks (human-marked, zero AI).
              ...(isMock || !confidenceLabel ? [] : [{ label: t('writing.submissions.results.highlights.confidence'), value: confidenceLabel, tone: 'default' as const, icon: <Sparkles /> }]),
            ]}
          />
        ) : (
          <LearnerPageHero
            eyebrow={t('writing.submissions.results.eyebrow')}
            icon={Award}
            accent="writing"
            title={t('writing.submissions.results.awaiting')}
            description={t('writing.submissions.results.description')}
          />
        )}
      </div>

      {error ? <InlineAlert variant="error">{error}</InlineAlert> : null}
      {actionStatus ? <InlineAlert variant="info">{actionStatus}</InlineAlert> : null}
      {assessment && !assessmentVisible ? (
        <InlineAlert variant="info">
          This submission is not yet candidate-visible under the v1.1 release gate.
          {assessment.blockingCodes.length ? ` Blocked by: ${assessment.blockingCodes.join(', ')}.` : ''}
        </InlineAlert>
      ) : null}

      {!submission && !error ? <LearnerSkeleton variant="list" /> : null}

      {priorities.length ? (
        <MotionSection delayIndex={0}>
          <section data-testid="result-section" data-section="priorities" aria-labelledby="priorities-heading" className={sectionCard}>
            <h2 id="priorities-heading" className="text-lg font-bold text-navy">{t('writing.submissions.results.priorities.heading')}</h2>
            <ol className="mt-3 grid grid-cols-1 gap-3 md:grid-cols-3">
              {priorities.map((priority, idx) => (
                <li key={idx} className="min-w-0">
                  <MotionItem delayIndex={Math.min(idx, 5)} className="h-full rounded-xl bg-background-light p-3">
                    <Badge variant="warning" size="sm" className="tabular-nums">#{idx + 1}</Badge>
                    {/* Priorities are AI-generated English content. */}
                    <p className="mt-2 break-words text-sm text-navy" dir="ltr">{priority}</p>
                  </MotionItem>
                </li>
              ))}
            </ol>
          </section>
        </MotionSection>
      ) : null}

      {modelAnswer?.modelAnswerText ? (
        <MotionSection delayIndex={1}>
          <section data-testid="result-section" data-section="model-answer" aria-labelledby="model-answer-heading" className={sectionCard}>
            <h2 id="model-answer-heading" className="text-lg font-bold text-navy">{t('writing.submissions.results.modelAnswer.heading')}</h2>
            <article data-testid="grounded-model-answer-card" className="mt-3 rounded-xl border border-primary/30 bg-primary/5 p-4">
              {/* Rendered exactly as stored: every line break and blank line is
                  part of the letter layout (Addendum Rev8 §12.3/§19.2) — never
                  trim, split, or collapse whitespace here. */}
              <p data-testid="grounded-model-answer" className="max-w-3xl whitespace-pre-wrap font-sans text-sm text-navy" dir="ltr">{modelAnswer.modelAnswerText}</p>
              {modelAnswer.whyThisWorks.length ? <p className="mt-2 text-sm text-muted">{modelAnswer.whyThisWorks.join(' ')}</p> : null}
            </article>
          </section>
        </MotionSection>
      ) : null}

      {scores ? (
        <MotionSection delayIndex={2}>
          <section data-testid="result-section" data-section="criteria" aria-labelledby="criteria-heading" className={sectionCard}>
            <h2 id="criteria-heading" className="text-lg font-bold text-navy">{t('writing.submissions.results.criteria.perCriterion')}</h2>
            <ul className="mt-3 space-y-3" data-testid="criteria-list">
              {/* Iterate the fixed six criteria so every one always renders
                  with its score. Mock: the tutor's human written comments only
                  (zero AI). Normal: the AI feedback, plus — once the v1.1 report
                  is candidate-visible — how many corrections fall under the
                  criterion, its most severe one and the next step. */}
              {(Object.keys(CRITERION_NAMES) as WritingCriterionCode[]).map((code, index) => {
                const ai = isMock ? undefined : grade?.perCriterion?.[code];
                const findings = findingsByCriterion[code] ?? [];
                const top = findings[0];
                const nextStep = visibleReport?.criteria.find((c) => V11_CRITERION_KEY[c.criterionCode] === code)?.improvementAction;
                return (
                  <li key={code}>
                    <MotionItem delayIndex={Math.min(index, 5)}>
                      <CriterionScoreRow
                        label={CRITERION_NAMES[code]}
                        score={scores[code]}
                        max={CRITERION_MAX[code]}
                        target={CRITERION_TARGET[code]}
                        feedback={isMock ? tutorReview?.perCriterionComments?.[code] ?? null : ai?.quote ? (
                          <>
                            <mark className="rounded bg-warning/10 px-0.5 text-warning-strong">“{ai.quote}”</mark>{' '}
                            {ai.feedback}
                          </>
                        ) : ai?.feedback}
                        exemplar={ai?.exemplarFix ? (
                          <>
                            <span className="font-bold">{t('writing.submissions.results.criteria.exemplarFix')}</span>{' '}
                            <span dir="ltr">{ai.exemplarFix}</span>
                          </>
                        ) : null}
                        meta={visibleReport && !isMock ? (
                          <div className="space-y-1 break-words" data-testid="criterion-evidence">
                            <p className="font-bold">{t('writing.submissions.results.criteria.findings', { count: findings.length })}</p>
                            {top && (top.candidateWording || top.correction) ? (
                              <p dir="ltr">
                                {top.candidateWording ? <span className="text-navy">“{top.candidateWording}”</span> : null}
                                {top.candidateWording && top.correction ? ' → ' : null}
                                {top.correction ? <span className="text-primary">{top.correction}</span> : null}
                              </p>
                            ) : null}
                            {nextStep ? <p className="text-navy" dir="ltr">{nextStep}</p> : null}
                          </div>
                        ) : null}
                      />
                    </MotionItem>
                  </li>
                );
              })}
            </ul>
          </section>
        </MotionSection>
      ) : null}

      {visibleReport || canonViolations.length ? (
        <MotionSection delayIndex={3}>
          <section data-testid="result-section" data-section="corrections" aria-labelledby="corrections-heading" className={sectionCard}>
            <h2 id="corrections-heading" className="text-lg font-bold text-navy">{t('writing.submissions.results.corrections.heading')}</h2>
            {visibleReport && corrections.length === 0 ? (
              <p className="mt-2 text-sm text-muted">{t('writing.submissions.results.corrections.empty')}</p>
            ) : null}
            {corrections.length ? (
              <ul
                id="corrections-list"
                data-testid={correctionsCollapsible && !showAllCorrections ? 'corrections-preview' : 'corrections-full-list'}
                className="mt-2 divide-y divide-border"
              >
                {shownCorrections.map((item) => (
                  <li key={item.id} className="min-w-0 break-words py-3 text-sm first:pt-1 last:pb-0">
                    <div className="flex flex-wrap items-center gap-2">
                      <Badge variant={item.severity.toLowerCase() === 'critical' ? 'danger' : 'warning'} size="sm">{item.severity}</Badge>
                      <span className="font-semibold text-navy">{CRITERION_NAMES[V11_CRITERION_KEY[item.primaryCriterionCode]] ?? item.primaryCriterionCode}</span>
                      <span className="text-muted">{item.ruleSource ?? item.category}</span>
                    </div>
                    {item.candidateWording ? <p className="mt-1 text-navy" dir="ltr">“{item.candidateWording}”</p> : null}
                    {item.correction ? <p className="mt-1 text-primary" dir="ltr">{item.correction}</p> : null}
                    {item.whyItMatters ? <p className="mt-1 text-muted">{item.whyItMatters}</p> : null}
                  </li>
                ))}
              </ul>
            ) : null}
            {correctionsCollapsible ? (
              <Button
                type="button"
                variant="outline"
                size="sm"
                className="mt-3"
                data-testid="corrections-view-all"
                aria-expanded={showAllCorrections}
                aria-controls="corrections-list"
                onClick={() => setShowAllCorrections((open) => !open)}
              >
                {showAllCorrections
                  ? t('writing.submissions.results.corrections.showFewer')
                  : t('writing.submissions.results.corrections.viewAll', { count: corrections.length })}
              </Button>
            ) : null}
            {/* Legacy canon-engine rule checks, still disputable, kept below the corrections. */}
            {canonViolations.length ? (
              <ReferenceDetails summary={t('writing.submissions.results.canon.heading', { count: canonViolations.length })}>
                <div className="grid grid-cols-1 gap-2 md:grid-cols-2">
                  {canonViolations.map((v) => (
                    <CanonViolationCard key={v.id} violation={v} onDispute={(rid, vid) => onDisputeViolation(rid, vid)} />
                  ))}
                </div>
              </ReferenceDetails>
            ) : null}
          </section>
        </MotionSection>
      ) : null}

      {submission ? (
        <MotionSection delayIndex={4}>
          <section data-testid="result-section" data-section="reference" aria-labelledby="reference-heading" className={sectionCard}>
            <h2 id="reference-heading" className="text-lg font-bold text-navy">{t('writing.submissions.results.reference.heading')}</h2>
            <div className="mt-3 space-y-3">
              {/* "Revise / Review the Letter" for THIS completed attempt is simply
                  reopening this results page: it re-fetches the saved submission via
                  GET only (no grading call, no credit deducted, no editable
                  resubmission), so the exact original letter belongs in this same
                  report alongside the score/criteria above (Writing Rule Enforcement
                  Addendum Rev5, 10 Sep 2026, §13). */}
              <ReferenceDetails summary="Your submitted letter">
                <Badge variant="muted" size="sm">Reviewing your saved submission — no credit used</Badge>
                {/* The submitted letter text is learner-authored English content. */}
                <pre className="mt-3 max-w-3xl whitespace-pre-wrap break-words rounded-control border border-border bg-background p-3 font-sans text-sm leading-relaxed sm:p-4" dir="ltr">
                  {submission.letterContent}
                </pre>
              </ReferenceDetails>

              {/* Case Notes PDF with the learner's own highlights — read-only review of which
                  portions they marked during the exam. Shown when a stimulus PDF exists. */}
              {caseNotes?.stimulusPdfDownloadPath ? (
                <ReferenceDetails summary="Your highlighted case notes" lazy>
                  <p className="text-sm text-muted">The portions you highlighted during the exam.</p>
                  <div className="mt-3 h-[75vh] overflow-hidden rounded-xl border border-border">
                    <WritingStimulusViewer
                      downloadPath={caseNotes.stimulusPdfDownloadPath}
                      title="Case Notes"
                      allowHighlight={false}
                      highlights={parseHighlights(caseNotes.caseNoteHighlightsJson)}
                    />
                  </div>
                </ReferenceDetails>
              ) : null}

              {/* Answer Sheet PDF — official answer to tally the letter against. Read-only
                  (no highlighter, copy blocked). Only shown when the task has one. */}
              {answerSheetPath ? (
                <ReferenceDetails summary="Answer sheet" lazy>
                  <p className="text-sm text-muted">Tally your letter against the official answer sheet.</p>
                  <div className="mt-3 h-[75vh] overflow-hidden rounded-xl border border-border">
                    <WritingStimulusViewer downloadPath={answerSheetPath} title="Answer Sheet" />
                  </div>
                </ReferenceDetails>
              ) : null}

              {/* Tutor text feedback — shown in BOTH modes when the tutor left an
                  optional written note. For mocks it's the human-marked written
                  channel, so it starts open there. */}
              {tutorReview?.freeTextFeedback ? (
                <ReferenceDetails summary="Tutor feedback" defaultOpen={isMock}>
                  <p className="max-w-3xl whitespace-pre-line text-sm text-navy" dir="ltr">{tutorReview.freeTextFeedback}</p>
                </ReferenceDetails>
              ) : null}

              {/* Tutor voice note — mock + normal; renders nothing without a note. */}
              <TutorVoiceNotePlayer submissionId={submissionId} className="rounded-xl border border-border p-3 sm:p-4" />
            </div>
          </section>
        </MotionSection>
      ) : null}

      <section data-testid="result-section" data-section="next-actions" aria-labelledby="actions-heading" className={sectionCard}>
        <h2 id="actions-heading" className="text-lg font-bold text-navy">{t('writing.submissions.results.next.heading')}</h2>
        <p className="mt-1 text-sm text-muted">{t('writing.submissions.results.next.description')}</p>
        {freeSampleForThisLetter?.state === 'completed' ? (
          <InlineAlert variant="info" className="mt-3">
            <span data-testid="free-sample-completed">{t('freeSample.completed')}</span>
          </InlineAlert>
        ) : null}
        <div className="mt-3 flex flex-wrap gap-2">
          {freeRevisionHref ? (
            <Button asChild>
              <Link href={freeRevisionHref} data-testid="free-sample-revise-cta">
                <RefreshCw className="h-4 w-4" aria-hidden="true" /> {t('freeSample.writing.retryCta')}
              </Link>
            </Button>
          ) : null}
          {paidRevisionHref ? (
            <Button asChild>
              <Link href={paidRevisionHref} data-testid="revise-and-resubmit">
                <PenLine className="h-4 w-4" aria-hidden="true" /> {t('writing.submissions.results.actions.reviseResubmit')}
              </Link>
            </Button>
          ) : null}
          {/* "Practice this again" is a genuinely new attempt — links to the
              scenario's practice session so it runs the same entitlement
              gate as any other new attempt (Writing Rule Enforcement
              Addendum Rev5, 10 Sep 2026, §13). This submission's own
              letter/score/feedback stay reviewable, unchanged, above. */}
          {submission ? (
            <Button asChild variant={freeRevisionHref || paidRevisionHref ? 'outline' : 'primary'}>
              <Link href={`/writing/practice/session/${encodeURIComponent(submission.scenarioId)}`}>
                <RefreshCw className="h-4 w-4" aria-hidden="true" /> {t('writing.submissions.results.actions.practiceAgain')}
              </Link>
            </Button>
          ) : null}
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
          <InlineAlert variant="warning" live="polite" className="mt-4">
            <span className="font-bold">{t('writing.submissions.results.next.whyRevise')}</span>{' '}
            <span dir="ltr">{grade.revisionInvite.reason}</span>
          </InlineAlert>
        ) : null}
      </section>
    </>
  );
}

/**
 * A collapsed reference block. `lazy` mounts the content on first open only:
 * a PDF viewer inside a closed <details> would load the file for nothing and
 * measure a zero-width box.
 */
function ReferenceDetails({
  summary,
  lazy = false,
  defaultOpen = false,
  children,
}: {
  summary: ReactNode;
  lazy?: boolean;
  defaultOpen?: boolean;
  children: ReactNode;
}) {
  const [opened, setOpened] = useState(defaultOpen);
  return (
    <details
      open={defaultOpen}
      onToggle={(event) => {
        if (event.currentTarget.open) setOpened(true);
      }}
      className="rounded-xl border border-border px-3 sm:px-4"
    >
      <summary className="cursor-pointer rounded-control py-3 text-sm font-bold text-navy focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary">
        {summary}
      </summary>
      <div className="pb-3 sm:pb-4">{!lazy || opened ? children : null}</div>
    </details>
  );
}
