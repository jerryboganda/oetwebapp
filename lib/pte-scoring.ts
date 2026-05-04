// ============================================================================
// PTE Canonical Scoring Types & Helpers — Foundation Layer
// ============================================================================
//
// PTE is DEFERRED per product strategy (Documents 01/04/06). This module
// provides ONLY the shared type contracts and score-range validation so
// that the shared core can reference PTE without assuming OET or IELTS
// semantics. The full PTE question-type engine and rapid-drill scoring
// will be built in a future dedicated engine program.
//
// Verified from Pearson PTE official sources:
//   - Score range: 10–90 (overall and per communicative skill)
//   - Results typically within 48 hours, up to 5 days
//   - Score reports include overall + communicative-skill breakdown
//
// Reference:
//   - https://www.pearsonpte.com/ctf-assets/yqwtwibiobs4/4WyCZezpBrj8Ft7oyEnYkY/e1e5bb43fa60cd2be42fa295ff18f347/Test_Taker_Handbook_-_PTE_Academic_-_July_2025__web_.pdf
// ============================================================================

// ---------------------------------------------------------------------------
// Types
// ---------------------------------------------------------------------------

/** PTE communicative skills. */
export type PteCommunicativeSkill = 'listening' | 'reading' | 'speaking' | 'writing';

/** PTE enabling skills (reported separately). */
export type PteEnablingSkill =
  | 'grammar'
  | 'oral_fluency'
  | 'pronunciation'
  | 'spelling'
  | 'vocabulary'
  | 'written_discourse';

/** PTE overall and skill scores (10–90 scale). */
export interface PteScoreReport {
  overall: number;
  listening: number;
  reading: number;
  speaking: number;
  writing: number;
  enablingSkills?: Partial<Record<PteEnablingSkill, number>>;
}

/** PTE score range bounds. */
export const PTE_SCORE_MIN = 10 as const;
export const PTE_SCORE_MAX = 90 as const;

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

/** Clamp a PTE score to the valid 10–90 range. */
export function clampPteScore(value: number): number {
  if (!Number.isFinite(value)) return PTE_SCORE_MIN;
  return Math.max(PTE_SCORE_MIN, Math.min(PTE_SCORE_MAX, Math.round(value)));
}

/** Validate that a string/number is a valid PTE score (10–90, integer). */
export function isValidPteScore(value: string | number | null | undefined): boolean {
  if (value === null || value === undefined || value === '') return false;
  const num = typeof value === 'string' ? parseFloat(value) : value;
  if (!Number.isFinite(num)) return false;
  if (num < PTE_SCORE_MIN || num > PTE_SCORE_MAX) return false;
  return Number.isInteger(num);
}

/** Typical nursing registration target for PTE (65 = equivalent to IELTS 7.0). */
export const PTE_DEFAULT_TARGET_SCORE = 65 as const;

/** Map a PTE score to a readiness band for shared-core display. */
export function pteReadinessBand(score: number): 'not_ready' | 'developing' | 'borderline' | 'exam_ready' | 'strong' {
  const s = clampPteScore(score);
  if (s < 50) return 'not_ready';
  if (s < 58) return 'developing';
  if (s < PTE_DEFAULT_TARGET_SCORE) return 'borderline';
  if (s < 79) return 'exam_ready';
  return 'strong';
}

/** Human-readable label for a PTE readiness band. */
export function pteReadinessBandLabel(band: ReturnType<typeof pteReadinessBand>): string {
  switch (band) {
    case 'not_ready': return 'Not ready';
    case 'developing': return 'Developing';
    case 'borderline': return 'Borderline';
    case 'exam_ready': return 'Exam-ready';
    case 'strong': return 'Strong';
  }
}
