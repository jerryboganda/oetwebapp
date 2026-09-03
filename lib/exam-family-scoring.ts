// ============================================================================
// Exam-Family Scoring Strategy Pattern & Dispatcher — Shared-Core Microkernel
// ============================================================================
//
// This module implements the Extensible Strategy Pattern for multi-exam scoring
// (OET, IELTS, PTE, TOEFL). Shared-core workflows MUST use these strategy
// abstractions instead of hardcoding OET assumptions.
//
// OET-specific code should still import from `lib/scoring.ts` directly when
// the context is known to be OET-only.
// ============================================================================

import type { ExamFamilyCode } from './mock-data';
import { oetGradeFromScaled, oetGradeLabel, OET_SCALED_MIN, OET_SCALED_MAX } from './scoring';
import {
  ieltsBandDisplay,
  ieltsRoundBand,
  IELTS_BAND_MIN,
  IELTS_BAND_MAX,
  IELTS_DEFAULT_TARGET_BAND,
} from './ielts-scoring';
import {
  clampPteScore,
  pteReadinessBand,
  pteReadinessBandLabel,
  PTE_SCORE_MIN,
  PTE_SCORE_MAX,
  PTE_DEFAULT_TARGET_SCORE,
} from './pte-scoring';
import {
  clampToeflScore,
  toeflReadinessBand,
  toeflReadinessBandLabel,
  formatToeflScoreDisplay,
  formatToeflGradeDisplay,
  TOEFL_SCORE_MIN,
  TOEFL_SCORE_MAX,
  TOEFL_DEFAULT_TARGET_SCORE,
} from './toefl-scoring';

// ---------------------------------------------------------------------------
// Shared Types & Strategy Interface
// ---------------------------------------------------------------------------

/** Readiness band for any exam family, normalized to a shared vocabulary. */
export type SharedReadinessBand = 'not_ready' | 'developing' | 'borderline' | 'exam_ready' | 'strong';

/**
 * Strategy interface for exam-family-specific scoring, normalization, and presentation behavior.
 */
export interface IExamScoringStrategy {
  readonly examFamily: ExamFamilyCode;
  readonly label: string;
  readonly scoreHint: { hint: string; placeholder: string };
  readonly minScore: number;
  readonly maxScore: number;
  readonly defaultTarget: number;
  formatScore(score: number, subtest?: string): string;
  formatGrade(score: number, subtest?: string): string;
  normalizeTargetScore(value: string | number | null | undefined): number | null;
  getReadinessBand(score: number): SharedReadinessBand;
  isPass(score: number, countryCode?: string | null): boolean;
}

// ---------------------------------------------------------------------------
// Concrete Strategy Implementations
// ---------------------------------------------------------------------------

/** OET Canonical Scoring Strategy */
export class OetScoringStrategy implements IExamScoringStrategy {
  readonly examFamily: ExamFamilyCode = 'oet';
  readonly label = 'OET';
  readonly scoreHint = { hint: 'OET scores use the 0 to 500 scale.', placeholder: 'e.g. 350' };
  readonly minScore = OET_SCALED_MIN;
  readonly maxScore = OET_SCALED_MAX;
  readonly defaultTarget = 350;

  formatScore(score: number): string {
    return `${Math.round(score)}/${OET_SCALED_MAX}`;
  }

  formatGrade(score: number): string {
    const grade = oetGradeFromScaled(Math.round(score));
    return oetGradeLabel(grade);
  }

  normalizeTargetScore(value: string | number | null | undefined): number | null {
    if (value === null || value === undefined || value === '') return null;
    const num = typeof value === 'string' ? parseFloat(value) : value;
    if (!Number.isFinite(num)) return null;
    const rounded = Math.round(num);
    if (rounded < OET_SCALED_MIN || rounded > OET_SCALED_MAX) return null;
    return rounded;
  }

  getReadinessBand(score: number): SharedReadinessBand {
    const s = Math.round(score);
    if (s < 250) return 'not_ready';
    if (s < 300) return 'developing';
    if (s < 350) return 'borderline';
    if (s < 420) return 'exam_ready';
    return 'strong';
  }

  isPass(score: number, countryCode?: string | null): boolean {
    const s = Math.round(score);
    const cc = countryCode?.trim().toUpperCase();
    if (cc === 'US' || cc === 'QA') {
      return s >= 300;
    }
    return s >= 350;
  }
}

/** IELTS Canonical Scoring Strategy */
export class IeltsScoringStrategy implements IExamScoringStrategy {
  readonly examFamily: ExamFamilyCode = 'ielts';
  readonly label = 'IELTS';
  readonly scoreHint = { hint: 'IELTS scores use the 0 to 9 band scale (0.5 increments).', placeholder: 'e.g. 7.0' };
  readonly minScore = IELTS_BAND_MIN;
  readonly maxScore = IELTS_BAND_MAX;
  readonly defaultTarget = IELTS_DEFAULT_TARGET_BAND;

  formatScore(score: number): string {
    return ieltsBandDisplay(score);
  }

  formatGrade(score: number): string {
    return `Band ${ieltsBandDisplay(score)}`;
  }

  normalizeTargetScore(value: string | number | null | undefined): number | null {
    if (value === null || value === undefined || value === '') return null;
    const num = typeof value === 'string' ? parseFloat(value) : value;
    if (!Number.isFinite(num)) return null;
    if (num < IELTS_BAND_MIN || num > IELTS_BAND_MAX) return null;
    return ieltsRoundBand(num);
  }

  getReadinessBand(score: number): SharedReadinessBand {
    const b = ieltsRoundBand(score);
    if (b < 5.0) return 'not_ready';
    if (b < 5.5) return 'developing';
    if (b < IELTS_DEFAULT_TARGET_BAND) return 'borderline';
    if (b < 7.5) return 'exam_ready';
    return 'strong';
  }

  isPass(score: number): boolean {
    return ieltsRoundBand(score) >= IELTS_DEFAULT_TARGET_BAND;
  }
}

/** PTE Academic Scoring Strategy */
export class PteScoringStrategy implements IExamScoringStrategy {
  readonly examFamily: ExamFamilyCode = 'pte';
  readonly label = 'PTE';
  readonly scoreHint = { hint: 'PTE scores use the 10 to 90 scale.', placeholder: 'e.g. 65' };
  readonly minScore = PTE_SCORE_MIN;
  readonly maxScore = PTE_SCORE_MAX;
  readonly defaultTarget = PTE_DEFAULT_TARGET_SCORE;

  formatScore(score: number): string {
    return String(clampPteScore(score));
  }

  formatGrade(score: number): string {
    return `Score ${clampPteScore(score)}`;
  }

  normalizeTargetScore(value: string | number | null | undefined): number | null {
    if (value === null || value === undefined || value === '') return null;
    const num = typeof value === 'string' ? parseFloat(value) : value;
    if (!Number.isFinite(num)) return null;
    const clamped = Math.round(num);
    if (clamped < PTE_SCORE_MIN || clamped > PTE_SCORE_MAX) return null;
    return clamped;
  }

  getReadinessBand(score: number): SharedReadinessBand {
    return pteReadinessBand(score);
  }

  isPass(score: number): boolean {
    return clampPteScore(score) >= PTE_DEFAULT_TARGET_SCORE;
  }
}

/** TOEFL iBT Scoring Strategy */
export class ToeflScoringStrategy implements IExamScoringStrategy {
  readonly examFamily: ExamFamilyCode = 'toefl';
  readonly label = 'TOEFL';
  readonly scoreHint = { hint: 'TOEFL scores use the 0 to 120 scale (4 sub-tests 0–30).', placeholder: 'e.g. 80' };
  readonly minScore = TOEFL_SCORE_MIN;
  readonly maxScore = TOEFL_SCORE_MAX;
  readonly defaultTarget = TOEFL_DEFAULT_TARGET_SCORE;

  formatScore(score: number): string {
    return formatToeflScoreDisplay(score);
  }

  formatGrade(score: number): string {
    return formatToeflGradeDisplay(score);
  }

  normalizeTargetScore(value: string | number | null | undefined): number | null {
    if (value === null || value === undefined || value === '') return null;
    const num = typeof value === 'string' ? parseFloat(value) : value;
    if (!Number.isFinite(num)) return null;
    const clamped = Math.round(num);
    if (clamped < TOEFL_SCORE_MIN || clamped > TOEFL_SCORE_MAX) return null;
    return clamped;
  }

  getReadinessBand(score: number): SharedReadinessBand {
    return toeflReadinessBand(score);
  }

  isPass(score: number): boolean {
    return clampToeflScore(score) >= TOEFL_DEFAULT_TARGET_SCORE;
  }
}

// ---------------------------------------------------------------------------
// Strategy Registry
// ---------------------------------------------------------------------------

const registry: Record<string, IExamScoringStrategy> = {
  oet: new OetScoringStrategy(),
  ielts: new IeltsScoringStrategy(),
  pte: new PteScoringStrategy(),
  toefl: new ToeflScoringStrategy(),
};

/**
 * Register or override a strategy for an exam family.
 */
export function registerExamScoringStrategy(strategy: IExamScoringStrategy): void {
  registry[strategy.examFamily.toLowerCase()] = strategy;
}

/**
 * Resolve an IExamScoringStrategy instance for the given exam family code.
 * Defaults to OET if unknown or omitted.
 */
export function getExamScoringStrategy(examFamily: ExamFamilyCode | string | null | undefined): IExamScoringStrategy {
  const key = (examFamily || 'oet').toLowerCase().trim();
  return registry[key] || registry['oet'];
}

// ---------------------------------------------------------------------------
// Backward-Compatible Exported Wrappers
// ---------------------------------------------------------------------------

/**
 * Format a score for display according to the exam family's conventions.
 *
 *   OET   -> "380/500" (scaled score)
 *   IELTS -> "7.0"     (band score)
 *   PTE   -> "65"      (10–90 score)
 *   TOEFL -> "90/120"  (0–120 score)
 */
export function formatScoreDisplay(
  examFamily: ExamFamilyCode,
  score: number,
): string {
  return getExamScoringStrategy(examFamily).formatScore(score);
}

/**
 * Format a grade label for display according to the exam family.
 *
 *   OET   -> "Grade B"
 *   IELTS -> "Band 7.0"
 *   PTE   -> "Score 65"
 *   TOEFL -> "Score 90"
 */
export function formatGradeDisplay(
  examFamily: ExamFamilyCode,
  score: number,
): string {
  return getExamScoringStrategy(examFamily).formatGrade(score);
}

/**
 * Validate that a goal/target score string is valid for the given exam family.
 * Returns a normalized number or null if invalid.
 */
export function normalizeTargetScore(
  examFamily: ExamFamilyCode,
  value: string | number | null | undefined,
): number | null {
  return getExamScoringStrategy(examFamily).normalizeTargetScore(value);
}

/**
 * Map a score to a shared readiness band, using exam-family-specific thresholds.
 */
export function sharedReadinessBand(
  examFamily: ExamFamilyCode,
  score: number,
): SharedReadinessBand {
  return getExamScoringStrategy(examFamily).getReadinessBand(score);
}

/** Human-readable label for a shared readiness band. */
export function sharedReadinessBandLabel(band: SharedReadinessBand): string {
  switch (band) {
    case 'not_ready': return 'Not ready';
    case 'developing': return 'Developing';
    case 'borderline': return 'Borderline';
    case 'exam_ready': return 'Exam-ready';
    case 'strong': return 'Strong';
  }
}

/**
 * Human-friendly label for an exam family code.
 */
export function examFamilyLabel(code: ExamFamilyCode): string {
  return getExamScoringStrategy(code).label;
}

/**
 * Score hint / placeholder text for an exam family.
 */
export function examFamilyScoreHint(code: ExamFamilyCode): { hint: string; placeholder: string } {
  return getExamScoringStrategy(code).scoreHint;
}
