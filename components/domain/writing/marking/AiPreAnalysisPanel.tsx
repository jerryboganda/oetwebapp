'use client';

/**
 * AiPreAnalysisPanel — surfaces the deterministic/LLM pre-assessment that feeds
 * the tutor (spec §13.2): estimated bands, confidence, word count vs guide,
 * key-content coverage, missing/irrelevant content, language notes, and
 * suggested per-criterion feedback.
 *
 * Confirm / Edit / Reject affordance:
 *  - "Use AI suggestion" → applies estimatedBands + suggested feedback into the
 *    editable rubric/comment fields (via onApply) and marks accepted = true.
 *  - The tutor can then freely edit those fields; the suggestion stays visible.
 *  - "Reject" → clears the accepted flag (suggestion ignored); fields keep
 *    whatever the tutor has typed.
 *
 * This component is presentational + emits intent; the parent owns the rubric
 * and comment state and the `acceptedAiPreAssessment` submit flag.
 */

import { AlertTriangle, Check, Sparkles, X } from 'lucide-react';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { cn } from '@/lib/utils';
import type { WritingConfidenceFlag, WritingPreAssessmentDto } from '@/lib/writing/types';
import { CRITERION_CODES, CRITERION_LABEL, CRITERION_MAX } from './shared';

export interface AiPreAnalysisPanelProps {
  preAssessment: WritingPreAssessmentDto;
  /** Tutor has chosen to use the AI suggestion as their starting point. */
  accepted: boolean;
  onApply: () => void;
  onReject: () => void;
  readOnly?: boolean;
  className?: string;
}

const CONFIDENCE_VARIANT: Record<WritingConfidenceFlag, 'success' | 'warning' | 'danger'> = {
  high: 'success',
  medium: 'warning',
  low: 'danger',
};

export function AiPreAnalysisPanel({
  preAssessment,
  accepted,
  onApply,
  onReject,
  readOnly = false,
  className,
}: AiPreAnalysisPanelProps) {
  const pa = preAssessment;
  const coverage = Math.max(0, Math.min(100, Math.round(pa.keyContentCoveragePercent)));

  return (
    <section
      aria-labelledby="ai-pre-heading"
      className={cn('rounded-2xl border border-primary/20 bg-lavender/40 p-4', className)}
    >
      <div className="flex flex-col gap-2 sm:flex-row sm:items-start sm:justify-between">
        <div className="min-w-0">
          <p id="ai-pre-heading" className="flex items-center gap-2 text-sm font-bold text-primary">
            <Sparkles className="h-4 w-4" aria-hidden="true" /> AI pre-analysis
          </p>
          <p className="mt-1 text-xs text-navy">
            Advisory starting point from the {pa.source === 'llm' ? 'language model' : 'heuristic'} pre-assessor.
            You remain responsible for the final judgement.
          </p>
        </div>
        <div className="flex shrink-0 items-center gap-2">
          <Badge variant={CONFIDENCE_VARIANT[pa.confidence]} size="sm">
            {pa.confidence} confidence
          </Badge>
          {accepted ? (
            <Badge variant="default" size="sm">
              <Check className="h-3 w-3" aria-hidden="true" /> Applied
            </Badge>
          ) : null}
        </div>
      </div>

      {/* Estimated bands */}
      <div className="mt-3 rounded-xl border border-border bg-surface/80 p-3">
        <div className="flex items-center justify-between">
          <p className="text-xs font-bold uppercase tracking-wider text-muted">Estimated bands</p>
          <p className="text-sm font-black text-navy">
            {pa.estimatedBandLabel} · raw {pa.estimatedRawTotal}/38
          </p>
        </div>
        <ul className="mt-2 grid grid-cols-2 gap-1.5 sm:grid-cols-3">
          {CRITERION_CODES.map((c) => (
            <li key={c} className="rounded-lg bg-background-light px-2 py-1 text-xs text-navy">
              <span className="font-semibold">{CRITERION_LABEL[c].replace(/ .*/, '')}</span>
              <span className="ml-1 text-muted">{pa.estimatedBands[c]}/{CRITERION_MAX[c]}</span>
            </li>
          ))}
        </ul>
      </div>

      {/* Coverage + word count */}
      <div className="mt-3 grid gap-2 sm:grid-cols-2">
        <div className="rounded-xl border border-border bg-surface/80 p-3">
          <p className="text-xs font-bold uppercase tracking-wider text-muted">Word count</p>
          <p className="mt-1 text-sm text-navy">
            <span className="font-black">{pa.wordCount}</span> words ·{' '}
            <span className={pa.withinWordGuide ? 'text-success' : 'text-error'}>
              {pa.withinWordGuide ? 'within guide' : 'outside guide'}
            </span>
          </p>
        </div>
        <div className="rounded-xl border border-border bg-surface/80 p-3">
          <p className="text-xs font-bold uppercase tracking-wider text-muted">Key-content coverage</p>
          <div className="mt-1.5 flex items-center gap-2">
            <div
              className="h-2 flex-1 overflow-hidden rounded-full bg-background-light"
              role="progressbar"
              aria-valuenow={coverage}
              aria-valuemin={0}
              aria-valuemax={100}
            >
              <div
                className={cn('h-full rounded-full', coverage >= 80 ? 'bg-success' : coverage >= 50 ? 'bg-warning' : 'bg-error')}
                style={{ width: `${coverage}%` }}
              />
            </div>
            <span className="text-sm font-bold tabular-nums text-navy">{coverage}%</span>
          </div>
        </div>
      </div>

      {/* Missing / irrelevant / language notes */}
      {pa.missingKeyContent.length > 0 ? (
        <div className="mt-3 rounded-xl border border-error/30 bg-error/5 p-3">
          <p className="flex items-center gap-1.5 text-xs font-bold uppercase tracking-wider text-error">
            <AlertTriangle className="h-3.5 w-3.5" aria-hidden="true" /> Missing key content ({pa.missingKeyContent.length})
          </p>
          <ul className="mt-1.5 list-disc space-y-0.5 pl-5 text-sm text-navy">
            {pa.missingKeyContent.map((item, i) => <li key={i}>{item}</li>)}
          </ul>
        </div>
      ) : null}

      {pa.detectedIrrelevantContent.length > 0 ? (
        <div className="mt-3 rounded-xl border border-warning/30 bg-warning/10 p-3">
          <p className="text-xs font-bold uppercase tracking-wider text-warning">
            Detected irrelevant content ({pa.detectedIrrelevantContent.length})
          </p>
          <ul className="mt-1.5 list-disc space-y-0.5 pl-5 text-sm text-navy">
            {pa.detectedIrrelevantContent.map((item, i) => <li key={i}>{item}</li>)}
          </ul>
        </div>
      ) : null}

      {pa.languageNotes.length > 0 ? (
        <div className="mt-3 rounded-xl border border-border bg-surface/80 p-3">
          <p className="text-xs font-bold uppercase tracking-wider text-muted">Language notes</p>
          <ul className="mt-1.5 list-disc space-y-0.5 pl-5 text-sm text-navy">
            {pa.languageNotes.map((note, i) => <li key={i}>{note}</li>)}
          </ul>
        </div>
      ) : null}

      {Object.keys(pa.suggestedCriterionFeedback ?? {}).length > 0 ? (
        <details className="mt-3 rounded-xl border border-border bg-surface/80 p-3">
          <summary className="cursor-pointer text-xs font-bold uppercase tracking-wider text-muted">
            Suggested per-criterion feedback
          </summary>
          <ul className="mt-2 space-y-1.5">
            {CRITERION_CODES.filter((c) => pa.suggestedCriterionFeedback[c]).map((c) => (
              <li key={c} className="text-sm text-navy">
                <span className="font-semibold">{CRITERION_LABEL[c]}: </span>
                {pa.suggestedCriterionFeedback[c]}
              </li>
            ))}
          </ul>
        </details>
      ) : null}

      {/* Confirm / Edit / Reject */}
      {!readOnly ? (
        <div className="mt-4 flex flex-wrap items-center justify-end gap-2">
          {accepted ? (
            <>
              <span className="mr-auto flex items-center gap-1 text-xs text-muted">
                Suggestion applied — edit the rubric and comments freely.
              </span>
              <Button size="sm" variant="outline" onClick={onApply}>
                <Sparkles className="h-3.5 w-3.5" aria-hidden="true" /> Re-apply
              </Button>
              <Button size="sm" variant="ghost" onClick={onReject}>
                <X className="h-3.5 w-3.5" aria-hidden="true" /> Reject
              </Button>
            </>
          ) : (
            <Button size="sm" onClick={onApply}>
              <Sparkles className="h-3.5 w-3.5" aria-hidden="true" /> Use AI suggestion
            </Button>
          )}
        </div>
      ) : null}
    </section>
  );
}
