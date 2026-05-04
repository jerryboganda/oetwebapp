// ============================================================================
// Exam-Family Scoring Dispatcher — Shared-Core Abstraction
// ============================================================================
//
// This module provides exam-family-aware scoring entry points that dispatch
// to the correct exam-specific scoring module (OET, IELTS, PTE). Shared-core
// workflows MUST use these helpers instead of hardcoding OET assumptions.
//
// OET-specific code should still import from `lib/scoring.ts` directly when
// the context is known to be OET-only.
// ============================================================================

import type { ExamFamilyCode } from './mock-data';
import { oetGradeFromScaled, oetGradeLabel, formatScaledScore, OET_SCALED_MAX } from './scoring';
import { ieltsBandDisplay, ieltsRoundBand, IELTS_BAND_MAX, IELTS_DEFAULT_TARGET_BAND } from './ielts-scoring';
import { clampPteScore, pteReadinessBand, pteReadinessBandLabel, PTE_SCORE_MAX } from './pte-scoring';

// ---------------------------------------------------------------------------
// Exam-family score display
// ---------------------------------------------------------------------------

/**
 * Format a score for display according to the exam family's conventions.
 *
 *   OET   → "380/500" (scaled score)
 *   IELTS → "7.0"     (band score)
 *   PTE   → "65"      (10–90 score)
 */
export function formatScoreDisplay(
  examFamily: ExamFamilyCode,
  score: number,
): string {
  switch (examFamily) {
    case 'oet':
      return `${Math.round(score)}/${OET_SCALED_MAX}`;
    case 'ielts':
      return ieltsBandDisplay(score);
    case 'pte':
      return String(clampPteScore(score));
    default:
      return String(score);
  }
}

// ---------------------------------------------------------------------------
// Exam-family grade / band display
// ---------------------------------------------------------------------------

/**
 * Format a grade label for display according to the exam family.
 *
 *   OET   → "Grade B"
 *   IELTS → "Band 7.0"
 *   PTE   → "Score 65"
 */
export function formatGradeDisplay(
  examFamily: ExamFamilyCode,
  score: number,
): string {
  switch (examFamily) {
    case 'oet': {
      const grade = oetGradeFromScaled(Math.round(score));
      return oetGradeLabel(grade);
    }
    case 'ielts': {
      return `Band ${ieltsBandDisplay(score)}`;
    }
    case 'pte': {
      return `Score ${clampPteScore(score)}`;
    }
    default:
      return String(score);
  }
}

// ---------------------------------------------------------------------------
// Exam-family target validation
// ---------------------------------------------------------------------------

/**
 * Validate that a goal/target score string is valid for the given exam family.
 * Returns a normalized number or null if invalid.
 */
export function normalizeTargetScore(
  examFamily: ExamFamilyCode,
  value: string | number | null | undefined,
): number | null {
  if (value === null || value === undefined || value === '') return null;
  const num = typeof value === 'string' ? parseFloat(value) : value;
  if (!Number.isFinite(num)) return null;

  switch (examFamily) {
    case 'oet': {
      const rounded = Math.round(num);
      if (rounded < 0 || rounded > OET_SCALED_MAX) return null;
      return rounded;
    }
    case 'ielts': {
      const band = ieltsRoundBand(num);
      if (band < 0 || band > IELTS_BAND_MAX) return null;
      return band;
    }
    case 'pte': {
      const clamped = Math.round(num);
      if (clamped < 10 || clamped > PTE_SCORE_MAX) return null;
      return clamped;
    }
    default:
      return null;
  }
}

// ---------------------------------------------------------------------------
// Exam-family readiness band
// ---------------------------------------------------------------------------

/** Readiness band for any exam family, normalized to a shared vocabulary. */
export type SharedReadinessBand = 'not_ready' | 'developing' | 'borderline' | 'exam_ready' | 'strong';

/**
 * Map a score to a shared readiness band, using exam-family-specific thresholds.
 *
 *   OET:  <250 not_ready, <300 developing, <350 borderline, <420 exam_ready, ≥420 strong
 *   IELTS: <5.0 not_ready, <5.5 developing, <6.5 borderline, <7.5 exam_ready, ≥7.5 strong
 *   PTE:   <50 not_ready, <58 developing, <65 borderline, <79 exam_ready, ≥79 strong
 */
export function sharedReadinessBand(
  examFamily: ExamFamilyCode,
  score: number,
): SharedReadinessBand {
  switch (examFamily) {
    case 'oet': {
      const s = Math.round(score);
      if (s < 250) return 'not_ready';
      if (s < 300) return 'developing';
      if (s < 350) return 'borderline';
      if (s < 420) return 'exam_ready';
      return 'strong';
    }
    case 'ielts': {
      const b = ieltsRoundBand(score);
      if (b < 5.0) return 'not_ready';
      if (b < 5.5) return 'developing';
      if (b < IELTS_DEFAULT_TARGET_BAND) return 'borderline';
      if (b < 7.5) return 'exam_ready';
      return 'strong';
    }
    case 'pte':
      return pteReadinessBand(score);
    default:
      return 'not_ready';
  }
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

// ---------------------------------------------------------------------------
// Exam-family copy helpers
// ---------------------------------------------------------------------------

/**
 * Human-friendly label for an exam family code.
 */
export function examFamilyLabel(code: ExamFamilyCode): string {
  switch (code) {
    case 'oet': return 'OET';
    case 'ielts': return 'IELTS';
    case 'pte': return 'PTE';
    default:
      // Exhaustiveness check
      const _exhaustive: never = code;
      return String(_exhaustive);
  }
}

/**
 * Score hint / placeholder text for an exam family.
 */
export function examFamilyScoreHint(code: ExamFamilyCode): { hint: string; placeholder: string } {
  switch (code) {
    case 'oet':
      return { hint: 'OET scores use the 0 to 500 scale.', placeholder: 'e.g. 350' };
    case 'ielts':
      return { hint: 'IELTS scores use the 0 to 9 band scale (0.5 increments).', placeholder: 'e.g. 7.0' };
    case 'pte':
      return { hint: 'PTE scores use the 10 to 90 scale.', placeholder: 'e.g. 65' };
    default:
      return { hint: 'Enter your target score.', placeholder: '' };
  }
}
