/**
 * ============================================================================
 * Writing — Improvement Score (rewrite vs original)
 * ============================================================================
 *
 * Pure, deterministic computation of the rewrite "improvement_score" called
 * out in the WRITING module spec section 20 (rewrites table).
 *
 * Inputs:
 *   - per-criterion deltas (original vs revised, with max possible band)
 *   - count of unresolved issues remaining after the rewrite
 *
 * Output: 0–100 score, plus a band label and a short summary suitable for
 * surfacing on the learner revision page.
 *
 * No I/O, no AI calls. Trivially testable.
 * ============================================================================
 */

import type { CriteriaDelta } from '@/lib/mock-data';

export interface ImprovementScoreInput {
  deltas: CriteriaDelta[];
  unresolvedIssuesCount: number;
}

export type ImprovementBand = 'major' | 'moderate' | 'minor' | 'minimal' | 'regressed';

export interface ImprovementScore {
  /** Whole-number score in [0, 100]. */
  score: number;
  band: ImprovementBand;
  /** Headline shown on the banner. */
  headline: string;
  /** One-sentence summary for the banner body. */
  summary: string;
  /** Sum of positive criterion gains (capped at theoretical max). */
  totalGained: number;
  /** Sum of (max - original) across criteria — the upper bound of possible gain. */
  totalPossible: number;
  /** Penalty applied (0..0.3) for unresolved issues. */
  issuePenalty: number;
  /** Number of criteria that improved by ≥1 point. */
  criteriaImproved: number;
  /** Number of criteria that regressed (revised < original). */
  criteriaRegressed: number;
}

const MAX_ISSUE_PENALTY = 0.3;
const PENALTY_PER_ISSUE = 0.05;

function clampNonNegative(value: number): number {
  return value < 0 ? 0 : value;
}

function bandFor(score: number, criteriaRegressed: number): ImprovementBand {
  if (criteriaRegressed > 0 && score < 10) return 'regressed';
  if (score >= 70) return 'major';
  if (score >= 40) return 'moderate';
  if (score >= 10) return 'minor';
  return 'minimal';
}

function headlineFor(band: ImprovementBand): string {
  switch (band) {
    case 'major':
      return 'Major improvement';
    case 'moderate':
      return 'Moderate improvement';
    case 'minor':
      return 'Minor improvement';
    case 'regressed':
      return 'Some criteria regressed';
    case 'minimal':
    default:
      return 'Minimal change';
  }
}

function summaryFor(
  band: ImprovementBand,
  criteriaImproved: number,
  unresolvedIssuesCount: number,
): string {
  const issuesLine =
    unresolvedIssuesCount > 0
      ? ` ${unresolvedIssuesCount} issue${unresolvedIssuesCount === 1 ? '' : 's'} still open.`
      : ' All flagged issues resolved.';

  switch (band) {
    case 'major':
      return `Strong rewrite — ${criteriaImproved} criteria improved.${issuesLine}`;
    case 'moderate':
      return `Solid progress — ${criteriaImproved} criteria improved.${issuesLine}`;
    case 'minor':
      return `Some improvement detected.${issuesLine}`;
    case 'regressed':
      return `Watch for criteria that scored lower than the original.${issuesLine}`;
    case 'minimal':
    default:
      return `The rewrite did not move scores meaningfully.${issuesLine}`;
  }
}

export function computeImprovementScore(input: ImprovementScoreInput): ImprovementScore {
  const { deltas, unresolvedIssuesCount } = input;

  let totalGained = 0;
  let totalPossible = 0;
  let criteriaImproved = 0;
  let criteriaRegressed = 0;

  for (const d of deltas) {
    const possible = clampNonNegative(d.max - d.original);
    totalPossible += possible;
    const diff = d.revised - d.original;
    if (diff > 0) {
      // Cap each criterion's gain at the theoretical headroom — guards against
      // upstream data inconsistencies (revised > max) inflating the score.
      const gained = Math.min(diff, possible);
      totalGained += gained;
      criteriaImproved += 1;
    } else if (diff < 0) {
      criteriaRegressed += 1;
    }
  }

  const gainRatio = totalPossible > 0 ? totalGained / totalPossible : 0;
  const issuePenalty = Math.min(
    MAX_ISSUE_PENALTY,
    Math.max(0, unresolvedIssuesCount) * PENALTY_PER_ISSUE,
  );
  const rawScore = clampNonNegative(gainRatio - issuePenalty);
  const score = Math.round(rawScore * 100);

  const band = bandFor(score, criteriaRegressed);

  return {
    score,
    band,
    headline: headlineFor(band),
    summary: summaryFor(band, criteriaImproved, unresolvedIssuesCount),
    totalGained,
    totalPossible,
    issuePenalty,
    criteriaImproved,
    criteriaRegressed,
  };
}
