'use client';

/**
 * ModerationPanel — double-marking + senior moderation surface (spec §14).
 *
 * Two responsibilities:
 *  1. Status banner: communicates where this submission sits in the marking
 *     sequence (first / second marker) and the moderation lifecycle.
 *  2. Senior moderation: when a moderation is pending and the current user is
 *     acting as senior, render a side-by-side comparison of first vs second
 *     marker scores, the variance + reason, and a final-score editor wired to
 *     finalizeWritingModeration.
 */

import { useMemo } from 'react';
import { Gavel, Scale } from 'lucide-react';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { cn } from '@/lib/utils';
import type {
  WritingCriteriaScoresDto,
  WritingMarkerSequence,
  WritingModerationDto,
  WritingModerationStatus,
} from '@/lib/writing/types';
import {
  CRITERION_CODES,
  CRITERION_LABEL,
  CRITERION_MAX,
  RAW_TOTAL_MAX,
  draftRawTotal,
  isDraftComplete,
  parseScoreInput,
  type ScoreDraft,
} from './shared';

export interface ModerationPanelProps {
  moderation: WritingModerationDto | null;
  markerSequence: WritingMarkerSequence;
  /** Whether the current user may act as senior moderator. */
  canModerate: boolean;
  finalDraft: ScoreDraft;
  onFinalDraftChange: (next: ScoreDraft) => void;
  finalNote: string;
  onFinalNoteChange: (note: string) => void;
  onFinalize: () => Promise<void> | void;
  finalizing?: boolean;
  className?: string;
}

const STATUS_LABEL: Record<WritingModerationStatus, string> = {
  pending_first: 'Awaiting first marking',
  pending_second: 'Awaiting second marking',
  pending_moderation: 'Pending senior moderation',
  finalized: 'Finalized',
};

const STATUS_VARIANT: Record<WritingModerationStatus, 'default' | 'warning' | 'success' | 'info'> = {
  pending_first: 'info',
  pending_second: 'warning',
  pending_moderation: 'warning',
  finalized: 'success',
};

function sumScores(scores: WritingCriteriaScoresDto | null): number | null {
  if (!scores) return null;
  return CRITERION_CODES.reduce((sum, c) => sum + (scores[c] ?? 0), 0);
}

export function ModerationPanel({
  moderation,
  markerSequence,
  canModerate,
  finalDraft,
  onFinalDraftChange,
  finalNote,
  onFinalNoteChange,
  onFinalize,
  finalizing = false,
  className,
}: ModerationPanelProps) {
  const firstTotal = useMemo(() => sumScores(moderation?.firstScore ?? null), [moderation?.firstScore]);
  const secondTotal = useMemo(() => sumScores(moderation?.secondScore ?? null), [moderation?.secondScore]);

  const showSeniorEditor = canModerate && moderation?.status === 'pending_moderation';

  const finalComplete = isDraftComplete(finalDraft);
  const finalTotal = draftRawTotal(finalDraft);

  // Nothing meaningful to show if there's no moderation context and this is a
  // plain first marking.
  if (!moderation && markerSequence === 'first') return null;

  return (
    <section
      aria-labelledby="moderation-heading"
      className={cn('rounded-2xl border border-border bg-surface p-4', className)}
    >
      <div className="flex flex-wrap items-center justify-between gap-2">
        <h3 id="moderation-heading" className="flex items-center gap-2 text-sm font-bold text-navy">
          <Scale className="h-4 w-4 text-primary" aria-hidden="true" /> Double-marking & moderation
        </h3>
        <div className="flex items-center gap-2">
          <Badge variant="default" size="sm">{markerSequence} marker</Badge>
          {moderation ? (
            <Badge variant={STATUS_VARIANT[moderation.status]} size="sm">
              {STATUS_LABEL[moderation.status]}
            </Badge>
          ) : null}
        </div>
      </div>

      {markerSequence === 'second' ? (
        <p className="mt-2 rounded-lg border border-info/30 bg-info/10 p-2.5 text-xs text-info">
          This is the <span className="font-semibold">second independent marking</span>. Mark the response on
          its merits; your scores will be compared with the first marker and moderated if they diverge.
        </p>
      ) : null}

      {/* Comparison table (whenever both markings exist or moderation is in play) */}
      {moderation && (moderation.firstScore || moderation.secondScore) ? (
        <div className="mt-3 overflow-hidden rounded-xl border border-border">
          <table className="w-full text-sm">
            <thead>
              <tr className="bg-background-light text-xs uppercase tracking-wider text-muted">
                <th scope="col" className="px-3 py-2 text-left font-semibold">Criterion</th>
                <th scope="col" className="px-3 py-2 text-center font-semibold">1st</th>
                <th scope="col" className="px-3 py-2 text-center font-semibold">2nd</th>
                <th scope="col" className="px-3 py-2 text-center font-semibold" aria-label="Difference">Δ</th>
                {moderation.finalScore ? (
                  <th scope="col" className="px-3 py-2 text-center font-semibold">Final</th>
                ) : null}
              </tr>
            </thead>
            <tbody>
              {CRITERION_CODES.map((c) => {
                const first = moderation.firstScore?.[c] ?? null;
                const second = moderation.secondScore?.[c] ?? null;
                const delta = first !== null && second !== null ? second - first : null;
                const final = moderation.finalScore?.[c] ?? null;
                return (
                  <tr key={c} className="border-t border-border">
                    <td className="px-3 py-1.5 text-navy">{CRITERION_LABEL[c]}</td>
                    <td className="px-3 py-1.5 text-center tabular-nums text-navy">{first ?? '—'}</td>
                    <td className="px-3 py-1.5 text-center tabular-nums text-navy">{second ?? '—'}</td>
                    <td className="px-3 py-1.5 text-center text-xs tabular-nums">
                      {delta === null ? (
                        <span className="text-muted">—</span>
                      ) : delta === 0 ? (
                        <span className="text-muted">0</span>
                      ) : (
                        <span className={Math.abs(delta) >= 2 ? 'font-bold text-error' : 'font-semibold text-warning'}>
                          {delta > 0 ? `+${delta}` : delta}
                        </span>
                      )}
                    </td>
                    {moderation.finalScore ? (
                      <td className="px-3 py-1.5 text-center font-bold tabular-nums text-success">{final ?? '—'}</td>
                    ) : null}
                  </tr>
                );
              })}
              <tr className="border-t border-border bg-background-light/60 font-bold">
                <td className="px-3 py-1.5 text-navy">Raw total</td>
                <td className="px-3 py-1.5 text-center tabular-nums text-navy">{firstTotal ?? '—'}</td>
                <td className="px-3 py-1.5 text-center tabular-nums text-navy">{secondTotal ?? '—'}</td>
                <td className="px-3 py-1.5 text-center tabular-nums text-muted">
                  {firstTotal !== null && secondTotal !== null ? Math.abs(secondTotal - firstTotal) : '—'}
                </td>
                {moderation.finalScore ? (
                  <td className="px-3 py-1.5 text-center tabular-nums text-success">{sumScores(moderation.finalScore)}</td>
                ) : null}
              </tr>
            </tbody>
          </table>
        </div>
      ) : null}

      {/* Variance + reason */}
      {moderation && (moderation.variancePoints !== null || moderation.varianceReason) ? (
        <div className="mt-3 flex flex-wrap items-center gap-2 text-xs">
          {moderation.variancePoints !== null ? (
            <Badge variant={moderation.variancePoints >= 4 ? 'danger' : 'warning'} size="sm">
              Variance: {moderation.variancePoints} pts
            </Badge>
          ) : null}
          {moderation.varianceReason ? (
            <span className="text-muted">{moderation.varianceReason}</span>
          ) : null}
        </div>
      ) : null}

      {/* Finalized decision note */}
      {moderation?.status === 'finalized' && moderation.finalDecisionNote ? (
        <div className="mt-3 rounded-xl border border-success/30 bg-success/10 p-3">
          <p className="text-xs font-bold uppercase tracking-wider text-success">Moderator decision</p>
          <p className="mt-1 whitespace-pre-line text-sm text-navy">{moderation.finalDecisionNote}</p>
        </div>
      ) : null}

      {/* Senior moderation editor */}
      {showSeniorEditor ? (
        <div className="mt-4 rounded-xl border border-primary/30 bg-lavender/40 p-3">
          <p className="flex items-center gap-2 text-sm font-bold text-primary">
            <Gavel className="h-4 w-4" aria-hidden="true" /> Final moderated score
          </p>
          <p className="mt-1 text-xs text-navy">
            Set the definitive band for each criterion, then record your decision rationale.
          </p>
          <div className="mt-3 grid grid-cols-2 gap-2 sm:grid-cols-3">
            {CRITERION_CODES.map((c) => (
              <label key={c} className="flex flex-col gap-1 text-[11px] font-bold uppercase tracking-wider text-muted">
                {CRITERION_LABEL[c].replace(/ .*/, '')} (0–{CRITERION_MAX[c]})
                <input
                  type="number"
                  min={0}
                  max={CRITERION_MAX[c]}
                  step={1}
                  value={finalDraft[c]}
                  onChange={(e) => onFinalDraftChange({ ...finalDraft, [c]: e.target.value })}
                  onBlur={(e) => {
                    const parsed = parseScoreInput(c, e.target.value);
                    onFinalDraftChange({ ...finalDraft, [c]: parsed === null ? '' : String(parsed) });
                  }}
                  className="h-8 rounded-md border border-border bg-surface px-2 text-center text-sm font-bold tabular-nums text-navy focus:outline-none focus:ring-2 focus:ring-primary/40"
                  aria-label={`Final score for ${CRITERION_LABEL[c]}`}
                />
              </label>
            ))}
          </div>
          <p className="mt-2 text-right text-xs font-bold text-navy">
            Final raw total: <span className="tabular-nums">{finalTotal}/{RAW_TOTAL_MAX}</span>
          </p>
          <label className="mt-2 flex flex-col gap-1 text-[11px] font-bold uppercase tracking-wider text-muted">
            Decision note
            <textarea
              rows={3}
              value={finalNote}
              onChange={(e) => onFinalNoteChange(e.target.value)}
              placeholder="Explain how the final score was reconciled from the two markings."
              className="rounded-lg border border-border bg-surface px-2 py-1.5 text-sm font-normal normal-case text-foreground focus:outline-none focus:ring-2 focus:ring-primary/40"
            />
          </label>
          <div className="mt-3 flex justify-end">
            <Button
              size="sm"
              onClick={() => void onFinalize()}
              loading={finalizing}
              disabled={!finalComplete || !finalNote.trim()}
            >
              <Gavel className="h-3.5 w-3.5" aria-hidden="true" /> Finalize moderation
            </Button>
          </div>
          {(!finalComplete || !finalNote.trim()) ? (
            <p className="mt-1.5 text-right text-xs text-muted">
              All six criteria and a decision note are required to finalize.
            </p>
          ) : null}
        </div>
      ) : null}
    </section>
  );
}
