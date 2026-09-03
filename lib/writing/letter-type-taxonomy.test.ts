import { describe, expect, it } from 'vitest';

import {
  WRITING_LETTER_TYPE_LABELS,
  WRITING_LETTER_TYPES,
  writingLetterTypesForProfession,
} from '@/components/domain/writing/admin/builder-state';
import { WRITING_PROFESSION_LABELS, WRITING_PROFESSIONS } from './types';
import { writingProfileSchema } from './zod';

const BASE_PROFILE = {
  profession: 'medicine',
  subDiscipline: null,
  yearsExperience: 3,
  targetBand: 'B',
  examDate: '2026-10-01',
  daysPerWeek: 5,
  minutesPerDay: 45,
  targetCountry: 'UK',
} as const;

describe('writing catalogue letter-type taxonomy (2026-09 revision)', () => {
  it('WR-TAX-01: excludes retired Response (LT-RP)', () => {
    expect((WRITING_LETTER_TYPES as string[])).not.toContain('LT-RP');
    expect(Object.keys(WRITING_LETTER_TYPE_LABELS)).not.toContain('LT-RP');
  });

  it('WR-TAX-02: includes Other Letters with the exact candidate-facing label', () => {
    expect((WRITING_LETTER_TYPES as string[])).toContain('LT-OT');
    expect(WRITING_LETTER_TYPE_LABELS['LT-OT']).toBe('Other Letters');
  });

  it('keeps the five known categories with stable labels', () => {
    expect(WRITING_LETTER_TYPE_LABELS).toMatchObject({
      'LT-RR': 'Routine referral',
      'LT-UR': 'Urgent referral',
      'LT-DG': 'Discharge',
      'LT-TR': 'Transfer',
      'LT-NM': 'Non-medical referral',
    });
  });

  it('WR-TAX-03: exposes Other Letters under every profession', () => {
    for (const profession of WRITING_PROFESSIONS) {
      expect(
        writingLetterTypesForProfession(profession),
        `profession ${profession} must expose LT-OT`,
      ).toContain('LT-OT');
    }
  });

  it('preserves the veterinary non-medical exclusion and nothing else', () => {
    expect(writingLetterTypesForProfession('veterinary')).not.toContain('LT-NM');
    for (const profession of WRITING_PROFESSIONS) {
      if (profession === 'veterinary') continue;
      expect(writingLetterTypesForProfession(profession)).toHaveLength(
        WRITING_LETTER_TYPES.length,
      );
    }
  });

  it('covers every profession id with a display label', () => {
    for (const profession of WRITING_PROFESSIONS) {
      expect(WRITING_PROFESSION_LABELS[profession]).toBeTruthy();
    }
  });

  it('WR-TAX-04 / WR-CLS-08: onboarding focus rejects LT-RP and accepts LT-OT', () => {
    expect(
      writingProfileSchema.safeParse({ ...BASE_PROFILE, letterTypeFocus: ['LT-RP'] })
        .success,
    ).toBe(false);

    expect(
      writingProfileSchema.safeParse({ ...BASE_PROFILE, letterTypeFocus: ['all'] })
        .success,
    ).toBe(false);

    const parsed = writingProfileSchema.parse({
      ...BASE_PROFILE,
      letterTypeFocus: ['LT-RR', 'LT-DG', 'LT-OT'],
    });
    expect(parsed.letterTypeFocus).toContain('LT-OT');
  });
});
