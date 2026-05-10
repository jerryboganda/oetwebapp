import {
  CANONICAL_LETTER_TYPES,
  fromEngineLetterType,
  isCanonicalLetterType,
  LETTER_TYPE_DISPLAY_LABELS,
  letterTypeLabel,
  toEngineLetterType,
} from '../letter-types';

describe('writing letter types — single source of truth', () => {
  it('matches the backend canonical vocabulary exactly', () => {
    expect([...CANONICAL_LETTER_TYPES]).toEqual([
      'routine_referral',
      'urgent_referral',
      'non_medical_referral',
      'update_discharge',
      'update_referral_specialist_to_gp',
      'transfer_letter',
    ]);
  });

  it('exposes a display label for every canonical code', () => {
    for (const code of CANONICAL_LETTER_TYPES) {
      expect(LETTER_TYPE_DISPLAY_LABELS[code]).toBeTruthy();
      expect(letterTypeLabel(code)).toBe(LETTER_TYPE_DISPLAY_LABELS[code]);
    }
  });

  it('returns the raw value for unknown labels (no undefined)', () => {
    expect(letterTypeLabel('not_a_real_code')).toBe('not_a_real_code');
  });

  it('isCanonicalLetterType narrows safely', () => {
    expect(isCanonicalLetterType('routine_referral')).toBe(true);
    expect(isCanonicalLetterType('referral')).toBe(false);
    expect(isCanonicalLetterType(null)).toBe(false);
    expect(isCanonicalLetterType(undefined)).toBe(false);
    expect(isCanonicalLetterType(123)).toBe(false);
  });

  it('round-trips canonical → engine → canonical', () => {
    for (const code of CANONICAL_LETTER_TYPES) {
      const engine = toEngineLetterType(code);
      expect(fromEngineLetterType(engine)).toBe(code);
    }
  });

  it('maps update_discharge to engine "discharge"', () => {
    expect(toEngineLetterType('update_discharge')).toBe('discharge');
    expect(toEngineLetterType('update_referral_specialist_to_gp')).toBe('specialist_to_gp');
    expect(toEngineLetterType('transfer_letter')).toBe('transfer');
  });
});
