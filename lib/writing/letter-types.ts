/**
 * ============================================================================
 * Writing Letter Types — Single Source of Truth
 * ============================================================================
 *
 * Canonical OET Writing letter type codes. These MUST match the backend
 * contract in `backend/src/OetLearner.Api/Services/Content/WritingContentStructure.cs`
 * — they are the on-the-wire vocabulary used for content authoring,
 * scoring requests, AI grounding, and analytics.
 *
 * The deterministic rule engine in `lib/rulebook/types.ts` ships its own
 * shorter `LetterType` alias (`'discharge' | 'transfer' | 'specialist_to_gp' | ...`)
 * for engine-internal pattern matching. Use {@link toEngineLetterType} to
 * convert from the canonical wire vocabulary into the engine vocabulary
 * when calling `lintWritingLetter`.
 *
 * Rule of thumb:
 *  - Anywhere a letter type leaves the browser (POST, query string, analytics),
 *    use {@link CanonicalLetterType}.
 *  - Anywhere a letter type is fed into the local rule engine,
 *    convert via {@link toEngineLetterType}.
 * ============================================================================
 */

import type { LetterType as EngineLetterType } from '@/lib/rulebook/types';

export const CANONICAL_LETTER_TYPES = [
  'routine_referral',
  'urgent_referral',
  'non_medical_referral',
  'update_discharge',
  'update_referral_specialist_to_gp',
  'transfer_letter',
] as const;

export type CanonicalLetterType = (typeof CANONICAL_LETTER_TYPES)[number];

export const LETTER_TYPE_DISPLAY_LABELS: Record<CanonicalLetterType, string> = {
  routine_referral: 'Routine Referral',
  urgent_referral: 'Urgent Referral',
  non_medical_referral: 'Referral to Non-Medical Professional',
  update_discharge: 'Discharge Letter',
  update_referral_specialist_to_gp: 'Referral to GP',
  transfer_letter: 'Transfer Letter',
};

/** Look up the human-readable label for a canonical code. Falls back to the
 *  raw code if the value is unknown so the UI never renders `undefined`. */
export function letterTypeLabel(code: CanonicalLetterType | string): string {
  return LETTER_TYPE_DISPLAY_LABELS[code as CanonicalLetterType] ?? code;
}

/** True when the value is one of the canonical six codes. */
export function isCanonicalLetterType(value: unknown): value is CanonicalLetterType {
  return typeof value === 'string'
    && (CANONICAL_LETTER_TYPES as readonly string[]).includes(value);
}

/**
 * Map the canonical wire vocabulary to the deterministic rule engine's
 * shorter `LetterType` alias. This is a lossy view by design: the engine
 * does not distinguish e.g. `update_discharge` from a hypothetical
 * `discharge_summary`; both collapse to `'discharge'`.
 */
export function toEngineLetterType(code: CanonicalLetterType): EngineLetterType {
  switch (code) {
    case 'routine_referral':
      return 'routine_referral';
    case 'urgent_referral':
      return 'urgent_referral';
    case 'non_medical_referral':
      return 'non_medical_referral';
    case 'update_discharge':
      return 'discharge';
    case 'update_referral_specialist_to_gp':
      return 'specialist_to_gp';
    case 'transfer_letter':
      return 'transfer';
  }
}

/** Reverse mapping for migrating engine outputs back to wire format. */
export function fromEngineLetterType(code: EngineLetterType): CanonicalLetterType {
  switch (code) {
    case 'routine_referral':
      return 'routine_referral';
    case 'urgent_referral':
      return 'urgent_referral';
    case 'non_medical_referral':
      return 'non_medical_referral';
    case 'discharge':
      return 'update_discharge';
    case 'specialist_to_gp':
      return 'update_referral_specialist_to_gp';
    case 'transfer':
      return 'transfer_letter';
  }
}
