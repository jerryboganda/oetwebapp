'use client';

/**
 * One column of the dual-scoring layout (AI or Tutor).
 *
 * AI column uses indigo/blue accents and surfaces evidence quotes per criterion.
 * Tutor column uses emerald/green accents and surfaces strengths + improvements.
 *
 * If `assessment` is null, the column shows a placeholder/CTA so the layout
 * stays balanced — symmetry is the whole point of the dual-scoring view.
 */

import { useState, type ReactNode } from 'react';
import { ChevronDown, ChevronRight, Info, Sparkles, UserRound } from 'lucide-react';

import { Badge } from '@/components/ui/badge';
import { Card } from '@/components/ui/card';
import { cn } from '@/lib/utils';
import {
  CLINICAL_CRITERIA,
  CRITERION_LABEL,
  CRITERION_MAX,
  LINGUISTIC_CRITERIA,
  readinessBandLabel,
  type AiAssessment,
  type SpeakingCriterionCode,
  type SpeakingFeedbackReport,
  type TutorAssessment,
} from '@/lib/api/speaking-assessments';
import { oetReportedGradeFromScaled, oetReportedScoreFromScaled } from '@/lib/scoring';
import { isProvisionalScore, PROVISIONAL_SCORE_TITLE } from '@/lib/speaking/score-label';

import { IntelligibilityEvidenceNote } from './IntelligibilityEvidenceNote';

export type DualAssessmentColumnKind = 'ai' | 'tutor';

export interface DualAssessmentColumnAttribution {
  name?: string;
  photoUrl?: string;
  submittedAt?: string;
  provider?: string;
  modelId?: string;
}

export interface DualAssessmentColumnProps {
  kind: DualAssessmentColumnKind;
  title: string;
  assessment: AiAssessment | TutorAssessment | null;
  attribution?: DualAssessmentColumnAttribution;
  placeholderCta?: ReactNode;
  showFullCriteria?: boolean;
  showReadinessBand?: boolean;
}

const KIND_STYLES: Record<DualAssessmentColumnKind, { header: string; ring: string; bar: string; chip: string; icon: ReactNode; tooltip: string }> = {
  ai: {
    header: 'bg-lavender border-primary/20',
    ring: 'ring-1 ring-primary/20',
    bar: 'bg-primary',
    chip: 'bg-lavender text-primary-dark',
    icon: <Sparkles className="h-4 w-4" aria-hidden />,
    tooltip:
      'AI-generated estimate from your transcript and, when a recording was kept, how your speech sounded. Advisory only, not an official OET score.',
  },
  tutor: {
    header: 'bg-success/10 border-success/20',
    ring: 'ring-1 ring-success/20',
    bar: 'bg-success',
    chip: 'bg-success/10 text-success-strong',
    icon: <UserRound className="h-4 w-4" aria-hidden />,
    tooltip:
      'Human tutor assessment from a calibrated OET expert. Reflects nuance that AI may miss.',
  },
};

function isAiAssessment(a: AiAssessment | TutorAssessment | null): a is AiAssessment {
  return !!a && 'criterionScores' in a && typeof (a as AiAssessment).criterionScores === 'object';
}

function isTutorAssessment(a: AiAssessment | TutorAssessment | null): a is TutorAssessment {
  return !!a && 'tutorId' in a;
}

function readScore(
  assessment: AiAssessment | TutorAssessment,
  code: SpeakingCriterionCode,
): { score: number; max: number; rationale?: string; quotes?: string[] } {
  const max = CRITERION_MAX[code];
  if (isAiAssessment(assessment)) {
    const entry = assessment.criterionScores?.[code];
    if (entry) {
      return {
        score: entry.score,
        max: entry.maxScore ?? max,
        rationale: entry.rationale,
        quotes: entry.evidenceQuotes,
      };
    }
    return { score: 0, max };
  }
  // Tutor assessment: flat properties matching criterion codes.
  const score = (assessment as unknown as Record<string, number>)[code] ?? 0;
  return { score, max };
}

function formatTimestamp(iso?: string): string | null {
  if (!iso) return null;
  try {
    return new Date(iso).toLocaleString();
  } catch {
    return iso;
  }
}

function CriterionRow({
  code,
  score,
  max,
  barClass,
  rationale,
  quotes,
  children,
}: {
  code: SpeakingCriterionCode;
  score: number;
  max: number;
  barClass: string;
  rationale?: string;
  quotes?: string[];
  children?: ReactNode;
}) {
  const [expanded, setExpanded] = useState(false);
  const hasQuotes = !!(quotes && quotes.length > 0);
  const pct = Math.max(0, Math.min(100, (score / max) * 100));

  return (
    <div className="rounded-xl border border-border bg-background-light/60 p-3">
      <div className="flex items-center justify-between gap-3">
        <span className="flex-1 text-left text-sm font-semibold text-navy">{CRITERION_LABEL[code]}</span>
        <span
          className="rounded-full bg-surface px-2.5 py-0.5 text-xs font-bold text-navy ring-1 ring-border"
          aria-label={`${CRITERION_LABEL[code]} ${score} of ${max}`}
        >
          {score} / {max}
        </span>
      </div>
      <div className="mt-2 h-2 w-full overflow-hidden rounded-full bg-border/60">
        <div
          className={cn('h-full rounded-full transition-[width] duration-500', barClass)}
          style={{ width: `${pct}%` }}
          aria-hidden
        />
      </div>
      {/* A short explanation under every criterion — a bare "4 / 6" tells the candidate nothing. */}
      {rationale ? (
        <p className="mt-2 text-xs leading-relaxed text-navy" data-testid={`criterion-explanation-${code}`}>
          {rationale}
        </p>
      ) : null}
      {children}
      {hasQuotes ? (
        <div className="mt-2">
          <button
            type="button"
            onClick={() => setExpanded((v) => !v)}
            className="inline-flex items-center gap-1 text-xs font-semibold text-primary hover:underline"
            aria-expanded={expanded}
          >
            {expanded ? <ChevronDown className="h-3.5 w-3.5" aria-hidden /> : <ChevronRight className="h-3.5 w-3.5" aria-hidden />}
            {expanded ? 'Hide what you said' : 'Show what you said'}
          </button>
          {expanded ? (
            <ul className="mt-2 list-disc space-y-1 rounded-lg bg-surface p-3 pl-7 text-xs leading-relaxed text-muted">
              {quotes!.map((q, i) => (
                <li key={i} className="italic">
                  &ldquo;{q}&rdquo;
                </li>
              ))}
            </ul>
          ) : null}
        </div>
      ) : null}
    </div>
  );
}

function criterionName(code: string): string {
  return (CRITERION_LABEL as Record<string, string>)[code] ?? 'Overall';
}

/** Owner spec 4 Oct 2026 §10: 2–4 strengths and 2–5 priority weaknesses, each weakness ending in an action. */
function ReportSections({ report }: { report: SpeakingFeedbackReport }) {
  return (
    <div className="grid gap-3 md:grid-cols-2" data-testid="speaking-report-sections">
      {report.strengths.length > 0 ? (
        <section className="rounded-xl border border-success/20 bg-success/10 p-3" aria-label="Strengths">
          <h5 className="mb-1.5 eyebrow text-success-strong">What went well</h5>
          <ul className="space-y-2 text-sm text-navy">
            {report.strengths.map((item, i) => (
              <li key={i}>
                <span className="font-semibold">{criterionName(item.criterion)}: </span>
                {item.text}
                {item.quote ? <span className="mt-0.5 block text-xs italic text-muted">&ldquo;{item.quote}&rdquo;</span> : null}
              </li>
            ))}
          </ul>
        </section>
      ) : null}
      {report.priorityWeaknesses.length > 0 ? (
        <section className="rounded-xl border border-warning/20 bg-warning/10 p-3" aria-label="Priority weaknesses">
          <h5 className="mb-1.5 eyebrow text-warning-strong">What to work on first</h5>
          <ol className="space-y-3 text-sm text-navy">
            {report.priorityWeaknesses.map((item, i) => (
              <li key={i}>
                <span className="font-semibold">{criterionName(item.criterion)}: </span>
                {item.text}
                {item.quote ? <span className="mt-0.5 block text-xs italic text-muted">&ldquo;{item.quote}&rdquo;</span> : null}
                {item.action ? (
                  <span className="mt-1 block">
                    <span className="font-semibold">Next time: </span>
                    {item.action}
                  </span>
                ) : null}
              </li>
            ))}
          </ol>
        </section>
      ) : null}
    </div>
  );
}

function TooltipHint({ text }: { text: string }) {
  const [open, setOpen] = useState(false);
  return (
    <span className="relative inline-flex">
      <button
        type="button"
        aria-label="What does this column mean?"
        onMouseEnter={() => setOpen(true)}
        onMouseLeave={() => setOpen(false)}
        onFocus={() => setOpen(true)}
        onBlur={() => setOpen(false)}
        onClick={() => setOpen((v) => !v)}
        className="inline-flex h-5 w-5 items-center justify-center rounded-full text-muted hover:text-primary"
      >
        <Info className="h-4 w-4" aria-hidden />
      </button>
      {open && (
        <span
          role="tooltip"
          className="absolute right-0 top-6 z-30 w-64 rounded-lg border border-border bg-surface p-3 text-xs leading-relaxed text-navy shadow-clinical"
        >
          {text}
        </span>
      )}
    </span>
  );
}

export function DualAssessmentColumn({
  kind,
  title,
  assessment,
  attribution,
  placeholderCta,
  showFullCriteria = true,
  showReadinessBand = true,
}: DualAssessmentColumnProps) {
  const styles = KIND_STYLES[kind];
  // Both sides report the same way: 0–500 in 10-point steps with the OET letter grade (no B+).
  const reportedScore = assessment ? oetReportedScoreFromScaled(assessment.estimatedScaledScore) : null;
  const serverGrade = assessment && 'grade' in assessment ? assessment.grade : null;
  const scoreLabel = kind === 'ai' && assessment && 'scoreLabel' in assessment ? assessment.scoreLabel : null;

  return (
    <Card
      padding="none"
      className={cn('flex h-full flex-col overflow-hidden', styles.ring)}
      aria-label={`${title} assessment column`}
      data-testid={`dual-column-${kind}`}
    >
      {/* Header */}
      <div className={cn('flex items-start justify-between gap-3 border-b p-4', styles.header)}>
        <div className="flex items-center gap-2">
          <span className={cn('inline-flex h-8 w-8 items-center justify-center rounded-full', styles.chip)}>
            {styles.icon}
          </span>
          <div>
            <h3 className="text-base font-bold text-navy">{title}</h3>
            {attribution && (
              <p className="text-xs text-muted">
                {/* The grader's internal route and model id ("writing-claude-sub · claude-opus-5-5") mean nothing to a candidate. */}
                {kind === 'ai'
                  ? formatTimestamp(attribution.submittedAt)
                  : [attribution.name, formatTimestamp(attribution.submittedAt)].filter(Boolean).join(' · ')}
              </p>
            )}
          </div>
        </div>
        <TooltipHint text={styles.tooltip} />
      </div>

      {/* Body */}
      <div className="flex flex-1 flex-col gap-4 p-4">
        {!assessment ? (
          <div className="flex flex-1 flex-col items-center justify-center gap-3 rounded-xl border border-dashed border-border bg-background-light/40 p-6 text-center text-sm text-muted">
            {placeholderCta ?? (
              <p>
                {kind === 'ai'
                  ? 'Assessment processing... check back in a few moments.'
                  : 'No tutor review yet.'}
              </p>
            )}
          </div>
        ) : (
          <>
            {/* Scaled score + readiness band */}
            <div className="flex flex-col items-start gap-2 rounded-2xl border border-border bg-background-light/60 p-4">
              <span className="eyebrow text-muted">
                Estimated scaled score
              </span>
              <div className="flex items-baseline gap-3">
                <span className="text-4xl font-bold text-navy">
                  {reportedScore}
                </span>
                <span className="text-sm text-muted">/ 500</span>
              </div>
              {reportedScore != null ? (
                <p className="text-sm font-semibold text-navy" data-testid={`dual-grade-${kind}`}>
                  Grade {serverGrade ?? oetReportedGradeFromScaled(reportedScore)}
                </p>
              ) : null}
              {kind === 'ai' && isProvisionalScore(scoreLabel) ? (
                <p className="text-xs font-semibold text-warning-strong" data-testid="speaking-score-provisional">
                  {PROVISIONAL_SCORE_TITLE}
                </p>
              ) : null}
              {showReadinessBand ? (
                <Badge variant={kind === 'ai' ? 'info' : 'success'}>
                  {readinessBandLabel(assessment.readinessBand)}
                </Badge>
              ) : null}
              {isAiAssessment(assessment) && assessment.confidenceBand && (
                <p className="text-xs text-muted">Confidence: {assessment.confidenceBand}</p>
              )}
            </div>

            {/* Overall summary (AI) or feedback markdown (Tutor) */}
            {isAiAssessment(assessment) && assessment.overallSummary && (
              <div className="rounded-xl border border-border bg-surface p-3 text-sm leading-relaxed text-navy">
                <p className="mb-1 eyebrow text-muted">Summary</p>
                <p>{assessment.overallSummary}</p>
              </div>
            )}
            {isAiAssessment(assessment) && assessment.report
              && (assessment.report.strengths.length > 0 || assessment.report.priorityWeaknesses.length > 0) ? (
              <ReportSections report={assessment.report} />
            ) : null}
            {isTutorAssessment(assessment) && assessment.overallFeedbackMarkdown && (
              <div className="rounded-xl border border-border bg-surface p-3 text-sm leading-relaxed text-navy">
                <p className="mb-1 eyebrow text-muted">Tutor feedback</p>
                <p className="whitespace-pre-line">{assessment.overallFeedbackMarkdown}</p>
              </div>
            )}

            {/* Linguistic criteria (0-6) */}
            {showFullCriteria ? (
              <>
                <section aria-label="Linguistic criteria">
                  <h4 className="mb-2 eyebrow text-muted">
                    Linguistic Criteria (0–6)
                  </h4>
                  <div className="flex flex-col gap-2">
                    {LINGUISTIC_CRITERIA.map((code) => {
                      const { score, max, rationale, quotes } = readScore(assessment, code);
                      return (
                        <CriterionRow
                          key={code}
                          code={code}
                          score={score}
                          max={max}
                          barClass={styles.bar}
                          rationale={rationale}
                          quotes={quotes}
                        >
                          {code === 'intelligibility' && isAiAssessment(assessment) ? (
                            <IntelligibilityEvidenceNote evidence={assessment.intelligibilityEvidence} />
                          ) : null}
                        </CriterionRow>
                      );
                    })}
                  </div>
                </section>

                {/* Clinical communication criteria (0-3) */}
                <section aria-label="Clinical communication criteria">
                  <h4 className="mb-2 eyebrow text-muted">
                    Clinical Communication (0–3)
                  </h4>
                  <div className="flex flex-col gap-2">
                    {CLINICAL_CRITERIA.map((code) => {
                      const { score, max, rationale, quotes } = readScore(assessment, code);
                      return (
                        <CriterionRow
                          key={code}
                          code={code}
                          score={score}
                          max={max}
                          barClass={styles.bar}
                          rationale={rationale}
                          quotes={quotes}
                        />
                      );
                    })}
                  </div>
                </section>
              </>
            ) : null}

            {/* Tutor-only: strengths + improvements */}
            {isTutorAssessment(assessment) && (assessment.strengths.length > 0 || assessment.improvements.length > 0) && (
              <div className="grid gap-3 md:grid-cols-2">
                {assessment.strengths.length > 0 && (
                  <div className="rounded-xl border border-success/20 bg-success/10 p-3">
                    <h5 className="mb-1.5 eyebrow text-success-strong">
                      Strengths
                    </h5>
                    <ul className="ml-4 list-disc space-y-1 text-sm text-navy">
                      {assessment.strengths.map((s, i) => (
                        <li key={i}>{s}</li>
                      ))}
                    </ul>
                  </div>
                )}
                {assessment.improvements.length > 0 && (
                  <div className="rounded-xl border border-warning/20 bg-warning/10 p-3">
                    <h5 className="mb-1.5 eyebrow text-warning-strong">
                      Areas to improve
                    </h5>
                    <ul className="ml-4 list-disc space-y-1 text-sm text-navy">
                      {assessment.improvements.map((s, i) => (
                        <li key={i}>{s}</li>
                      ))}
                    </ul>
                  </div>
                )}
              </div>
            )}
          </>
        )}
      </div>
    </Card>
  );
}

export default DualAssessmentColumn;
