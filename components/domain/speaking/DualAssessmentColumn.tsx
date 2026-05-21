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
  type TutorAssessment,
} from '@/lib/api/speaking-assessments';

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
}

const KIND_STYLES: Record<DualAssessmentColumnKind, { header: string; ring: string; bar: string; chip: string; icon: ReactNode; tooltip: string }> = {
  ai: {
    header: 'bg-gradient-to-br from-indigo-50 to-blue-50 dark:from-indigo-950/30 dark:to-blue-950/30 border-indigo-200/60 dark:border-indigo-800/40',
    ring: 'ring-1 ring-indigo-200/60 dark:ring-indigo-800/40',
    bar: 'bg-gradient-to-r from-indigo-500 to-blue-500',
    chip: 'bg-indigo-100 text-indigo-800 dark:bg-indigo-900/40 dark:text-indigo-200',
    icon: <Sparkles className="h-4 w-4" aria-hidden />,
    tooltip:
      'AI-generated estimate based on the recorded transcript. Advisory only — not an official OET score.',
  },
  tutor: {
    header: 'bg-gradient-to-br from-emerald-50 to-green-50 dark:from-emerald-950/30 dark:to-green-950/30 border-emerald-200/60 dark:border-emerald-800/40',
    ring: 'ring-1 ring-emerald-200/60 dark:ring-emerald-800/40',
    bar: 'bg-gradient-to-r from-emerald-500 to-green-500',
    chip: 'bg-emerald-100 text-emerald-800 dark:bg-emerald-900/40 dark:text-emerald-200',
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
}: {
  code: SpeakingCriterionCode;
  score: number;
  max: number;
  barClass: string;
  rationale?: string;
  quotes?: string[];
}) {
  const [expanded, setExpanded] = useState(false);
  const canExpand = !!(rationale || (quotes && quotes.length > 0));
  const pct = Math.max(0, Math.min(100, (score / max) * 100));

  return (
    <div className="rounded-xl border border-border bg-background-light/60 p-3">
      <div className="flex items-center justify-between gap-3">
        <button
          type="button"
          onClick={() => canExpand && setExpanded((v) => !v)}
          className={cn(
            'flex flex-1 items-center gap-2 text-left text-sm font-semibold text-navy',
            canExpand ? 'cursor-pointer hover:text-primary' : 'cursor-default',
          )}
          aria-expanded={canExpand ? expanded : undefined}
          aria-label={`${CRITERION_LABEL[code]} ${score} of ${max}${canExpand ? '; click to ' + (expanded ? 'collapse' : 'expand') + ' rationale' : ''}`}
          disabled={!canExpand}
        >
          {canExpand && (
            <span aria-hidden className="text-muted">
              {expanded ? <ChevronDown className="h-3.5 w-3.5" /> : <ChevronRight className="h-3.5 w-3.5" />}
            </span>
          )}
          <span>{CRITERION_LABEL[code]}</span>
        </button>
        <span className="rounded-full bg-surface px-2.5 py-0.5 text-xs font-bold text-navy ring-1 ring-border">
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
      {expanded && canExpand && (
        <div className="mt-3 space-y-2 rounded-lg bg-surface p-3 text-xs leading-relaxed text-muted">
          {rationale && <p className="text-navy">{rationale}</p>}
          {quotes && quotes.length > 0 && (
            <ul className="list-disc space-y-1 pl-4">
              {quotes.map((q, i) => (
                <li key={i} className="italic">
                  &ldquo;{q}&rdquo;
                </li>
              ))}
            </ul>
          )}
        </div>
      )}
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
}: DualAssessmentColumnProps) {
  const styles = KIND_STYLES[kind];

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
                {kind === 'ai'
                  ? [attribution.provider, attribution.modelId].filter(Boolean).join(' · ')
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
              <span className="text-xs font-semibold uppercase tracking-wide text-muted">
                Estimated scaled score
              </span>
              <div className="flex items-baseline gap-3">
                <span className="text-4xl font-bold text-navy">
                  {Math.round(assessment.estimatedScaledScore)}
                </span>
                <span className="text-sm text-muted">/ 500</span>
              </div>
              <Badge variant={kind === 'ai' ? 'info' : 'success'}>
                {readinessBandLabel(assessment.readinessBand)}
              </Badge>
              {isAiAssessment(assessment) && assessment.confidenceBand && (
                <p className="text-xs text-muted">Confidence: {assessment.confidenceBand}</p>
              )}
            </div>

            {/* Overall summary (AI) or feedback markdown (Tutor) */}
            {isAiAssessment(assessment) && assessment.overallSummary && (
              <div className="rounded-xl border border-border bg-surface p-3 text-sm leading-relaxed text-navy">
                <p className="mb-1 text-xs font-semibold uppercase tracking-wide text-muted">Summary</p>
                <p>{assessment.overallSummary}</p>
              </div>
            )}
            {isTutorAssessment(assessment) && assessment.overallFeedbackMarkdown && (
              <div className="rounded-xl border border-border bg-surface p-3 text-sm leading-relaxed text-navy">
                <p className="mb-1 text-xs font-semibold uppercase tracking-wide text-muted">Tutor feedback</p>
                <p className="whitespace-pre-line">{assessment.overallFeedbackMarkdown}</p>
              </div>
            )}

            {/* Linguistic criteria (0-6) */}
            <section aria-label="Linguistic criteria">
              <h4 className="mb-2 text-xs font-bold uppercase tracking-wide text-muted">
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
                    />
                  );
                })}
              </div>
            </section>

            {/* Clinical communication criteria (0-3) */}
            <section aria-label="Clinical communication criteria">
              <h4 className="mb-2 text-xs font-bold uppercase tracking-wide text-muted">
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

            {/* Tutor-only: strengths + improvements */}
            {isTutorAssessment(assessment) && (assessment.strengths.length > 0 || assessment.improvements.length > 0) && (
              <div className="grid gap-3 md:grid-cols-2">
                {assessment.strengths.length > 0 && (
                  <div className="rounded-xl border border-emerald-200/60 bg-emerald-50/40 p-3 dark:border-emerald-800/40 dark:bg-emerald-950/20">
                    <h5 className="mb-1.5 text-xs font-bold uppercase tracking-wide text-emerald-800 dark:text-emerald-300">
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
                  <div className="rounded-xl border border-amber-200/60 bg-amber-50/40 p-3 dark:border-amber-800/40 dark:bg-amber-950/20">
                    <h5 className="mb-1.5 text-xs font-bold uppercase tracking-wide text-amber-800 dark:text-amber-300">
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
