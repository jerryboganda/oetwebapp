// ============================================================================
// TOEFL iBT Canonical Scoring Module — SINGLE SOURCE OF TRUTH
// ============================================================================
//
// Verified from ETS TOEFL iBT official sources:
//   - Total score scale: 0–120 (sum of 4 section scores)
//   - Four sub-tests: Reading, Listening, Speaking, Writing (0–30 scale each)
//   - Section performance levels mapped to CEFR proficiency bands
//   - Default healthcare/academic target: 80–84 (common benchmark for nursing/boards)
//
// References:
//   - https://www.ets.org/toefl/test-takers/ibt/scores/understand-scores.html
// ============================================================================

import type { SharedReadinessBand } from './exam-family-scoring';

// ---------------------------------------------------------------------------
// Types
// ---------------------------------------------------------------------------

/** TOEFL iBT sub-tests. */
export type ToeflSubtest = 'reading' | 'listening' | 'speaking' | 'writing';

/** TOEFL iBT section performance level (CEFR-aligned). */
export type ToeflSectionBand =
  | 'advanced'              // CEFR C1 or higher
  | 'high_intermediate'     // CEFR B2
  | 'low_intermediate'      // CEFR B1
  | 'below_low_intermediate'; // Below B1

/** TOEFL iBT overall and section score report. */
export interface ToeflScoreReport {
  overall: number;
  reading: number;
  listening: number;
  speaking: number;
  writing: number;
  bands?: Partial<Record<ToeflSubtest, ToeflSectionBand>>;
}

/** Result of a TOEFL score determination. */
export interface ToeflScoreResult {
  score: number;
  scoreDisplay: string;
  meetsTarget: boolean | null;
  targetScore: number | null;
  readinessBand: SharedReadinessBand;
}

// ---------------------------------------------------------------------------
// Invariants / Constants
// ---------------------------------------------------------------------------

/** Total score bounds. */
export const TOEFL_SCORE_MIN = 0 as const;
export const TOEFL_SCORE_MAX = 120 as const;

/** Section score bounds (0–30). */
export const TOEFL_SECTION_MIN = 0 as const;
export const TOEFL_SECTION_MAX = 30 as const;

/** Default healthcare registration / university benchmark target. */
export const TOEFL_DEFAULT_TARGET_SCORE = 80 as const;

/** Section proficiency thresholds [Advanced Min, High-Intermediate Min, Low-Intermediate Min]. */
export const TOEFL_SECTION_THRESHOLDS: Record<
  ToeflSubtest,
  { advanced: number; highIntermediate: number; lowIntermediate: number }
> = {
  reading: { advanced: 24, highIntermediate: 18, lowIntermediate: 10 },
  listening: { advanced: 22, highIntermediate: 17, lowIntermediate: 9 },
  speaking: { advanced: 25, highIntermediate: 20, lowIntermediate: 16 },
  writing: { advanced: 24, highIntermediate: 17, lowIntermediate: 13 },
} as const;

// ---------------------------------------------------------------------------
// Pure Scoring & Normalization Helpers
// ---------------------------------------------------------------------------

/** Clamp a TOEFL total score to the valid 0–120 range. */
export function clampToeflScore(value: number): number {
  if (!Number.isFinite(value)) return TOEFL_SCORE_MIN;
  return Math.max(TOEFL_SCORE_MIN, Math.min(TOEFL_SCORE_MAX, Math.round(value)));
}

/** Clamp a TOEFL section score to the valid 0–30 range. */
export function clampToeflSectionScore(value: number): number {
  if (!Number.isFinite(value)) return TOEFL_SECTION_MIN;
  return Math.max(TOEFL_SECTION_MIN, Math.min(TOEFL_SECTION_MAX, Math.round(value)));
}

/** Validate that a value is a valid TOEFL total score (0–120 integer). */
export function isValidToeflScore(value: string | number | null | undefined): boolean {
  if (value === null || value === undefined || value === '') return false;
  if (typeof value === 'string') {
    const trimmed = value.trim();
    if (!/^\d+$/.test(trimmed)) return false;
    const num = parseInt(trimmed, 10);
    return num >= TOEFL_SCORE_MIN && num <= TOEFL_SCORE_MAX;
  }
  if (typeof value === 'number') {
    return Number.isFinite(value) && Number.isInteger(value) && value >= TOEFL_SCORE_MIN && value <= TOEFL_SCORE_MAX;
  }
  return false;
}

/** Validate that a value is a valid TOEFL section score (0–30 integer). */
export function isValidToeflSectionScore(value: string | number | null | undefined): boolean {
  if (value === null || value === undefined || value === '') return false;
  if (typeof value === 'string') {
    const trimmed = value.trim();
    if (!/^\d+$/.test(trimmed)) return false;
    const num = parseInt(trimmed, 10);
    return num >= TOEFL_SECTION_MIN && num <= TOEFL_SECTION_MAX;
  }
  if (typeof value === 'number') {
    return Number.isFinite(value) && Number.isInteger(value) && value >= TOEFL_SECTION_MIN && value <= TOEFL_SECTION_MAX;
  }
  return false;
}

/** Compute overall score by summing 4 clamped section scores. */
export function toeflOverallScore(sections: {
  reading: number;
  listening: number;
  speaking: number;
  writing: number;
}): number {
  const r = clampToeflSectionScore(sections.reading);
  const l = clampToeflSectionScore(sections.listening);
  const s = clampToeflSectionScore(sections.speaking);
  const w = clampToeflSectionScore(sections.writing);
  return clampToeflScore(r + l + s + w);
}

/** Determine section performance level (CEFR-aligned band). */
export function toeflSectionBand(subtest: ToeflSubtest, score: number): ToeflSectionBand {
  const s = clampToeflSectionScore(score);
  const thresholds = TOEFL_SECTION_THRESHOLDS[subtest] || TOEFL_SECTION_THRESHOLDS.reading;

  if (s >= thresholds.advanced) return 'advanced';
  if (s >= thresholds.highIntermediate) return 'high_intermediate';
  if (s >= thresholds.lowIntermediate) return 'low_intermediate';
  return 'below_low_intermediate';
}

/** Human-readable label for a TOEFL section band. */
export function toeflSectionBandLabel(band: ToeflSectionBand): string {
  switch (band) {
    case 'advanced': return 'Advanced';
    case 'high_intermediate': return 'High-Intermediate';
    case 'low_intermediate': return 'Low-Intermediate';
    case 'below_low_intermediate': return 'Below Low-Intermediate';
  }
}

/**
 * Map a TOEFL total score (0–120) to a shared readiness band.
 *
 *   < 60  -> not_ready
 *   < 70  -> developing
 *   < 80  -> borderline
 *   < 95  -> exam_ready
 *   >= 95 -> strong
 */
export function toeflReadinessBand(score: number): SharedReadinessBand {
  const s = clampToeflScore(score);
  if (s < 60) return 'not_ready';
  if (s < 70) return 'developing';
  if (s < TOEFL_DEFAULT_TARGET_SCORE) return 'borderline';
  if (s < 95) return 'exam_ready';
  return 'strong';
}

/** Human-readable label for a TOEFL readiness band. */
export function toeflReadinessBandLabel(band: SharedReadinessBand): string {
  switch (band) {
    case 'not_ready': return 'Not ready';
    case 'developing': return 'Developing';
    case 'borderline': return 'Borderline';
    case 'exam_ready': return 'Exam-ready';
    case 'strong': return 'Strong';
  }
}

/** Format a TOEFL score for display (e.g. "92/120" or "92"). */
export function formatToeflScoreDisplay(score: number): string {
  return `${clampToeflScore(score)}/${TOEFL_SCORE_MAX}`;
}

/** Format a TOEFL grade / score label for display. */
export function formatToeflGradeDisplay(score: number): string {
  return `Score ${clampToeflScore(score)}`;
}

// ---------------------------------------------------------------------------
// Invariant Self-Check IIFE
// ---------------------------------------------------------------------------
(() => {
  const minClamped = clampToeflScore(-10);
  const maxClamped = clampToeflScore(150);
  if (minClamped !== 0 || maxClamped !== 120) {
    throw new Error(`[toefl-scoring] Invariant failure: Score clamping [${minClamped}, ${maxClamped}] != [0, 120]`);
  }

  const overall = toeflOverallScore({ reading: 25, listening: 20, speaking: 22, writing: 23 });
  if (overall !== 90) {
    throw new Error(`[toefl-scoring] Invariant failure: Overall 25+20+22+23 = ${overall} != 90`);
  }

  const rBand = toeflSectionBand('reading', 24);
  if (rBand !== 'advanced') {
    throw new Error(`[toefl-scoring] Invariant failure: Reading 24 should be advanced, got ${rBand}`);
  }

  const readiness = toeflReadinessBand(80);
  if (readiness !== 'exam_ready') {
    throw new Error(`[toefl-scoring] Invariant failure: Score 80 should be exam_ready, got ${readiness}`);
  }
})();
