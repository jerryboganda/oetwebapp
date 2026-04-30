/**
 * ============================================================================
 * Writing — Weakness Analytics types
 * ============================================================================
 *
 * Canonical error-tag taxonomy mirrors WRITING.md section 20 (rewrites table)
 * and the deterministic drill grader in `lib/writing-drills/grader.ts`. New
 * tags must be appended (never re-ordered) — analytics charts are keyed by
 * position in this list.
 * ============================================================================
 */

import type { WritingCriterionKey } from '@/lib/types/expert';

export const WRITING_ERROR_TAGS = [
  'missing_key_content',
  'irrelevant_content',
  'unclear_purpose',
  'informal_tone',
  'abbreviation_issue',
  'poor_paragraphing',
  'inaccurate_transfer',
  'grammar_articles',
] as const;

export type WritingErrorTag = (typeof WRITING_ERROR_TAGS)[number];

export const WRITING_ERROR_TAG_LABELS: Record<WritingErrorTag, string> = {
  missing_key_content: 'Missing key content',
  irrelevant_content: 'Irrelevant content',
  unclear_purpose: 'Unclear purpose',
  informal_tone: 'Informal tone',
  abbreviation_issue: 'Abbreviation issue',
  poor_paragraphing: 'Poor paragraphing',
  inaccurate_transfer: 'Inaccurate transfer of facts',
  grammar_articles: 'Grammar / articles',
};

/**
 * One tagged data point — a drill result, an expert criterion comment, or
 * a rule-engine finding. The aggregator does not care where it came from.
 */
export interface WeaknessDataPoint {
  /** ISO timestamp. */
  occurredAt: string;
  /** One of the canonical tags above. Unknown tags are silently dropped. */
  tag: WritingErrorTag | string;
  /** Optional criterion link (lets us pivot by criterion). */
  criterion?: WritingCriterionKey;
}

export interface WeaknessTagSummary {
  tag: WritingErrorTag;
  label: string;
  count: number;
  /** Share of total observations, in 0..1. */
  share: number;
}

export interface WeaknessCriterionSummary {
  criterion: WritingCriterionKey;
  label: string;
  count: number;
  share: number;
}

export interface WeaknessTrendBucket {
  /** ISO date (YYYY-MM-DD) for the bucket. */
  date: string;
  /** Total observations falling into this bucket. */
  count: number;
}

export interface WeaknessSummary {
  totalObservations: number;
  /** Top-N tags by count, sorted descending. */
  topTags: WeaknessTagSummary[];
  /** Per-criterion breakdown, sorted descending. */
  byCriterion: WeaknessCriterionSummary[];
  /** Daily trend bucket (last N days requested). */
  trend: WeaknessTrendBucket[];
  /** ISO date of the first / last observation; empty string when none. */
  firstSeenAt: string;
  lastSeenAt: string;
}
